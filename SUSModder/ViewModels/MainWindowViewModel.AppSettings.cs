using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający zarządzanie ustawieniami aplikacji i widokami pomocniczymi
    /// </summary>
    public partial class MainWindowViewModel
    {
        private async void ShowAppSettings()
        {
            try
            {
                var settingsWindow = new AppSettingsWindow();

                // Bezpieczne rzutowanie z sprawdzeniem null
                if (settingsWindow.DataContext is not AppSettingsViewModel settingsViewModel)
                {
                    System.Diagnostics.Debug.WriteLine("Error: AppSettingsWindow DataContext is not AppSettingsViewModel");
                    await ShowErrorDialogAsync("Błąd inicjalizacji okna ustawień.", "Błąd");
                    return;
                }

                // Subskrybuj event zapisania ustawień
                settingsViewModel.SettingsSaved += OnSettingsSaved;

                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await settingsWindow.ShowDialog(mainWindow);
                }

                // Odsubskrybuj po zamknięciu okna
                settingsViewModel.SettingsSaved -= OnSettingsSaved;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening settings window: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć okna ustawień: {ex.Message}", "Błąd");
            }
        }

        private void OnSettingsSaved()
        {
            System.Diagnostics.Debug.WriteLine("Settings were saved - refreshing application state");

            Dispatcher.UIThread.InvokeAsync(async () =>
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
            });
        }

        private void ShowAdditionalActions()
        {
            IsAdditionalActionsVisible = !IsAdditionalActionsVisible;

            if (IsAdditionalActionsVisible)
            {
                IsInfoPanelVisible = false;
                IsDllModificationsVisible = false;
                IsDllInstallDialogVisible = false;
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void ShowInfo()
        {
            IsInfoPanelVisible = !IsInfoPanelVisible;

            if (IsInfoPanelVisible)
            {
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;
                IsDllInstallDialogVisible = false;
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }
    }
}
