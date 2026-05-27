using Microsoft.Extensions.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Net;

namespace SUSModder.Core.Configuration
{
    public class SUStatsService
    {
        // Statyczny HttpClient współdzielony przez wszystkie instancje (best practice)
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;
        private readonly ISustatsCredentialsRepository? _credentialsRepository;

        /// <summary>
        /// Konstruktor używany przez nowy Discord OAuth flow.
        /// </summary>
        public SUStatsService(
            IConfiguration configuration,
            IDiagnosticsOutput diagnosticsOutput,
            ISustatsCredentialsRepository credentialsRepository)
        {
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
            _credentialsRepository = credentialsRepository;
        }

        /// <summary>
        /// Stary konstruktor bez repozytorium — do usunięcia po pełnej migracji na Discord OAuth.
        /// </summary>
        [Obsolete("Use constructor with ISustatsCredentialsRepository for Discord OAuth flow")]
        public SUStatsService(IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
            _credentialsRepository = null;
        }

        #region Deprecated — susmodder-api flow (do usunięcia po migracji na Discord OAuth)

        /// <summary>
        /// Pobiera listę serwerów SUStats z susmodder-api.
        /// </summary>
        [Obsolete("Use Discord OAuth flow instead. Call GetActiveCredentialsAsync() or use ISustatsCredentialsRepository directly.")]
        public async Task<List<AmongToken>> GetSUStatsServersAsync()
        {
            try
            {
                _diagnosticsOutput.Write("Rozpoczynanie pobierania serwerów SUStats...");

                // Pobierz konfigurację - używaj tych samych kluczy co w AmongTokensService
                var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"];
                var apiConfig = _configuration.GetSection("Configuration")["ApiConfig"];

                _diagnosticsOutput.Write($"BaseUrl from config: '{baseUrl}'");
                _diagnosticsOutput.Write($"ApiConfig from config: '{apiConfig}'");

                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiConfig))
                {
                    _diagnosticsOutput.Write("BŁĄD: Brak BaseUrl lub ApiConfig w konfiguracji");
                    return new List<AmongToken>();
                }

                var fullUrl = baseUrl.TrimEnd('/') + apiConfig;
                _diagnosticsOutput.Write($"Full URL: {fullUrl}");

                // Pobierz token
                var token = SecretProvider.GetDownloadToken();
                _diagnosticsOutput.Write("Download token retrieved.");

                if (string.IsNullOrEmpty(token))
                {
                    _diagnosticsOutput.Write("BŁĄD: Download token is empty");
                    return new List<AmongToken>();
                }

                // Skonfiguruj nagłówki
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                _diagnosticsOutput.Write("Authorization header configured.");

                // Wykonaj żądanie
                var response = await _httpClient.GetAsync(fullUrl);

                _diagnosticsOutput.Write($"HTTP Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _diagnosticsOutput.Write($"BŁĄD: HTTP {response.StatusCode} - {errorContent}");
                    return new List<AmongToken>();
                }

