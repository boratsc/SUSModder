using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;
using SUSModder.Core.Services;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using System.IO;
using System.Diagnostics;

namespace SUSModder.Views
{
    public partial class AppUpdateDialog : Window
    {
        private readonly AppUpdateService _updateService;
        private readonly string _currentVersion;
        private readonly string _newVersion;
        public bool UpdateAccepted { get; private set; } = false;

        // Publiczny konstruktor bezparametrowy - wymagany dla XAML
        public AppUpdateDialog()
        {
            InitializeComponent();

            _currentVersion = "0.0.0";
            _newVersion = "0.0.0";

            // Inicjalizuj service z domyślnymi wartościami
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = configBuilder.Build();

            var diagnosticsOutput = new DebugDiagnosticsOutput();
            _updateService = new AppUpdateService(_currentVersion, configuration, diagnosticsOutput);

            // Ustaw domyślne teksty
            CurrentVersionText.Text = $"Obecna wersja: {_currentVersion}";
            NewVersionText.Text = $"Nowa wersja: {_newVersion}";
        }

        public AppUpdateDialog(string currentVersion, string newVersion) : this()
        {
            _currentVersion = currentVersion;
            _newVersion = newVersion;

            // Ponownie inicjalizuj service z prawidłową wersją
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = configBuilder.Build();

            var diagnosticsOutput = new DebugDiagnosticsOutput();
            _updateService = new AppUpdateService(currentVersion, configuration, diagnosticsOutput);

            // Ustaw prawidłowe teksty
            CurrentVersionText.Text = $"Obecna wersja: {currentVersion}";
            NewVersionText.Text = $"Nowa wersja: {newVersion}";
        }

        // reszta metod pozostaje bez zmian...
        private async void YesButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateAccepted = true;
            await StartUpdateProcess();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateAccepted = false;
            Close();
        }

        private async Task StartUpdateProcess()
        {
            try
            {
                // Ukryj przyciski i pokaż progress
                YesButton.IsEnabled = false;
                NoButton.IsEnabled = false;
                ProgressPanel.IsVisible = true;

                // Progress reporter
                var progress = new Progress<int>(percentage =>
                {
                    ProgressBar.Value = percentage;
                    ProgressText.Text = $"Pobieranie aktualizacji... {percentage}%";
                });

                // Pobierz aktualizację
                var downloadResult = await _updateService.DownloadUpdateAsync(progress);

                if (!downloadResult.Success)
                {
                    await ShowErrorAndClose($"Błąd podczas pobierania aktualizacji: {downloadResult.ErrorMessage}");
                    return;
                }

                // Aktualizuj progress
                ProgressText.Text = "Uruchamianie updatera...";
                ProgressBar.Value = 100;

                // Uruchom updater
                bool updaterStarted = _updateService.RunUpdater(downloadResult.FilePath!);

                if (!updaterStarted)
                {
                    await ShowErrorAndClose("Nie udało się uruchomić updatera.");
                    return;
                }

                // Zamknij aplikację
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                await ShowErrorAndClose($"Nieoczekiwany błąd: {ex.Message}");
            }
        }

        private async Task ShowErrorAndClose(string errorMessage)
        {
            var errorDialog = new MessageDialog("Błąd aktualizacji", errorMessage);
            await errorDialog.ShowDialog(this);
            Close();
        }

        private class DebugDiagnosticsOutput : IDiagnosticsOutput
        {
            public void Write(string message)
            {
                Debug.WriteLine($"[AppUpdate] {message}");
            }
        }
    }
}
