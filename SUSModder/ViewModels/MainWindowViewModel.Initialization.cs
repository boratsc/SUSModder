using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Diagnostics;
using SUSModder.Services;
using SUSModder.Views;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający logikę inicjalizacji aplikacji
    /// </summary>
    public partial class MainWindowViewModel
    {
        private async void InitializeApplicationAsync()
        {
            try
            {
                // KROK 1: Ładowanie konfiguracji modów
                await Task.Run(() =>
                {
                    var configService = new ConfigService();
                    var configs = configService.LoadConfig();
                    System.Diagnostics.Debug.WriteLine($"Loaded {configs.Count} configs from service");

                    // Zapisz do pola klasy
                    _loadedConfigs = configs;
                });

                // KROK 2: Wyszukiwanie i konfiguracja Vanilla Among Us - PRZED sprawdzaniem aktualizacji
                bool vanillaSetupSuccess = await SetupVanillaGameAsync();

                if (vanillaSetupSuccess)
                {
                    // KROK 3: Przeładuj konfigurację po dodaniu Vanilla
                    await Task.Run(() =>
                    {
                        var configService = new ConfigService();
                        _loadedConfigs = configService.LoadConfig();
                    });

                    // KROK 4: Sprawdzanie aktualizacji modów
                    await CheckForModUpdatesAsync();
                }

                // KROK 5: Odświeżenie interfejsu (zawsze, niezależnie od sukcesu Vanilla)
                await RefreshModsListAsync();

                // KROK 6: Odświeżenie panelu statusu
                await RefreshStatusBarAsync();

                // KROK 7: Uruchom auto-refresh statusu API w tle
                StartApiStatusAutoRefresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during application initialization: {ex.Message}");
                await ShowDetailedErrorDialogAsync("Błąd podczas inicjalizacji aplikacji", ex);
            }
        }

        private async Task<bool> SetupVanillaGameAsync()
        {
            while (true)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Starting Vanilla game setup...");

                    // Stwórz IConfiguration z appsettings.json
                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                    var configuration = configBuilder.Build();

                    // Wywołaj asynchroniczną wersję z interfejsem użytkownika
                    bool success = await GameLocator.CheckAndSetupVanillaModAsync(
                        _loadedConfigs,
                        configuration,
                        _userInteractionService
                    );

                    System.Diagnostics.Debug.WriteLine($"Vanilla game setup completed with result: {success}");

                    if (success)
                        return true;

                    // Jeśli niepowodzenie, przejdź do obsługi poniżej
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during Vanilla setup: {ex.Message}");
                }

                // Dialog z wyborem: Spróbuj ponownie / Zamknij
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var dialog = new ConfirmDialog(
                    "Nie wybrano pliku Among Us.exe",
                    "Nie udało się wykryć poprawnej instalacji gry Among Us. Wybierz poprawny plik Among Us.exe lub zamknij aplikację."
                );
                dialog.OkButtonText = "Spróbuj ponownie";
                dialog.CancelButtonText = "Zamknij";

                if (mainWindow != null)
                    await dialog.ShowDialog(mainWindow);

                if (dialog.Result)
                {
                    // Spróbuj ponownie
                    continue;
                }
                else
                {
                    // Usuń config.json i zamknij aplikację
                    try
                    {
                        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                        string configPath = Path.Combine(exeDir, "config.json");
                        if (File.Exists(configPath))
                            File.Delete(configPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Błąd podczas kasowania config.json: {ex.Message}");
                    }
                    Environment.Exit(0);
                    return false;
                }
            }
        }

        private async void CheckForAppUpdatesOnStartup()
        {
            try
            {
                // Poczekaj chwilę żeby UI się załadował
                await Task.Delay(2000);

                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[AppUpdate] {message}");
                });

                var updateService = new AppUpdateService(AppVersion, configuration, diagnosticsOutput);
                var updateCheck = await updateService.CheckForUpdateAsync();

                if (updateCheck.Success && updateCheck.IsUpdateAvailable)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var updateDialog = new AppUpdateDialog(updateCheck.CurrentVersion, updateCheck.LatestVersion);
                        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                        if (mainWindow != null)
                        {
                            await updateDialog.ShowDialog(mainWindow);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for app updates: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - aktualizacje nie są krytyczne
            }
        }

        private void LoadAppVersion()
        {
            var configService = new ConfigService();
            AppVersion = configService.GetAppVersion();
        }

        private void LoadWindowTitle()
        {
            try
            {
                string platform = DeterminePlatform();
                WindowTitle = $"SUSModder | {platform}";
                System.Diagnostics.Debug.WriteLine($"Window title set to: {WindowTitle}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading window title: {ex.Message}");
                WindowTitle = "SUSModder"; // Fallback
            }
        }

        private void ClearEpicLogsOnStartup()
        {
            try
            {
                string platform = DeterminePlatform();
                if (platform.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                {
                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Epic Startup] {message}");
                    });

                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    epicManager.ClearLegendaryLog();
                    System.Diagnostics.Debug.WriteLine("Epic legendary log cleared on startup");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear Epic logs on startup: {ex.Message}");
            }
        }
    }
}
