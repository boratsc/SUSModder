using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using Microsoft.Extensions.Configuration;

namespace SUSModder.Core.GameIntegration
{
    public class ModManagerUserCallbacks
    {
        public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }
        public Func<string, string, Task>? ShowErrorAsync { get; set; }
        public Func<string, string, Task>? ShowInfoAsync { get; set; }
        public Func<SteamQrDownloadContext, Task<bool>>? RunSteamQrDownloadAsync { get; set; }
    }

    public class ModManager
    {
        private readonly IConfiguration configuration;
        private readonly SteamVanillaProvider _steamVanillaProvider;
        private IDiagnosticsOutput? log;

        public ModManager(IConfiguration configuration)
        {
            this.configuration = configuration;
            _steamVanillaProvider = new SteamVanillaProvider(configuration);
        }

        public async Task<ModInstallResult> ModifyAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            string mode,
            Action<string>? onSpeedUpdate = null)
        {
            this.log = log;

            if (modConfig.ModType != "full")
                return ModInstallResult.Succeeded();

            if (mode == "steam")
            {
                return await InstallSteamAsync(
                    modConfig,
                    modConfigs,
                    progress,
                    log,
                    userCallbacks,
                    onSpeedUpdate,
                    targetInstallPath: null,
                    updateCatalogInstallPath: true,
                    allowReplaceExistingTarget: true);
            }

            const string epicMessage = "Instalacja modów Epic obsługiwana jest przez EpicVersionManager.";
            if (userCallbacks.ShowInfoAsync != null)
                await userCallbacks.ShowInfoAsync(epicMessage, "Informacja");

            return ModInstallResult.Failed(epicMessage);
        }

        /// <summary>
        /// Instaluje moda FULL do konkretnego folderu bez aktualizowania katalogowego InstallPath.
        /// Używane przez lokalne instancje modpacków, gdzie jedna pozycja katalogowa może mieć wiele instalacji.
        /// </summary>
        public Task<ModInstallResult> InstallFullModToPathAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            string mode,
            Action<string>? onSpeedUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(targetInstallPath))
                throw new ArgumentException("Target install path cannot be empty.", nameof(targetInstallPath));

            this.log = log;

            if (modConfig.ModType != "full")
                throw new InvalidOperationException("Only full mods can be installed as local instances.");

            if (mode == "steam")
            {
                return InstallSteamAsync(
                    modConfig,
                    new List<ModConfiguration> { modConfig },
                    progress,
                    log,
                    userCallbacks,
                    onSpeedUpdate,
                    targetInstallPath,
                    updateCatalogInstallPath: false,
                    allowReplaceExistingTarget: false);
            }

            throw new NotSupportedException("Local instance installation currently supports Steam full mods only.");
        }

        private async Task<ModInstallResult> InstallSteamAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null,
            string? targetInstallPath = null,
            bool updateCatalogInstallPath = true,
            bool allowReplaceExistingTarget = true)
        {
            string modsInstallPath = PathSettings.ModsInstallPath;
            Directory.CreateDirectory(modsInstallPath);

            string uniqueTempId = Guid.NewGuid().ToString("N");
            string tempDir = Path.Combine(modsInstallPath, "temp", uniqueTempId);
            string modFolderPath = string.IsNullOrWhiteSpace(targetInstallPath)
                ? Path.Combine(modsInstallPath, modConfig.ModName)
                : targetInstallPath;
            string modFile = Path.Combine(tempDir, "mod.zip");

            // Pobierz moda
            progress.Report(30, "Pobieranie moda...");
            Directory.CreateDirectory(tempDir);

            try
            {
            var downloadResolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "steam");
            log.Write($"[CDN] URL pobierania moda (Steam): {downloadResolution.Url}");

            bool retryMod;
            do
            {
                retryMod = false;
                progress.Report(30, "Pobieranie moda...");

                bool modDownloaded = await DownloadFileWithMemoryManagementAsync(
                    downloadResolution.Url,
                    modFile,
                    progress,
                    $"mod {modConfig.ModName}",
                    onSpeedUpdate,
                    downloadResolution.ExpectedSha256);

                if (!modDownloaded)
                {
                    if (userCallbacks.ConfirmAsync == null)
                        throw new InvalidOperationException("Brak obsługi potwierdzenia (ConfirmAsync) w ModManagerUserCallbacks!");

                    bool retry = await userCallbacks.ConfirmAsync(
                        $"Błąd pobierania moda: {modConfig.ModName}\nCzy chcesz spróbować ponownie?",
                        "Błąd pobierania");

                    if (!retry)
                        return ModInstallResult.Failed("Przerwano instalację moda.");

                    retryMod = retry;
                }
            } while (retryMod);

            // Przygotuj katalog moda
            progress.Report(50, "Przygotowywanie katalogu...");
            if (Directory.Exists(modFolderPath))
            {
                if (!allowReplaceExistingTarget)
                {
                    if (Directory.EnumerateFileSystemEntries(modFolderPath).Any())
                    {
                        throw new IOException($"Target instance directory already exists and is not empty: {modFolderPath}");
                    }
                }
                else
                {
                    log.Write($"Usuwam istniejący katalog moda: {modFolderPath}");
                    try
                    {
                        Directory.Delete(modFolderPath, true);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        log.Write($"[ERROR] Brak dostępu do katalogu: {modFolderPath} - {ex.Message}");
                        return ModInstallResult.Failed(
                            $"Nie można usunąć katalogu:\n{modFolderPath}\n\n" +
                            $"Dostęp został zabroniony. Upewnij się, że:\n" +
                            $"- Gra Among Us NIE jest uruchomiona\n" +
                            $"- Żaden plik z tego folderu nie jest otwarty\n" +
                            $"- Folder nie jest tylko do odczytu\n" +
                            $"- Masz uprawnienia administratora\n\n" +
                            $"Szczegóły: {ex.Message}");
                    }
                }
            }
            Directory.CreateDirectory(modFolderPath);

            // Vanilla: domyślnie paczka 7z; DepotDownloader opcjonalnie (user_settings.prefer_depot_downloader)
            log.Write($"[Vanilla] Wersja Among Us: {modConfig.AmongVersion}");
            var vanillaResult = await _steamVanillaProvider.AcquireAsync(
                modConfig.AmongVersion,
                modFolderPath,
                progress,
                log,
                userCallbacks);

            if (!vanillaResult.Success)
            {
                var vanillaError = vanillaResult.ErrorMessage ?? "Nie udało się pobrać gry vanilla.";
                log.Write($"[Vanilla] Nie udało się przygotować vanilli: {vanillaError}");
                return ModInstallResult.Failed(vanillaError);
            }

            log.Write($"[Vanilla] Źródło: {vanillaResult.Source}");

            // Rozpakuj moda do temp
            progress.Report(80, "Rozpakowywanie moda...");
            string tempExtractPath = Path.Combine(tempDir, "extractMod");

            await SafeDeleteDirectory(tempExtractPath);
            Directory.CreateDirectory(tempExtractPath);

            try
            {
                log.Write($"Rozpakowuję archiwum moda: {modFile} do {tempExtractPath}");

                var extractionProgress = new Progress<ExtractionProgress>(p =>
                {
                    int pct;
                    if (p.PercentComplete.HasValue)
                    {
                        pct = (int)p.PercentComplete.Value;
                    }
                    else if (p.TotalBytes > 0)
                    {
                        pct = (int)(p.BytesExtracted * 100 / p.TotalBytes);
                    }
                    else
                    {
                        progress.Report(85, $"Rozpakowywanie moda... ({FormatSize(p.BytesExtracted)})");
                        return;
                    }

                    int mappedProgress = 80 + (pct * 10 / 100); // 80-90% overall
                    progress.Report(mappedProgress,
                        $"Rozpakowywanie moda: {pct}% ({FormatSize(p.BytesExtracted)}/{FormatSize(p.TotalBytes)})");
                });

                var extractor = new SharpCompressExtractor();
                await extractor.ExtractAsync(modFile, tempExtractPath, progress: extractionProgress);

                log.Write("Pomyślnie rozpakowano archiwum moda");
            }
            catch (Exception ex)
            {
                log.Write($"ERROR podczas rozpakowywania moda: {ex.Message}");
                return ModInstallResult.Failed($"Błąd podczas rozpakowywania archiwum moda: {ex.Message}");
            }

            // Skopiuj pliki moda do katalogu moda
            progress.Report(90, "Kopiowanie plików moda...");
            string sourcePath = Directory.Exists(Path.Combine(tempExtractPath, "BepInEx"))
                ? tempExtractPath
                : Directory.GetDirectories(tempExtractPath).FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(sourcePath))
            {
                log.Write("ERROR: Nie znaleziono plików do skopiowania");
                return ModInstallResult.Failed("Nie znaleziono plików do skopiowania.");
            }

            log.Write($"Kopiuję pliki z {sourcePath} do {modFolderPath}");
            CopyContent(sourcePath, modFolderPath);

            // Zapisz konfigurację i posprzątaj temp
            if (updateCatalogInstallPath)
            {
                var existingConfig = modConfigs.FirstOrDefault(c => c.Id == modConfig.Id);
                if (existingConfig != null)
                {
                    existingConfig.InstallPath = modFolderPath;
                    existingConfig.LastUpdated = DateTime.Now;
                    log.Write($"Zaktualizowano konfigurację dla istniejącego moda: {modConfig.ModName}");
                }
                else
                {
                    modConfig.InstallPath = modFolderPath;
                    modConfigs.Add(modConfig);
                    log.Write($"Dodano nową konfigurację dla moda: {modConfig.ModName}");
                }

                ConfigManager.SaveConfig(modConfigs);
            }

            // === NOWY KOD: Stwórz Installation Map ===
            try
            {
                var installationMap = new InstallationMap
                {
                    InstalledAt = DateTime.Now,
                    InstalledBy = $"SUSModder v{GetAppVersion()}",
                    Platform = "steam",
                    FullMod = new FullModInstallation
                    {
                        ModId = modConfig.Id,
                        ModName = modConfig.ModName,
                        ModVersion = modConfig.ModVersion ?? "unknown",
                        AmongVersion = modConfig.AmongVersion ?? "unknown",
                        InstallPath = modFolderPath,
                        InstalledFrom = modConfig.GitHubRepoOrLink ?? "unknown",
                        LastUpdated = DateTime.Now
                    },
                    InstalledDlls = new List<DllModInstallation>()
                };

                await InstallationMapManager.SaveInstallationMapAsync(modFolderPath, installationMap);
                log.Write($"[InstallationMap] Zapisano mapę instalacji w: {modFolderPath}");
            }
            catch (Exception ex)
            {
                log.Write($"[WARNING] Nie udało się zapisać Installation Map: {ex.Message}");
                // Nie przerywamy instalacji jeśli zapis mapy się nie powiódł
            }
            // === KONIEC NOWEGO KODU ===

            ForceMemoryCleanup();

            progress.Report(100, "Zakończono instalację moda.");
            log.Write($"SUCCESS: Instalacja moda '{modConfig.ModName}' zakończona pomyślnie");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            return ModInstallResult.Succeeded();
            }
            finally
            {
                ModsStorageCleanupService.TryDeleteInstallTempDirectory(tempDir, log.Write);
            }
        }

        public async Task ModifyDllAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> selectedFullMods,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            string mode = "steam")
        {
            string modsInstallPath = PathSettings.ModsInstallPath;
            
            // Unikalna nazwa katalogu temp dla każdej instalacji, aby uniknąć konfliktów przy równoczesnych instalacjach
            string uniqueTempId = Guid.NewGuid().ToString("N");
            string tempDir = Path.Combine(modsInstallPath, "temp", uniqueTempId);
            Directory.CreateDirectory(tempDir);

            string modFile = Path.Combine(tempDir, "mod.dll");
            string downloadUrl = await ModDownloadUrlBuilder.ResolveAsync(modConfig, mode);
            log.Write($"[CDN] URL pobierania DLL moda ({mode}): {downloadUrl}");

            try
            {
            bool retry;
            do
            {
                retry = false;
                try
                {
                    progress.Report(0, "Ściąganie moda DLL...");
                    log.Write($"Rozpoczynam pobieranie DLL moda '{modConfig.ModName}' z: {downloadUrl}");

                    using (var client = new HttpClient())
                    {
                        string downloadToken = SecretProvider.GetDownloadToken();
                        client.DefaultRequestHeaders.Add("Authorization", downloadToken);

                        using var response = await client.GetAsync(downloadUrl);
                        response.EnsureSuccessStatusCode();
                        using var fs = new FileStream(modFile, FileMode.Create, FileAccess.Write);
                        await response.Content.CopyToAsync(fs);
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"[ERROR] Błąd pobierania DLL: {ex}");
                    if (userCallbacks.ConfirmAsync == null)
                        throw new InvalidOperationException("Brak obsługi potwierdzenia (ConfirmAsync) w ModManagerUserCallbacks!");

                    retry = await userCallbacks.ConfirmAsync($"Błąd pobierania DLL moda: {ex.Message}\nCzy chcesz spróbować ponownie?", "Błąd pobierania");
                    if (!retry)
                    {
                        if (userCallbacks.ShowErrorAsync != null)
                            await userCallbacks.ShowErrorAsync("Przerwano instalację DLL moda.", "Błąd");
                        return;
                    }
                }
            } while (retry);

            progress.Report(50, "Kopiowanie DLL do wybranych modów...");
            foreach (var fullMod in selectedFullMods)
            {
                // Sprawdź czy InstallPath nie jest null ani pusty
                if (string.IsNullOrEmpty(fullMod.InstallPath))
                {
                    log.Write($"Pominięto mod '{fullMod.ModName}' - brak ścieżki instalacji");
                    continue;
                }

                var actualModPath = PathSettings.GetActualModPath(fullMod.InstallPath);
                if (!ModPackInstaller.TryResolveSafeDllDirectory(actualModPath, modConfig.DllInstallPath, out var dllTargetDir) ||
                    !ModPackInstaller.TryResolveSafeDllPath(dllTargetDir, $"{modConfig.ModName}.dll", out var dllTargetPath))
                {
                    log.Write($"Pominięto mod '{fullMod.ModName}' - niebezpieczna ścieżka DLL");
                    continue;
                }

                Directory.CreateDirectory(dllTargetDir);
                File.Copy(modFile, dllTargetPath, overwrite: true);
                log.Write($"Skopiowano DLL do: {dllTargetPath}");
            }

            progress.Report(100, "Zakończono instalację DLL moda.");
            log.Write($"SUCCESS: Instalacja DLL moda '{modConfig.ModName}' zakończona pomyślnie");

            if (userCallbacks.ShowInfoAsync != null)
                await userCallbacks.ShowInfoAsync($"Instalacja DLL moda {modConfig.ModName} zakończona pomyślnie.", "Sukces");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            }
            finally
            {
                ModsStorageCleanupService.TryDeleteInstallTempDirectory(tempDir, log.Write);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        private async Task SafeDeleteDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    // Spróbuj usunąć katalog normalnie
                    Directory.Delete(directoryPath, true);
                    log?.Write($"Pomyślnie usunięto katalog: {directoryPath}");
                    return;
                }
                catch (IOException ex) when (currentRetry < maxRetries - 1)
                {
                    log?.Write($"Próba {currentRetry + 1} usunięcia katalogu nieudana: {ex.Message}");
                    currentRetry++;

                    // Spróbuj zwolnić pliki poprzez GC
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // Odczekaj chwilę
                    await Task.Delay(1000);

                    // Spróbuj usunąć pliki jeden po jednym
                    try
                    {
                        await ForceDeleteDirectory(directoryPath);
                        return;
                    }
                    catch (Exception innerEx)
                    {
                        log?.Write($"Nie udało się wymusić usunięcia: {innerEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    log?.Write($"Błąd podczas usuwania katalogu: {ex.Message}");
                    throw;
                }
            }

            throw new IOException($"Nie udało się usunąć katalogu {directoryPath} po {maxRetries} próbach.");
        }

        private async Task ForceDeleteDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            await Task.Run(() =>
            {
                try
                {
                    Directory.Delete(directoryPath, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting directory: {ex.Message}");
                }
            });
        }

        private void CopyContent(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destinationDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destinationDir);
            }
        }

        private void DirectoryCopy(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destinationDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destinationDir);
            }
        }

        private async Task<bool> DownloadFileWithMemoryManagementAsync(
            string url,
            string filePath,
            IProgressReporter progress,
            string fileDescription,
            Action<string>? onSpeedUpdate = null,
            string? expectedSha256 = null)
        {
            const int bufferSize = 8192; // 8KB buffer
            const long maxMemoryThreshold = 50 * 1024 * 1024; // 50MB threshold dla GC
            const long speedUpdateInterval = 512 * 1024; // Aktualizuj prędkość co ~512KB

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(30); // Timeout dla dużych plików

                string downloadToken = SecretProvider.GetDownloadToken();
                client.DefaultRequestHeaders.Add("Authorization", downloadToken);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var headerSha256 = response.Headers.TryGetValues("X-SUSModder-SHA256", out var shaValues)
                    ? shaValues.FirstOrDefault()?.Trim().ToLowerInvariant()
                    : null;
                var expectedHash = !string.IsNullOrWhiteSpace(expectedSha256)
                    ? expectedSha256.Trim().ToLowerInvariant()
                    : headerSha256;

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                var lastGcBytes = 0L;
                var lastSpeedBytes = 0L;
                var stopwatch = Stopwatch.StartNew();

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize))
                {
                    var buffer = new byte[bufferSize];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var percentage = (int)((downloadedBytes * 100) / totalBytes);
                            progress?.Report(percentage, $"Pobieranie {fileDescription}...");
                        }

                        if (onSpeedUpdate != null && downloadedBytes - lastSpeedBytes > speedUpdateInterval)
                        {
                            lastSpeedBytes = downloadedBytes;
                            var speed = CalculateDownloadSpeed(downloadedBytes, stopwatch);
                            if (speed != null)
                                onSpeedUpdate(speed);
                        }

                        if (downloadedBytes - lastGcBytes > maxMemoryThreshold)
                        {
                            lastGcBytes = downloadedBytes;
                            GC.Collect(0, GCCollectionMode.Optimized);
                            GC.WaitForPendingFinalizers();
                        }
                    }

                    await fileStream.FlushAsync();
                }

                if (onSpeedUpdate != null)
                {
                    var finalSpeed = CalculateDownloadSpeed(downloadedBytes, stopwatch);
                    if (finalSpeed != null)
                        onSpeedUpdate(finalSpeed);
                }

                stopwatch.Stop();

                if (!string.IsNullOrWhiteSpace(expectedHash))
                {
                    await using var verifyStream = File.OpenRead(filePath);
                    using var sha = SHA256.Create();
                    var hashBytes = await sha.ComputeHashAsync(verifyStream);
                    var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        log?.Write($"Błąd weryfikacji SHA256 {fileDescription}: oczekiwano {expectedHash}, otrzymano {actualHash}");
                        try { File.Delete(filePath); } catch { }
                        return false;
                    }

                    log?.Write($"SHA256 OK dla {fileDescription}");
                }

                log?.Write($"Pobrano {fileDescription}: {downloadedBytes:N0} bajtów do {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                log?.Write($"Błąd pobierania {fileDescription}: {ex.Message}");

                // Usuń częściowo pobrany plik
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }

                return false;
            }
            finally
            {
                // Wymuś czyszczenie pamięci po zakończeniu
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private void ForceMemoryCleanup()
        {
            // Agresywne czyszczenie pamięci
            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced);

            // Dodatkowe czyszczenie dla .NET
            if (Environment.Version.Major >= 5)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            }
        }

        /// <summary>
        /// Pobiera wersję aplikacji
        /// </summary>
        private static string GetAppVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Oblicza prędkość pobierania na podstawie pobranych bajtów i czasu.
        /// </summary>
        private static string? CalculateDownloadSpeed(long bytesReceived, Stopwatch stopwatch)
        {
            var elapsed = stopwatch.Elapsed;
            if (elapsed.TotalSeconds < 0.5)
                return null;

            double speed = bytesReceived / elapsed.TotalSeconds;

            if (speed > 1_000_000)
                return $"{speed / 1_000_000:F1} MB/s";
            if (speed > 1_000)
                return $"{speed / 1_000:F0} KB/s";
            return $"{speed:F0} B/s";
        }
    }
}
