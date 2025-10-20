using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.ViewModels.Helpers;

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
                var configService = new ConfigService();
                var allConfigs = configService.LoadConfig();

                // Znajdź mod "Among Us" (Vanilla)
                var amongUsConfig = allConfigs?.FirstOrDefault(c =>
                    c.ModName.Equals("Among Us", StringComparison.OrdinalIgnoreCase) ||
                    c.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
                    c.ModName.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
                );

                if (amongUsConfig != null && !string.IsNullOrEmpty(amongUsConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[Platform Detection] Found Among Us config with path: {amongUsConfig.InstallPath}");

                    // Sprawdź czy ścieżka zawiera "Epic" lub "EpicGames"
                    if (amongUsConfig.InstallPath.Contains("Epic", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine("[Platform Detection] Detected Epic Games platform");
                        return "epic";
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[Platform Detection] Detected Steam platform");
                        return "steam";
                    }
                }

                // Jeśli nie znaleziono, zwróć steam jako domyślny
                System.Diagnostics.Debug.WriteLine("[Platform Detection] No Among Us config found, defaulting to steam");
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas odświeżania listy modów: {ex.Message}");
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
