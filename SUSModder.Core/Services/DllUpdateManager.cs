using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Manager aktualizacji modów DLL w wielu lokalizacjach
    /// </summary>
    public class DllUpdateManager
    {
        private readonly DllModificationService _dllModService;
        private readonly ConfigService _configService;
        private readonly IDiagnosticsOutput _log;

        public DllUpdateManager(
            DllModificationService dllModService,
            ConfigService configService,
            IDiagnosticsOutput log)
        {
            _dllModService = dllModService;
            _configService = configService;
            _log = log;
        }

        /// <summary>
        /// Sprawdź dostępne aktualizacje modów DLL
        /// </summary>
        public async Task<List<DllUpdateInfo>> CheckDllUpdatesAsync(string platform)
        {
            var updatesList = new List<DllUpdateInfo>();

            try
            {
                _log.Write("[DllUpdateManager] Sprawdzanie aktualizacji DLL...");

                // 1. Pobierz najnowsze wersje z API
                var remoteConfigs = await _configService.LoadConfigFromApiAsync();
                if (remoteConfigs == null || !remoteConfigs.Any())
                {
                    _log.Write("[DllUpdateManager] Nie udało się pobrać danych z API");
                    return new List<DllUpdateInfo>();
                }

                // 2. Pobierz lokalne konfiguracje
                var localConfigs = _configService.LoadConfig();

                // 3. Filtruj tylko mody DLL
                var remoteDlls = remoteConfigs
                    .Where(m => m.ModType.Equals("dll", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _log.Write($"[DllUpdateManager] Sprawdzam {remoteDlls.Count} modów DLL");

                foreach (var remoteDll in remoteDlls)
                {
                    _log.Write($"[DllUpdateManager] Sprawdzanie {remoteDll.ModName} (ID: {remoteDll.Id})...");

                    // Znajdź gdzie DLL jest zainstalowany
                    var locations = _dllModService.GetModsWithDllInstalled(remoteDll, platform);

                    if (!locations.Any())
                    {
                        _log.Write($"[DllUpdateManager] {remoteDll.ModName} nie jest zainstalowany w żadnym modzie FULL - pomijam");
                        continue;
                    }

                    _log.Write($"[DllUpdateManager] {remoteDll.ModName} zainstalowany w {locations.Count} lokalizacjach");

                    // Sprawdź wersje w InstallationMap każdej lokalizacji i znajdź te wymagające aktualizacji
                    var locationUpdates = new List<DllLocationUpdate>();
                    var versionMap = new Dictionary<string, List<string>>(); // version -> list of mod names
                    
                    foreach (var fullMod in locations)
                    {
                        try
                        {
                            var installMap = await InstallationMapManager.LoadInstallationMapAsync(fullMod.InstallPath);
                            if (installMap?.InstalledDlls != null)
                            {
                                var dllInfo = installMap.InstalledDlls.FirstOrDefault(d => d.ModId == remoteDll.Id);
                                if (dllInfo != null && !string.IsNullOrEmpty(dllInfo.ModVersion))
                                {
                                    var installedVersion = dllInfo.ModVersion;
                                    _log.Write($"[DllUpdateManager]   {fullMod.ModName}: wersja {installedVersion}");
                                    
                                    // Dodaj do mapy wersji
                                    if (!versionMap.ContainsKey(installedVersion))
                                    {
                                        versionMap[installedVersion] = new List<string>();
                                    }
                                    versionMap[installedVersion].Add(fullMod.ModName);
                                    
                                    // Jeśli wersja jest różna od najnowszej, dodaj do listy do aktualizacji
                                    if (!installedVersion.Equals(remoteDll.ModVersion, StringComparison.OrdinalIgnoreCase))
                                    {
                                        locationUpdates.Add(new DllLocationUpdate
                                        {
                                            FullMod = fullMod,
                                            CurrentVersion = installedVersion,
                                            NewVersion = remoteDll.ModVersion
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Write($"[DllUpdateManager] Błąd odczytu InstallationMap z {fullMod.ModName}: {ex.Message}");
                        }
                    }

                    // Jeśli są lokalizacje wymagające aktualizacji, utwórz wpis
                    if (locationUpdates.Any())
                    {
                        // Znajdź najczęstszą zainstalowaną wersję jako "current" (dla backward compatibility)
                        var mostCommonVersion = versionMap.OrderByDescending(kvp => kvp.Value.Count).First().Key;
                        
                        _log.Write($"[DllUpdateManager] ✓ Znaleziono aktualizację: {remoteDll.ModName}");
                        _log.Write($"[DllUpdateManager]   Lokalizacje do zaktualizowania: {locationUpdates.Count}/{locations.Count}");
                        foreach (var loc in locationUpdates)
                        {
                            _log.Write($"[DllUpdateManager]     - {loc.FullMod.ModName} ({loc.CurrentVersion} → {remoteDll.ModVersion})");
                        }

                        updatesList.Add(new DllUpdateInfo
                        {
                            DllMod = remoteDll,
                            CurrentVersion = mostCommonVersion, // Deprecated, ale zachowujemy
                            NewVersion = remoteDll.ModVersion,
                            LocationUpdates = locationUpdates,
                            InstallLocations = locationUpdates.Select(lu => lu.FullMod).ToList(), // Deprecated
                            SelectedLocations = locationUpdates.Select(lu => lu.FullMod).ToList() // Domyślnie wszystkie
                        });
                    }
                    else if (versionMap.Count == 0)
                    {
                        _log.Write($"[DllUpdateManager] ⚠ Nie znaleziono wersji dla {remoteDll.ModName} - pomijam");
                    }
                    else
                    {
                        _log.Write($"[DllUpdateManager] ✓ {remoteDll.ModName} jest aktualny we wszystkich lokalizacjach ({remoteDll.ModVersion})");
                    }
                }

                _log.Write($"[DllUpdateManager] Znaleziono {updatesList.Count} aktualizacji DLL");
                return updatesList;
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Błąd sprawdzania aktualizacji DLL: {ex.Message}");
                return new List<DllUpdateInfo>();
            }
        }

        /// <summary>
        /// Zaktualizuj DLL w wybranych lokalizacjach
        /// </summary>
        public async Task<DllUpdateResult> UpdateDllInLocationsAsync(
            DllUpdateInfo updateInfo,
            string platform)
        {
            var result = new DllUpdateResult
            {
                DllName = updateInfo.DllMod.ModName,
                TotalLocations = updateInfo.SelectedLocations.Count
            };

            _log.Write($"[DllUpdateManager] Aktualizowanie {result.DllName} w {result.TotalLocations} lokalizacjach");

            foreach (var fullMod in updateInfo.SelectedLocations)
            {
                try
                {
                    _log.Write($"[DllUpdate] Aktualizowanie {updateInfo.DllMod.ModName} w {fullMod.ModName}");

                    var installedPath = await _dllModService.InstallDllToModAsync(
                        updateInfo.DllMod,
                        fullMod,
                        platform
                    );

                    if (!string.IsNullOrEmpty(installedPath))
                    {
                        result.SuccessfulUpdates++;
                        result.UpdatedLocations.Add(fullMod.ModName);
                        _log.Write($"[DllUpdate] ✓ Zaktualizowano w {fullMod.ModName}");
                    }
                    else
                    {
                        result.FailedUpdates++;
                        result.FailedLocations.Add(fullMod.ModName);
                        _log.Write($"[DllUpdate] ✗ Nie udało się zaktualizować w {fullMod.ModName}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Write($"[ERROR] Błąd aktualizacji w {fullMod.ModName}: {ex.Message}");
                    result.FailedUpdates++;
                    result.FailedLocations.Add(fullMod.ModName);
                }
            }

            _log.Write($"[DllUpdateManager] Aktualizacja zakończona: {result.SuccessfulUpdates}/{result.TotalLocations} udanych");
            return result;
        }

        /// <summary>
        /// Zaktualizuj wszystkie DLL z listy w ich lokalizacjach
        /// </summary>
        public async Task<List<DllUpdateResult>> UpdateAllDllsAsync(
            List<DllUpdateInfo> updates,
            string platform)
        {
            var results = new List<DllUpdateResult>();

            _log.Write($"[DllUpdateManager] Rozpoczynam aktualizację {updates.Count} modów DLL");

            foreach (var update in updates)
            {
                var result = await UpdateDllInLocationsAsync(update, platform);
                results.Add(result);
            }

            var totalSuccess = results.Sum(r => r.SuccessfulUpdates);
            var totalFailed = results.Sum(r => r.FailedUpdates);
            _log.Write($"[DllUpdateManager] Wszystkie aktualizacje zakończone: {totalSuccess} udanych, {totalFailed} nieudanych");

            return results;
        }

        // ─────────────────────────────────────────────
        // Auto-update helpers (v2.x)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Dzieli listę znalezionych aktualizacji DLL na automatyczne (DLL z AutoUpdateEnabled)
        /// i ręczne (DLL bez flagi). Skanuje InstallationMap dla każdego DLL.
        /// </summary>
        public async Task<(List<DllUpdateInfo> AutoUpdates, List<DllUpdateInfo> ManualUpdates)>
            FilterAutoUpdateDllsAsync(List<DllUpdateInfo> allUpdates, List<ModConfiguration> installedFullMods)
        {
            var autoUpdates = new List<DllUpdateInfo>();
            var manualUpdates = new List<DllUpdateInfo>();

            foreach (var update in allUpdates)
            {
                var state = await InstallationMapManager.GetDllAutoUpdateStateAsync(
                    update.DllMod.Id, installedFullMods);

                if (state == InstallationMapManager.DllAutoUpdateState.Enabled)
                    autoUpdates.Add(update);
                else
                    manualUpdates.Add(update);
            }

            _log.Write($"[DllUpdateManager] Auto-update: {autoUpdates.Count}, Manual: {manualUpdates.Count}");
            return (autoUpdates, manualUpdates);
        }

        /// <summary>
        /// Uruchamia ciche (auto) aktualizacje DLL. Zwraca wyniki.
        /// Błędy są logowane, ale nie przerywają procesu.
        /// </summary>
        public async Task<List<DllUpdateResult>> RunAutoUpdatesAsync(
            List<DllUpdateInfo> autoUpdates, string platform)
        {
            if (!autoUpdates.Any())
                return new List<DllUpdateResult>();

            _log.Write($"[DllUpdateManager] Rozpoczynam ciche auto-update {autoUpdates.Count} DLL...");

            var results = await UpdateAllDllsAsync(autoUpdates, platform);

            foreach (var result in results)
            {
                if (result.FailedUpdates > 0)
                {
                    _log.Write($"[DllUpdateManager] ⚠ Auto-update {result.DllName}: " +
                               $"{result.SuccessfulUpdates}/{result.TotalLocations} udanych, " +
                               $"{result.FailedUpdates} nieudanych – ponowna próba przy następnym sprawdzeniu");
                }
                else
                {
                    _log.Write($"[DllUpdateManager] ✓ Auto-update {result.DllName}: " +
                               $"{result.SuccessfulUpdates}/{result.TotalLocations} udanych");
                }
            }

            return results;
        }
    }
}
