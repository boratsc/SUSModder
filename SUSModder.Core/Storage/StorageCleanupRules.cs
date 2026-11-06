using System.Text.Json;
using SUSModder.Core.GameIntegration;

namespace SUSModder.Core.Storage;

/// <summary>
/// Kwalifikacja archiwum vanilla — dowód, że archiwum jest zbędne względem rozpakowanego cache.
/// Kwalifikacja jest informacją inwentaryzacyjną: w 3.1-08 nie uprawnia do usunięcia.
/// </summary>
public sealed record VanillaArchiveQualification
{
    /// <summary>Kanoniczna ścieżka archiwum.</summary>
    public required string ArchivePath { get; init; }

    /// <summary>Kanoniczna ścieżka rozpakowanego cache.</summary>
    public required string ExtractedPath { get; init; }

    /// <summary>Wersja pamięciowa (nazwa pliku bez rozszerzenia).</summary>
    public required string StorageVersion { get; init; }

    /// <summary>Rozmiar archiwum w chwili kwalifikacji.</summary>
    public long Length { get; init; }

    /// <summary>Czas ostatniej modyfikacji archiwum w chwili kwalifikacji.</summary>
    public DateTimeOffset LastWriteTimeUtc { get; init; }
}

/// <summary>
/// Reguły kwalifikowania plików do sprzątania.
///
/// Zasada 3.1-08: dopuszczamy do usunięcia tylko to, czego bezpieczeństwo da się udowodnić.
/// Ponieważ allowlista nie została zatwierdzona, kwalifikacja służy wyłącznie do opisu
/// inwentaryzacji — nie zmienia uprawnienia kategorii na „do usunięcia”.
/// </summary>
public static class StorageCleanupRules
{
    /// <summary>Stabilne kody powodów — UI lokalizuje po nich.</summary>
    public const string ReasonVanillaArchiveRedundant = "vanilla_archive_redundant";
    public const string ReasonVanillaArchiveCacheNotProven = "vanilla_archive_cache_not_proven";
    public const string ReasonInstallWorkspaceActiveUseUnprovable = "install_workspace_active_use_unprovable";
    public const string ReasonModInstallation = "mod_installation";
    public const string ReasonProtectedData = "protected_data";
    public const string ReasonUnclassifiedData = "unclassified_data";
    public const string ReasonLinkedEntry = "linked_entry_not_followed";
    public const string ReasonSafetyNotProven = "safety_not_proven";

    /// <summary>Nazwa katalogu roboczego instalacji wewnątrz biblioteki modów.</summary>
    public const string InstallWorkspaceDirectoryName = "temp";

    /// <summary>Nazwa katalogu cache vanilla wewnątrz biblioteki modów.</summary>
    public const string VanillaCacheDirectoryName = "Among Us - Vanilla";

    /// <summary>Nazwa katalogu rozpakowanego cache vanilla.</summary>
    public const string VanillaExtractedDirectoryName = "extracted";

    private const string GameDataDirectoryName = "Among Us_Data";

    /// <summary>
    /// Sprawdza, czy archiwum vanilla jest redundantne, tzn. czy istnieje kompletny,
    /// oznaczony markerem rozpakowany cache tej samej wersji.
    ///
    /// Dowód opiera się na trzech elementach:
    /// 1. marker <c>.vanilla-cache.json</c> jest zapisywany dopiero po zakończonym rozpakowaniu,
    /// 2. w rozpakowanym cache są pliki gry (<c>Among Us.exe</c> + <c>Among Us_Data</c>),
    /// 3. archiwum leży bezpośrednio w katalogu cache i nie jest reparse pointem.
    ///
    /// Warunek jest mocniejszy niż w automatycznym sprzątaniu (<see cref="GameIntegration.ModsStorageCleanupService"/>),
    /// bo wymaga obecności markera — stare, rozpakowane bez markera cache nie przechodzi kwalifikacji.
    ///
    /// Kwalifikacja NIE uprawnia do usunięcia w 3.1-08 (allowlista nie została zatwierdzona).
    /// </summary>
    public static bool TryQualifyVanillaArchive(
        string vanillaRoot,
        string archivePath,
        out VanillaArchiveQualification qualification)
    {
        qualification = null!;

        try
        {
            var root = StoragePathNormalizer.TryNormalize(vanillaRoot);
            var archive = StoragePathNormalizer.TryNormalize(archivePath);

            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(archive))
                return false;

            // Archiwum musi leżeć bezpośrednio w katalogu cache vanilla.
            var parent = StoragePathNormalizer.TryNormalize(Path.GetDirectoryName(archive) ?? string.Empty);
            if (string.IsNullOrEmpty(parent) || !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!archive!.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                return false;

            var fileInfo = new FileInfo(archive);
            if (!fileInfo.Exists)
                return false;

            if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;

            var storageVersion = Path.GetFileNameWithoutExtension(archive);
            if (string.IsNullOrWhiteSpace(storageVersion))
                return false;

            var extractedPath = VanillaCacheService.GetExtractedPath(root, storageVersion);
            if (!Directory.Exists(extractedPath))
                return false;

            // Marker = dowód zakończonego rozpakowania.
            if (!TryReadMarkerStorageVersion(extractedPath, out var markerStorageVersion))
                return false;

            if (!string.IsNullOrWhiteSpace(markerStorageVersion)
                && !string.Equals(markerStorageVersion, storageVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Kompletność rozpakowanego cache.
            if (!File.Exists(Path.Combine(extractedPath, AmongUsPathDiscovery.GameExeName)))
                return false;

            if (!Directory.Exists(Path.Combine(extractedPath, GameDataDirectoryName)))
                return false;

            qualification = new VanillaArchiveQualification
            {
                ArchivePath = archive,
                ExtractedPath = StoragePathNormalizer.TryNormalize(extractedPath) ?? extractedPath,
                StorageVersion = storageVersion,
                Length = fileInfo.Length,
                LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
            };

            return true;
        }
        catch
        {
            // Kwalifikacja musi być powściągliwa: brak dowodu = brak zgody na usunięcie.
            return false;
        }
    }

    /// <summary>
    /// Czy nazwa katalogu roboczego odpowiada wzorcowi tworzemu przez instalatory
    /// (GUID w formacie „N”). Sam wzorzec dowodzi pochodzenia, ale NIE dowodzi braku aktywnego użycia.
    /// </summary>
    public static bool LooksLikeOwnedInstallWorkspace(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName) || directoryName.Length != 32)
            return false;

        foreach (var c in directoryName)
        {
            var isHex = c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';
            if (!isHex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Odczytuje wersję pamięciową z markera cache vanilla.
    /// </summary>
    private static bool TryReadMarkerStorageVersion(string extractedPath, out string? storageVersion)
    {
        storageVersion = null;

        try
        {
            var markerPath = Path.Combine(extractedPath, VanillaCacheService.CacheMarkerFileName);
            if (!File.Exists(markerPath))
                return false;

            var json = File.ReadAllText(markerPath);
            var marker = JsonSerializer.Deserialize<VanillaCacheMarker>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (marker is null)
                return false;

            storageVersion = marker.StorageVersion;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class VanillaCacheMarker
    {
        public string AmongVersion { get; set; } = string.Empty;
        public string StorageVersion { get; set; } = string.Empty;
        public string? ManifestId { get; set; }
        public string? BuildId { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset FetchedAt { get; set; }
    }
}
