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
                IsPaneOpen = false;
                // Zamknij inne panele i pokaż panel ustawień
                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;
                IsSUStatsConfigVisible = false;
                IsRecommendedDiscordsVisible = false;
                IsRepairOptionsVisible = false;
                CloseDllSelectionModal();
                IsAppSettingsVisible = true;
                SelectedMod = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening app settings panel: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć ustawień: {ex.Message}", "Błąd");
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
        
        private async void OnUpdateChannelChanged(string newChannel)
        {
            System.Diagnostics.Debug.WriteLine($"Update channel changed to: {newChannel}");
            
            // Pokaż dialog o konieczności restartu
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ShowMessageAsync(
                    "Restart wymagany", 
                    "Zmiana kanału aktualizacji wymaga restartu aplikacji.\n\n" +
                    "Aplikacja zostanie zrestartowana automatycznie."
                );
                
                // Restart aplikacji
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            });
        }
        
        public void HandleUpdateChannelChange(string newChannel)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindowVM] Update channel changed to: {newChannel}");
            OnUpdateChannelChanged(newChannel);
        }

        private void ShowAdditionalActions()
        {
            IsPaneOpen = false;
            IsAdditionalActionsVisible = !IsAdditionalActionsVisible;

            if (IsAdditionalActionsVisible)
            {
                IsInfoPanelVisible = false;
                IsLobbyBoardVisible = false;
                IsDllModificationsVisible = false;
                IsSUStatsConfigVisible = false;
                IsAppSettingsVisible = false;
                IsRecommendedDiscordsVisible = false;
                IsRepairOptionsVisible = false;
                IsDllInstallDialogVisible = false;
                CloseDllSelectionModal();
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void ShowInfo()
        {
            IsPaneOpen = false;
            IsInfoPanelVisible = !IsInfoPanelVisible;

            if (IsInfoPanelVisible)
            {
                IsAdditionalActionsVisible = false;
                IsLobbyBoardVisible = false;
                IsDllModificationsVisible = false;
                IsSUStatsConfigVisible = false;
                IsAppSettingsVisible = false;
                IsRecommendedDiscordsVisible = false;
                IsRepairOptionsVisible = false;
                IsDllInstallDialogVisible = false;
                CloseDllSelectionModal();
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }
    }
}
