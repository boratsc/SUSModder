using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Updater
{
    public class Updater
    {
        private readonly IConfiguration configuration;
        private readonly string currentVersion;

        public Updater(string currentVersion, IConfiguration configuration)
        {
            this.currentVersion = currentVersion ?? "0.0.0";
            this.configuration = configuration;
        }

        public async Task CheckAndPromptForUpdateAsync(
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress = null)
        {
            try
            {
                string latestVersion = await GetLatestVersionAsync(log);
                bool needsUpdaterUpdate = NeedsUpdaterUpdate();
                bool needsCleanup = NeedsCleanup();

                if (string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    bool doUpdate = userInteraction.Confirm(
                        $"Dostępna jest nowa wersja aplikacji.\nObecna wersja: {currentVersion}\nNowa wersja: {latestVersion}\nCzy chcesz zaktualizować?",
                        "Aktualizacja aplikacji");

                    if (doUpdate)
                    {
                        await DownloadAndRunUpdaterAsync(latestVersion, log, userInteraction, progress);
                    }
                }
                else if (needsUpdaterUpdate || needsCleanup)
                {
                    await UpdateUpdaterIfNeededAsync(log);
                    CleanupOldFrameworkFilesIfNeeded(log);
                    log.Write("Wykonano niezbędne czynności porządkowe i/lub aktualizację updatera. Możesz teraz bezpiecznie korzystać z aplikacji.");
                }
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas sprawdzania wersji: {ex.Message}");
                userInteraction.ShowError($"Błąd podczas sprawdzania wersji: {ex.Message}", "Błąd");
            }
        }

        private async Task<string> GetLatestVersionAsync(IDiagnosticsOutput log)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync("https://susfuckr.boracik.pl/api/susfuckr-current-version");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var versionInfo = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                string version = versionInfo?["version"] ?? "0.0.0";
                log.Write($"[Updater] Najnowsza wersja: {version}");
                return version;
            }
        }

        private bool NeedsUpdaterUpdate()
        {
            string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDirPath)) return false;

            string updaterDir = Path.Combine(appDirPath, "updater");
            string depsPath = Path.Combine(updaterDir, "Updater.deps.json");
            return File.Exists(depsPath);
        }

        private bool NeedsCleanup()
        {
            string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDirPath)) return false;

            string runtimeConfigPath = Path.Combine(appDirPath, "SUSModder.runtimeconfig.json");
            return File.Exists(runtimeConfigPath);
        }

        private async Task UpdateUpdaterIfNeededAsync(IDiagnosticsOutput log)
        {
            string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDirPath)) return;

            string updaterDir = Path.Combine(appDirPath, "updater");
            string depsPath = Path.Combine(updaterDir, "Updater.deps.json");

            if (File.Exists(depsPath))
            {
                string updaterExeUrl = "https://susfuckr.boracik.pl/susfuckr/updater/Updater.exe";
                string tempUpdaterPath = Path.Combine(Path.GetTempPath(), "Updater.exe");

                using (HttpClient client = new HttpClient())
                {
                    log.Write("Wykryto stary updater. Pobieranie nowego Updater.exe...");
                    HttpResponseMessage response = await client.GetAsync(updaterExeUrl);
                    response.EnsureSuccessStatusCode();
                    using (FileStream fs = new FileStream(tempUpdaterPath, FileMode.Create, FileAccess.Write))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                foreach (var file in Directory.GetFiles(updaterDir))
                {
                    try { File.Delete(file); } catch { }
                }

                string newUpdaterPath = Path.Combine(updaterDir, "updater.exe");
                File.Copy(tempUpdaterPath, newUpdaterPath, true);
                File.Delete(tempUpdaterPath);

                log.Write("Updater został zaktualizowany.");
            }
        }

        private void CleanupOldFrameworkFilesIfNeeded(IDiagnosticsOutput log)
        {
            string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDirPath)) return;

            string runtimeConfigPath = Path.Combine(appDirPath, "SUSModder.runtimeconfig.json");

            if (File.Exists(runtimeConfigPath))
            {
                log.Write("Wykryto plik SUSModder.runtimeconfig.json – sprzątanie starych plików...");

                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "SUSModder.exe",
                    "appsettings.json",
                    "config.json"
                };

                foreach (var file in Directory.GetFiles(appDirPath))
                {
                    string fileName = Path.GetFileName(file);
                    if (!allowed.Contains(fileName))
                    {
                        try
                        {
                            File.Delete(file);
                            log.Write($"Usunięto plik: {fileName}");
                        }
                        catch (Exception ex)
                        {
                            log.Write($"Nie udało się usunąć pliku {fileName}: {ex.Message}");
                        }
                    }
                }

                string runtimesDir = Path.Combine(appDirPath, "runtimes");
                if (Directory.Exists(runtimesDir))
                {
                    try
                    {
                        Directory.Delete(runtimesDir, true);
                        log.Write("Usunięto katalog: runtimes");
                    }
                    catch (Exception ex)
                    {
                        log.Write($"Nie udało się usunąć katalogu runtimes: {ex.Message}");
                    }
                }
            }
        }

        private async Task DownloadAndRunUpdaterAsync(
            string latestVersion,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress)
        {
            string tempFilePath = Path.Combine(Path.GetTempPath(), "LatestVersion.zip");

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    log.Write("Pobieranie najnowszej wersji...");
                    HttpResponseMessage response = await client.GetAsync("https://susfuckr.boracik.pl/api/download-latest");
                    response.EnsureSuccessStatusCode();
                    using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                await UpdateUpdaterIfNeededAsync(log);
                CleanupOldFrameworkFilesIfNeeded(log);
                await UpdateConfigurationBeforeExitAsync(log);

                string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                if (string.IsNullOrEmpty(appDirPath))
                {
                    throw new InvalidOperationException("Nie można określić katalogu aplikacji.");
                }

                string updaterPath = Path.Combine(appDirPath, "updater", "updater.exe");

                log.Write("Uruchamiam updater...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true,
                    Arguments = $"\"{appDirPath}\" \"{tempFilePath}\""
                });

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas pobierania i uruchamiania Updater: {ex.Message}");
                userInteraction.ShowError($"Błąd aktualizacji: {ex.Message}", "Błąd");
            }
        }

        private async Task UpdateConfigurationBeforeExitAsync(IDiagnosticsOutput log)
        {
            try
            {
                string? appDirPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                if (string.IsNullOrEmpty(appDirPath))
                {
                    throw new InvalidOperationException("Nie można określić katalogu aplikacji.");
                }

                var tempFilePath = Path.Combine(appDirPath, "config.temp.json");
                using (HttpClient client = new HttpClient())
                {
                    string? updateServerUrl = configuration["Configuration:UpdateServerUrl"];
                    if (string.IsNullOrEmpty(updateServerUrl))
                    {
                        throw new InvalidOperationException("UpdateServerUrl is null or empty.");
                    }

                    HttpResponseMessage response = await client.GetAsync(updateServerUrl);
                    response.EnsureSuccessStatusCode();
                    using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                ConfigUpdater.CompareAndMergeConfigurations(tempFilePath);
                File.Delete(tempFilePath);
                log.Write("[Updater] Konfiguracja została zaktualizowana przed restartem.");
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas aktualizacji konfiguracji: {ex.Message}");
            }
        }
    }
}
