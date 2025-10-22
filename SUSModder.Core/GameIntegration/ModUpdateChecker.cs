using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using SUSModder.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SUSModder.Core.Services;
using SUSModder.Core.Models;

namespace SUSModder.Core.GameIntegration
{
    public static class ModUpdateChecker
    {
        public static async Task CheckForModUpdatesAsync(
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress = null)
        {
            try
            {
                // 1. Pobierz config.json z endpointu
                var remoteConfigs = await DownloadRemoteConfigAsync(configuration, log);
                if (remoteConfigs == null || !remoteConfigs.Any())
                {
                    log.Write("[Aktualizacje] Brak danych z serwera.");
                    return;
                }

                // 2. Pobierz lokalne konfiguracje
                var localConfigs = ConfigManager.LoadConfig();

                // 3. Znajdź zainstalowane mody które mają dostępne aktualizacje
                var modsToUpdate = FindModsWithUpdates(localConfigs, remoteConfigs);

                // 4. Zaproponuj aktualizację użytkownikowi (tylko przez interfejs, bez UI-form)
                if (modsToUpdate.Any())
                {
                    ProposeUpdatesToUser(modsToUpdate, configuration, log, userInteraction, progress);
                }
                else
                {
                    log.Write("[Aktualizacje] Wszystkie mody są aktualne.");
                }
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas sprawdzania aktualizacji modów: {ex.Message}");
            }
        }

        /// <summary>
        /// Sprawdza dostępne aktualizacje bez pytania użytkownika
        /// </summary>
        public static async Task<List<ModUpdateInfo>> GetAvailableUpdatesAsync(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            try
            {
                // 1. Pobierz config.json z endpointu
                var remoteConfigs = await DownloadRemoteConfigAsync(configuration, log);
                if (remoteConfigs == null || !remoteConfigs.Any())
                {
                    log.Write("[Aktualizacje] Brak danych z serwera.");
                    return new List<ModUpdateInfo>();
                }

                // 2. Pobierz lokalne konfiguracje
                var localConfigs = ConfigManager.LoadConfig();

                // 3. Znajdź zainstalowane mody które mają dostępne aktualizacje
                var modsToUpdate = FindModsWithUpdates(localConfigs, remoteConfigs);

                log.Write($"[Aktualizacje] Znaleziono {modsToUpdate.Count} modów do aktualizacji");

                return modsToUpdate;
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas sprawdzania aktualizacji modów: {ex.Message}");
                return new List<ModUpdateInfo>();
            }
        }

        /// <summary>
        /// Aktualizuje wybrane mody
        /// </summary>
        public static async Task UpdateSelectedModsAsync(
            List<ModUpdateInfo> selectedMods,
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress = null)
        {
            foreach (var modUpdate in selectedMods)
            {
                try
                {
                    if (modUpdate.LocalMod == null)
                    {
                        log.Write("[ERROR] LocalMod is null");
                        return;
                    }
                    log.Write($"[Aktualizacje] Aktualizuję mod: {modUpdate.LocalMod.ModName}");
                    await UpdateSingleMod(modUpdate, configuration, log, userInteraction, progress);
                    log.Write($"[Aktualizacje] Pomyślnie zaktualizowano: {modUpdate.LocalMod.ModName}");
                }
                catch (Exception ex)
                {
                    if (modUpdate.LocalMod == null)
                    {
                        log.Write("[ERROR] LocalMod is null");
                        return;
                    }
                    log.Write($"[ERROR] Błąd podczas aktualizacji {modUpdate.LocalMod.ModName}: {ex.Message}");
                    userInteraction.ShowError($"Błąd podczas aktualizacji {modUpdate.LocalMod.ModName}: {ex.Message}", "Błąd aktualizacji");
                }
            }
        }

