using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Pełne dane paczki modów z API (GET /api/mod-packs/:packCode).
    /// </summary>
    public sealed class ModPack
    {
        public string PackId { get; set; } = string.Empty;
        public string PackCode { get; set; } = string.Empty;
        public string? CreatorName { get; set; }
        public ModPackFullMod? FullMod { get; set; }
        public string? ModName { get; set; }
        public string? DiscordInvite { get; set; }
        public bool IncludeIntegrationDll { get; set; }
        public int TtlDays { get; set; }
        public string VtStatus { get; set; } = "unknown";
        public JsonElement? Metadata { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public IReadOnlyList<ModPackDllMod> DllMods { get; set; } = Array.Empty<ModPackDllMod>();
        public IReadOnlyList<ModPackExternalDll> ExternalDlls { get; set; } = Array.Empty<ModPackExternalDll>();
        public JsonElement? TouConfig { get; set; }

        public bool HasExternalDlls => ExternalDlls.Count > 0;
        public bool HasSuspiciousExternalDll =>
            ExternalDlls.Any(d => string.Equals(d.VtStatus, "suspicious", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class ModPackFullMod
    {
        public int Id { get; set; }
        public string Version { get; set; } = string.Empty;
    }

    public sealed class ModPackDllMod
    {
        public int DllModId { get; set; }
        public string DllModVersion { get; set; } = string.Empty;
    }

    public sealed class ModPackExternalDll
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string VtStatus { get; set; } = "unknown";
        public string? VtPermalink { get; set; }
        public string? DownloadUrl { get; set; }
    }

    /// <summary>
    /// Wynik tworzenia paczki (POST /api/mod-packs).
    /// </summary>
    public sealed class ModPackCreateResult
    {
        public bool Success { get; set; }
        public string? PackId { get; set; }
        public string? PackCode { get; set; }
        public string? ShareUrl { get; set; }
        public string? DeepLink { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Wpis na liście paczek użytkownika (GET /api/mod-packs?creatorHash=...).
    /// </summary>
    public sealed class ModPackListEntry
    {
        public string PackId { get; set; } = string.Empty;
        public string PackCode { get; set; } = string.Empty;
        public string? ModName { get; set; }
        public int FullModId { get; set; }
        public string FullModVersion { get; set; } = string.Empty;
        public int TtlDays { get; set; }
        public string VtStatus { get; set; } = "unknown";
        public int DllCount { get; set; }
        public int ExternalDllCount { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>
    /// Żądanie utworzenia paczki wysyłane do API.
    /// </summary>
    public sealed class ModPackCreateRequest
    {
        public string CreatorHash { get; set; } = string.Empty;
        public string? CreatorName { get; set; }
        public int FullModId { get; set; }
        public string FullModVersion { get; set; } = "latest";
        public string? ModName { get; set; }
        public string? DiscordInvite { get; set; }
        public bool IncludeIntegrationDll { get; set; }
        public int TtlDays { get; set; } = 30;
        public IReadOnlyList<ModPackDllModRequest> DllMods { get; set; } = Array.Empty<ModPackDllModRequest>();
        public JsonElement? TouConfig { get; set; }

        /// <summary>
        /// Metadane zewnętrznych DLL (lokalne). API v2 nie przyjmuje tego pola w POST /modpacks —
        /// pliki uploaduje się osobno przez POST /modpacks/:code/dlls.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<ModPackExternalDllDeclaration> ExternalDlls { get; set; } =
            Array.Empty<ModPackExternalDllDeclaration>();

        /// <summary>
        /// Ścieżki plików zewnętrznych DLL do uploadu po utworzeniu paczki.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<string> ExternalDllFilePaths { get; set; } = Array.Empty<string>();
    }

    public sealed class ModPackDllModRequest
    {
        [JsonPropertyName("dllModId")]
        public int DllModId { get; set; }

        [JsonPropertyName("dllModVersion")]
        public string DllModVersion { get; set; } = string.Empty;
    }

    public sealed class ModPackExternalDllDeclaration
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("fileSha256")]
        public string FileSha256 { get; set; } = string.Empty;

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }
    }

    /// <summary>
    /// Wynik instalacji paczki po stronie klienta.
    /// </summary>
    public sealed class ModPackInstallResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? InstanceId { get; set; }
        public List<string> InstalledMods { get; } = new();
        public List<string> SkippedMods { get; } = new();
        public List<string> FailedMods { get; } = new();
        public bool IsPartial => Success && (SkippedMods.Count > 0 || FailedMods.Count > 0);
    }

    /// <summary>
    /// Wynik walidacji paczki przed instalacją.
    /// </summary>
    public sealed class ModPackValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public bool RequiresExternalDllConsent { get; set; }
        public bool BlocksExternalDllInstall { get; set; }
    }
}
