using Microsoft.Extensions.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Implementacja flow Discord OAuth2 z PKCE.
/// Odpowiada za logowanie, odświeżanie tokenów i zarządzanie sesją Discord.
/// </summary>
public class DiscordOAuthService : IDiscordOAuthService
{
    // Statyczny HttpClient dla wywołań API (best practice — współdzielony)
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Osobny HttpClient z krótszym timeoutem dla szybkich zapytań o dane użytkownika
    private static readonly HttpClient _userInfoHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Stały port dla lokalnego serwera callback OAuth
    private const int CallbackPort = 53124;

    private readonly IConfiguration _configuration;
    private readonly IDiscordAuthRepository _authRepository;
    private readonly IDiagnosticsOutput _diagnosticsOutput;

    // Cache dla discord_client_id (pobierany raz z Clair API)
    private string? _discordClientId;

    // Tymczasowo przechowywany code_verifier między StartLoginAsync a CompleteLoginAsync
    private string? _lastCodeVerifier;

    public DiscordOAuthService(
        IConfiguration configuration,
        IDiscordAuthRepository authRepository,
        IDiagnosticsOutput diagnosticsOutput)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authRepository = authRepository ?? throw new ArgumentNullException(nameof(authRepository));
        _diagnosticsOutput = diagnosticsOutput ?? throw new ArgumentNullException(nameof(diagnosticsOutput));
    }

    /// <inheritdoc />
    public async Task<OAuthStartResult> StartLoginAsync()
    {
        try
        {
            _diagnosticsOutput.Write("[DiscordOAuth] Starting login flow...");

            // 1. Generuj code_verifier (64 bajty, URL-safe Base64 bez padding)
            var codeVerifier = GenerateCodeVerifier();
            _lastCodeVerifier = codeVerifier;
            _diagnosticsOutput.Write("[DiscordOAuth] Code verifier generated.");

            // 2. Generuj code_challenge = URL-safe Base64(SHA256(code_verifier)), bez padding
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            _diagnosticsOutput.Write("[DiscordOAuth] Code challenge computed.");

            // 3. Pobierz discord_client_id z Clair API
            var discordClientId = await GetDiscordClientIdAsync();
            _discordClientId = discordClientId;

            // 4. Zbuduj URL autoryzacji Discord OAuth2
            var redirectUri = $"http://127.0.0.1:{CallbackPort}/susmodder/callback";
            var authUrl = BuildAuthorizationUrl(discordClientId, redirectUri, codeChallenge);

            _diagnosticsOutput.Write($"[DiscordOAuth] Auth URL built (redirect_uri: {redirectUri})");

            return new OAuthStartResult(authUrl, CallbackPort, codeVerifier);
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] StartLoginAsync error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OAuthCompleteResult> CompleteLoginAsync(string code, string redirectUri)
    {
        try
        {
            _diagnosticsOutput.Write("[DiscordOAuth] Completing login...");

            if (string.IsNullOrEmpty(code))
            {
                return new OAuthCompleteResult(false, "Kod autoryzacyjny jest pusty.");
            }

            if (string.IsNullOrEmpty(_lastCodeVerifier))
            {
                return new OAuthCompleteResult(false, "Brak code_verifier. Uruchom StartLoginAsync przed CompleteLoginAsync.");
            }

            // Upewnij się, że mamy client_id
            if (string.IsNullOrEmpty(_discordClientId))
            {
                _discordClientId = await GetDiscordClientIdAsync();
            }

            // 1. Wymień code na tokeny — POST do Discord OAuth2 token endpoint
            _diagnosticsOutput.Write("[DiscordOAuth] Exchanging code for token...");

            var tokenResponse = await ExchangeCodeForTokenAsync(code, redirectUri, _lastCodeVerifier, _discordClientId);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return new OAuthCompleteResult(false, "Nie udało się wymienić kodu na token. Odpowiedź Discord jest pusta.");
            }

            _diagnosticsOutput.Write("[DiscordOAuth] Token received successfully.");

            // 2. Pobierz dane użytkownika z Discord API
            _diagnosticsOutput.Write("[DiscordOAuth] Fetching user info...");

            var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (userInfo == null)
            {
                return new OAuthCompleteResult(false, "Nie udało się pobrać danych użytkownika z Discord.");
            }

            _diagnosticsOutput.Write($"[DiscordOAuth] User info fetched: {userInfo.Username} (ID: {userInfo.Id})");

            // 3. Oblicz datę wygaśnięcia tokena
            var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            // 4. Zaszyfruj tokeny
            var encryptedAccessToken = CredentialProtector.Protect(tokenResponse.AccessToken);
            var encryptedRefreshToken = CredentialProtector.Protect(tokenResponse.RefreshToken);

            // 5. Zapisz do bazy
            var tokenInfo = new DiscordTokenInfo
            {
                AccessTokenEncrypted = encryptedAccessToken,
                RefreshTokenEncrypted = encryptedRefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresAt = expiresAt,
                DiscordUserId = userInfo.Id,
                DiscordUsername = userInfo.Username,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _authRepository.SaveTokenInfoAsync(tokenInfo);
            _diagnosticsOutput.Write("[DiscordOAuth] Token saved to repository.");

            return new OAuthCompleteResult(true, null);
        }
        catch (HttpRequestException ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] HTTP error during login: {ex.Message}");
            return new OAuthCompleteResult(false, $"Błąd sieciowy: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] JSON parse error during login: {ex.Message}");
            return new OAuthCompleteResult(false, $"Błąd parsowania odpowiedzi: {ex.Message}");
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] Unexpected error during login: {ex.Message}");
            return new OAuthCompleteResult(false, $"Nieoczekiwany błąd: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var tokenInfo = await _authRepository.GetTokenInfoAsync();
            if (tokenInfo == null)
            {
                _diagnosticsOutput.Write("[DiscordOAuth] IsLoggedIn: no token found.");
                return false;
            }

            // Jeśli token wygasł, spróbuj odświeżyć
            if (tokenInfo.ExpiresAt < DateTime.UtcNow)
            {
                _diagnosticsOutput.Write("[DiscordOAuth] Token expired, attempting refresh...");
                var refreshed = await RefreshTokenAsync();
                if (!refreshed)
                {
                    _diagnosticsOutput.Write("[DiscordOAuth] Refresh failed.");
                    return false;
                }
            }

            _diagnosticsOutput.Write("[DiscordOAuth] IsLoggedIn: valid token exists.");
            return true;
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] IsLoggedInAsync error: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshTokenAsync()
    {
        try
        {
            _diagnosticsOutput.Write("[DiscordOAuth] Refreshing token...");

            var tokenInfo = await _authRepository.GetTokenInfoAsync();
            if (tokenInfo == null)
            {
                _diagnosticsOutput.Write("[DiscordOAuth] Refresh failed: no token stored.");
                return false;
            }

            if (string.IsNullOrEmpty(tokenInfo.RefreshTokenEncrypted))
            {
                _diagnosticsOutput.Write("[DiscordOAuth] Refresh failed: no refresh token.");
                return false;
            }

            // Odszyfruj refresh token
            string refreshToken;
            try
            {
                refreshToken = CredentialProtector.Unprotect(tokenInfo.RefreshTokenEncrypted);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"[DiscordOAuth] Failed to decrypt refresh token: {ex.Message}");
                return false;
            }

            // Upewnij się, że mamy client_id
            if (string.IsNullOrEmpty(_discordClientId))
            {
                try
                {
                    _discordClientId = await GetDiscordClientIdAsync();
                }
                catch
                {
                    _diagnosticsOutput.Write("[DiscordOAuth] Cannot refresh: failed to get client_id.");
                    return false;
                }
            }

            // POST do Discord OAuth2 token endpoint z grant_type=refresh_token
            var formData = new Dictionary<string, string>
            {
                { "client_id", _discordClientId },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            };

            using var requestContent = new FormUrlEncodedContent(formData);
            using var response = await _httpClient.PostAsync(
                "https://discord.com/api/oauth2/token", requestContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _diagnosticsOutput.Write($"[DiscordOAuth] Refresh HTTP {response.StatusCode}: {errorBody}");

                // Jeśli 400 — refresh token nieważny, wyczyść lokalnie
                if ((int)response.StatusCode == 400)
                {
                    _diagnosticsOutput.Write("[DiscordOAuth] Refresh token invalid, clearing local data.");
                    await _authRepository.ClearTokenAsync();
                }

                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<DiscordTokenResponse>(json, JsonOptions);

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _diagnosticsOutput.Write("[DiscordOAuth] Refresh: empty response.");
                return false;
            }

            // Aktualizuj tokeny
            var encryptedAccessToken = CredentialProtector.Protect(tokenResponse.AccessToken);
            var encryptedNewRefreshToken = !string.IsNullOrEmpty(tokenResponse.RefreshToken)
                ? CredentialProtector.Protect(tokenResponse.RefreshToken)
                : tokenInfo.RefreshTokenEncrypted; // zachowaj stary refresh token jeśli nie dostałeś nowego

            tokenInfo.AccessTokenEncrypted = encryptedAccessToken;
            tokenInfo.RefreshTokenEncrypted = encryptedNewRefreshToken;
            tokenInfo.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            tokenInfo.UpdatedAt = DateTime.UtcNow;

            await _authRepository.SaveTokenInfoAsync(tokenInfo);
            _diagnosticsOutput.Write("[DiscordOAuth] Token refreshed successfully.");

            return true;
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] RefreshTokenAsync error: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync()
    {
        try
        {
            _diagnosticsOutput.Write("[DiscordOAuth] Logging out...");

            // Pobierz aktualny token (jeśli istnieje)
            var tokenInfo = await _authRepository.GetTokenInfoAsync();

            if (tokenInfo != null && !string.IsNullOrEmpty(tokenInfo.AccessTokenEncrypted))
            {
                // Odszyfruj access token i odwołaj go po stronie Discord
                try
                {
                    var accessToken = CredentialProtector.Unprotect(tokenInfo.AccessTokenEncrypted);
                    await RevokeTokenAsync(accessToken);
                    _diagnosticsOutput.Write("[DiscordOAuth] Token revoked on Discord side.");
                }
                catch (Exception ex)
                {
                    // Nawet jeśli revoke fail, kontynuujemy czyszczenie lokalne
                    _diagnosticsOutput.Write($"[DiscordOAuth] Token revoke failed (continuing): {ex.Message}");
                }
            }

            // Zawsze czyścimy lokalnie
            await _authRepository.ClearTokenAsync();
            _lastCodeVerifier = null;
            _discordClientId = null;

            _diagnosticsOutput.Write("[DiscordOAuth] Logout completed.");
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] LogoutAsync error: {ex.Message}");
            // Nie rzucamy wyjątku — użytkownik ma być wylogowany nawet jeśli revoke fail
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetUsernameAsync()
    {
        try
        {
            var tokenInfo = await _authRepository.GetTokenInfoAsync();
            return tokenInfo?.DiscordUsername;
        }
        catch (Exception ex)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] GetUsernameAsync error: {ex.Message}");
            return null;
        }
    }

    #region PKCE Helpers

    /// <summary>
    /// Generuje code_verifier: 64 bajty kryptograficznie losowych danych,
    /// zakodowane URL-safe Base64 bez znaków padding ('=').
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(randomBytes);
    }

    /// <summary>
    /// Oblicza code_challenge = URL-safe Base64(SHA256(code_verifier)), bez padding.
    /// </summary>
    private static string GenerateCodeChallenge(string codeVerifier)
    {
        byte[] codeVerifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        byte[] sha256Bytes = SHA256.HashData(codeVerifierBytes);
        return Base64UrlEncode(sha256Bytes);
    }

    /// <summary>
    /// Koduje bajty do URL-safe Base64 bez znaków padding.
    /// </summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #endregion

    #region Discord OAuth2 API Calls

    /// <summary>
    /// Pobiera discord_client_id z Clair API (GET /api/susmodder/config).
    /// </summary>
    private async Task<string> GetDiscordClientIdAsync()
    {
        var configUrl = BuildClairApiUrl("config");

        _diagnosticsOutput.Write($"[DiscordOAuth] Fetching OAuth config from {configUrl}");

        using var response = await _httpClient.GetAsync(configUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var config = JsonSerializer.Deserialize<ClairOAuthConfig>(json, JsonOptions);

        if (config == null || string.IsNullOrEmpty(config.DiscordClientId))
        {
            throw new InvalidOperationException(
                "Nie udało się pobrać discord_client_id z Clair API. " +
                "Sprawdź połączenie z internetem i dostępność serwisu Clair.");
        }

        _diagnosticsOutput.Write($"[DiscordOAuth] discord_client_id obtained.");
        return config.DiscordClientId;
    }

    /// <summary>
    /// Buduje URL autoryzacji Discord OAuth2 z parametrami PKCE.
    /// </summary>
    private static string BuildAuthorizationUrl(string clientId, string redirectUri, string codeChallenge)
    {
        return $"https://discord.com/api/oauth2/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString("identify guilds")}" +
            $"&code_challenge={codeChallenge}" +
            $"&code_challenge_method=S256";
    }

    /// <summary>
    /// Wymienia kod autoryzacyjny na tokeny OAuth2 (POST /oauth2/token).
    /// </summary>
    private async Task<DiscordTokenResponse?> ExchangeCodeForTokenAsync(
        string code, string redirectUri, string codeVerifier, string clientId)
    {
        var formData = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "code_verifier", codeVerifier }
        };

        using var requestContent = new FormUrlEncodedContent(formData);
        using var response = await _httpClient.PostAsync(
            "https://discord.com/api/oauth2/token", requestContent);

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] Token exchange failed: HTTP {response.StatusCode} - {responseBody}");
            return null;
        }

        return JsonSerializer.Deserialize<DiscordTokenResponse>(responseBody, JsonOptions);
    }

    /// <summary>
    /// Pobiera dane użytkownika Discord (GET /users/@me).
    /// </summary>
    private async Task<DiscordUserInfo?> GetUserInfoAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _userInfoHttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _diagnosticsOutput.Write($"[DiscordOAuth] User info failed: HTTP {response.StatusCode} - {responseBody}");
            return null;
        }

        return JsonSerializer.Deserialize<DiscordUserInfo>(responseBody, JsonOptions);
    }

    /// <summary>
    /// Odwołuje token OAuth2 po stronie Discord (POST /oauth2/token/revoke).
    /// </summary>
    private async Task RevokeTokenAsync(string accessToken)
    {
        var formData = new Dictionary<string, string>
        {
            { "token", accessToken },
            { "token_type_hint", "access_token" }
        };

        // Do revoke, Discord wymaga albo client_id+client_secret albo podstawowego auth
        // Dla tokenów publicznych (PKCE), wystarczy podać token
        if (!string.IsNullOrEmpty(_discordClientId))
        {
            formData.Add("client_id", _discordClientId);
        }

        using var requestContent = new FormUrlEncodedContent(formData);
        using var response = await _httpClient.PostAsync(
            "https://discord.com/api/oauth2/token/revoke", requestContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _diagnosticsOutput.Write($"[DiscordOAuth] Token revoke: HTTP {response.StatusCode} - {errorBody}");
        }
    }

    #endregion

    #region Configuration Helpers

    /// <summary>
    /// Odczytuje bazowy URL Clair API z konfiguracji.
    /// </summary>
    private string GetClairApiBaseUrl()
    {
        var baseUrl = _configuration.GetSection("Configuration")["ClairApiBaseUrl"];
        if (string.IsNullOrEmpty(baseUrl))
        {
            _diagnosticsOutput.Write("[DiscordOAuth] WARNING: ClairApiBaseUrl not configured, using default.");
            return "https://clairbot.app";
        }
        return baseUrl;
    }

    /// <summary>
    /// Buduje pełny URL endpointu Clair API.
    /// </summary>
    private string BuildClairApiUrl(string path)
    {
        var baseUrl = GetClairApiBaseUrl();
        var endpoint = _configuration.GetSection("Configuration")["ClairApiSusmodderEndpoint"] ?? "/susmodder";
        return $"{baseUrl.TrimEnd('/')}{endpoint}/{path}";
    }

    #endregion

    #region JSON Serialization Options

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Response DTOs (private, for Discord API deserialization)

    /// <summary>
    /// Odpowiedź z Discord OAuth2 token endpoint.
    /// </summary>
    private class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;
    }

    /// <summary>
    /// Odpowiedź z Discord API GET /users/@me.
    /// </summary>
    private class DiscordUserInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }

        [JsonPropertyName("discriminator")]
        public string? Discriminator { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }
    }

    #endregion
}