        private static async Task<List<ModConfiguration>?> DownloadRemoteConfigAsync(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            try
            {
                var tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.temp.json");

                using (HttpClient client = new HttpClient())
                {
                    string downloadToken = SecretProvider.GetDownloadToken();
                    client.DefaultRequestHeaders.Add("Authorization", downloadToken);

                    HttpResponseMessage response = await client.GetAsync(configuration["Configuration:UpdateServerUrl"]);
                    response.EnsureSuccessStatusCode();

                    using (FileStream fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                var jsonContent = await File.ReadAllTextAsync(tempFilePath);
                var remoteConfigs = JsonSerializer.Deserialize<List<ModConfiguration>>(jsonContent) ?? new List<ModConfiguration>();

                File.Delete(tempFilePath);

                return remoteConfigs;
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Nie udało się pobrać konfiguracji z serwera: {ex.Message}");
                return null;
            }
        }

        private static List<ModUpdateInfo> FindModsWithUpdates(List<ModConfiguration> localConfigs, List<ModConfiguration> remoteConfigs)
        {
            var modsToUpdate = new List<ModUpdateInfo>();

            foreach (var localMod in localConfigs)
            {
                // Sprawdź tylko zainstalowane mody typu "full" i "dll"
                if ((localMod.ModType != "full" && localMod.ModType != "dll") ||
                    string.IsNullOrEmpty(localMod.InstallPath) ||
                    !Directory.Exists(localMod.InstallPath))
                    continue;

                var remoteMod = remoteConfigs.FirstOrDefault(r => r.Id == localMod.Id);
                if (remoteMod == null)
                    continue;

                // Pobierz RZECZYWISTĄ wersję z Installation Map (a nie z config.json cache!)
                string installedVersion = localMod.ModVersion ?? "Nieznana";

                try
                {
                    var installMap = InstallationMapManager.LoadInstallationMapAsync(localMod.InstallPath).GetAwaiter().GetResult();
                    if (installMap?.FullMod != null && !string.IsNullOrEmpty(installMap.FullMod.ModVersion))
                    {
                        installedVersion = installMap.FullMod.ModVersion;
                    }
                }
                catch
                {
                    // Fallback do config.json jeśli InstallationMap nie istnieje
                }

                // Porównaj RZECZYWISTĄ wersję z dysku z wersją z API
                if (HasNewerVersionComparedTo(installedVersion, remoteMod.ModVersion))
                {
                    modsToUpdate.Add(new ModUpdateInfo
                    {
                        LocalMod = localMod,
                        RemoteMod = remoteMod,
                        CurrentVersion = installedVersion,
                        NewVersion = remoteMod.ModVersion ?? "Nieznana",
                        ModName = localMod.ModName ?? "Nieznany",
                        Description = remoteMod.Description ?? "",
                        IsSelected = true // Domyślnie zaznaczone
                    });
                }
            }

            return modsToUpdate;
        }

        private static bool HasNewerVersion(ModConfiguration localMod, ModConfiguration remoteMod)
        {
            if (!string.IsNullOrEmpty(localMod.ModVersion) && !string.IsNullOrEmpty(remoteMod.ModVersion))
            {
                return !string.Equals(localMod.ModVersion, remoteMod.ModVersion, StringComparison.OrdinalIgnoreCase);
            }
            if (string.IsNullOrEmpty(localMod.ModVersion) && !string.IsNullOrEmpty(remoteMod.ModVersion))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sprawdza czy remoteVersion jest nowsza niż installedVersion
        /// </summary>
        private static bool HasNewerVersionComparedTo(string? installedVersion, string? remoteVersion)
        {
            if (!string.IsNullOrEmpty(installedVersion) && !string.IsNullOrEmpty(remoteVersion))
            {
                return !string.Equals(installedVersion, remoteVersion, StringComparison.OrdinalIgnoreCase);
            }
            if (string.IsNullOrEmpty(installedVersion) && !string.IsNullOrEmpty(remoteVersion))
            {
                return true;
            }
            return false;
        }

        private static void ProposeUpdatesToUser(
            List<ModUpdateInfo> modsToUpdate,
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress)
        {
            // W Core: tylko tekstowa propozycja i wybór przez Confirm
            foreach (var modUpdate in modsToUpdate)
            {
                // Sprawdź czy LocalMod nie jest null
                if (modUpdate.LocalMod == null)
                {
                    log.Write("[Aktualizacje] Pominięto mod - brak danych lokalnego moda");
                    continue;
                }

                var modName = modUpdate.LocalMod.ModName ?? "Nieznany";

                bool update = userInteraction.Confirm(
                    $"Dostępna jest aktualizacja dla moda {modName} ({modUpdate.CurrentVersion} → {modUpdate.NewVersion}).\nCzy chcesz zaktualizować?",
                    "Aktualizacja modów");

                if (update)
                {
                    log.Write($"[Aktualizacje] Aktualizuję mod: {modName}");
                    UpdateSingleMod(modUpdate, configuration, log, userInteraction, progress).GetAwaiter().GetResult();
                }
                else
                {
                    log.Write($"[Aktualizacje] Pominięto aktualizację moda: {modName}");
                }
            }

        }

        private static async Task UpdateSingleMod(
            ModUpdateInfo modUpdate,
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IProgressReporter? progress)
        {
            try
            {
                var currentConfigs = ConfigManager.LoadConfig();
                if (modUpdate.LocalMod == null)
                {
                    log.Write("[ERROR] LocalMod is null");
                    return;
                }

                // KROK 0: Zapisz listę zainstalowanych DLL PRZED usunięciem
                List<DllModInstallation> installedDlls = new List<DllModInstallation>();

                if (modUpdate.LocalMod.ModType == "full" && !string.IsNullOrEmpty(modUpdate.LocalMod.InstallPath))
                {
                    try
                    {
                        var installMap = await InstallationMapManager.LoadInstallationMapAsync(modUpdate.LocalMod.InstallPath);
                        if (installMap?.InstalledDlls != null && installMap.InstalledDlls.Any())
                        {
                            installedDlls = new List<DllModInstallation>(installMap.InstalledDlls);
                            log.Write($"[Aktualizacje] Zachowuję {installedDlls.Count} zainstalowanych DLL: {string.Join(", ", installedDlls.Select(d => d.ModName))}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Write($"[Aktualizacje] Nie udało się odczytać Installation Map: {ex.Message}");
                    }
                }

                // KROK 1: Usuń stary mod
                var modToDelete = currentConfigs.FirstOrDefault(c => c.Id == modUpdate.LocalMod.Id);
                if (modToDelete != null)
                {
                    ModDelete.DeleteMod(modToDelete, currentConfigs, userInteraction);
                }

                // KROK 2: Pobierz zaktualizowaną konfigurację po usunięciu
                var updatedConfigs = ConfigManager.LoadConfig();
                if (modUpdate.RemoteMod == null)
                {
                    log.Write("[ERROR] RemoteMod is null");
                    return;
                }
                var modToInstall = updatedConfigs.FirstOrDefault(c => c.Id == modUpdate.RemoteMod.Id);

                if (modToInstall != null)
                {
                    var modManager = new ModManager(configuration);

                    // Adaptery muszą być przekazane z UI, tu tylko null-check
                    if (progress == null)
                        progress = new NullProgressReporter();

                    var callbacks = new ModManagerUserCallbacks
                    {
                        ConfirmAsync = userInteraction.ShowConfirmAsync,
                        ShowErrorAsync = userInteraction.ShowErrorAsync,
                        ShowInfoAsync = userInteraction.ShowInfoAsync
                    };

                    // KROK 3: Zainstaluj nową wersję moda FULL
                    await modManager.ModifyAsync(
                        modToInstall,
                        updatedConfigs,
                        progress,
                        log,
                        callbacks,
                        configuration["Configuration:Mode"] ?? "steam"
                    );

                    // KROK 4: Przywróć zainstalowane DLL (jeśli były)
                    if (installedDlls.Any())
                    {
                        log.Write($"[Aktualizacje] Przywracanie {installedDlls.Count} modów DLL...");
                        await RestoreDllModsAsync(modToInstall, installedDlls, configuration, log, userInteraction);
                    }
                }

                var modName = modUpdate.LocalMod?.ModName ?? "Nieznany";
                log.Write($"[Aktualizacje] Pomyślnie zaktualizowano mod: {modName}");
            }
            catch (Exception ex)
            {
                var modName = modUpdate.LocalMod?.ModName ?? "Nieznany";
                log.Write($"[ERROR] Błąd podczas aktualizacji moda {modName}: {ex.Message}");
                userInteraction.ShowError($"Błąd podczas aktualizacji moda {modName}: {ex.Message}", "Błąd");
            }
        }

        /// <summary>
        /// Przywraca zainstalowane mody DLL po aktualizacji moda FULL
        /// </summary>
        private static async Task RestoreDllModsAsync(
            ModConfiguration fullMod,
            List<DllModInstallation> dllsToRestore,
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction)
        {
            if (!dllsToRestore.Any())
                return;

            // Pobierz aktualną konfigurację z DLL
            var configs = ConfigManager.LoadConfig();
            var configService = new ConfigService();
            var dllModificationService = new DllModificationService(configService, log);
            var platform = configuration["Configuration:Mode"] ?? "steam";

            int restoredCount = 0;
            int failedCount = 0;

            foreach (var dllInfo in dllsToRestore)
            {
                try
                {
                    // Znajdź konfigurację DLL w config.json
                    var dllConfig = configs.FirstOrDefault(c => c.Id == dllInfo.ModId);

                    if (dllConfig == null)
                    {
                        log.Write($"[Aktualizacje] Nie znaleziono konfiguracji dla DLL (ID: {dllInfo.ModId}, nazwa: {dllInfo.ModName}) - pomijam");
                        failedCount++;
                        continue;
                    }

                    log.Write($"[Aktualizacje] Przywracanie DLL: {dllConfig.ModName} v{dllInfo.ModVersion}");

                    // Zainstaluj DLL do zaktualizowanego moda FULL
                    var installedPath = await dllModificationService.InstallDllToModAsync(
                        dllConfig,
                        fullMod,
                        platform
                    );

                    if (!string.IsNullOrEmpty(installedPath))
                    {
                        restoredCount++;
                        log.Write($"[Aktualizacje] ✓ Przywrócono: {dllConfig.ModName}");
                    }
                    else
                    {
                        failedCount++;
                        log.Write($"[Aktualizacje] ✗ Nie udało się przywrócić: {dllConfig.ModName}");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    log.Write($"[ERROR] Błąd podczas przywracania DLL {dllInfo.ModName}: {ex.Message}");
                }
            }

            // Pokaż podsumowanie przywracania DLL
            if (restoredCount > 0 || failedCount > 0)
            {
                string summary = $"Przywrócono {restoredCount} mod(ów) DLL.";
                if (failedCount > 0)
                {
                    summary += $"\n\nNie udało się przywrócić: {failedCount} mod(ów) DLL.";
                    summary += "\nMożesz je zainstalować ponownie ręcznie z menu 'Zarządzaj modami DLL'.";
                }

                if (failedCount > 0)
                {
                    await userInteraction.ShowInfoAsync("Aktualizacja zakończona z ostrzeżeniami", summary);
                }
                else
                {
                    log.Write($"[Aktualizacje] Podsumowanie: {summary}");
                }
            }
        }
    }

    public class ModUpdateInfo : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public string ModName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public ModConfiguration? LocalMod { get; set; }
        public ModConfiguration? RemoteMod { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Null-object pattern dla progresu, jeśli nie przekazano adaptera
    public class NullProgressReporter : IProgressReporter
    {
        public void Report(int percent, string? message = null) { }
    }
}
