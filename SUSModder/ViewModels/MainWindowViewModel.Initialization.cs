using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Publiczna metoda inicjalizacji aplikacji wywoływana przez App.axaml.cs
        /// </summary>
        /// <param name="progressCallback">Callback do raportowania postępu (0.0 - 1.0, status text)</param>
        public async Task InitializeApplicationAsync(Action<double, string>? progressCallback = null)
        {
            try
            {
                // KROK 0: Wczytaj wersję aplikacji
                LoadAppVersion();
                
                // KROK 1: Ładowanie konfiguracji modów (10%)
                progressCallback?.Invoke(0.0, "Ładowanie konfiguracji modów...");
                await Task.Run(() =>
                {
                    var configService = new ConfigService();
                    var configs = configService.LoadConfig();
                    System.Diagnostics.Debug.WriteLine($"Loaded {configs.Count} configs from service");
                    _loadedConfigs = configs;
                });

                // KROK 2: Wyszukiwanie i konfiguracja Vanilla Among Us (30%)
                progressCallback?.Invoke(0.1, "Wykrywanie Among Us...");
                bool vanillaSetupSuccess = await SetupVanillaGameAsync();

                // KROK 3: Przeładuj konfigurację (40%)
                progressCallback?.Invoke(0.3, "Aktualizacja konfiguracji...");
                await Task.Run(() =>
                {
                    var configService = new ConfigService();
                    _loadedConfigs = configService.LoadConfig();
                });

                // Odśwież tytuł okna po wykryciu platformy (zawsze, niezależnie czy Vanilla był nowy czy już istniał)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadWindowTitle();
                });

                // KROK 4: Odświeżenie interfejsu (70%)
                progressCallback?.Invoke(0.4, "Odświeżanie listy modów...");
                await RefreshModsListAsync();

                // KROK 5: Równoległe operacje w tle (80%)
                progressCallback?.Invoke(0.6, "Ładowanie zasobów...");
                var backgroundTasks = new List<Task>
                {
                    // Preload ikon modów
                    Task.Run(async () =>
                    {
                        var iconFileNames = _loadedConfigs
                            .Select(c => c.PngFileName)
                            .Where(f => !string.IsNullOrWhiteSpace(f))
                            .Distinct()
                            .ToList();
                        await ModIconPreloader.PreloadIconsAsync(iconFileNames);
                    }),

                    // Auto-logowanie SUStats
                    Task.Run(async () =>
                    {
                        try
                        {
                            await SUStatsConfigViewModel.TryAutoLoginOnStartupAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd podczas auto-logowania w tle: {ex.Message}");
                        }
                    }),

                    // Migracja instalacji
                    MigrateExistingInstallationsAsync()
                };

                // Nie czekamy na backgroundTasks - niech działają w tle

                // KROK 6: Odświeżenie panelu statusu (90%)
                progressCallback?.Invoke(0.8, "Finalizacja...");
                await RefreshStatusBarAsync();

                // KROK 7: Uruchom auto-refresh (95%)
                progressCallback?.Invoke(0.9, "Uruchamianie usług...");
                StartApiStatusAutoRefresh();
                StartModUpdatesAutoRefresh();

                // KROK 8: Pierwsze sprawdzenie aktualizacji dla status bar (100%)
                await CheckForModUpdatesForStatusBarAsync();
                progressCallback?.Invoke(1.0, "Gotowe!");

                // KROK 9: Sprawdź rejestrację w Windows Registry (nie blokuje, tylko na Windows)
                if (OperatingSystem.IsWindows())
                {
                    CheckWindowsRegistryRegistration();
                }

                // Uruchom sprawdzanie aktualizacji aplikacji w tle (nie blokuje)
                CheckForAppUpdatesOnStartup();
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
                    var vanillaMod = await GameLocator.CheckAndSetupVanillaModAsync(
                        _loadedConfigs,
                        configuration,
                        _userInteractionService
                    );

                    System.Diagnostics.Debug.WriteLine($"Vanilla game setup completed with result: {(vanillaMod != null ? "success" : "already exists or cancelled")}");

                    // Jeśli zwrócono nowy mod, dodaj go do listy
                    if (vanillaMod != null)
                    {
                        _loadedConfigs.Add(vanillaMod);
                        return true;
                    }

                    // Jeśli null - już istniał lub user anulował
                    var existingVanilla = _loadedConfigs.FirstOrDefault(x => x.ModName == "AmongUs" && x.ModType == "Vanilla");
                    if (existingVanilla != null)
                        return true;

                    // Jeśli niepowodzenie, przejdź do obsługi poniżej
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during Vanilla setup: {ex.Message}");
                }

                // Dialog z wyborem: Spróbuj ponownie / Zamknij
                var shouldRetry = await ShowInlineConfirmAsync(
                    "Nie wybrano pliku Among Us.exe",
                    "Nie wybrałeś pliku Among Us.exe. Spróbuj ponownie albo zamknij aplikację.",
                    "Spróbuj ponownie",
                    "Zamknij");

                if (shouldRetry)
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
                await Task.Delay(2000);
                await CheckForAppUpdatesCoreAsync(notifyWhenNoUpdates: false, showErrorsToUser: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for app updates: {ex.Message}");
            }
        }

        private Task CheckForAppUpdatesManuallyAsync()
        {
            return CheckForAppUpdatesCoreAsync(notifyWhenNoUpdates: true, showErrorsToUser: true);
        }

        private async Task CheckForAppUpdatesCoreAsync(bool notifyWhenNoUpdates, bool showErrorsToUser)
        {
            try
            {
                var basePath = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var diagnosticsOutput = new UIDiagnosticsOutput(message =>
                {
                    System.Diagnostics.Debug.WriteLine($"[AppUpdate] {message}");
                });

                await TryHandleVelopackAppUpdatesAsync(configuration, diagnosticsOutput, notifyWhenNoUpdates, showErrorsToUser);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for app updates: {ex.Message}");

                if (showErrorsToUser)
                {
                    await ShowErrorDialogAsync(BuildUpdateErrorMessage(ex.Message), GetUpdateDialogTitle());
                }
            }
        }

        private async Task TryHandleVelopackAppUpdatesAsync(IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput, bool notifyWhenNoUpdates, bool showErrorsToUser)
        {
            try
            {
                // Utwórz VelopackUpdateService jeśli nie istnieje (tylko raz)
                if (_velopackUpdateService == null)
                {
                    _velopackUpdateService = new VelopackUpdateService(AppVersion, configuration, diagnosticsOutput);
                }

                bool velopackEnvironmentDetected = await _velopackUpdateService.IsInstalledAsync();

                diagnosticsOutput.Write($"[AppUpdate] Velopack environment detected: {velopackEnvironmentDetected}");

                if (!velopackEnvironmentDetected)
                {
                    diagnosticsOutput.Write("[AppUpdate] Velopack not detected. Please ensure application is installed via Velopack installer.");
                    if (showErrorsToUser)
                    {
                        await ShowErrorDialogAsync("Aplikacja nie została zainstalowana poprawnie. Pobierz najnowszą wersję z oficjalnej strony.", GetUpdateDialogTitle());
                    }
                    return;
                }

                diagnosticsOutput.Write($"[AppUpdate] Checking for Velopack updates...");
                var velopackResult = await _velopackUpdateService.CheckForUpdateAsync();
                
                diagnosticsOutput.Write($"[AppUpdate] Check result - Success: {velopackResult.Success}, UpdateAvailable: {velopackResult.IsUpdateAvailable}");
                diagnosticsOutput.Write($"[AppUpdate] Current: {velopackResult.CurrentVersion}, Latest: {velopackResult.LatestVersion}");
                
                if (!velopackResult.Success)
                {
                    diagnosticsOutput.Write($"[AppUpdate] Velopack check failed: {velopackResult.ErrorMessage}");
                    if (showErrorsToUser)
                    {
                        await ShowErrorDialogAsync(BuildUpdateErrorMessage(velopackResult.ErrorMessage), GetUpdateDialogTitle());
                    }

                    return;
                }

                if (velopackResult.IsUpdateAvailable && velopackResult.UpdateInfo != null)
                {
                    diagnosticsOutput.Write($"[AppUpdate] Update available: {velopackResult.CurrentVersion} -> {velopackResult.LatestVersion}");
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var dialog = new VelopackUpdateDialog(AppVersion, velopackResult, _velopackUpdateService);
                        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                        if (mainWindow != null)
                        {
                            await dialog.ShowDialog(mainWindow);
                        }
                    });

                    return;
                }

                diagnosticsOutput.Write("[AppUpdate] No Velopack updates available");
                if (notifyWhenNoUpdates)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var dialog = new NoUpdateDialog(AppVersion);
                        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                        if (mainWindow != null)
                        {
                            await dialog.ShowDialog(mainWindow);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                diagnosticsOutput.Write($"[AppUpdate] Velopack check failed: {ex.Message}");

                if (showErrorsToUser)
                {
                    await ShowErrorDialogAsync(BuildUpdateErrorMessage(ex.Message), GetUpdateDialogTitle());
                }
            }
            // NIE dispose'uj - service jest używany przez cały cykl życia aplikacji
        }

        private string GetUpdateDialogTitle()
        {
            return _localizationService?.Get("Updates.UpdateAvailable") ?? "Application update";
        }

        private string BuildUpdateErrorMessage(string? details)
        {
            if (_localizationService == null)
            {
                return string.IsNullOrWhiteSpace(details)
                    ? "Failed to check for updates."
                    : $"Failed to check for updates: {details}";
            }

            if (string.IsNullOrWhiteSpace(details))
            {
                return _localizationService.Get("Updates.CheckFailedWithoutDetails");
            }

            return _localizationService.GetFormatted("Updates.CheckFailed", details);
        }

        private void LoadAppVersion()
        {
            var appVersion = _userSettingsService.LoadAppVersion();
            AppVersion = appVersion.CurrentVersion;
        }

        private void LoadWindowTitle()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                string platform = userSettings.Mode;
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
                var userSettings = _userSettingsService.LoadUserSettings();
                string platform = userSettings.Mode;
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
        /// Sprawdza aktualizacje modów i DLL po załadowaniu głównego okna
        /// </summary>
        public async Task CheckForUpdatesAfterMainWindowLoadAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Post-Init] Rozpoczynam sprawdzanie aktualizacji po załadowaniu głównego okna...");

                // Zwiększone opóźnienie aby UI był w pełni zainicjalizowany i uniknąć błędów PlatformImpl
                await Task.Delay(1500);

                // Sprawdź aktualizacje SEKWENCYJNIE - najpierw mody FULL, potem DLL
                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji modów FULL...");
                await CheckForModUpdatesAsync();

                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji modów DLL...");
                await CheckDllUpdates();

                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji zakończone");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Post-Init] Błąd podczas sprawdzania aktualizacji: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - aktualizacje nie są krytyczne
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

        /// <summary>
        /// Sprawdza czy aplikacja jest zarejestrowana w Windows Registry i pyta użytkownika o rejestrację jeśli nie
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void CheckWindowsRegistryRegistration()
        {
            try
            {
                await Task.Delay(3000); // Opóźnienie aby UI był w pełni gotowy

                if (RegistryInstaller.IsRegistered())
                {
                    System.Diagnostics.Debug.WriteLine("[Registry] Application already registered in Windows");
                    _diagnosticsOutput?.Write("[Registry] Aplikacja jest już zarejestrowana w systemie Windows");
                    return;
                }

                _diagnosticsOutput?.Write("[Registry] Aplikacja nie jest zarejestrowana w systemie Windows");
                System.Diagnostics.Debug.WriteLine("[Registry] Application not registered, asking user...");

                // Pokaż dialog pytając użytkownika
                var shouldRegister = await ShowInlineConfirmAsync(
                    "Rejestracja w systemie Windows",
                    "SUSModder nie jest zarejestrowany w systemie Windows.\n\n" +
                    "Czy chcesz zarejestrować aplikację w \"Dodaj/usuń programy\"?\n\n" +
                    "Umożliwi to łatwą deinstalację przez panel Windows.",
                    "Zarejestruj",
                    "Pomiń");

                if (shouldRegister)
                {
                    System.Diagnostics.Debug.WriteLine("[Registry] User chose to register");
                    var success = RegistryInstaller.RegisterApplication(AppVersion);

                    if (success)
                    {
                        _diagnosticsOutput?.Write("[Registry] Aplikacja została zarejestrowana pomyślnie");
                        System.Diagnostics.Debug.WriteLine("[Registry] Application registered successfully");

                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await ShowMessageAsync("Sukces", "Aplikacja została zarejestrowana w systemie Windows.");
                        });
                    }
                    else
                    {
                        _diagnosticsOutput?.Write("[Registry] Nie udało się zarejestrować aplikacji");
                        System.Diagnostics.Debug.WriteLine("[Registry] Failed to register application");

                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await ShowErrorDialogAsync("Nie udało się zarejestrować aplikacji.", "Błąd rejestracji");
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Registry] User skipped registration");
                    _diagnosticsOutput?.Write("[Registry] Użytkownik pominął rejestrację");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Registry] Error during registration check: {ex.Message}");
                _diagnosticsOutput?.Write($"[Registry] Błąd podczas sprawdzania rejestracji: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - rejestracja nie jest krytyczna
            }
        }
    }
}
