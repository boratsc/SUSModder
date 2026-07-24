using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Lobby;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Implementacja ILobbyBoardService.
    /// Komunikuje się z API v2 /lobby oraz bezpośrednio
    /// z serwerami modowanych regionów Among Us dla live lookupu.
    /// </summary>
    public sealed class LobbyBoardService : ILobbyBoardService
    {
        private readonly IDiagnosticsOutput _log;
        private readonly string _userHash;
        private readonly ISUSModderApiClient _apiClient;

        // HttpClient dla region serverów Among Us (nie przez SUSModder API)
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

        public LobbyBoardService(
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IHardwareIdProvider hardwareIdProvider,
            ISUSModderApiClient? apiClient = null)
        {
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            var rawHash = (hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider)))
                .GetAnonymousUserHash();
            _userHash = AnonymousUserHash.EnsureValid(rawHash);
            if (!string.Equals(rawHash, _userHash, StringComparison.Ordinal))
                _log.Write($"[LobbyBoard] userHash znormalizowany (len {rawHash?.Length ?? 0} → {_userHash.Length})");
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? new SUSModderApiClient(configuration, log);
        }

        private static string LobbyPath(string? path = null) =>
            string.IsNullOrWhiteSpace(path) ? "lobby" : $"lobby/{path.TrimStart('/')}";

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

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = LobbyPath(),
                    Content = content,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = DeserializeLobbyPayload<PostLobbyEntryResponse>(body);
                    _log.Write($"[LobbyBoard] POST failed ({response.StatusCode}): {body}");
                    return new PostEntryResult(false, null, null, err?.ErrorCode ?? "UNKNOWN_ERROR", false);
                }

                var result = DeserializeLobbyPayload<PostLobbyEntryResponse>(body);
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
                var query = new Dictionary<string, string?>
                {
                    ["limit"] = Math.Clamp(limit, 1, 50).ToString()
                };
                if (modId.HasValue) query["modId"] = modId.Value.ToString();
                if (type.HasValue && type != LobbyEntryType.All)
                    query["type"] = type.Value.ToString().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(region))
                    query["region"] = region;

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Get,
                    RelativePath = LobbyPath(),
                    Query = query,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct);
                var result = DeserializeLobbyPayload<LobbyBoardResponse>(body);

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
            return await SimpleAuthenticatedRequest(HttpMethod.Delete, LobbyPath(entryId), ct);
        }

        public async Task<bool> ReportEntryAsync(string entryId, string reason, CancellationToken ct = default)
        {
            try
            {
                var payload = new { reason };
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{LobbyPath(entryId)}/report",
                    Content = content,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct);
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

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Patch,
                    RelativePath = LobbyPath(entryId),
                    Content = content,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] PATCH exception: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SimpleAuthenticatedRequest(HttpMethod method, string relativePath, CancellationToken ct)
        {
            try
            {
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = method,
                    RelativePath = relativePath,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBoard] {method} {relativePath} exception: {ex.Message}");
                return false;
            }
        }

        private T? DeserializeLobbyPayload<T>(string body) where T : class
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var element = doc.RootElement;

                if (element.ValueKind == JsonValueKind.Array && typeof(T) == typeof(LobbyBoardResponse))
                {
                    var entries = JsonSerializer.Deserialize<List<LobbyBoardEntry>>(body, _jsonOptions);
                    return (T)(object)new LobbyBoardResponse
                    {
                        Success = true,
                        Entries = entries ?? new List<LobbyBoardEntry>(),
                        Total = entries?.Count ?? 0
                    };
                }

                if (element.TryGetProperty("error", out var errorElement))
                {
                    // Extract error code from backend error envelope (e.g. {"error":{"code":"INTERNAL_ERROR","message":"..."}})
                    if (typeof(T) == typeof(PostLobbyEntryResponse)
                        && errorElement.TryGetProperty("code", out var codeElement))
                    {
                        var errorCode = codeElement.GetString();
                        return (T)(object)new PostLobbyEntryResponse
                        {
                            Success = false,
                            ErrorCode = errorCode
                        };
                    }

                    return null;
                }

                if (element.TryGetProperty("data", out var dataElement))
                {
                    if (dataElement.ValueKind == JsonValueKind.Array && typeof(T) == typeof(LobbyBoardResponse))
                    {
                        var entries = JsonSerializer.Deserialize<List<LobbyBoardEntry>>(dataElement.GetRawText(), _jsonOptions);
                        return (T)(object)new LobbyBoardResponse
                        {
                            Success = true,
                            Entries = entries ?? new List<LobbyBoardEntry>(),
                            Total = entries?.Count ?? 0
                        };
                    }

                    if (dataElement.TryGetProperty("entries", out _))
                        return JsonSerializer.Deserialize<T>(dataElement.GetRawText(), _jsonOptions);

                    return JsonSerializer.Deserialize<T>(dataElement.GetRawText(), _jsonOptions);
                }

                return JsonSerializer.Deserialize<T>(body, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _log.Write($"[LobbyBoard] JSON parse error: {ex.Message}");
                return null;
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
