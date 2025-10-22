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
using SUSModder.Core.Utilities;

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

                // KROK 5.5: Preload ikon modów w tle
                _ = Task.Run(async () =>
                {
                    var iconFileNames = _loadedConfigs
                        .Select(c => c.PngFileName)
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Distinct()
                        .ToList();
                    
                    await ModIconPreloader.PreloadIconsAsync(iconFileNames);
                });

                // KROK 6: Odświeżenie panelu statusu
                await RefreshStatusBarAsync();

                // KROK 7: Uruchom auto-refresh statusu API i aktualizacji modów w tle
                StartApiStatusAutoRefresh();
                StartModUpdatesAutoRefresh();

                // KROK 8: Pierwsze sprawdzenie aktualizacji modów
                await CheckForModUpdatesForStatusBarAsync();

                // KROK 9: Auto-logowanie SUStats w tle (asynchronicznie, nie blokujemy UI)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SUStatsConfigViewModel.TryAutoLoginOnStartupAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas auto-logowania w tle: {ex.Message}");
                    }
                });
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

        /// <summary>
        /// Migruje istniejące instalacje do Installation Map System
        /// </summary>
        private async Task MigrateExistingInstallationsAsync()
        {
            try
            {
                Console.WriteLine("[InstallationMap] ===== ROZPOCZĘCIE MIGRACJI =====");
                System.Diagnostics.Debug.WriteLine("[InstallationMap] Rozpoczynam migrację istniejących instalacji...");

                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    Console.WriteLine($"[Migration] {message}");
                    System.Diagnostics.Debug.WriteLine($"[Migration] {message}");
                });

                var configService = new ConfigService();
                var modConfigs = configService.LoadConfig();
                string platform = DeterminePlatform().ToLower();

                Console.WriteLine($"[InstallationMap] Załadowano {modConfigs.Count} konfiguracji, platforma: {platform}");

                // KROK 0: Odkryj zainstalowane mody z katalogów
                Console.WriteLine("[InstallationMap] KROK 0: Odkrywanie zainstalowanych modów...");
                string modsBasePath = PathSettings.ModsInstallPath;
                Console.WriteLine($"[InstallationMap] Skanowanie katalogu: {modsBasePath}");

                var discoveredMaps = await InstallationMapManager.DiscoverInstalledModsAsync(
                    modsBasePath,
                    diagnosticsOutput
                );

                Console.WriteLine($"[InstallationMap] Odkryto {discoveredMaps.Count} modów z Installation Map");

                // Import odkrytych modów do config.json
                if (discoveredMaps.Count > 0)
                {
                    var imported = InstallationMapManager.ImportDiscoveredMods(
                        discoveredMaps,
                        modConfigs,
                        diagnosticsOutput
                    );

                    if (imported.Count > 0)
                    {
                        ConfigManager.SaveConfig(modConfigs);
                        Console.WriteLine($"[InstallationMap] ✅ Zaimportowano {imported.Count} modów do config.json");

                        // Odśwież UI po imporcie
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await RefreshModsListAsync();
                        });
                    }
                }

                // KROK 1: Walidacja - wyczyść mody które nie istnieją
                Console.WriteLine("[InstallationMap] KROK 1: Walidacja...");
                int cleaned = InstallationMapManager.ValidateAndCleanInstalledMods(
                    modConfigs,
                    diagnosticsOutput
                );

                // Jeśli coś zostało wyczyszczone, zapisz config
                if (cleaned > 0)
                {
                    ConfigManager.SaveConfig(modConfigs);
                    Console.WriteLine($"[InstallationMap] ✅ Wyczyszczono {cleaned} modów z config.json");
                    System.Diagnostics.Debug.WriteLine($"[InstallationMap] Wyczyszczono {cleaned} modów z config.json");

                    // Odśwież UI po oczyszczeniu
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await RefreshModsListAsync();
                    });
                }
                else
                {
                    Console.WriteLine("[InstallationMap] ✅ Brak modów do wyczyszczenia");
                }

                // KROK 2: Migruj istniejące instalacje (te które mają pliki ale nie mają mapy)
                Console.WriteLine("[InstallationMap] KROK 2: Migracja...");
                int migrated = await InstallationMapManager.MigrateExistingInstallationsAsync(
                    modConfigs,
                    platform,
                    diagnosticsOutput
                );

                Console.WriteLine($"[InstallationMap] ===== MIGRACJA ZAKOŃCZONA: {migrated} modów zmigrowano =====");
                System.Diagnostics.Debug.WriteLine($"[InstallationMap] Migracja zakończona: {migrated} modów zmigrowano");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[InstallationMap] ❌ BŁĄD podczas migracji: {ex.Message}");
                Console.WriteLine($"[InstallationMap] Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[InstallationMap] Błąd podczas migracji: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - migracja nie jest krytyczna
            }
        }
    }
}
