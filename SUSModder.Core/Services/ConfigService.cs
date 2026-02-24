using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Repositories;

namespace SUSModder.Core.Services
{
    public class ConfigService
    {
        /// <summary>
        /// Ładuje konfigurację modów z pliku config.json lub z API.
        /// </summary>
        public List<ModConfiguration> LoadConfig()
        {
            return ConfigManager.LoadConfig();
        }

        /// <summary>
        /// W pełni asynchroniczna wersja LoadConfig - unika blokującego .GetAwaiter().GetResult()
        /// przy pobieraniu konfiguracji z API (15s timeout).
        /// </summary>
        public Task<List<ModConfiguration>> LoadConfigAsync()
        {
            return ConfigManager.LoadConfigAsync();
        }

        /// <summary>
        /// Zapisuje konfigurację modów do pliku config.json.
        /// </summary>
        public void SaveConfig(List<ModConfiguration> configs)
        {
            ConfigManager.SaveConfig(configs);
        }

        /// <summary>
        /// Zapisuje pojedyncze ustawienie do appsettings.json (np. tryb Mode).
        /// </summary>
        public void SaveConfigurationSetting(string key, string value)
        {
            ConfigManager.SaveConfigurationSetting(key, value);
        }
        public string GetAppVersion()
        {
            try
            {
                var userSettingsService = new UserSettingsService();
                var appVersion = userSettingsService.LoadAppVersion();
                return appVersion.CurrentVersion;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading app version: {ex.Message}");
                return "Nieznana";
            }
        }

        public async Task<ModConfiguration?> CheckSingleModUpdateAsync(string modName)
        {
            try
            {
                var currentConfigs = LoadConfig();
                var currentMod = currentConfigs.FirstOrDefault(c => c.ModName == modName);

                if (currentMod == null)
                    return null;

                // Pobierz najnowszą konfigurację z API
                var latestConfigs = await LoadConfigFromApiAsync();
                var latestMod = latestConfigs?.FirstOrDefault(c => c.ModName == modName);

                if (latestMod == null)
                    return null;

                // Pobierz RZECZYWISTĄ wersję z Installation Map (a nie z config.json cache!)
                string installedVersion = currentMod.ModVersion ?? "unknown";

                if (!string.IsNullOrEmpty(currentMod.InstallPath))
                {
                    try
                    {
                        var installMap = await InstallationMapManager.LoadInstallationMapAsync(currentMod.InstallPath);
                        if (installMap?.FullMod != null && !string.IsNullOrEmpty(installMap.FullMod.ModVersion))
                        {
                            installedVersion = installMap.FullMod.ModVersion;
                            System.Diagnostics.Debug.WriteLine($"[ConfigService] {modName} - rzeczywista wersja z InstallationMap: {installedVersion}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigService] Nie udało się odczytać InstallationMap dla {modName}: {ex.Message}");
                        // Fallback do config.json
                    }
                }

                // Sprawdź czy jest nowsza wersja (porównaj z RZECZYWISTĄ wersją!)
                if (IsNewerVersion(latestMod.ModVersion, installedVersion))
                {
                    // Zachowaj ścieżkę instalacji z obecnej konfiguracji
                    latestMod.InstallPath = currentMod.InstallPath;
                    latestMod.LastUpdated = currentMod.LastUpdated;

                    System.Diagnostics.Debug.WriteLine($"[ConfigService] ✓ Wykryto aktualizację: {modName} ({installedVersion} → {latestMod.ModVersion})");
                    return latestMod;
                }

                System.Diagnostics.Debug.WriteLine($"[ConfigService] Brak aktualizacji dla {modName} (zainstalowana: {installedVersion}, najnowsza: {latestMod.ModVersion})");
                return null; // Brak aktualizacji
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking update for {modName}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UpdateSingleModConfigAsync(ModConfiguration updatedMod)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var configs = LoadConfig();
                    var existingMod = configs.FirstOrDefault(c => c.ModName == updatedMod.ModName);

                    if (existingMod != null)
                    {
                        var index = configs.IndexOf(existingMod);
                        configs[index] = updatedMod;
                    }
                    else
                    {
                        configs.Add(updatedMod);
                    }

                    ConfigManager.SaveConfig(configs);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating config for {updatedMod.ModName}: {ex.Message}");
                    return false;
                }
            });
        }

        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            if (string.IsNullOrEmpty(newVersion) || string.IsNullOrEmpty(currentVersion))
                return false;

            try
            {
                // Prosta porównanie wersji - można rozszerzyć o bardziej zaawansowaną logikę
                var newVer = new Version(newVersion.Replace("v", ""));
                var currentVer = new Version(currentVersion.Replace("v", ""));

                return newVer > currentVer;
            }
            catch
            {
                // Fallback - porównanie stringów
                return string.Compare(newVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }
        public async Task<List<ModConfiguration>?> LoadConfigFromApiAsync()
        {
            try
            {
                // Użyj ConfigRepository do pobrania z API
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var configRepo = new ConfigRepository(exeDir);

                return await configRepo.LoadConfigFromApiAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config from API: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> RefreshConfigFromApiAsync()
        {
            return await ConfigManager.RefreshConfigFromApiAsync();
        }


    }
}
