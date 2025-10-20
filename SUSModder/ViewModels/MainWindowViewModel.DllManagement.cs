using System;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający zarządzanie modyfikacjami DLL
    /// </summary>
    public partial class MainWindowViewModel
    {
        private void ShowDllModifications()
        {
            IsDllModificationsVisible = !IsDllModificationsVisible;

            if (IsDllModificationsVisible)
            {
                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                SelectedMod = null;
                IsDllInstallDialogVisible = false;

                LoadDllMods();
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void SelectDllMod(ModItem dllMod)
        {
            SelectedDllMod = dllMod;
            IsDllInstallDialogVisible = true;
            LoadAvailableFullMods();
        }

        private void CloseDllDialog()
        {
            IsDllInstallDialogVisible = false;
            SelectedDllMod = null;
            ModsWithDllInstalled.Clear();
            ModsWithoutDllInstalled.Clear();
        }

        private void LoadDllMods()
        {
            try
            {
                var dllConfigs = _dllModificationService.GetDllMods();
                var dllModItems = dllConfigs.Select(ModItemAdapter.FromConfig).ToList();

                DllMods.Clear();
                foreach (var mod in dllModItems)
                {
                    DllMods.Add(mod);
                }

                System.Diagnostics.Debug.WriteLine($"Loaded {DllMods.Count} DLL mods");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading DLL mods: {ex.Message}");
            }
        }

        private void LoadAvailableFullMods()
        {
            if (SelectedDllMod == null) return;

            try
            {
                string platform = DeterminePlatform();
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);

                // Załaduj mody z zainstalowaną DLL
                var modsWithDll = _dllModificationService.GetModsWithDllInstalled(dllConfig, platform);
                var modsWithDllItems = modsWithDll.Select(ModItemAdapter.FromConfig).ToList();

                ModsWithDllInstalled.Clear();
                foreach (var mod in modsWithDllItems)
                {
                    ModsWithDllInstalled.Add(mod);
                }

                // Załaduj mody bez zainstalowanej DLL
                var modsWithoutDll = _dllModificationService.GetModsWithoutDllInstalled(dllConfig, platform);
                var modsWithoutDllItems = modsWithoutDll.Select(ModItemAdapter.FromConfig).ToList();

                ModsWithoutDllInstalled.Clear();
                foreach (var mod in modsWithoutDllItems)
                {
                    ModsWithoutDllInstalled.Add(mod);
                }

                System.Diagnostics.Debug.WriteLine($"Found {ModsWithDllInstalled.Count} mods with DLL installed");
                System.Diagnostics.Debug.WriteLine($"Found {ModsWithoutDllInstalled.Count} mods without DLL installed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading available full mods: {ex.Message}");
            }
        }

        private async Task InstallDllToMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null) return;

            try
            {
                // Konwertuj ModItem na ModConfiguration
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);

                string platform = DeterminePlatform();

                string? installedPath = await _dllModificationService.InstallDllToModAsync(dllConfig, targetConfig, platform);

                if (!string.IsNullOrEmpty(installedPath))
                {
                    LoadAvailableFullMods(); // Odśwież listę
                }
                else
                {
                    await ShowErrorDialogAsync("Nie udało się zainstalować modyfikacji DLL.", "Błąd instalacji");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error installing DLL: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd instalacji");
            }
        }

        private async Task UninstallDllFromMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null) return;

            try
            {
                bool confirm = await ShowConfirmDialogAsync(
                    $"Czy na pewno chcesz usunąć mod DLL '{SelectedDllMod.Name}' z '{targetMod.Name}'?",
                    "Potwierdzenie usunięcia");

                if (!confirm) return;

                // Konwertuj ModItem na ModConfiguration
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);

                string platform = DeterminePlatform();

                bool success = await _dllModificationService.UninstallDllFromModAsync(dllConfig, targetConfig, platform);

                if (success)
                {
                    LoadAvailableFullMods(); // Odśwież listę
                }
                else
                {
                    await ShowErrorDialogAsync("Nie udało się usunąć modyfikacji DLL.", "Błąd usuwania");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error uninstalling DLL: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas usuwania: {ex.Message}", "Błąd usuwania");
            }
        }
    }
}
