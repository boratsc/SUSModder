using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services;
using SUSModder.Core.Repositories;
using System.IO;
using SUSModder.Core.GameIntegration;

namespace SUSModder.Core.Services
{
    public class ModUpdateManager
    {
        private readonly ConfigService _configService;
        private readonly ConfigRepository _configRepository;

        public ModUpdateManager()
        {
            _configService = new ConfigService();

            // Inicjalizuj ConfigRepository
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            _configRepository = new ConfigRepository(exeDir);
        }

        public async Task<ModUpdateResult> CheckForUpdatesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking for mod updates...");

                var currentConfigs = _configService.LoadConfig();
                var installedConfigs = currentConfigs.Where(c => !string.IsNullOrEmpty(c.InstallPath)).ToList();
                var uninstalledConfigs = currentConfigs.Where(c => string.IsNullOrEmpty(c.InstallPath)).ToList();

                // 1. Sprawdź aktualizacje dla zainstalowanych modów
                var availableUpdates = await CheckInstalledModUpdatesAsync(currentConfigs);
                // 2. Aktualizuj konfigurację dla niezainstalowanych modów (cicho)
                bool configUpdated = await UpdateUninstalledModsConfigAsync(uninstalledConfigs);

                return new ModUpdateResult
                {
                    InstalledModUpdates = availableUpdates,
                    ConfigWasUpdated = configUpdated,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during update check: {ex.Message}");
                return new ModUpdateResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<List<ModUpdateInfo>> CheckInstalledModUpdatesAsync(List<ModConfiguration> currentConfigs)
        {
            var availableUpdates = new List<ModUpdateInfo>();
            bool configChanged = false;

            var remoteConfigs = await _configRepository.LoadConfigFromApiAsync();

            foreach (var config in currentConfigs.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
            {
                var updatedConfig = remoteConfigs.FirstOrDefault(r => r.Id == config.Id);
                if (updatedConfig != null)
                {
                    bool updated = false;

                    System.Diagnostics.Debug.WriteLine($"[DEBUG] {config.ModName} (ID: {config.Id}) - Local PngFileName: '{config.PngFileName}', Remote PngFileName: '{updatedConfig.PngFileName}'");

                    if (string.IsNullOrEmpty(config.PngFileName) && !string.IsNullOrEmpty(updatedConfig.PngFileName))
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] UZUPEŁNIAM PngFileName dla {config.ModName} (ID: {config.Id}) z '{config.PngFileName}' na '{updatedConfig.PngFileName}'");
                        config.PngFileName = updatedConfig.PngFileName;
                        updated = true;
                    }
                    if (string.IsNullOrEmpty(config.DllInstallPath) && !string.IsNullOrEmpty(updatedConfig.DllInstallPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] UZUPEŁNIAM DllInstallPath dla {config.ModName} (ID: {config.Id}) z '{config.DllInstallPath}' na '{updatedConfig.DllInstallPath}'");
                        config.DllInstallPath = updatedConfig.DllInstallPath;
                        updated = true;
                    }

                    if (updated)
                        configChanged = true;

                    if (!string.Equals(config.ModVersion, updatedConfig.ModVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        availableUpdates.Add(new ModUpdateInfo
                        {
                            ModName = config.ModName,
                            CurrentVersion = config.ModVersion ?? "Nieznana",
                            NewVersion = updatedConfig.ModVersion ?? "Nieznana",
                            Description = updatedConfig.Description ?? "",
                            IsSelected = true,
                            LocalMod = config,
                            RemoteMod = updatedConfig
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Brak remoteConfig dla {config.ModName} (ID: {config.Id})");
                }
            }

            if (configChanged)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Zapisuję config po uzupełnieniu PngFileName/DllInstallPath dla zainstalowanych modów");
                ConfigManager.SaveConfig(currentConfigs);

                System.Diagnostics.Debug.WriteLine("[DEBUG] Zapisano config. Sprawdzam zawartość:");
                var checkConfigs = _configService.LoadConfig();
                var syzyf = checkConfigs.FirstOrDefault(c => c.Id == 14);
                if (syzyf != null)
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Po zapisie: Syzyfowa Beta PngFileName = '{syzyf.PngFileName}'");
            }

            System.Diagnostics.Debug.WriteLine($"Found {availableUpdates.Count} updates for installed mods");
            return availableUpdates;
        }

        private async Task<bool> UpdateUninstalledModsConfigAsync(List<ModConfiguration> uninstalledConfigs)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Updating config for {uninstalledConfigs.Count} uninstalled mods...");

                // Użyj ConfigRepository zamiast ConfigService
                var remoteConfigs = await _configRepository.LoadConfigFromApiAsync();

                // DODAJ WIĘCEJ DEBUGOWANIA
                if (remoteConfigs == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ LoadConfigFromApiAsync returned NULL");
                    return false;
                }

                if (!remoteConfigs.Any())
                {
                    System.Diagnostics.Debug.WriteLine("❌ LoadConfigFromApiAsync returned empty list");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Loaded {remoteConfigs.Count} remote configs from API");

                // DEBUGUJ KONKRETNY MOD
                foreach (var remoteMod in remoteConfigs.Take(3)) // Pokaż pierwsze 3
                {
                    System.Diagnostics.Debug.WriteLine($"  Remote mod: {remoteMod.ModName} v{remoteMod.ModVersion} (ID: {remoteMod.Id})");
                }

                var currentConfigs = _configService.LoadConfig();

                // DEBUGUJ LOKALNE MODY
                System.Diagnostics.Debug.WriteLine($"Local configs count: {currentConfigs.Count}");
                foreach (var localMod in currentConfigs.Where(c => string.IsNullOrEmpty(c.InstallPath)).Take(3))
                {
                    System.Diagnostics.Debug.WriteLine($"  Local uninstalled mod: {localMod.ModName} v{localMod.ModVersion} (ID: {localMod.Id})");
                }

                bool configChanged = false;

                // 1. Aktualizuj istniejące niezainstalowane mody
                var updatedCount = await UpdateExistingUninstalledMods(uninstalledConfigs, remoteConfigs, currentConfigs);
                if (updatedCount > 0) configChanged = true;

                // 2. Usuń mody które nie istnieją już na serwerze
                var removedCount = RemoveObsoleteMods(uninstalledConfigs, remoteConfigs, currentConfigs);
                if (removedCount > 0) configChanged = true;

                // 3. Dodaj nowe mody z serwera
                var addedCount = AddNewMods(remoteConfigs, currentConfigs);
                if (addedCount > 0) configChanged = true;

                // 4. Zapisz zmiany jeśli były jakieś aktualizacje
                if (configChanged)
                {
                    System.Diagnostics.Debug.WriteLine($"💾 Saving config changes...");
                    ConfigManager.SaveConfig(currentConfigs);
                    System.Diagnostics.Debug.WriteLine($"✅ Config saved: {updatedCount} updated, {removedCount} removed, {addedCount} added");

                    System.Diagnostics.Debug.WriteLine($"Config updated: {updatedCount} updated, {removedCount} removed, {addedCount} added");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No changes needed for uninstalled mods config");
                }

                return configChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error updating uninstalled mods config: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<int> UpdateExistingUninstalledMods(List<ModConfiguration> uninstalledConfigs,
            List<ModConfiguration> remoteConfigs, List<ModConfiguration> currentConfigs)
        {
            return await Task.Run(() =>
            {
                int updatedCount = 0;

                foreach (var localMod in uninstalledConfigs)
                {
                    var remoteMod = remoteConfigs.FirstOrDefault(r => r.Id == localMod.Id);
                    if (remoteMod != null && HasModChanged(localMod, remoteMod))
                    {
                        var configToUpdate = currentConfigs.FirstOrDefault(c => c.Id == localMod.Id);
                        if (configToUpdate != null)
                        {
                            UpdateModConfiguration(configToUpdate, remoteMod);
                            updatedCount++;

                            System.Diagnostics.Debug.WriteLine($"Updated uninstalled mod: {configToUpdate.ModName} " +
                                $"({localMod.ModVersion} → {remoteMod.ModVersion})");
                        }
                    }
                }

                return updatedCount;
            });
        }

        private bool HasModChanged(ModConfiguration local, ModConfiguration remote)
        {
            bool nameChanged = !string.Equals(local.ModName, remote.ModName, StringComparison.OrdinalIgnoreCase);
            bool versionChanged = !string.Equals(local.ModVersion, remote.ModVersion, StringComparison.OrdinalIgnoreCase);
            bool descChanged = !string.Equals(local.Description, remote.Description, StringComparison.OrdinalIgnoreCase);
            bool githubChanged = !string.Equals(local.GitHubRepoOrLink, remote.GitHubRepoOrLink, StringComparison.OrdinalIgnoreCase);
            bool epicChanged = !string.Equals(local.EpicGitHubRepoOrLink, remote.EpicGitHubRepoOrLink, StringComparison.OrdinalIgnoreCase);

            // Nowe: sprawdź czy są puste pola do uzupełnienia
            bool pngChanged = string.IsNullOrEmpty(local.PngFileName) && !string.IsNullOrEmpty(remote.PngFileName);
            bool dllPathChanged = string.IsNullOrEmpty(local.DllInstallPath) && !string.IsNullOrEmpty(remote.DllInstallPath);

            bool hasChanged = nameChanged || versionChanged || descChanged || githubChanged || epicChanged || pngChanged || dllPathChanged;

            // DEBUGUJ KONKRETNE ZMIANY
            if (hasChanged)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Mod {local.ModName} (ID: {local.Id}) has changes:");
                if (nameChanged) System.Diagnostics.Debug.WriteLine($"  Name: '{local.ModName}' → '{remote.ModName}'");
                if (versionChanged) System.Diagnostics.Debug.WriteLine($"  Version: '{local.ModVersion}' → '{remote.ModVersion}'");
                if (descChanged) System.Diagnostics.Debug.WriteLine($"  Description changed");
                if (githubChanged) System.Diagnostics.Debug.WriteLine($"  GitHub link changed");
                if (epicChanged) System.Diagnostics.Debug.WriteLine($"  Epic link changed");
                if (pngChanged) System.Diagnostics.Debug.WriteLine($"  PngFileName uzupełnione z API");
                if (dllPathChanged) System.Diagnostics.Debug.WriteLine($"  DllInstallPath uzupełnione z API");
            }

            return hasChanged;
        }

        private void UpdateModConfiguration(ModConfiguration target, ModConfiguration source)
        {
            var originalInstallPath = target.InstallPath;

            System.Diagnostics.Debug.WriteLine($"🔄 Updating mod config for {target.ModName}:");
            System.Diagnostics.Debug.WriteLine($"  Name: '{target.ModName}' → '{source.ModName}'");
            System.Diagnostics.Debug.WriteLine($"  Version: '{target.ModVersion}' → '{source.ModVersion}'");

            target.ModName = source.ModName;
            target.ModVersion = source.ModVersion;
            target.Description = source.Description;
            target.GitHubRepoOrLink = source.GitHubRepoOrLink;
            target.EpicGitHubRepoOrLink = source.EpicGitHubRepoOrLink;
            target.AmongVersion = source.AmongVersion;
            target.ModType = source.ModType;

            // UZUPEŁNIJ BRAKUJĄCE POLA
            if (string.IsNullOrEmpty(target.PngFileName) && !string.IsNullOrEmpty(source.PngFileName))
                target.PngFileName = source.PngFileName;
            if (string.IsNullOrEmpty(target.DllInstallPath) && !string.IsNullOrEmpty(source.DllInstallPath))
                target.DllInstallPath = source.DllInstallPath;

            // Zachowaj oryginalną ścieżkę instalacji
            target.InstallPath = originalInstallPath;

            System.Diagnostics.Debug.WriteLine($"✅ Updated mod config: {target.ModName} v{target.ModVersion}");
        }

        private int RemoveObsoleteMods(List<ModConfiguration> uninstalledConfigs,
            List<ModConfiguration> remoteConfigs, List<ModConfiguration> currentConfigs)
        {
            int removedCount = 0;
            var modsToRemove = new List<ModConfiguration>();

            foreach (var localMod in uninstalledConfigs)
            {
                // Nie usuwaj Vanilla (ID = 0)
                if (localMod.Id == 0) continue;

                var remoteMod = remoteConfigs.FirstOrDefault(r => r.Id == localMod.Id);
                if (remoteMod == null)
                {
                    modsToRemove.Add(localMod);
                    System.Diagnostics.Debug.WriteLine($"Marking for removal: {localMod.ModName} (ID: {localMod.Id}) - not found on server");
                }
            }

            foreach (var modToRemove in modsToRemove)
            {
                currentConfigs.RemoveAll(c => c.Id == modToRemove.Id);
                removedCount++;
            }

            return removedCount;
        }

        private int AddNewMods(List<ModConfiguration> remoteConfigs, List<ModConfiguration> currentConfigs)
        {
            int addedCount = 0;

            foreach (var remoteMod in remoteConfigs)
            {
                var existingMod = currentConfigs.FirstOrDefault(c => c.Id == remoteMod.Id);
                if (existingMod == null)
                {
                    var newMod = new ModConfiguration
                    {
                        Id = remoteMod.Id,
                        ModName = remoteMod.ModName,
                        ModVersion = remoteMod.ModVersion,
                        Description = remoteMod.Description,
                        GitHubRepoOrLink = remoteMod.GitHubRepoOrLink,
                        EpicGitHubRepoOrLink = remoteMod.EpicGitHubRepoOrLink,
                        AmongVersion = remoteMod.AmongVersion,
                        ModType = remoteMod.ModType,
                        InstallPath = string.Empty,
                        PngFileName = remoteMod.PngFileName,           // <-- dodano
                        DllInstallPath = remoteMod.DllInstallPath      // <-- dodano
                    };

                    currentConfigs.Add(newMod);
                    addedCount++;

                    System.Diagnostics.Debug.WriteLine($"Added new mod: {newMod.ModName} (ID: {newMod.Id})");
                }
            }

            return addedCount;
        }
    }

    public class ModUpdateResult
    {
        public List<ModUpdateInfo> InstalledModUpdates { get; set; } = new();
        public bool ConfigWasUpdated { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
