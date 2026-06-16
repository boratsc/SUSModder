using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający logikę inicjalizacji aplikacji
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Inicjalizuje ciężkie serwisy w tle (poza konstruktorem).
        /// Wywoływane z App.axaml.cs po utworzeniu VM.
        /// </summary>
        public async Task InitializeServicesAsync()
        {
            // ClearEpicLogsOnStartup tworzy EpicVersionManager - ciężka operacja
            // Przeniesione z konstruktora aby nie blokować UI thread
            await Task.Run(() => ClearEpicLogsOnStartup());
        }

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
                var configService = new ConfigService();
                var configs = await configService.LoadConfigAsync();
                System.Diagnostics.Debug.WriteLine($"Loaded {configs.Count} configs from service");
                _loadedConfigs = configs;

                // KROK 2: Wyszukiwanie i konfiguracja Vanilla Among Us (30%)
                progressCallback?.Invoke(0.1, "Wykrywanie Among Us...");
                bool vanillaSetupSuccess = await SetupVanillaGameAsync();

                // KROK 3: Odświeżenie konfiguracji i interfejsu (40%)
                // Pominięto redundantne przeładowanie configu - SetupVanillaGameAsync już zaktualizowało _loadedConfigs

                // Odśwież tytuł okna po wykryciu platformy (zawsze, niezależnie czy Vanilla był nowy czy już istniał)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadWindowTitle();
                });

                // KROK 4: Odświeżenie interfejsu (70%)
                progressCallback?.Invoke(0.4, "Odświeżanie listy modów...");
                await RefreshModsListAsync(preloadedConfigs: _loadedConfigs);
                await RefreshPackInstancesAsync();

                // Odblokuj interakcję — po kroku 4 lista modów jest w pełni załadowana
                _isInitializing = false;

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
                    MigrateExistingInstallationsAsync(),

                    // Batch VirusTotal fetch dla wszystkich pełnych modów (best-effort, w tle)
                    FetchVirusTotalForCatalogAsync()
                };

                // Nie czekamy na backgroundTasks - niech działają w tle

                // KROK 6: Odświeżenie panelu statusu (90%)
                progressCallback?.Invoke(0.8, "Finalizacja...");
                await RefreshStatusBarAsync();

                // KROK 7: Uruchom auto-refresh (95%)
                progressCallback?.Invoke(0.9, "Uruchamianie usług...");
                StartApiStatusAutoRefresh();
                StartModUpdatesAutoRefresh();

                // KROK 8: Finalizacja (100%)
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
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting Vanilla game setup...");

                var vanillaMod = await GameLocator.CheckAndSetupVanillaModAsync(
                    _loadedConfigs,
                    _configuration!,
                    userInteraction: null);

                if (vanillaMod != null)
                    _loadedConfigs.Add(vanillaMod);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during Vanilla setup: {ex.Message}");
                return true;
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
                // Użyj cache'owanej konfiguracji z DI zamiast budowania nowej
                var configuration = _configuration!;

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

                // KROK 0: Sprawdź czy pokazać "Co nowego" po aktualizacji
                await ShowChangelogIfNewVersionAsync();

                // Sprawdź aktualizacje SEKWENCYJNIE - najpierw mody FULL, potem DLL
                if (ShouldRunInteractiveModUpdateCheck())
                {
                    System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji modów FULL...");

                    // KROK 1: Sprawdź dostępne aktualizacje
                    var updateManager = new ModUpdateManager();
                    var result = await updateManager.CheckForUpdatesAsync();

                    // Jeśli config został zaktualizowany, odśwież listę modów
                    if (result.ConfigWasUpdated)
                    {
                        await RefreshModsListAsync(deferIfToolModalOpen: true);
                    }

                    if (result.Success && result.InstalledModUpdates.Any())
                    {
                        // KROK 2: Auto-aktualizuj mody z włączoną auto-aktualizacją (ciche, w tle)
                        // IsModAutoUpdateEnabledAsync ładuje ustawienie bezpośrednio z Installation Map,
                        // omijając asynchroniczne ładowanie w RefreshModsListAsync (fire-and-forget).
                        System.Diagnostics.Debug.WriteLine($"[Post-Init] Auto-aktualizuję mody z włączoną auto-aktualizacją...");
                        await ProcessAutoUpdatesSilentlyAsync(result.InstalledModUpdates);

                        // KROK 3: Odśwież licznik statusu — ProcessAutoUpdatesSilentlyAsync już zaktualizował
                        // status, jeśli coś auto-zaktualizował. Jeśli zostały jeszcze jakieś aktualizacje
                        // (mod bez auto-aktualizacji), zobaczą licznik na pasku statusu.
                        await CheckForModUpdatesForStatusBarAsync(force: true);
                    }
                    else
                    {
                        // Brak aktualizacji lub błąd — zaktualizuj status
                        await CheckForModUpdatesForStatusBarAsync(force: true);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Post-Init] Pomijam sprawdzanie modów FULL (niedawno wykonane).");
                }

                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji modów DLL...");
                await CheckDllUpdates();

                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji modpacków...");
                await CheckModPackUpdatesAsync();

                System.Diagnostics.Debug.WriteLine("[Post-Init] Sprawdzanie aktualizacji zakończone");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Post-Init] Błąd podczas sprawdzania aktualizacji: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - aktualizacje nie są krytyczne
            }
        }

        /// <summary>
        /// Sprawdza czy aplikacja została zaktualizowana od ostatniego uruchomienia.
        /// Jeśli tak: pobiera changelog z GitHub API i pokazuje dialog "Co nowego".
        /// Jeśli GitHub API nie odpowiada – pokazuje tylko toast z linkiem do GitHub.
        /// Zawsze pokazuje toast "Zaktualizowano do wersji X".
        /// </summary>
        private async Task ShowChangelogIfNewVersionAsync()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                var lastSeenVersion = userSettings.LastSeenVersion ?? string.Empty;

                using var changelogService = new SUSModder.Core.Services.ChangelogService();

                // Pierwszy start / reset fabryczny — zapisz wersję bez toasta „Zaktualizowano do…”
                if (string.IsNullOrWhiteSpace(lastSeenVersion))
                {
                    _userSettingsService.SaveLastSeenVersion(AppVersion);
                    System.Diagnostics.Debug.WriteLine($"[Changelog] Pierwszy start — zapisano lastSeenVersion = {AppVersion} (bez toasta)");
                    return;
                }

                // Toast tylko po rzeczywistej aktualizacji (np. 2.9.0 → 3.0.0)
                bool isNewVersion = changelogService.IsNewerVersion(AppVersion, lastSeenVersion);

                if (!isNewVersion)
                {
                    System.Diagnostics.Debug.WriteLine($"[Changelog] Wersja {AppVersion} już wyświetlona (lastSeen: {lastSeenVersion})");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Changelog] Nowa wersja {AppVersion} (lastSeen: {lastSeenVersion})");

                // Zapisz od razu — żeby toast nie wracał po restarcie nawet gdy UI się wywali
                _userSettingsService.SaveLastSeenVersion(AppVersion);

                // KROK 1: Spróbuj pobrać z GitHub API (na potrzeby toasta)
                var changelogData = await TryFetchFromGitHubAsync(changelogService);
                ChangelogData? capturedData = changelogData;

                // KROK 2: Toast z informacją o aktualizacji (jedyny widoczny element)
                // Dialog changeloga otwiera się dopiero po kliknięciu w toasta
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var toastTitle = _localizationService.GetFormatted("Toast.AppUpdated", AppVersion);

                        if (capturedData != null)
                        {
                            // GitHub OK – kliknięcie w toasta otwiera dialog
                            ToastService.ShowInfo(toastTitle, autoCloseMs: 8000, onClick: () =>
                            {
                                Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    try
                                    {
                                        var dialog = new Views.ChangelogDialog(capturedData, _localizationService);
                                        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                                        if (mainWindow != null)
                                            await dialog.ShowDialog(mainWindow);
                                    }
                                    catch { /* ignoruj */ }
                                });
                            });
                        }
                        else
                        {
                            // GitHub fail – kliknięcie otwiera GitHub releases
                            var githubUrl = "https://github.com/boratsc/SUSModder/releases";
                            ToastService.ShowInfo(toastTitle, autoCloseMs: 8000, onClick: () =>
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = githubUrl,
                                        UseShellExecute = true
                                    });
                                }
                                catch { /* ignoruj */ }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Changelog] Błąd toasta: {ex.Message}");
                    }
                });

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Changelog] Błąd: {ex.Message}");
                // Nie blokujemy startu aplikacji
            }
        }

        /// <summary>
        /// Próbuje pobrać changelog z GitHub API.
        /// </summary>
        private async Task<ChangelogData?> TryFetchFromGitHubAsync(SUSModder.Core.Services.ChangelogService changelogService)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Changelog] Próba GitHub API...");
                var result = await changelogService.FetchFromGitHubAsync("boratsc", "SUSModder");

                if (result != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Changelog] GitHub API sukces: wersja {result.Version}, {result.Sections.Count} sekcji");
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Changelog] GitHub API błąd: {ex.Message}");
                return null;
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

                        // Odśwież UI po imporcie (pomiń jeśli trwa instalacja)
                        if (_activeInstallationsCount == 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                await RefreshModsListAsync(deferIfToolModalOpen: true);
                            });
                        }
                        else
                        {
                            Console.WriteLine("[InstallationMap] ⏭ Pomijam odświeżenie UI - trwa instalacja");
                        }
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

                    // Odśwież UI po oczyszczeniu (pomiń jeśli trwa instalacja)
                    if (_activeInstallationsCount == 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await RefreshModsListAsync(deferIfToolModalOpen: true);
                        });
                    }
                    else
                    {
                        Console.WriteLine("[InstallationMap] ⏭ Pomijam odświeżenie UI - trwa instalacja");
                    }
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

        /// <summary>
        /// Pobiera raporty VirusTotal dla wszystkich pełnych modów w tle (best-effort).
        /// </summary>
        private async Task FetchVirusTotalForCatalogAsync()
        {
            if (_securityScanService == null || _loadedConfigs.Count == 0)
                return;

            try
            {
                var platform = DeterminePlatform();
                var fullMods = _loadedConfigs
                    .Where(c => !c.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                    .Select(c => (c.Id, c.ModVersion))
                    .ToList();

                if (fullMods.Count == 0)
                    return;

                System.Diagnostics.Debug.WriteLine(
                    $"[SecurityScan] Rozpoczynam batch VT fetch dla {fullMods.Count} modów...");

                await _securityScanService.FetchAndStoreVtForCatalogAsync(fullMods, platform);

                // Po zapisie do DB, odśwież listę modów w UI
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await RefreshModsListAsync();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityScan] Batch VT fetch failed: {ex.Message}");
            }
        }
    }
}
