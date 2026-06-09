using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public sealed class ModPackService : IModPackService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly IDiagnosticsOutput _log;
        private readonly ISUSModderApiClient _apiClient;

        public ModPackService(
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IHardwareIdProvider hardwareIdProvider,
            ISUSModderApiClient? apiClient = null)
        {
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            CreatorHash = (hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider)))
                .GetAnonymousUserHash();
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? new SUSModderApiClient(configuration, log);
        }

        public string CreatorHash { get; }

        private static string PackPath(string? path = null) =>
            string.IsNullOrWhiteSpace(path) ? "modpacks" : $"modpacks/{path.TrimStart('/')}";

        public async Task<ModPackCreateResult> CreatePackAsync(ModPackCreateRequest request, CancellationToken ct = default)
        {
            try
            {
                request.CreatorHash = CreatorHash;
                var json = ModPackCreateRequestSerializer.ToJson(request);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = PackPath(),
                    Content = content,
                    UserHash = CreatorHash,
                    IncludeAuthToken = true
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = ParseApiError(body);
                    _log.Write($"[ModPack] POST failed ({response.StatusCode}): {body}");
                    return new ModPackCreateResult
                    {
                        Success = false,
                        ErrorCode = err?.Code ?? response.StatusCode.ToString(),
                        ErrorMessage = err?.Message ?? body
                    };
                }

                var result = ParseCreatePackResponse(body);
                if (result == null || string.IsNullOrWhiteSpace(result.PackCode))
                {
                    _log.Write($"[ModPack] POST unexpected response: {body}");
                    return new ModPackCreateResult
                    {
                        Success = false,
                        ErrorCode = "INVALID_RESPONSE",
                        ErrorMessage = "Nieoczekiwana odpowiedź serwera przy tworzeniu zestawu."
                    };
                }

                await UploadPendingExternalDllsAsync(result.PackCode, request.ExternalDllFilePaths, ct);

                return result;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] POST exception: {ex.Message}");
                return new ModPackCreateResult
                {
                    Success = false,
                    ErrorCode = "NETWORK_ERROR",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ModPack?> GetPackAsync(string packCode, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode))
                return null;

            var normalized = ModPackCodeValidator.Normalize(packCode);
            try
            {
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Get,
                    RelativePath = PackPath(normalized)
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == System.Net.HttpStatusCode.Gone ||
                    response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _log.Write($"[ModPack] GET {normalized}: {response.StatusCode}");
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] GET failed ({response.StatusCode}): {body}");
                    return null;
                }

                var pack = TryDeserialize<GetPackApiResponse>(body)?.Pack;
                pack ??= ParsePackFromJson(body);

                if (pack != null && string.IsNullOrEmpty(pack.PackCode))
                    pack.PackCode = normalized;

                if (pack != null)
                    _log.Write($"[ModPack] GET {normalized}: mod={pack.ModName}, fullMod={pack.FullMod?.Id}, dlls={pack.DllMods.Count}");

                return pack;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] GET exception: {ex.Message}");
                return null;
            }
        }

        public async Task<IReadOnlyList<ModPackListEntry>> ListOwnPacksAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Get,
                    RelativePath = PackPath(),
                    Query = new Dictionary<string, string?> { ["creatorHash"] = CreatorHash },
                    UserHash = CreatorHash,
                    IncludeAuthToken = true
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] LIST failed ({response.StatusCode}): {body}");
                    return Array.Empty<ModPackListEntry>();
                }

                var result = TryDeserialize<ListPacksApiResponse>(body);
                return (IReadOnlyList<ModPackListEntry>?)result?.Packs ?? Array.Empty<ModPackListEntry>();
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] LIST exception: {ex.Message}");
                return Array.Empty<ModPackListEntry>();
            }
        }

        public async Task<bool> DeletePackAsync(string packCode, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode))
                return false;

            try
            {
                var normalized = ModPackCodeValidator.Normalize(packCode);
                var payload = JsonSerializer.Serialize(new { creatorHash = CreatorHash }, JsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Delete,
                    RelativePath = PackPath(normalized),
                    Content = content,
                    UserHash = CreatorHash,
                    IncludeAuthToken = true
                }, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] DELETE exception: {ex.Message}");
                return false;
            }
        }

        public async Task<ModPackExternalDll?> UploadExternalDllAsync(
            string packCode, string filePath, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode) || !File.Exists(filePath))
                return null;

            try
            {
                var normalized = ModPackCodeValidator.Normalize(packCode);
                using var form = new MultipartFormDataContent();
                await using var fileStream = File.OpenRead(filePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{PackPath(normalized)}/dlls",
                    Content = form,
                    UserHash = CreatorHash,
                    IncludeAuthToken = true
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] Upload DLL failed ({response.StatusCode}): {body}");
                    return null;
                }

                var result = TryDeserialize<UploadDllApiResponse>(body);
                return result?.DllEntry;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Upload DLL exception: {ex.Message}");
                return null;
            }
        }

        public ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven)
        {
            if (pack.FullMod == null || pack.FullMod.Id <= 0)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "INVALID_FULL_MOD",
                    ErrorMessage = "Brak moda głównego w paczce."
                };
            }

            var configs = ConfigManager.LoadConfig();
            var fullMod = configs.Find(c => c.Id == pack.FullMod.Id);
            if (fullMod == null)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "MOD_NOT_IN_CATALOG",
                    ErrorMessage = "Mod główny nie istnieje w katalogu SUSModder."
                };
            }

            if (!pack.HasExternalDlls)
                return new ModPackValidationResult { IsValid = true };

            if (pack.HasSuspiciousExternalDll)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "DLL_SUSPICIOUS",
                    ErrorMessage = "Zewnętrzne DLL oznaczone jako podejrzane — instalacja zablokowana.",
                    RequiresExternalDllConsent = true,
                    BlocksExternalDllInstall = true
                };
            }

            if (!externalDllConsentGiven)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "CONSENT_REQUIRED",
                    ErrorMessage = "Wymagana zgoda na instalację zewnętrznych DLL.",
                    RequiresExternalDllConsent = true
                };
            }

            return new ModPackValidationResult { IsValid = true, RequiresExternalDllConsent = true };
        }

        /// <summary>
        /// Oblicza SHA256 pliku (deklaracja external DLL przy tworzeniu paczki).
        /// </summary>
        public static string ComputeFileSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task UploadPendingExternalDllsAsync(
            string packCode,
            IReadOnlyList<string> filePaths,
            CancellationToken ct)
        {
            if (filePaths.Count == 0)
                return;

            foreach (var filePath in filePaths)
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _log.Write($"[ModPack] Pominięto upload external DLL — brak pliku: {filePath}");
                    continue;
                }

                var uploaded = await UploadExternalDllAsync(packCode, filePath, ct);
                if (uploaded == null)
                    _log.Write($"[ModPack] Upload external DLL nie powiódł się: {Path.GetFileName(filePath)}");
            }
        }

        private static ModPackCreateResult? ParseCreatePackResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var dataEl))
                    return ParseCreatePackElement(dataEl);

                if (root.TryGetProperty("packCode", out _))
                    return ParseCreatePackElement(root);

                var legacy = TryDeserialize<CreatePackApiResponse>(json);
                if (legacy == null)
                    return null;

                return new ModPackCreateResult
                {
                    Success = legacy.Success,
                    PackId = legacy.PackId,
                    PackCode = legacy.PackCode,
                    ShareUrl = legacy.ShareUrl,
                    DeepLink = legacy.DeepLink,
                    ExpiresAt = legacy.ExpiresAt
                };
            }
            catch
            {
                return null;
            }
        }

        private static ModPackCreateResult ParseCreatePackElement(JsonElement el)
        {
            DateTimeOffset? expiresAt = null;
            if (el.TryGetProperty("expiresAt", out var exp) &&
                exp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(exp.GetString(), out var parsed))
                expiresAt = parsed;

            return new ModPackCreateResult
            {
                Success = true,
                PackId = GetString(el, "packId", "pack_id"),
                PackCode = GetString(el, "packCode", "pack_code"),
                ShareUrl = GetString(el, "shareUrl", "share_url"),
                DeepLink = GetString(el, "deepLink", "deep_link"),
                ExpiresAt = expiresAt
            };
        }

        private static ApiErrorBody? ParseApiError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
                    return JsonSerializer.Deserialize<ApiErrorBody>(errorEl.GetRawText(), JsonOptions);

                var legacy = TryDeserialize<ApiErrorResponse>(json);
                if (legacy == null)
                    return null;

                return new ApiErrorBody
                {
                    Code = legacy.ErrorCode,
                    Message = legacy.DisplayMessage
                };
            }
            catch
            {
                return null;
            }
        }

        private static ModPack? ParsePackFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var dataEl))
                {
                    if (dataEl.TryGetProperty("pack", out var nestedPack))
                        return ParsePackElement(nestedPack);
                    if (dataEl.ValueKind == JsonValueKind.Object && dataEl.TryGetProperty("packCode", out _))
                        return ParsePackElement(dataEl);
                }
                if (root.TryGetProperty("pack", out var packEl))
                    return ParsePackElement(packEl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModPack] ParsePackFromJson: {ex.Message}");
            }

            return null;
        }

        private static ModPack ParsePackElement(JsonElement el)
        {
            var pack = new ModPack
            {
                PackId = GetString(el, "packId", "pack_id") ?? string.Empty,
                PackCode = GetString(el, "packCode", "pack_code") ?? string.Empty,
                CreatorName = GetString(el, "creatorName", "creator_name"),
                ModName = GetString(el, "modName", "mod_name"),
                DiscordInvite = GetString(el, "discordInvite", "discord_invite"),
                IncludeIntegrationDll = GetBool(el, "includeIntegrationDll", "include_integration_dll"),
                TtlDays = GetInt(el, "ttlDays", "ttl_days") is var ttl && ttl > 0 ? ttl : 30,
                VtStatus = GetString(el, "vtStatus", "vt_status") ?? "unknown"
            };

            if (TryGetProperty(el, "fullMod", "full_mod", out var fullModEl))
            {
                pack.FullMod = new ModPackFullMod
                {
                    Id = GetInt(fullModEl, "id"),
                    Version = GetString(fullModEl, "version") ?? "latest"
                };
            }
            else
            {
                var fullModId = GetInt(el, "fullModId", "full_mod_id");
                if (fullModId > 0)
                {
                    pack.FullMod = new ModPackFullMod
                    {
                        Id = fullModId,
                        Version = GetString(el, "fullModVersion", "full_mod_version") ?? "latest"
                    };
                }
            }

            if (TryGetProperty(el, "expiresAt", "expires_at", out var exp) &&
                exp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(exp.GetString(), out var expires))
                pack.ExpiresAt = expires;

            if (TryGetProperty(el, "createdAt", "created_at", out var created) &&
                created.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(created.GetString(), out var createdAt))
                pack.CreatedAt = createdAt;

            if (TryGetProperty(el, "dllMods", "dll_mods", out var dlls) && dlls.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ModPackDllMod>();
                foreach (var item in dlls.EnumerateArray())
                {
                    list.Add(new ModPackDllMod
                    {
                        DllModId = GetInt(item, "dllModId", "dll_mod_id"),
                        DllModVersion = GetString(item, "dllModVersion", "dll_mod_version") ?? "latest"
                    });
                }
                pack.DllMods = list;
            }

            if (TryGetProperty(el, "externalDlls", "external_dlls", out var ext) && ext.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ModPackExternalDll>();
                foreach (var item in ext.EnumerateArray())
                {
                    list.Add(new ModPackExternalDll
                    {
                        Id = GetInt(item, "id"),
                        FileName = GetString(item, "fileName", "file_name") ?? string.Empty,
                        Sha256 = GetString(item, "sha256", "fileSha256", "file_sha256") ?? string.Empty,
                        FileSize = GetLong(item, "fileSize", "file_size"),
                        VtStatus = GetString(item, "vtStatus", "vt_status") ?? "unknown",
                        VtPermalink = GetString(item, "vtPermalink", "vt_permalink"),
                        DownloadUrl = GetString(item, "downloadUrl", "download_url")
                    });
                }
                pack.ExternalDlls = list;
            }

            if (TryGetProperty(el, "touConfig", "tou_config", out var tou))
                pack.TouConfig = tou.Clone();

            return pack;
        }

        private static bool TryGetProperty(JsonElement el, string name1, string name2, out JsonElement value)
        {
            if (el.TryGetProperty(name1, out value)) return true;
            if (el.TryGetProperty(name2, out value)) return true;
            value = default;
            return false;
        }

        private static string? GetString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        private static int GetInt(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var prop))
                    continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n))
                    return n;
            }
            return 0;
        }

        private static long GetLong(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var prop))
                    continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var n))
                    return n;
            }
            return 0;
        }

        private static bool GetBool(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var prop))
                    continue;
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return false;
        }

        private static T? TryDeserialize<T>(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        private sealed class ApiErrorBody
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        private sealed class ApiErrorResponse
        {
            public string? ErrorCode { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            public string? Message { get; set; }

            public string? DisplayMessage => Error ?? Message ?? ErrorCode;
        }

        private sealed class CreatePackApiResponse
        {
            public bool Success { get; set; }
            public string? PackId { get; set; }
            public string? PackCode { get; set; }
            public string? ShareUrl { get; set; }
            public string? DeepLink { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }
        }

        private sealed class GetPackApiResponse
        {
            public bool Success { get; set; }
            public ModPack? Pack { get; set; }
        }

        private sealed class ListPacksApiResponse
        {
            public bool Success { get; set; }
            public List<ModPackListEntry> Packs { get; set; } = new();
        }

        private sealed class UploadDllApiResponse
        {
            public bool Success { get; set; }
            public ModPackExternalDll? DllEntry { get; set; }
        }
    }
}
