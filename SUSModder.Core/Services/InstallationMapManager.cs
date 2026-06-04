using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Zarządza mapami instalacji modów (.susmodder-install.json)
    /// Zapewnia trwałość informacji o instalacjach niezależnie od config.json
    /// </summary>
    public static class InstallationMapManager
    {
        private const string MapFileName = ".susmodder-install.json";

        /// <summary>
        /// Zapisz Installation Map w katalogu moda
        /// </summary>
        /// <param name="modInstallPath">Ścieżka do katalogu moda</param>
        /// <param name="map">Mapa instalacji do zapisania</param>
        public static async Task SaveInstallationMapAsync(
            string modInstallPath,
            InstallationMap map)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                throw new ArgumentNullException(nameof(modInstallPath));

            if (!Directory.Exists(modInstallPath))
                throw new DirectoryNotFoundException($"Katalog nie istnieje: {modInstallPath}");

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(map, options);
            await File.WriteAllTextAsync(mapFilePath, json);
        }

        /// <summary>
        /// Wczytaj Installation Map z katalogu moda
        /// </summary>
        /// <param name="modInstallPath">Ścieżka do katalogu moda</param>
        /// <returns>Mapa instalacji lub null jeśli nie istnieje</returns>
        public static async Task<InstallationMap?> LoadInstallationMapAsync(
            string? modInstallPath)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                return null;

            if (!Directory.Exists(modInstallPath))
                return null;

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);

            if (!File.Exists(mapFilePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(mapFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var map = JsonSerializer.Deserialize<InstallationMap>(json, options);
                return map;
            }
            catch (Exception)
            {
                // Jeśli plik jest uszkodzony, zwróć null
                return null;
            }
        }

        /// <summary>
        /// Sprawdź czy Installation Map istnieje w katalogu
        /// </summary>
        /// <param name="modInstallPath">Ścieżka do katalogu moda</param>
        /// <returns>True jeśli mapa istnieje</returns>
        public static bool InstallationMapExists(string? modInstallPath)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                return false;

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);
            return File.Exists(mapFilePath);
        }

        /// <summary>
        /// Odkryj wszystkie zainstalowane mody skanując katalogi
        /// </summary>
        /// <param name="modsBasePath">Bazowa ścieżka do katalogu z modami</param>
        /// <param name="log">Logger</param>
        /// <returns>Lista odkrytych map instalacji</returns>
        public static async Task<List<InstallationMap>> DiscoverInstalledModsAsync(
            string modsBasePath,
            IDiagnosticsOutput log)
        {
            var discoveredMods = new List<InstallationMap>();

            if (!Directory.Exists(modsBasePath))
            {
                log.Write($"[InstallationMapManager] Katalog nie istnieje: {modsBasePath}");
                return discoveredMods;
            }

            log.Write($"[InstallationMapManager] Skanowanie katalogu: {modsBasePath}");

            try
            {
                // Przeszukaj wszystkie podkatalogi (STEAM: poziom 1)
                var directories = Directory.GetDirectories(modsBasePath);

                foreach (var dir in directories)
                {
                    // Sprawdź bezpośrednio w katalogu (STEAM)
                    var map = await LoadInstallationMapAsync(dir);

                    if (map != null)
                    {
                        log.Write($"[Odkryto] {map.FullMod.ModName} v{map.FullMod.ModVersion} w {dir}");
                        discoveredMods.Add(map);
                    }
                    else
                    {
                        // Sprawdź podkatalogi (EPIC: poziom 2, np. "ModName\AmongUs\")
                        try
                        {
                            var subDirectories = Directory.GetDirectories(dir);
                            foreach (var subDir in subDirectories)
                            {
                                var subMap = await LoadInstallationMapAsync(subDir);
                                if (subMap != null)
                                {
                                    log.Write($"[Odkryto] {subMap.FullMod.ModName} v{subMap.FullMod.ModVersion} w {subDir}");
                                    discoveredMods.Add(subMap);
                                }
                            }
                        }
                        catch (Exception subEx)
                        {
                            log.Write($"[WARN] Błąd podczas skanowania podkatalogu {dir}: {subEx.Message}");
                        }
                    }
                }

                log.Write($"[InstallationMapManager] Znaleziono {discoveredMods.Count} modów z Installation Map");
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Błąd podczas skanowania: {ex.Message}");
            }

            return discoveredMods;
        }

        /// <summary>
        /// Zaimportuj odkryte mody do config.json
        /// </summary>
        /// <param name="discoveredMaps">Lista odkrytych map instalacji</param>
        /// <param name="existingConfigs">Istniejące konfiguracje</param>
        /// <param name="log">Logger</param>
        /// <returns>Lista zaimportowanych/zaktualizowanych konfiguracji</returns>
        public static List<ModConfiguration> ImportDiscoveredMods(
            List<InstallationMap> discoveredMaps,
            List<ModConfiguration> existingConfigs,
            IDiagnosticsOutput log)
        {
            var imported = new List<ModConfiguration>();

            foreach (var map in discoveredMaps)
            {
                try
                {
                    if (!IsImportableFullModMap(map))
                    {
                        log.Write($"[Import] Pomijam niepełną mapę instalacji: {map.DisplayName ?? map.FullMod?.InstallPath ?? "unknown"}");
                        continue;
                    }

                    var fullMod = map.FullMod!;

                    // Sprawdź czy mod już istnieje w config
                    var existing = existingConfigs.FirstOrDefault(c => c.Id == fullMod.ModId);

                    if (existing != null)
                    {
                        if (!MatchesDiscoveredFullMod(existing, map))
                        {
                            log.Write($"[Import] Pomijam niespójną mapę instalacji dla ID {fullMod.ModId}: katalog='{existing.ModName}' ({existing.ModType}), mapa='{fullMod.ModName}'.");
                            continue;
                        }

                        // Aktualizuj InstallPath jeśli jest inny
                        if (existing.InstallPath != fullMod.InstallPath)
                        {
                            log.Write($"[Import] Aktualizuję InstallPath dla {existing.ModName}");
                            existing.InstallPath = fullMod.InstallPath;
                            existing.ModVersion = fullMod.ModVersion;
                            existing.AmongVersion = fullMod.AmongVersion;
                            existing.LastUpdated = fullMod.LastUpdated;
                            imported.Add(existing);
                        }
                    }
                    else
                    {
                        // Dodaj nowy mod do config
                        var newConfig = new ModConfiguration
                        {
                            Id = fullMod.ModId,
                            ModName = fullMod.ModName,
                            ModType = "full",
                            ModVersion = fullMod.ModVersion,
                            AmongVersion = fullMod.AmongVersion,
                            InstallPath = fullMod.InstallPath,
                            LastUpdated = fullMod.LastUpdated,
                            GitHubRepoOrLink = fullMod.InstalledFrom
                        };

                        log.Write($"[Import] Dodaję nowy mod: {newConfig.ModName}");
                        existingConfigs.Add(newConfig);
                        imported.Add(newConfig);
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"[ERROR] Błąd importu moda {map.FullMod?.ModName ?? map.DisplayName ?? "unknown"}: {ex.Message}");
                }
            }

            return imported;
        }

        private static bool IsImportableFullModMap(InstallationMap map)
        {
            return map.FullMod != null &&
                   map.FullMod.ModId > 0 &&
                   !string.IsNullOrWhiteSpace(map.FullMod.ModName) &&
                   !string.IsNullOrWhiteSpace(map.FullMod.InstallPath);
        }

        private static bool MatchesDiscoveredFullMod(ModConfiguration existing, InstallationMap map)
        {
            if (!string.Equals(existing.ModType, "full", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(
                existing.ModName?.Trim(),
                map.FullMod.ModName?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Migruj istniejące instalacje (stwórz Installation Map dla modów bez niego)
        /// </summary>
        /// <param name="modConfigs">Lista konfiguracji modów</param>
        /// <param name="platform">Platforma (steam/epic)</param>
        /// <param name="log">Logger</param>
        /// <returns>Liczba zmigrowanych modów</returns>
        public static async Task<int> MigrateExistingInstallationsAsync(
            List<ModConfiguration> modConfigs,
            string platform,
            IDiagnosticsOutput log)
        {
            log.Write("[InstallationMapManager] Rozpoczynam migrację istniejących instalacji...");

            int migrated = 0;

            foreach (var modConfig in modConfigs)
            {
                try
                {
                    // Tylko dla FULL modów z InstallPath
                    if (modConfig.ModType != "full" || string.IsNullOrEmpty(modConfig.InstallPath))
                        continue;

                    // Sprawdź czy katalog istnieje
                    if (!Directory.Exists(modConfig.InstallPath))
                        continue;

                    // Sprawdź czy Installation Map już istnieje
                    if (InstallationMapExists(modConfig.InstallPath))
                    {
                        log.Write($"[Migracja] {modConfig.ModName} - już ma Installation Map");
                        continue;
                    }

                    // Stwórz Installation Map
                    var installationMap = new InstallationMap
                    {
                        InstalledAt = modConfig.LastUpdated ?? DateTime.Now,
                        InstalledBy = "SUSModder (migrated)",
                        Platform = platform,
                        FullMod = new FullModInstallation
                        {
                            ModId = modConfig.Id,
                            ModName = modConfig.ModName,
                            ModVersion = modConfig.ModVersion ?? "unknown",
                            AmongVersion = modConfig.AmongVersion ?? "unknown",
                            InstallPath = modConfig.InstallPath,
                            InstalledFrom = modConfig.GitHubRepoOrLink ?? "unknown",
                            LastUpdated = modConfig.LastUpdated ?? DateTime.Now
                        },
                        InstalledDlls = new List<DllModInstallation>(),
                        Metadata = new InstallationMetadata
                        {
                            Notes = "Migrated from existing installation"
                        }
                    };

                    await SaveInstallationMapAsync(modConfig.InstallPath, installationMap);
                    log.Write($"[Migracja] ✓ {modConfig.ModName}");
                    migrated++;
                }
                catch (Exception ex)
                {
                    log.Write($"[Migracja] ✗ {modConfig.ModName}: {ex.Message}");
                }
            }

            log.Write($"[InstallationMapManager] Migracja zakończona: {migrated} modów");
            return migrated;
        }

        /// <summary>
        /// Pobierz wersję aplikacji (helper)
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
        /// Waliduje i czyści config.json z modów które nie istnieją na dysku
        /// Jeśli mod ma InstallPath ale brak Installation Map lub katalogu - usuwa InstallPath
        /// </summary>
        /// <param name="modConfigs">Lista konfiguracji modów</param>
        /// <param name="log">Logger</param>
        /// <returns>Liczba wyczyszczonych modów</returns>
        public static int ValidateAndCleanInstalledMods(
            List<ModConfiguration> modConfigs,
            IDiagnosticsOutput log)
        {
            log.Write("[InstallationMapManager] Rozpoczynam walidację zainstalowanych modów...");

            int cleanedCount = 0;

            foreach (var modConfig in modConfigs)
            {
                try
                {
                    // Tylko dla modów FULL z InstallPath
                    if (modConfig.ModType != "full" || string.IsNullOrEmpty(modConfig.InstallPath))
                        continue;

                    // Sprawdź czy katalog istnieje
                    bool directoryExists = Directory.Exists(modConfig.InstallPath);

                    // Sprawdź czy Installation Map istnieje
                    bool mapExists = InstallationMapExists(modConfig.InstallPath);

                    // Sprawdź czy katalog ma pliki (jeśli istnieje)
                    bool hasFiles = false;
                    if (directoryExists)
                    {
                        hasFiles = Directory.GetFiles(modConfig.InstallPath, "*", SearchOption.AllDirectories).Length > 0;
                    }

                    // Jeśli brak Installation Map I (katalog nie istnieje LUB jest pusty)
                    if (!mapExists && (!directoryExists || !hasFiles))
                    {
                        log.Write($"[Walidacja] Mod '{modConfig.ModName}' (ID: {modConfig.Id}) - katalog nie istnieje lub jest pusty, brak Installation Map");
                        log.Write($"[Walidacja] Czyszczę InstallPath: {modConfig.InstallPath}");

                        modConfig.InstallPath = null;
                        modConfig.LastUpdated = null;
                        cleanedCount++;
                    }
                    else if (!mapExists && directoryExists && hasFiles)
                    {
                        // Katalog istnieje i ma pliki, ale brak Installation Map
                        // To może być stara instalacja - powinna być objęta migracją
                        log.Write($"[Walidacja] Mod '{modConfig.ModName}' - katalog istnieje ale brak Installation Map (zostanie objęty migracją)");
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"[Walidacja] Błąd podczas walidacji moda {modConfig.ModName}: {ex.Message}");
                }
            }

            log.Write($"[InstallationMapManager] Walidacja zakończona: {cleanedCount} modów wyczyszczono");
            return cleanedCount;
        }
    }
}
