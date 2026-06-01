using System.Text.Json;
using SUSModder.Core.Models;

namespace SUSModder.Core.GameIntegration;

public sealed class VanillaCacheService
{
    public const string CacheMarkerFileName = ".vanilla-cache.json";

    public static string GetVanillaRoot(string modsInstallPath) =>
        Path.Combine(modsInstallPath, "Among Us - Vanilla");

    public static string GetExtractedPath(string vanillaRoot, string storageVersion) =>
        Path.Combine(vanillaRoot, "extracted", storageVersion);

    public static string GetArchivePath(string vanillaRoot, string storageVersion) =>
        Path.Combine(vanillaRoot, $"{storageVersion}.7z");

    public bool IsValidExtractedCache(string extractedPath, string? expectedManifestId = null)
    {
        if (!Directory.Exists(extractedPath))
            return false;

        var exePath = Path.Combine(extractedPath, "Among Us.exe");
        var dataPath = Path.Combine(extractedPath, "Among Us_Data");
        if (!File.Exists(exePath) || !Directory.Exists(dataPath))
            return false;

        var markerPath = Path.Combine(extractedPath, CacheMarkerFileName);
        if (!File.Exists(markerPath))
            return true;

        try
        {
            var json = File.ReadAllText(markerPath);
            var marker = JsonSerializer.Deserialize<VanillaCacheMarker>(json);
            if (marker is null)
                return false;

            if (!string.IsNullOrWhiteSpace(expectedManifestId)
                && !string.IsNullOrWhiteSpace(marker.ManifestId)
                && !string.Equals(marker.ManifestId, expectedManifestId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void WriteMarker(
        string extractedPath,
        string amongVersion,
        string storageVersion,
        VanillaAcquireSource source,
        string? manifestId = null,
        string? buildId = null)
    {
        Directory.CreateDirectory(extractedPath);
        var marker = new VanillaCacheMarker
        {
            AmongVersion = amongVersion,
            StorageVersion = storageVersion,
            ManifestId = manifestId,
            BuildId = buildId,
            Source = source.ToString(),
            FetchedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(extractedPath, CacheMarkerFileName), json);
    }

    public void CopyExtractedToTarget(string extractedPath, string targetDirectory)
    {
        if (Directory.Exists(targetDirectory))
            Directory.Delete(targetDirectory, recursive: true);

        Directory.CreateDirectory(targetDirectory);
        CopyDirectory(extractedPath, targetDirectory);
    }

    public static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            if (string.Equals(Path.GetFileName(file), CacheMarkerFileName, StringComparison.Ordinal))
                continue;

            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
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
