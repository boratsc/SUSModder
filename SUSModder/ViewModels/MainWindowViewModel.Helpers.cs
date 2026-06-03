using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;
using SUSModder.ViewModels.Helpers;
using SUSModder.Services;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający metody operacji na modach (Install, Update, Uninstall)
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Określa platformę gry (Steam/Epic) na podstawie konfiguracji
        /// </summary>
        public string DeterminePlatform()
        {
            try
            {
                // Pobierz z user settings
                var userSettings = _userSettingsService.LoadUserSettings();
                if (!string.IsNullOrEmpty(userSettings.Mode))
                {
                    System.Diagnostics.Debug.WriteLine($"[Platform Detection] Found Mode in user-settings: {userSettings.Mode}");
                    return userSettings.Mode.ToLower();
                }

                // Jeśli nie znaleziono, zwróć steam jako domyślny
                System.Diagnostics.Debug.WriteLine("[Platform Detection] No user settings found, defaulting to steam");
                return "steam";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Platform Detection] Error: {ex.Message}");
                return "steam"; // Domyślnie Steam
            }
        }

        private void WireModItemPropertyChanged(ModItem modItem)
        {
            modItem.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModItem.IsCheckedForBulk))
                    RaiseBulkUiProperties();
            };
        }

        private List<ModConfiguration> PrepareSortedFullModConfigs(List<ModConfiguration> configs)
        {
            var fullMods = configs
                .Where(c => c.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                            c.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var currentPlatform = DeterminePlatform();

            foreach (var config in fullMods)
            {
                if (string.IsNullOrEmpty(config.InstallPath))
                    continue;

                bool isVanilla = config.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
                                 config.Id == 0 ||
                                 config.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase);

                if (isVanilla && currentPlatform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Directory.Exists(config.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[RefreshModsList] Mod '{config.ModName}' InstallPath not found, clearing: {config.InstallPath}");
                    config.InstallPath = null;
                }
            }

            return fullMods
                .OrderBy(c => !c.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(c => string.IsNullOrEmpty(c.InstallPath) ? 1 : 0)
                .ThenBy(c => c.ModName)
                .ToList();
        }

        private void RefreshModsSortingKeepSelection(ModItem selectedMod)
        {
            _suppressSelectedModPanelReset = true;
            try
            {
                var currentMods = Mods.ToList();

                var sorted = currentMods
                    .OrderBy(m => !m.IsVanilla ? 1 : 0)
                    .ThenBy(m => string.IsNullOrEmpty(m.InstallPath) ? 1 : 0)
                    .ThenBy(m => m.Name)
                    .ToList();

                Mods.Clear();
                foreach (var mod in sorted)
                    Mods.Add(mod);

                SelectedMod = Mods.FirstOrDefault(m => m.Id == selectedMod.Id)
                                ?? Mods.FirstOrDefault(m => m.Name == selectedMod.Name);
            }
            finally
            {
                _suppressSelectedModPanelReset = false;
            }
        }

        private void SyncModsListInPlace(IReadOnlyList<ModConfiguration> sortedConfigs)
        {
            var selectedId = SelectedMod?.Id;
            var selectedName = SelectedMod?.Name;
            var bulkCheckedIds = Mods.Where(m => m.IsCheckedForBulk).Select(m => m.Id).ToHashSet();
            var updateBadges = Mods.Where(m => m.HasUpdateAvailable).Select(m => m.Id).ToHashSet();

            var configById = sortedConfigs.ToDictionary(c => c.Id);
            var existingById = Mods.ToDictionary(m => m.Id);

            foreach (var mod in Mods.ToList())
            {
                if (!configById.ContainsKey(mod.Id))
                    Mods.Remove(mod);
            }

            foreach (var config in sortedConfigs)
            {
                if (existingById.TryGetValue(config.Id, out var existing))
                {
                    ModItemAdapter.ApplyConfigToModItem(existing, config);
                    existing.IsCheckedForBulk = bulkCheckedIds.Contains(config.Id);
                    existing.HasUpdateAvailable = updateBadges.Contains(config.Id);
                }
                else
                {
                    var modItem = ModItemAdapter.FromConfig(config);
                    WireModItemPropertyChanged(modItem);
                    Mods.Add(modItem);
                }
            }

            var targetIds = sortedConfigs.Select(c => c.Id).ToList();
            var currentIds = Mods.Select(m => m.Id).ToList();
            if (!targetIds.SequenceEqual(currentIds))
            {
                var ordered = targetIds
                    .Select(id => Mods.First(m => m.Id == id))
                    .ToList();

                Mods.Clear();
                foreach (var mod in ordered)
                    Mods.Add(mod);
            }

            if (selectedId.HasValue)
            {
                SelectedMod = Mods.FirstOrDefault(m => m.Id == selectedId.Value)
                              ?? (string.IsNullOrEmpty(selectedName)
                                  ? null
                                  : Mods.FirstOrDefault(m => m.Name == selectedName));
            }
        }

        private async Task FlushPendingModsListRefreshAsync()
        {
            if (!_pendingModsListRefresh || _activeInstallationsCount > 0)
                return;

            _pendingModsListRefresh = false;
            var checkUpdates = _pendingModsListRefreshCheckUpdates;
            await RefreshModsListAsync(
                checkUpdates: checkUpdates,
                deferIfToolModalOpen: false);
        }

        private async Task RefreshModsListAsync(
            bool checkUpdates = true,
            List<ModConfiguration>? preloadedConfigs = null,
            bool showLoadingSkeleton = false,
            bool deferIfToolModalOpen = false)
        {
            if (_activeInstallationsCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshModsListAsync] Pomijam odświeżenie — {_activeInstallationsCount} aktywnych instalacji");
                return;
            }

            if (deferIfToolModalOpen && IsAnyToolModalOpen)
            {
                _pendingModsListRefresh = true;
                _pendingModsListRefreshCheckUpdates = checkUpdates;
                System.Diagnostics.Debug.WriteLine("[RefreshModsListAsync] Odłożono — otwarty panel narzędziowy");
                return;
            }

            try
            {
                List<ModConfiguration> configs;
                if (preloadedConfigs != null)
                    configs = preloadedConfigs;
                else
                {
                    var configService = new ConfigService();
                    configs = configService.LoadConfig();
                }

                var sortedConfigs = PrepareSortedFullModConfigs(configs);
                var useSkeleton = showLoadingSkeleton || Mods.Count == 0;
                var useInPlaceSync = !useSkeleton && Mods.Count > 0;

                if (useSkeleton)
                    await Dispatcher.UIThread.InvokeAsync(() => IsModsLoading = true);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _suppressSelectedModPanelReset = true;
                    try
                    {
                        if (useInPlaceSync)
                        {
                            SyncModsListInPlace(sortedConfigs);
                        }
                        else
                        {
                            var selectedModId = SelectedMod?.Id;
                            var selectedModName = SelectedMod?.Name;
                            var bulkCheckedIds = Mods.Where(m => m.IsCheckedForBulk).Select(m => m.Id).ToHashSet();
                            var updateBadgeIds = Mods.Where(m => m.HasUpdateAvailable).Select(m => m.Id).ToHashSet();

                            Mods.Clear();

                            foreach (var config in sortedConfigs)
                            {
                                var modItem = ModItemAdapter.FromConfig(config);
                                modItem.IsCheckedForBulk = bulkCheckedIds.Contains(config.Id);
                                modItem.HasUpdateAvailable = updateBadgeIds.Contains(config.Id);
                                WireModItemPropertyChanged(modItem);
                                Mods.Add(modItem);
                            }

                            if (selectedModId.HasValue)
                            {
                                SelectedMod = Mods.FirstOrDefault(m => m.Id == selectedModId.Value)
                                              ?? (string.IsNullOrEmpty(selectedModName)
                                                  ? null
                                                  : Mods.FirstOrDefault(m => m.Name == selectedModName));
                            }
                        }

                        if (useSkeleton)
                            IsModsLoading = false;
                    }
                    finally
                    {
                        _suppressSelectedModPanelReset = false;
                    }
                });

                // Sprawdź dostępność ról dla wszystkich modów w tle (nie blokuj UI)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var rolesService = new RolesService();
                        
                        foreach (var mod in Mods.ToList())
                        {
                            if (!string.IsNullOrWhiteSpace(mod.InstallPath))
                            {
                                try
                                {
                                    var installMap = await InstallationMapManager.LoadInstallationMapAsync(mod.InstallPath);
                                    if (installMap?.FullMod != null)
                                    {
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            mod.ModVersion = installMap.FullMod.ModVersion;
                                            mod.DisableAutoUpdatePrompt = installMap.FullMod.DisableAutoUpdatePrompt;
                                            mod.AutoUpdateEnabled = installMap.FullMod.AutoUpdateEnabled;
                                            mod.PinnedInstallVersion = installMap.FullMod.PinnedInstallVersion;
                                        });
                                    }
                                    else
                                    {
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            mod.DisableAutoUpdatePrompt = false;
                                            mod.AutoUpdateEnabled = false;
                                            mod.PinnedInstallVersion = null;
                                        });
                                    }
                                }
                                catch
                                {
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        mod.DisableAutoUpdatePrompt = false;
                                        mod.AutoUpdateEnabled = false;
                                        mod.PinnedInstallVersion = null;
                                    });
                                }
                            }
                            else
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    mod.DisableAutoUpdatePrompt = false;
                                    mod.AutoUpdateEnabled = false;
                                    mod.PinnedInstallVersion = null;
                                });
                            }

                            // Pomiń Vanilla - nie ma ról
                            if (mod.IsVanilla)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Roles Check] {mod.Name} - Vanilla, HasRoles = false");
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    mod.HasRoles = false;
                                });
                                continue;
                            }

                            // Sprawdź czy mod ma role w API
                            System.Diagnostics.Debug.WriteLine($"[Roles Check] Checking roles for {mod.Name} (ID: {mod.Id})...");
                            bool hasRoles = await rolesService.CheckIfHasRolesAsync(mod.Id);
                            System.Diagnostics.Debug.WriteLine($"[Roles Check] {mod.Name} (ID: {mod.Id}) - HasRoles = {hasRoles}");
                            
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                mod.HasRoles = hasRoles;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Błąd podczas sprawdzania dostępności ról: {ex.Message}");
                    }
                });

                // Odśwież licznik dostępnych aktualizacji
                if (checkUpdates)
                {
                    await CheckForModUpdatesForStatusBarAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas odświeżania listy modów: {ex.Message}");
            }
        }

        public async Task RefreshAfterSettingsChangeAsync()
        {
            try
            {
                // Wymuś przeładowanie ustawień ścieżki
                PathSettings.RefreshSettings();

                // Wymuś przeładowanie ustawień deweloperskich
                DeveloperModeSettings.RefreshSettings();

                // Przeładuj listę modów
                await RefreshModsListAsync();
                this.RaisePropertyChanged(nameof(IsDeveloperMode));

                System.Diagnostics.Debug.WriteLine($"Application refreshed with new settings - ModsInstallPath: {PathSettings.ModsInstallPath}, DeveloperMode: {DeveloperModeSettings.IsEnabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing after settings change: {ex.Message}");
            }
        }

        // Helper class dla diagnostyki
        private class DebugDiagnosticsOutput : IDiagnosticsOutput
        {
            public void Write(string message)
            {
                System.Diagnostics.Debug.WriteLine($"[ModUpdater] {message}");
            }
        }
    }
}
