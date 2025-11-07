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

        private void RefreshModsSortingKeepSelection(ModItem selectedMod)
        {
            var currentMods = Mods.ToList();

            var sorted = currentMods
                .OrderBy(m => !m.Name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(m => string.IsNullOrEmpty(m.InstallPath) ? 1 : 0)
                .ThenBy(m => m.Name)
                .ToList();

            Mods.Clear();
            foreach (var mod in sorted)
            {
                Mods.Add(mod);
            }

            // Przywróć zaznaczenie
            SelectedMod = Mods.FirstOrDefault(m => m.Name == selectedMod.Name);
        }

        private async Task RefreshModsListAsync()
        {
            try
            {
                var configService = new ConfigService();
                var configs = configService.LoadConfig();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Zachowaj obecny wybór
                    var selectedModName = SelectedMod?.Name;

                    Mods.Clear();

                    // Filtruj tylko mody typu "full" i "Vanilla" (bez DLL)
                    var fullMods = configs
                        .Where(c => c.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) || 
                                    c.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase));

                    // Weryfikuj InstallPath - jeśli folder nie istnieje, wyczyść path
                    foreach (var config in fullMods)
                    {
                        if (!string.IsNullOrEmpty(config.InstallPath))
                        {
                            if (!Directory.Exists(config.InstallPath))
                            {
                                System.Diagnostics.Debug.WriteLine($"[RefreshModsList] Mod '{config.ModName}' InstallPath not found, clearing: {config.InstallPath}");
                                config.InstallPath = null;
                            }
                        }
                    }

                    // Sortuj: Vanilla na górze, potem zainstalowane, potem reszta
                    var sortedConfigs = fullMods
                        .OrderBy(c => !c.ModName.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenBy(c => string.IsNullOrEmpty(c.InstallPath) ? 1 : 0)
                        .ThenBy(c => c.ModName);

                    foreach (var config in sortedConfigs)
                    {
                        var modItem = ModItemAdapter.FromConfig(config);
                        Mods.Add(modItem);
                    }

                    // Przywróć wybór jeśli możliwe
                    if (!string.IsNullOrEmpty(selectedModName))
                    {
                        SelectedMod = Mods.FirstOrDefault(m => m.Name == selectedModName);
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
                await CheckForModUpdatesForStatusBarAsync();
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