                // Parsuj odpowiedź
                var jsonContent = await response.Content.ReadAsStringAsync();
                _diagnosticsOutput.Write($"Received JSON content length: {jsonContent.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var tokensResponse = JsonSerializer.Deserialize<AmongTokensResponse>(jsonContent, options);

                if (tokensResponse == null)
                {
                    _diagnosticsOutput.Write("BŁĄD: Failed to deserialize JSON response");
                    return new List<AmongToken>();
                }

                _diagnosticsOutput.Write($"Deserialized response - Success: {tokensResponse.Success}, Count: {tokensResponse.Count}");

                if (!tokensResponse.Success)
                {
                    _diagnosticsOutput.Write("BŁĄD: API returned success=false");
                    return new List<AmongToken>();
                }

                _diagnosticsOutput.Write($"Successfully loaded {tokensResponse.Tokens.Count} SUStats servers");

                return tokensResponse.Tokens;
            }
            catch (HttpRequestException ex)
            {
                _diagnosticsOutput.Write($"HTTP Request Error: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (TaskCanceledException ex)
            {
                _diagnosticsOutput.Write($"Request Timeout: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (JsonException ex)
            {
                _diagnosticsOutput.Write($"JSON Parse Error: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Unexpected Error: {ex.Message}");
                return new List<AmongToken>();
            }
        }

        /// <summary>
        /// Waliduje sekret SUStats przez zapytanie do susmodder-api.
        /// </summary>
        [Obsolete("Use Discord OAuth flow instead. Credentials are obtained from Clair API via Discord OAuth, not from user-entered secrets.")]
        public async Task<AmongToken?> ValidateServerBySecretAsync(string secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            try
            {
                _diagnosticsOutput.Write("Rozpoczynanie walidacji klucza SUStats...");

                var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"];
                var apiConfig = _configuration.GetSection("Configuration")["ApiConfig"];

                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiConfig))
                {
                    _diagnosticsOutput.Write("BŁĄD: Brak BaseUrl lub ApiConfig w konfiguracji");
                    return null;
                }

                var fullUrl = $"{baseUrl.TrimEnd('/')}{apiConfig}?secret={Uri.EscapeDataString(secret)}";

                var token = SecretProvider.GetDownloadToken();
                if (string.IsNullOrEmpty(token))
                {
                    _diagnosticsOutput.Write("BŁĄD: Download token is empty");
                    return null;
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                var response = await _httpClient.GetAsync(fullUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _diagnosticsOutput.Write($"BŁĄD: Walidacja klucza HTTP {response.StatusCode} - {errorContent}");
                    return null;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var tokensResponse = JsonSerializer.Deserialize<AmongTokensResponse>(jsonContent, options);
                if (tokensResponse?.Success != true || tokensResponse.Tokens.Count == 0)
                {
                    _diagnosticsOutput.Write("Walidacja klucza: brak pasującego serwera");
                    return null;
                }

                var matchingServer = tokensResponse.Tokens.FirstOrDefault(s =>
                    s.Secret.Equals(secret, StringComparison.Ordinal));

                if (matchingServer == null && tokensResponse.Tokens.Count == 1)
                {
                    matchingServer = tokensResponse.Tokens[0];
                }

                if (matchingServer == null)
                {
                    _diagnosticsOutput.Write("Walidacja klucza: API zwróciło dane bez zgodnego klucza");
                }

                return matchingServer;
            }
            catch (HttpRequestException ex)
            {
                _diagnosticsOutput.Write($"HTTP Request Error: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _diagnosticsOutput.Write($"Request Timeout: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                _diagnosticsOutput.Write($"JSON Parse Error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Unexpected Error: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Discord OAuth flow

        /// <summary>
        /// Zwraca aktualnie aktywne dane uwierzytelniające SUStats
        /// pobrane z lokalnej bazy SQLite (przez Discord OAuth flow).
        /// </summary>
        public async Task<SustatsCredentials?> GetActiveCredentialsAsync()
        {
            if (_credentialsRepository == null)
            {
                _diagnosticsOutput.Write(
                    "BŁĄD: ISustatsCredentialsRepository nie jest skonfigurowane. " +
                    "Użyj konstruktora z ISustatsCredentialsRepository.");
                return null;
            }

            try
            {
                _diagnosticsOutput.Write("Pobieranie aktywnych danych uwierzytelniających SUStats...");
                var creds = await _credentialsRepository.GetActiveAsync();

                if (creds == null)
                {
                    _diagnosticsOutput.Write("Brak aktywnych danych uwierzytelniających SUStats.");
                    return null;
                }

                _diagnosticsOutput.Write(
                    $"Znaleziono aktywne dane dla serwera: {creds.ServerName} (GuildId: {creds.GuildId})");
                return creds;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas pobierania aktywnych danych SUStats: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sprawdza czy użytkownik ma aktywne uwierzytelnienie SUStats
        /// przez Discord OAuth (istniejące SustatsCredentials w SQLite).
        /// </summary>
        public async Task<bool> IsDiscordAuthAvailableAsync()
        {
            if (_credentialsRepository == null)
            {
                _diagnosticsOutput.Write(
                    "BŁĄD: ISustatsCredentialsRepository nie jest skonfigurowane. " +
                    "Użyj konstruktora z ISustatsCredentialsRepository.");
                return false;
            }

            try
            {
                var creds = await _credentialsRepository.GetActiveAsync();
                bool available = creds != null;

                _diagnosticsOutput.Write(
                    available
                        ? "SUStats Discord OAuth jest dostępny."
                        : "SUStats Discord OAuth nie jest dostępny — brak aktywnych danych.");

                return available;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Błąd podczas sprawdzania dostępności Discord OAuth: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wysyła dane statystyk gry do Clair API.
        /// Używa token+secret uzyskanych przez Discord OAuth flow.
        /// </summary>
        /// <param name="token">Token autoryzacyjny (Authorization: Bearer).</param>
        /// <param name="secret">Tajny klucz (X-Secret header).</param>
        /// <param name="endpoint">Bazowy URL endpointu Clair API.</param>
        /// <param name="statsData">Dane statystyk w formacie JSON.</param>
        /// <returns>True jeśli wysyłka się powiodła, false w przypadku błędu.</returns>
        public async Task<bool> SendGameStatsAsync(
            string token,
            string secret,
            string endpoint,
            string statsData)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _diagnosticsOutput.Write("BŁĄD: Token jest pusty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                _diagnosticsOutput.Write("BŁĄD: Secret jest pusty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _diagnosticsOutput.Write("BŁĄD: Endpoint jest pusty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(statsData))
            {
                _diagnosticsOutput.Write("BŁĄD: Dane statystyk są puste");
                return false;
            }

            try
            {
                var url = endpoint.TrimEnd('/') + "/api/among-data";
                _diagnosticsOutput.Write($"Wysyłanie statystyk gry do: {url}");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(statsData, Encoding.UTF8, "application/json")
                };

                request.Headers.Clear();
                request.Headers.Add("Authorization", $"Bearer {token}");
                request.Headers.Add("X-Secret", secret);
                request.Headers.Add("User-Agent", "SUSModder/1.0");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _diagnosticsOutput.Write(
                        $"Statystyki gry wysłane pomyślnie. HTTP {response.StatusCode}");
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _diagnosticsOutput.Write(
                    $"BŁĄD wysyłania statystyk gry: HTTP {response.StatusCode} - {errorContent}");

                // 401/403 oznaczają problem z autoryzacją — nie próbuj ponownie
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _diagnosticsOutput.Write(
                        "BŁĄD autoryzacji — token lub secret są nieprawidłowe.");
                }

                return false;
            }
            catch (HttpRequestException ex)
            {
                _diagnosticsOutput.Write($"Błąd HTTP podczas wysyłania statystyk gry: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _diagnosticsOutput.Write($"Timeout podczas wysyłania statystyk gry: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Nieoczekiwany błąd podczas wysyłania statystyk gry: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
