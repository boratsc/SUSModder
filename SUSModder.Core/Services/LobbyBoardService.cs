using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Lobby;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Implementacja ILobbyBoardService.
    /// Komunikuje się z susmodder.app /api/lobby-board oraz bezpośrednio
    /// z serwerami modowanych regionów Among Us dla live lookupu.
    /// </summary>
    public sealed class LobbyBoardService : ILobbyBoardService
    {
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _log;
        private readonly string _userHash;
        private readonly string _susmodderToken;
        private readonly string _baseUrl;
        private readonly string _lobbyEndpoint;

        // HttpClient dla susmodder.app (z autoryzacją SUSModder tokenem)
        private static readonly HttpClient _apiClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // HttpClient dla region serverów (bez autoryzacji SUSModder — używa AmongUsAuth)
        private static readonly HttpClient _regionClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Cache dla rate limiting lookupów (key = kod, value = DateTimeOffset ostatniego lookupu)
        private static readonly Dictionary<string, DateTimeOffset> _lookupCache = new();
        private static readonly object _lookupCacheLock = new();
        private static readonly TimeSpan LookupCooldown = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public LobbyBoardService(IConfiguration configuration, IDiagnosticsOutput log, IHardwareIdProvider hardwareIdProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _userHash = (hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider))).GetAnonymousUserHash();
            _susmodderToken = SecretProvider.GetDownloadToken();
            _baseUrl = (_configuration["Configuration:BaseUrl"] ?? "https://susmodder.app/").TrimEnd('/');
            _lobbyEndpoint = _configuration["Configuration:LobbyBoardEndpoint"] ?? "/api/lobby-board";
        }

        private string LobbyUrl(string? path = null) =>
            $"{_baseUrl}{_lobbyEndpoint}{(path != null ? "/" + path : "")}";

        // ═══════════════════════════════════════════════════════════════
        // Publikacja kodu
        // ═══════════════════════════════════════════════════════════════

        public async Task<PostEntryResult> PublishCodeAsync(
            string code, int modId, string region,
            int maxPlayers, int? currentPlayers,
            CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "code",
                ["modId"] = modId,
                ["code"] = code,
                ["region"] = region,
                ["maxPlayers"] = maxPlayers
            };
            if (currentPlayers.HasValue)
                payload["currentPlayers"] = currentPlayers.Value;

            return await PostEntryAsync(payload, ct);
        }

        public async Task<PostEntryResult> PublishMessageAsync(string content, int modId, CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "message",
                ["modId"] = modId,
                ["content"] = content
            };
            return await PostEntryAsync(payload, ct);
        }

        private async Task<PostEntryResult> PostEntryAsync(Dictionary<string, object?> payload, CancellationToken ct)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, LobbyUrl())
                {
                    Content = content
                };
                request.Headers.Add("Authorization", _susmodderToken);
                request.Headers.Add("X-User-Hash", _userHash);

                var response = await _apiClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = JsonSerializer.Deserialize<PostLobbyEntryResponse>(body, _jsonOptions);
                    _log.Write($"[LobbyBoard] POST failed ({response.StatusCode}): {body}");
                    return new PostEntryResult(false, null, null, err?.ErrorCode ?? "UNKNOWN_ERROR", false);
                }

                var result = JsonSerializer.Deserialize<PostLobbyEntryResponse>(body, _jsonOptions);
                return new PostEntryResult(
                    result?.Success ?? true,
                    result?.Id,
                    result?.ExpiresAt,
                    null,
                    result?.ModerationWarning ?? false
                );
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] POST exception: {ex.Message}");
                return new PostEntryResult(false, null, null, "NETWORK_ERROR", false);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Pobieranie wpisów
        // ═══════════════════════════════════════════════════════════════

        public async Task<IReadOnlyList<LobbyBoardEntry>> GetEntriesAsync(
            int? modId = null, LobbyEntryType? type = null,
            string? region = null, int limit = 20,
            CancellationToken ct = default)
        {
            try
            {
                var queryParts = new List<string>();
                if (modId.HasValue) queryParts.Add($"modId={modId.Value}");
                if (type.HasValue && type != LobbyEntryType.All)
                    queryParts.Add($"type={type.Value.ToString().ToLowerInvariant()}");
                if (!string.IsNullOrWhiteSpace(region)) queryParts.Add($"region={Uri.EscapeDataString(region)}");
                queryParts.Add($"limit={Math.Clamp(limit, 1, 50)}");

                var url = LobbyUrl() + "?" + string.Join("&", queryParts);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", _susmodderToken);
                request.Headers.Add("X-User-Hash", _userHash);

                var response = await _apiClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<LobbyBoardResponse>(body, _jsonOptions);

                return (IReadOnlyList<LobbyBoardEntry>?)result?.Entries.AsReadOnly() ?? Array.Empty<LobbyBoardEntry>();
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] GET exception: {ex.Message}");
                return Array.Empty<LobbyBoardEntry>();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Delete / Update / Report
        // ═══════════════════════════════════════════════════════════════

        public async Task<bool> DeleteOwnEntryAsync(string entryId, CancellationToken ct = default)
        {
            return await SimpleAuthenticatedRequest(HttpMethod.Delete, $"{LobbyUrl(entryId)}", ct);
        }

        public async Task<bool> ReportEntryAsync(string entryId, string reason, CancellationToken ct = default)
        {
            try
            {
                var payload = new { reason };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{LobbyUrl(entryId)}/report")
                {
                    Content = content
                };
                request.Headers.Add("Authorization", _susmodderToken);
                request.Headers.Add("X-User-Hash", _userHash);

                var response = await _apiClient.SendAsync(request, ct);
                // Report zawsze zwraca 200, niezależnie od wyniku
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] REPORT exception: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateCodeEntryAsync(string entryId, int? currentPlayers, int? maxPlayers, CancellationToken ct = default)
        {
            try
            {
                var payload = new Dictionary<string, object?>();
                if (currentPlayers.HasValue) payload["currentPlayers"] = currentPlayers.Value;
                if (maxPlayers.HasValue) payload["maxPlayers"] = maxPlayers.Value;

                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Patch, LobbyUrl(entryId))
                {
                    Content = content
                };
                request.Headers.Add("Authorization", _susmodderToken);
                request.Headers.Add("X-User-Hash", _userHash);

                var response = await _apiClient.SendAsync(request, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] PATCH exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SimpleAuthenticatedRequest(HttpMethod method, string url, CancellationToken ct)
        {
            try
            {
                var request = new HttpRequestMessage(method, url);
                request.Headers.Add("Authorization", _susmodderToken);
                request.Headers.Add("X-User-Hash", _userHash);

                var response = await _apiClient.SendAsync(request, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] {method} {url} exception: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Live lookup — NIE przez susmodder.app
        // ═══════════════════════════════════════════════════════════════

        public async Task<LobbyLookupResult?> LookupLobbyStateAsync(
            string code, string regionBaseUrl, AmongUsAuth auth,
            string? modsHeader = null, CancellationToken ct = default)
        {
            // Rate limiting
            var cacheKey = code.ToUpperInvariant();
            lock (_lookupCacheLock)
            {
                if (_lookupCache.TryGetValue(cacheKey, out var lastLookup))
                {
                    if (DateTimeOffset.UtcNow - lastLookup < LookupCooldown)
                        return null; // Skip — zbyt szybko
                }
            }

            try
            {
                regionBaseUrl = regionBaseUrl.TrimEnd('/');

                // 1. code → gameId
                int gameId;
                try
                {
                    gameId = LobbyCodeConverter.GameNameToInt(code);
                }
                catch (FormatException ex)
                {
                    _log.Write($"[LobbyBoard] Lookup — invalid code '{code}': {ex.Message}");
                    return null;
                }

                // 2. POST /api/user — pobierz region token
                var authPayload = new
                {
                    Puid = auth.Puid,
                    Username = auth.Username,
                    ClientVersion = auth.ClientVersion,
                    Language = 0
                };
                var authJson = JsonSerializer.Serialize(authPayload, _jsonOptions);
                var authContent = new StringContent(authJson, Encoding.UTF8, "application/json");

                var authRequest = new HttpRequestMessage(HttpMethod.Post, $"{regionBaseUrl}/api/user")
                {
                    Content = authContent
                };
                authRequest.Headers.Add("Authorization", $"Bearer {auth.IdToken}");
                authRequest.Headers.Add("Accept", "text/plain, */*");
                authRequest.Headers.TryAddWithoutValidation("User-Agent", "UnityPlayer/2022.3.44f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)");
                authRequest.Headers.TryAddWithoutValidation("X-Unity-Version", "2022.3.44f1");

                var authResponse = await _regionClient.SendAsync(authRequest, ct);
                if (!authResponse.IsSuccessStatusCode)
                {
                    _log.Write($"[LobbyBoard] Lookup — /api/user failed ({authResponse.StatusCode}) for code '{code}'");
                    return null;
                }

                var regionToken = (await authResponse.Content.ReadAsStringAsync(ct)).Trim();

                // 3. GET /api/games/{gameId}
                var gameRequest = new HttpRequestMessage(HttpMethod.Get, $"{regionBaseUrl}/api/games/{gameId}");
                gameRequest.Headers.Add("Authorization", $"Bearer {regionToken}");
                gameRequest.Headers.Add("Accept", "application/json");
                gameRequest.Headers.TryAddWithoutValidation("User-Agent", "UnityPlayer/2022.3.44f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)");
                gameRequest.Headers.TryAddWithoutValidation("X-Unity-Version", "2022.3.44f1");
                if (!string.IsNullOrWhiteSpace(modsHeader))
                    gameRequest.Headers.TryAddWithoutValidation("Client-Mods", modsHeader);

                var gameResponse = await _regionClient.SendAsync(gameRequest, ct);
                if (!gameResponse.IsSuccessStatusCode)
                {
                    _log.Write($"[LobbyBoard] Lookup — /api/games/{gameId} failed ({gameResponse.StatusCode}) for code '{code}'");
                    return null;
                }

                var gameBody = await gameResponse.Content.ReadAsStringAsync(ct);
                using var gameDoc = JsonDocument.Parse(gameBody);
                var root = gameDoc.RootElement;

                int playerCount = 0;
                int maxPlayers = 0;
                string? map = null;

                if (root.TryGetProperty("PlayerCount", out var pc))
                    playerCount = pc.GetInt32();
                if (root.TryGetProperty("MaxPlayers", out var mp))
                    maxPlayers = mp.GetInt32();
                if (root.TryGetProperty("Map", out var mapEl) && mapEl.ValueKind == JsonValueKind.String)
                    map = mapEl.GetString();

                // Update cache
                lock (_lookupCacheLock)
                {
                    _lookupCache[cacheKey] = DateTimeOffset.UtcNow;
                }

                _log.Write($"[LobbyBoard] Lookup OK for '{code}': {playerCount}/{maxPlayers} players");
                return new LobbyLookupResult(playerCount, maxPlayers, map, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] Lookup exception for '{code}': {ex.Message}");

                // Still update cache to prevent rapid retries
                lock (_lookupCacheLock)
                {
                    _lookupCache[cacheKey] = DateTimeOffset.UtcNow;
                }

                return null;
            }
        }
    }
}
