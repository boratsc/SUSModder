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
            var rawHash = (hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider)))
                .GetAnonymousUserHash();
            // Obrona przed historycznym fallbackiem GUID "N" (32 hex) i innymi niepoprawnymi wartościami.
            CreatorHash = AnonymousUserHash.EnsureValid(rawHash);
            if (!string.Equals(rawHash, CreatorHash, StringComparison.Ordinal))
                _log.Write($"[ModPack] creatorHash znormalizowany (len {rawHash?.Length ?? 0} → {CreatorHash.Length})");
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
            var detailed = await ListOwnPacksDetailedInternalAsync(ct);
            return detailed.Packs;
        }

        public async Task<bool> DeletePackAsync(string packCode, CancellationToken ct = default)
        {
            var result = await DeletePackDetailedAsync(packCode, ct);
            return result.Success;
        }

        public async Task<ModPackDeleteResult> DeletePackDetailedAsync(string packCode, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode))
            {
                return new ModPackDeleteResult
                {
                    Success = false,
                    ErrorCode = "INVALID_PACK_CODE",
                    ErrorMessage = "Nieprawidłowy kod paczki."
                };
            }

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
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] DELETE {normalized}: ok");
                    return new ModPackDeleteResult
                    {
                        Success = true,
                        StatusCode = (int)response.StatusCode
                    };
                }

                var err = ParseApiError(body);
                _log.Write($"[ModPack] DELETE {normalized} failed ({response.StatusCode}): {body}");
                return new ModPackDeleteResult
                {
                    Success = false,
                    ErrorCode = err?.Code ?? ((int)response.StatusCode).ToString(),
                    ErrorMessage = err?.Message,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] DELETE exception: {ex.Message}");
                return new ModPackDeleteResult
                {
                    Success = false,
                    ErrorCode = "NETWORK_ERROR",
                    ErrorMessage = ex.Message
                };
            }
        }

        public Task<ModPackListResult> ListOwnPacksDetailedAsync(CancellationToken ct = default)
        {
            // Domyślnie używamy tego samego endpointu co ListOwnPacksAsync (GET /modpacks?creatorHash=...),
            // ale zwracamy rozszerzony wynik z activeCount/maxAllowed, jeśli API je zwróci.
            return ListOwnPacksDetailedInternalAsync(ct);
        }

        private async Task<ModPackListResult> ListOwnPacksDetailedInternalAsync(CancellationToken ct)
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
                    _log.Write($"[ModPack] LIST detailed failed ({response.StatusCode}): {body}");
                    var err = ParseApiError(body);
                    return new ModPackListResult
                    {
                        Success = false,
                        StatusCode = (int)response.StatusCode,
                        ErrorCode = err?.Code ?? ((int)response.StatusCode).ToString(),
                        ErrorMessage = err?.Message ?? body,
                        Packs = Array.Empty<ModPackListEntry>()
                    };
                }

                return ParseListPacksResponse(body, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] LIST detailed exception: {ex.Message}");
                return new ModPackListResult
                {
                    Success = false,
                    ErrorCode = "NETWORK_ERROR",
                    ErrorMessage = ex.Message,
                    Packs = Array.Empty<ModPackListEntry>()
                };
            }
        }

        private ModPackListResult ParseListPacksResponse(string body, int statusCode)
        {
            var result = new ModPackListResult { StatusCode = statusCode };

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                JsonElement listContainer = root;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("data", out var dataEl) &&
                    dataEl.ValueKind == JsonValueKind.Object)
                {
                    listContainer = dataEl;
                }

                if (listContainer.TryGetProperty("packs", out var packsEl) &&
                    packsEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<ModPackListEntry>();
                    foreach (var el in packsEl.EnumerateArray())
                    {
                        var entry = ParseListEntryElement(el);
                        if (entry != null)
                            list.Add(entry);
                    }
                    result.Packs = list;
                }

                if (listContainer.TryGetProperty("activeCount", out var ac) &&
                    ac.ValueKind == JsonValueKind.Number && ac.TryGetInt32(out var active))
                {
                    result.ActiveCount = active;
                }

                if (listContainer.TryGetProperty("maxAllowed", out var mx) &&
                    mx.ValueKind == JsonValueKind.Number && mx.TryGetInt32(out var max))
                {
                    result.MaxAllowed = max;
                }

                result.Success = true;

                // Fallback: jeśli API nie zwróciło activeCount, wylicz z listy.
                if (result.ActiveCount == 0)
                    result.ActiveCount = result.Packs.Count(p => p.Active);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] LIST parse exception: {ex.Message}");
                result.Success = false;
                result.ErrorCode = "INVALID_RESPONSE";
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static ModPackListEntry? ParseListEntryElement(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object)
                return null;

            DateTimeOffset? createdAt = null;
            DateTimeOffset? expiresAt = null;

            if (el.TryGetProperty("createdAt", out var ca) && ca.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(ca.GetString(), out var parsedCreated))
            {
                createdAt = parsedCreated;
            }
            if (el.TryGetProperty("expiresAt", out var ea) && ea.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(ea.GetString(), out var parsedExpires))
            {
                expiresAt = parsedExpires;
            }

            return new ModPackListEntry
            {
                PackId = GetString(el, "id", "packId", "pack_id") ?? string.Empty,
                PackCode = GetString(el, "packCode", "pack_code") ?? string.Empty,
                ModName = GetString(el, "modName", "mod_name"),
                FullModId = GetInt(el, "fullModId", "full_mod_id"),
                FullModVersion = GetString(el, "fullModVersion", "full_mod_version") ?? string.Empty,
                TtlDays = GetInt(el, "ttlDays", "ttl_days"),
                VtStatus = GetString(el, "vtStatus", "vt_status") ?? "unknown",
                DllCount = GetInt(el, "dllCount", "dll_count"),
                ExternalDllCount = GetInt(el, "externalDllCount", "external_dll_count"),
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
                Active = GetBool(el, "active") || GetBool(el, "is_active")
            };
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
                form.Add(new StringContent(CreatorHash), "creatorHash");

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{PackPath(normalized)}/dlls",
                    Content = form,
                    UserHash = CreatorHash,
                    IncludeAuthToken = false
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] Upload DLL failed ({response.StatusCode}): {body}");
                    return null;
                }

                return ParseUploadExternalDllResponse(body);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Upload DLL exception: {ex.Message}");
                return null;
            }
        }

        public async Task<ModPackCustomArtifact?> UploadCustomDllAsync(
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
                form.Add(new StringContent(CreatorHash), "creatorHash");

                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{PackPath(normalized)}/dlls",
                    Content = form,
                    UserHash = CreatorHash,
                    IncludeAuthToken = false
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] Upload custom DLL failed ({response.StatusCode}): {body}");
                    return null;
                }

                return ParseUploadCustomDllResponse(body);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Upload custom DLL exception: {ex.Message}");
                return null;
            }
        }

        public async Task<ModPackArtifactStatusResult> GetExternalDllStatusAsync(
            string packCode, string sha256, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode) || string.IsNullOrWhiteSpace(sha256))
                return StatusError("INVALID_REQUEST", "Invalid pack code or SHA256.");

            var normalized = ModPackCodeValidator.Normalize(packCode);
            return await GetArtifactStatusAsync($"{PackPath(normalized)}/dlls/{Uri.EscapeDataString(sha256)}/status", ct);
        }

        public async Task<ModPackCustomArtifact?> DeclareGitHubCustomModAsync(
            string packCode, ModPackCustomGithubModRequest request, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode) || request == null || string.IsNullOrWhiteSpace(request.GithubUrl))
                return null;

            try
            {
                var normalized = ModPackCodeValidator.Normalize(packCode);
                request.CreatorHash = CreatorHash;
                var payload = JsonSerializer.Serialize(request, JsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{PackPath(normalized)}/custom-github-mods",
                    Content = content,
                    UserHash = CreatorHash,
                    IncludeAuthToken = false
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPack] Declare GitHub custom mod failed ({response.StatusCode}): {body}");
                    return null;
                }

                return ParseCustomArtifactResponse(body);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Declare GitHub custom mod exception: {ex.Message}");
                return null;
            }
        }

        public async Task<ModPackArtifactStatusResult> GetCustomArtifactStatusAsync(
            string packCode, string artifactId, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode) || string.IsNullOrWhiteSpace(artifactId))
                return StatusError("INVALID_REQUEST", "Invalid pack code or artifact id.");

            var normalized = ModPackCodeValidator.Normalize(packCode);
            return await GetArtifactStatusAsync(
                $"{PackPath(normalized)}/custom-artifacts/{Uri.EscapeDataString(artifactId)}/status",
                ct);
        }

        public async Task<ModPackFinalizeResult> FinalizePackAsync(string packCode, CancellationToken ct = default)
        {
            if (!ModPackCodeValidator.IsValid(packCode))
                return new ModPackFinalizeResult { Success = false, ErrorCode = "INVALID_PACK_CODE" };

            try
            {
                var normalized = ModPackCodeValidator.Normalize(packCode);
                var finalizeBody = JsonSerializer.Serialize(new { creatorHash = CreatorHash }, JsonOptions);
                using var content = new StringContent(finalizeBody, Encoding.UTF8, "application/json");
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{PackPath(normalized)}/finalize",
                    Content = content,
                    UserHash = CreatorHash,
                    IncludeAuthToken = false
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = ParseApiError(body);
                    return new ModPackFinalizeResult
                    {
                        Success = false,
                        ErrorCode = err?.Code ?? response.StatusCode.ToString(),
                        ErrorMessage = err?.Message ?? body
                    };
                }

                return ParseFinalizeResponse(body);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Finalize exception: {ex.Message}");
                return new ModPackFinalizeResult
                {
                    Success = false,
                    ErrorCode = "NETWORK_ERROR",
                    ErrorMessage = ex.Message
                };
            }
        }

        public ModPackValidationResult ValidatePack(ModPack pack, bool externalDllConsentGiven)
        {
            var isCustomFull = pack.HasCustomFullMod;

            if (!isCustomFull && (pack.FullMod == null || pack.FullMod.Id <= 0))
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "INVALID_FULL_MOD",
                    ErrorMessage = "Brak moda głównego w paczce."
                };
            }

            if (!isCustomFull)
            {
                var configs = ConfigManager.LoadConfig();
                var fullMod = configs.Find(c => c.Id == pack.FullMod!.Id);
                if (fullMod == null)
                {
                    return new ModPackValidationResult
                    {
                        IsValid = false,
                        ErrorCode = "MOD_NOT_IN_CATALOG",
                        ErrorMessage = "Mod główny nie istnieje w katalogu SUSModder."
                    };
                }
            }

            if (pack.Installable == false ||
                (isCustomFull && string.Equals(pack.Status, "draft", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(pack.Status, "scanning", StringComparison.OrdinalIgnoreCase) ||
                (isCustomFull && string.Equals(pack.CustomFullMod!.Status, "pending", StringComparison.OrdinalIgnoreCase)))
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "CUSTOM_CONTENT_PENDING_SCAN",
                    ErrorMessage = "Custom content is still being scanned.",
                    RequiresExternalDllConsent = pack.HasCustomContent,
                    BlocksExternalDllInstall = pack.HasCustomContent
                };
            }

            if (string.Equals(pack.Status, "blocked", StringComparison.OrdinalIgnoreCase) ||
                pack.HasNonCleanCustomArtifact)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = "CUSTOM_CONTENT_REJECTED",
                    ErrorMessage = "Custom content is not clean — installation blocked.",
                    RequiresExternalDllConsent = pack.HasCustomContent,
                    BlocksExternalDllInstall = true
                };
            }

            if (!pack.HasCustomContent)
                return new ModPackValidationResult { IsValid = true };

            if (pack.HasSuspiciousExternalDll || pack.HasNonCleanExternalDll)
            {
                return new ModPackValidationResult
                {
                    IsValid = false,
                    ErrorCode = pack.HasSuspiciousExternalDll ? "CUSTOM_CONTENT_SUSPICIOUS" : "CUSTOM_CONTENT_PENDING_SCAN",
                    ErrorMessage = "External DLL is not clean — installation blocked.",
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
                    _log.Write($"[ModPack] Pominięto upload external DLL — brak pliku: {Path.GetFileName(filePath)}");
                    continue;
                }

                var uploaded = await UploadExternalDllAsync(packCode, filePath, ct);
                if (uploaded == null)
                    _log.Write($"[ModPack] Upload external DLL nie powiódł się: {Path.GetFileName(filePath)}");
            }
        }

        private async Task<ModPackArtifactStatusResult> GetArtifactStatusAsync(string relativePath, CancellationToken ct)
        {
            try
            {
                var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Get,
                    RelativePath = relativePath,
                    IncludeAuthToken = false
                }, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = ParseApiError(body);
                    return StatusError(err?.Code ?? response.StatusCode.ToString(), err?.Message ?? body);
                }

                return ParseArtifactStatusResponse(body);
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPack] Artifact status exception: {ex.Message}");
                return StatusError("NETWORK_ERROR", ex.Message);
            }
        }

        private static ModPackArtifactStatusResult StatusError(string code, string? message = null) => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            Status = "unknown"
        };

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
                    ExpiresAt = expiresAt,
                    Status = GetString(el, "status") ?? "ready",
                    Installable = GetNullableBool(el, "installable"),
                    CustomArtifacts = ParseCustomArtifacts(el)
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
                VtStatus = GetString(el, "vtStatus", "vt_status") ?? "unknown",
                Status = GetString(el, "status") ?? "ready",
                Installable = GetNullableBool(el, "installable")
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
                    list.Add(ParseExternalDll(item));
                }
                pack.ExternalDlls = list;
            }

            pack.CustomArtifacts = ParseCustomArtifacts(el);
            pack.CustomFullMod = FindCustomFullArtifact(pack.CustomArtifacts);

            if (el.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                pack.Metadata = metadata.Clone();

            if (TryGetProperty(el, "touConfig", "tou_config", out var tou))
                pack.TouConfig = tou.Clone();

            // Fallback: amongVersion z metadata paczki, gdy artefakt nie zwraca pola (stary backend).
            if (pack.CustomFullMod != null &&
                string.IsNullOrWhiteSpace(pack.CustomFullMod.AmongVersion) &&
                TryReadAmongVersionFromMetadata(pack.Metadata, out var metaAmong))
            {
                pack.CustomFullMod.AmongVersion = metaAmong;
            }

            return pack;
        }

        private static ModPackExternalDll ParseExternalDll(JsonElement item) => new()
        {
            Id = GetInt(item, "id"),
            FileName = GetString(item, "fileName", "file_name") ?? string.Empty,
            Sha256 = GetString(item, "sha256", "fileSha256", "file_sha256") ?? string.Empty,
            FileSize = GetLong(item, "fileSize", "file_size"),
            VtStatus = GetString(item, "vtStatus", "vt_status", "status") ?? "unknown",
            VtPermalink = GetString(item, "vtPermalink", "vt_permalink"),
            DownloadUrl = GetString(item, "downloadUrl", "download_url"),
            DllInstallPath = GetString(item, "dllInstallPath", "dll_install_path")
        };

        private static ModPackCustomArtifact ParseCustomArtifact(JsonElement item)
        {
            var artifact = new ModPackCustomArtifact
            {
                ArtifactId = GetString(item, "artifactId", "artifact_id", "id") ?? string.Empty,
                SourceKind = GetString(item, "sourceKind", "source_kind") ?? "uploaded_dll",
                ModType = GetString(item, "modType", "mod_type") ?? "dll",
                DisplayName = GetString(item, "displayName", "display_name", "fileName", "file_name") ?? string.Empty,
                Version = GetString(item, "version"),
                AmongVersion = GetString(item, "amongVersion", "among_version"),
                OriginalSourceUrl = GetString(item, "originalSourceUrl", "original_source_url", "githubUrl", "github_url"),
                FileName = GetString(item, "fileName", "file_name") ?? string.Empty,
                Sha256 = GetString(item, "sha256", "fileSha256", "file_sha256") ?? string.Empty,
                FileSize = GetLong(item, "fileSize", "file_size"),
                Status = GetString(item, "status", "vtStatus", "vt_status") ?? "pending",
                VtPermalink = GetString(item, "vtPermalink", "vt_permalink"),
                DownloadUrl = GetString(item, "downloadUrl", "download_url"),
                DllInstallPath = GetString(item, "dllInstallPath", "dll_install_path")
            };

            if (TryGetProperty(item, "structureWarnings", "structure_warnings", out var warnings) &&
                warnings.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var warning in warnings.EnumerateArray())
                {
                    if (warning.ValueKind == JsonValueKind.String && warning.GetString() is { } text)
                        list.Add(text);
                }
                artifact.StructureWarnings = list;
            }

            return artifact;
        }

        private static IReadOnlyList<ModPackCustomArtifact> ParseCustomArtifacts(JsonElement el)
        {
            if (!TryGetProperty(el, "customArtifacts", "custom_artifacts", out var artifacts) ||
                artifacts.ValueKind != JsonValueKind.Array)
                return Array.Empty<ModPackCustomArtifact>();

            var list = new List<ModPackCustomArtifact>();
            foreach (var item in artifacts.EnumerateArray())
                list.Add(ParseCustomArtifact(item));
            return list;
        }

        private static ModPackCustomArtifact? FindCustomFullArtifact(IReadOnlyList<ModPackCustomArtifact> artifacts)
        {
            foreach (var artifact in artifacts)
            {
                if (string.Equals(artifact.ModType, "full", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(artifact.SourceKind, "github_full", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(artifact.SourceKind, "uploaded_full", StringComparison.OrdinalIgnoreCase)))
                {
                    return artifact;
                }
            }

            return null;
        }

        private static ModPackCustomArtifact? ParseUploadCustomDllResponse(string json)
        {
            if (!TryUnwrapData(json, out var el))
                return null;

            if (TryGetProperty(el, "customArtifact", "custom_artifact", out var artifactEl))
                return ParseCustomArtifact(artifactEl);

            if (TryGetProperty(el, "dllEntry", "dll_entry", out var dllEl))
            {
                var dll = ParseExternalDll(dllEl);
                return ExternalDllToCustomArtifact(dll);
            }

            if (el.ValueKind == JsonValueKind.Object &&
                (el.TryGetProperty("sha256", out _) || el.TryGetProperty("fileName", out _) || el.TryGetProperty("file_name", out _)))
                return ParseCustomArtifact(el);

            return null;
        }

        private static ModPackExternalDll? ParseUploadExternalDllResponse(string json)
        {
            var legacy = TryDeserialize<UploadDllApiResponse>(json);
            if (legacy?.DllEntry != null)
                return legacy.DllEntry;

            if (!TryUnwrapData(json, out var el))
                return null;

            if (TryGetProperty(el, "dllEntry", "dll_entry", out var dllEl))
                return ParseExternalDll(dllEl);

            if (TryGetProperty(el, "customArtifact", "custom_artifact", out var artifactEl))
                return CustomArtifactToExternalDll(ParseCustomArtifact(artifactEl));

            if (el.ValueKind == JsonValueKind.Object &&
                (el.TryGetProperty("sha256", out _) || el.TryGetProperty("fileName", out _) || el.TryGetProperty("file_name", out _)))
                return ParseExternalDll(el);

            return null;
        }

        private static ModPackCustomArtifact? ParseCustomArtifactResponse(string json)
        {
            if (!TryUnwrapData(json, out var el))
                return null;

            if (TryGetProperty(el, "customArtifact", "custom_artifact", out var artifactEl))
                return ParseCustomArtifact(artifactEl);

            if (el.ValueKind == JsonValueKind.Object &&
                (el.TryGetProperty("artifactId", out _) || el.TryGetProperty("artifact_id", out _) || el.TryGetProperty("id", out _)))
                return ParseCustomArtifact(el);

            return null;
        }

        private static ModPackArtifactStatusResult ParseArtifactStatusResponse(string json)
        {
            if (!TryUnwrapData(json, out var el))
                return StatusError("INVALID_RESPONSE", "Invalid artifact status response.");

            ModPackExternalDll? dllEntry = null;
            ModPackCustomArtifact? customArtifact = null;

            if (TryGetProperty(el, "dllEntry", "dll_entry", out var dllEl))
                dllEntry = ParseExternalDll(dllEl);

            if (TryGetProperty(el, "customArtifact", "custom_artifact", out var artifactEl))
                customArtifact = ParseCustomArtifact(artifactEl);
            else if (el.ValueKind == JsonValueKind.Object &&
                (el.TryGetProperty("artifactId", out _) || el.TryGetProperty("artifact_id", out _)))
                customArtifact = ParseCustomArtifact(el);

            var status = GetString(el, "status", "vtStatus", "vt_status")
                ?? customArtifact?.Status
                ?? dllEntry?.VtStatus
                ?? "unknown";

            return new ModPackArtifactStatusResult
            {
                Success = true,
                Status = status,
                DownloadAvailable = GetBool(el, "downloadAvailable", "download_available") ||
                    string.Equals(status, "clean", StringComparison.OrdinalIgnoreCase),
                DllEntry = dllEntry,
                CustomArtifact = customArtifact
            };
        }

        private static ModPackFinalizeResult ParseFinalizeResponse(string json)
        {
            if (!TryUnwrapData(json, out var el))
                return new ModPackFinalizeResult { Success = false, ErrorCode = "INVALID_RESPONSE" };

            return new ModPackFinalizeResult
            {
                Success = true,
                Status = GetString(el, "status") ?? "unknown",
                Installable = GetBool(el, "installable"),
                ShareUrl = GetString(el, "shareUrl", "share_url"),
                DeepLink = GetString(el, "deepLink", "deep_link")
            };
        }

        private static ModPackCustomArtifact ExternalDllToCustomArtifact(ModPackExternalDll dll) => new()
        {
            ArtifactId = string.IsNullOrWhiteSpace(dll.Sha256) ? dll.Id.ToString() : dll.Sha256,
            SourceKind = "uploaded_dll",
            ModType = "dll",
            DisplayName = dll.FileName,
            FileName = dll.FileName,
            Sha256 = dll.Sha256,
            FileSize = dll.FileSize,
            Status = dll.VtStatus,
            VtPermalink = dll.VtPermalink,
            DownloadUrl = dll.DownloadUrl,
            DllInstallPath = "BepInEx/plugins"
        };

        private static ModPackExternalDll CustomArtifactToExternalDll(ModPackCustomArtifact artifact) => new()
        {
            FileName = artifact.FileName,
            Sha256 = artifact.Sha256,
            FileSize = artifact.FileSize,
            VtStatus = artifact.Status,
            VtPermalink = artifact.VtPermalink,
            DownloadUrl = artifact.DownloadUrl,
            DllInstallPath = artifact.DllInstallPath
        };

        private static bool TryUnwrapData(string json, out JsonElement element)
        {
            element = default;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var dataEl))
                    element = dataEl.Clone();
                else
                    element = root.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetProperty(JsonElement el, string name1, string name2, out JsonElement value)
        {
            if (el.TryGetProperty(name1, out value)) return true;
            if (el.TryGetProperty(name2, out value)) return true;
            value = default;
            return false;
        }

        internal static bool TryReadAmongVersionFromMetadata(JsonElement? metadata, out string amongVersion)
        {
            amongVersion = string.Empty;
            if (metadata is not { ValueKind: JsonValueKind.Object } meta)
                return false;

            var value = GetString(meta, "amongVersion", "among_version");
            if (string.IsNullOrWhiteSpace(value))
                return false;

            amongVersion = value.Trim();
            return true;
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

        private static bool? GetNullableBool(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var prop))
                    continue;
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;
            }
            return null;
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
