using SUSModder.Core.Utilities;

namespace SUSModder.Core.GameIntegration;

/// <summary>
/// Usuwa zbędne pliki cache vanilla (7z z gotowym extracted) oraz katalogi <c>temp/</c>
/// z pobranymi, niewypakowanymi archiwami modów — te pliki są małe i zawsze można je pobrać ponownie.
/// </summary>
public static class ModsStorageCleanupService
{
    /// <summary>
    /// Uruchamiane w tle przy starcie/zamknięciu aplikacji — nie blokuje UI.
    /// </summary>
    public static void RunCleanup(Action<string>? log = null)
    {
        try
        {
            var modsInstallPath = PathSettings.ModsInstallPath;
            if (string.IsNullOrWhiteSpace(modsInstallPath) || !Directory.Exists(modsInstallPath))
                return;

            CleanupRedundantVanillaArchives(modsInstallPath, log);
            CleanupStaleTempDirectories(modsInstallPath, log);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Cleanup] Błąd ogólny: {ex.Message}");
        }
    }

    public static void CleanupRedundantVanillaArchives(string modsInstallPath, Action<string>? log = null)
    {
        var vanillaRoot = VanillaCacheService.GetVanillaRoot(modsInstallPath);
        if (!Directory.Exists(vanillaRoot))
            return;

        var cacheService = new VanillaCacheService();
        var removed = 0;

        foreach (var archivePath in Directory.EnumerateFiles(vanillaRoot, "*.7z"))
        {
            var storageVersion = Path.GetFileNameWithoutExtension(archivePath);
            if (string.IsNullOrWhiteSpace(storageVersion))
                continue;

            if (cacheService.TryDeleteArchiveWhenExtractedCacheValid(vanillaRoot, storageVersion, log))
                removed++;
        }

        if (removed > 0)
            log?.Invoke($"[Cleanup] Usunięto archiwów vanilla 7z: {removed}");
    }

    public static void CleanupStaleTempDirectories(string modsInstallPath, Action<string>? log = null)
    {
        var tempRoot = Path.Combine(modsInstallPath, "temp");
        if (!Directory.Exists(tempRoot))
            return;

        var removed = 0;

        foreach (var dir in Directory.EnumerateDirectories(tempRoot))
        {
            if (TryDeleteInstallTempDirectory(dir, log))
                removed++;
        }

        if (removed == 0)
            return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(tempRoot).Any())
                Directory.Delete(tempRoot);
        }
        catch
        {
            // Pusty temp/ lub zablokowany — ignoruj.
        }
    }

    /// <summary>
    /// Usuwa katalog roboczy instalacji (mod.zip / extractMod). Bezpieczne po zakończeniu lub przerwaniu instalacji.
    /// </summary>
    public static bool TryDeleteInstallTempDirectory(string tempDirectory, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(tempDirectory) || !Directory.Exists(tempDirectory))
            return false;

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
            log?.Invoke($"[Cleanup] Usunięto katalog temp: {tempDirectory}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Cleanup] Nie udało się usunąć temp {tempDirectory}: {ex.Message}");
            return false;
        }
    }
}
