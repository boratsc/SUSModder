using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Services;

namespace SUSModder.Views
{
    public partial class VelopackUpdateDialog : Window
    {
        private VelopackUpdateService? _updateService;
        private VelopackUpdateCheckResult _checkResult;
        private string _currentVersion = string.Empty;
        private string _newVersion = string.Empty;
        private bool _isInitialized;

        public VelopackUpdateDialog()
        {
            InitializeComponent();

            CurrentVersionText.Text = "-";
            NewVersionText.Text = "-";
        }

        public VelopackUpdateDialog(string currentVersion, VelopackUpdateCheckResult checkResult, VelopackUpdateService updateService)
            : this()
        {
            _currentVersion = currentVersion;
            _newVersion = checkResult.LatestVersion;
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _checkResult = checkResult;
            _isInitialized = true;

            CurrentVersionText.Text = currentVersion;
            NewVersionText.Text = _newVersion;
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            await StartUpdateProcessAsync();
        }

        private async Task StartUpdateProcessAsync()
        {
            try
            {
                if (!_isInitialized || _updateService == null)
                {
                    await ShowErrorAndCloseAsync("Okno aktualizacji zostało utworzone bez wymaganych zależności.");
                    return;
                }

                var updateInfo = _checkResult.UpdateInfo;
                if (updateInfo == null)
                {
                    await ShowErrorAndCloseAsync("Brak danych aktualizacji Velopack.");
                    return;
                }

                YesButton.IsEnabled = false;
                NoButton.IsEnabled = false;
                ProgressPanel.IsVisible = true;

                var progress = new Progress<int>(percentage =>
                {
                    ProgressBar.Value = percentage;
                    ProgressText.Text = $"Pobieranie aktualizacji... {percentage}%";
                });

                var downloadResult = await _updateService.DownloadUpdateAsync(updateInfo, progress);
                if (!downloadResult.Success)
                {
                    await ShowErrorAndCloseAsync($"Błąd podczas pobierania aktualizacji: {downloadResult.ErrorMessage}");
                    return;
                }

                ProgressText.Text = "Zastosowywanie aktualizacji...";
                ProgressBar.Value = 100;

                await _updateService.ApplyUpdateAndRestartAsync(updateInfo);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VelopackUpdateDialog] {ex}");
                await ShowErrorAndCloseAsync($"Nieoczekiwany błąd: {ex.Message}");
            }
        }

        private async Task ShowErrorAndCloseAsync(string message)
        {
            var dialog = new MessageDialog("Błąd aktualizacji", message);
            await dialog.ShowDialog(this);
            Close();
        }
    }
}
