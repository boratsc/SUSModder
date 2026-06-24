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
        public string Status { get; set; } = "ready";
        public bool? Installable { get; set; }
        public JsonElement? Metadata { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public IReadOnlyList<ModPackDllMod> DllMods { get; set; } = Array.Empty<ModPackDllMod>();
        public IReadOnlyList<ModPackExternalDll> ExternalDlls { get; set; } = Array.Empty<ModPackExternalDll>();
        public IReadOnlyList<ModPackCustomArtifact> CustomArtifacts { get; set; } = Array.Empty<ModPackCustomArtifact>();
        public JsonElement? TouConfig { get; set; }

        /// <summary>Custom full mod z GitHuba zamiast katalogowego FullMod.</summary>
        public ModPackCustomArtifact? CustomFullMod { get; set; }

        public bool HasCustomFullMod => CustomFullMod != null;
        public bool HasExternalDlls => ExternalDlls.Count > 0;
        public bool HasCustomArtifacts => CustomArtifacts.Count > 0;
        public bool HasCustomContent => HasCustomFullMod || HasExternalDlls || HasCustomArtifacts;
        public bool IsBlockedOrNonCleanPack =>
            HasNonCleanCustomArtifact ||
            (Installable == false && HasCustomContent) ||
            string.Equals(Status, "blocked", StringComparison.OrdinalIgnoreCase);
        public bool HasSuspiciousExternalDll =>
            ExternalDlls.Any(d => string.Equals(d.VtStatus, "suspicious", StringComparison.OrdinalIgnoreCase));
        public bool HasNonCleanExternalDll =>
            ExternalDlls.Any(d => !IsCleanStatus(d.VtStatus));
        public bool HasNonCleanCustomArtifact =>
            CustomArtifacts.Any(a => !IsCleanStatus(a.Status));

        private static bool IsCleanStatus(string? status) =>
            string.Equals(status, "clean", StringComparison.OrdinalIgnoreCase);
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
        public string? DllInstallPath { get; set; }
    }

    public sealed class ModPackCustomArtifact
    {
        public string ArtifactId { get; set; } = string.Empty;
        public string SourceKind { get; set; } = "uploaded_dll";
        public string ModType { get; set; } = "dll";
        public string DisplayName { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? OriginalSourceUrl { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Status { get; set; } = "pending";
        public string? VtPermalink { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DllInstallPath { get; set; }
        public IReadOnlyList<string> StructureWarnings { get; set; } = Array.Empty<string>();
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
        public string Status { get; set; } = "ready";
        public bool? Installable { get; set; }
        public IReadOnlyList<ModPackCustomArtifact> CustomArtifacts { get; set; } = Array.Empty<ModPackCustomArtifact>();
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
    /// Rozszerzony wynik listowania paczek użytkownika (GET /api/v2/modpacks?creatorHash=...).
    /// Zawiera listę paczek oraz metadane limitu (activeCount / maxAllowed) zwracane przez API.
    /// </summary>
    public sealed class ModPackListResult
    {
        public bool Success { get; set; }
        public IReadOnlyList<ModPackListEntry> Packs { get; set; } = Array.Empty<ModPackListEntry>();
        public int ActiveCount { get; set; }
        public int MaxAllowed { get; set; } = 10;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int StatusCode { get; set; }
    }

    /// <summary>
    /// Wynik operacji usunięcia paczki (DELETE /api/v2/modpacks/:packCode).
    /// Pozwala UI odróżnić brak paczki, brak autoryzacji i błąd sieci.
    /// </summary>
    public sealed class ModPackDeleteResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int StatusCode { get; set; }
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

    public sealed class ModPackCustomGithubModRequest
    {
        [JsonPropertyName("creatorHash")]
        public string CreatorHash { get; set; } = string.Empty;

        [JsonPropertyName("sourceKind")]
        public string SourceKind { get; set; } = "github_dll";

        [JsonPropertyName("modType")]
        public string ModType { get; set; } = "dll";

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("githubUrl")]
        public string GithubUrl { get; set; } = string.Empty;

        [JsonPropertyName("dllInstallPath")]
        public string? DllInstallPath { get; set; }
    }

    public sealed class ModPackArtifactStatusResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string Status { get; set; } = "unknown";
        public bool DownloadAvailable { get; set; }
        public ModPackExternalDll? DllEntry { get; set; }
        public ModPackCustomArtifact? CustomArtifact { get; set; }
    }

    public sealed class ModPackFinalizeResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string Status { get; set; } = "unknown";
        public bool Installable { get; set; }
        public string? ShareUrl { get; set; }
        public string? DeepLink { get; set; }
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
