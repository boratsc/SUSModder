using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SUSModder.ViewModels;
using SUSModder.Views;
using SUSModder.Services;
using SUSModder.Services.Localization;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services.Discord;
using SUSModder.Core.Utilities;

namespace SUSModder;

/// <summary>
/// Prosta implementacja IDiagnosticsOutput dla startu aplikacji (przed inicjalizacją UI).
/// Przekierowuje komunikaty do Debug.WriteLine.
/// </summary>
internal class DebugDiagnosticsOutput : IDiagnosticsOutput
{
    public static readonly DebugDiagnosticsOutput Instance = new();
    public void Write(string message) => System.Diagnostics.Debug.WriteLine($"[App/Diag] {message}");
}

public partial class App : Application
{
    private SplashWindow? _splashWindow;
    private static ServiceProvider? _serviceProvider;
    private TelemetryService? _telemetryService;
    private DatabaseService? _databaseService;
    private UserSettingsService? _dbUserSettingsService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Zbuduj IConfiguration raz i zarejestruj jako singleton
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
        var appSettingsPath = Path.Combine(exeDir, "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Rejestracja serwisu powiadomień toast
        services.AddSingleton<ToastService>();

        // Rejestracja DatabaseService i repozytoriów SQLite
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<IUserSettingsRepository>(sp =>
        {
            var db = sp.GetRequiredService<DatabaseService>();
            return new UserSettingsRepository(db);
        });
        services.AddSingleton<IModRepository>(sp =>
        {
            var db = sp.GetRequiredService<DatabaseService>();
            return new ModRepository(db);
        });
        services.AddSingleton<ITouConfigRepository>(sp =>
        {
            var db = sp.GetRequiredService<DatabaseService>();
            return new TouConfigRepository(db);
        });

        // Rejestracja serwisu lokalizacji
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var locService = new LocalizationService();

            // Odczytaj zapisany język z user settings (JSON fallback przed inicjalizacją bazy)
            var userSettingsService = new UserSettingsService();
            var userSettings = userSettingsService.LoadUserSettings();
            var savedLanguage = userSettings.Language;

            // Jeśli język jest ustawiony i dostępny, użyj go
            if (!string.IsNullOrEmpty(savedLanguage) && locService.IsCultureAvailable(savedLanguage))
            {
                locService.ChangeCulture(savedLanguage);
            }
            // W przeciwnym razie pozostaw domyślny język "pl" z LocalizationService

            return locService;
        });

        // Rejestracja diagnostyki
        services.AddSingleton<IDiagnosticsOutput>(_ => DebugDiagnosticsOutput.Instance);

        // Rejestracja OAuthLoopbackListener dla Discord OAuth2 flow
        services.AddSingleton<OAuthLoopbackListener>();

        // Rejestracja serwisów Discord OAuth2 (Core)
        services.AddSingleton<IDiscordAuthRepository>(sp =>
        {
            var db = sp.GetRequiredService<DatabaseService>();
            return new DiscordAuthRepository(db);
        });
        services.AddSingleton<ISustatsCredentialsRepository>(sp =>
        {
            var db = sp.GetRequiredService<DatabaseService>();
            return new SustatsCredentialsRepository(db);
        });
        services.AddSingleton<IDiscordOAuthService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var authRepo = sp.GetRequiredService<IDiscordAuthRepository>();
            var diag = sp.GetRequiredService<IDiagnosticsOutput>();
            return new DiscordOAuthService(config, authRepo, diag);
        });
        services.AddSingleton<IClairDiscordService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var diag = sp.GetRequiredService<IDiagnosticsOutput>();
            return new ClairDiscordService(config, diag);
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Pobiera serwis z DI container.
    /// </summary>
    public static T GetService<T>() where T : class
    {
        return _serviceProvider?.GetService<T>()
            ?? throw new InvalidOperationException($"Service {typeof(T)} not registered in DI container");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Show splash screen immediately
            _splashWindow = new SplashWindow();
            desktop.MainWindow = _splashWindow;
            _splashWindow.Show();

            // Start async initialization in background
            Task.Run(async () => await InitializeApplicationAsync(desktop));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeApplicationAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var forceOnboardingFlagPath = Path.Combine(UserSettingsService.GetAppDataFolder(), "force-onboarding.flag");
            var forceOnboarding = File.Exists(forceOnboardingFlagPath);

            // KROK 0: Inicjalizacja bazy danych SQLite (przed ustawieniami)
            _splashWindow?.UpdateProgress(0.0, "Inicjalizacja bazy danych...");
            _databaseService = _serviceProvider?.GetService<DatabaseService>();
            if (_databaseService != null)
            {
                await _databaseService.InitializeAsync();
                System.Diagnostics.Debug.WriteLine("[App] Baza danych SQLite zainicjalizowana.");

                // Ustaw repozytoria w ConfigManager (migracja z JSON na SQLite)
                var modRepo = _serviceProvider?.GetService<IModRepository>();
                if (modRepo != null)
                {
                    ConfigManager.SetRepository(modRepo);
                    System.Diagnostics.Debug.WriteLine("[App] ConfigManager -> SQLite repository set.");
                }

                var touRepo = _serviceProvider?.GetService<ITouConfigRepository>();
                if (touRepo != null)
                {
                    ConfigManager.SetTouConfigRepository(touRepo);
                    System.Diagnostics.Debug.WriteLine("[App] ConfigManager -> TouConfig repository set.");
                }
            }

            // Utwórz UserSettingsService z repozytorium SQLite (po inicjalizacji bazy)
            var userSettingsRepo = _serviceProvider?.GetService<IUserSettingsRepository>();
            _dbUserSettingsService = userSettingsRepo != null
                ? new UserSettingsService(userSettingsRepo)
                : new UserSettingsService();

            // Ustaw domyślne repozytorium, aby wszystkie nowe UserSettingsService() używały SQLite
            if (userSettingsRepo != null)
            {
                UserSettingsService.SetDefaultRepository(userSettingsRepo);
                System.Diagnostics.Debug.WriteLine("[App] UserSettingsService default repository set (SQLite).");
            }

            await _splashWindow?.AnimateProgressAsync(0.1)!;

            // KROK 1.5: Sprawdź czy język jest ustawiony, jeśli nie - pokaż dialog wyboru języka
            var userSettingsService = _dbUserSettingsService;
            if (forceOnboarding)
            {
                userSettingsService.UpdateUserSetting(settings => settings.Language = string.Empty);
            }

            var currentLanguage = userSettingsService.LoadUserSettings().Language;
            if (string.IsNullOrEmpty(currentLanguage))
            {
                // Język nie jest ustawiony - pokaż dialog wyboru
                string? selectedLanguage = null;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var languageDialog = new LanguageSelectionDialog();
                    selectedLanguage = await languageDialog.ShowDialog<string>(_splashWindow);
                });

                // Jeśli użytkownik zamknął dialog bez wyboru, użyj polskiego jako domyślnego
                if (string.IsNullOrEmpty(selectedLanguage))
                {
                    userSettingsService.UpdateUserSetting(settings => settings.Language = "pl");
                }

                // Dialog języka zapisuje ustawienia przez inny UserSettingsService (inna instancja).
                // Odśwież cache bieżącej instancji, aby odczytać aktualną wartość z pliku.
                userSettingsService.ClearCache();
            }

            // KROK 1.6: Sprawdź czy platforma jest ustawiona, jeśli nie - pokaż dialog wyboru platformy
            if (forceOnboarding)
            {
                userSettingsService.UpdateUserSetting(settings => settings.Mode = string.Empty);
            }

            var currentMode = userSettingsService.LoadUserSettings().Mode;
            if (forceOnboarding || string.IsNullOrEmpty(currentMode))
            {
                // Platforma nie jest ustawiona - pokaż dialog wyboru (wymagany wybór!)
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var platformDialog = new PlatformSelectionDialog();
                    await platformDialog.ShowDialog<string>(_splashWindow);
                });

                // Dialog platformy zapisuje ustawienia przez inny UserSettingsService (inna instancja).
                // Odśwież cache bieżącej instancji przed walidacją wyniku.
                userSettingsService.ClearCache();

                // Wymagany wybór: jeśli nadal pusto (np. zamknięto dialog), zamknij aplikację.
                currentMode = userSettingsService.LoadUserSettings().Mode;
                if (string.IsNullOrWhiteSpace(currentMode))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                            desktopLifetime.Shutdown();
                        else
                            Environment.Exit(0);
                    });
                    return;
                }
            }

            if (forceOnboarding)
            {
                try
                {
                    File.Delete(forceOnboardingFlagPath);
                }
                catch
                {
                    // Flaga zostanie usunięta przy kolejnym poprawnym starcie.
                }
            }

            // KROK 1.7: Jeśli tryb Epic – zweryfikuj logowanie (pobierz legendary jeśli trzeba)
            var finalMode = userSettingsService.LoadUserSettings().Mode;
            if (string.Equals(finalMode, "epic", StringComparison.OrdinalIgnoreCase))
            {
                await HandleEpicAuthenticationAsync(userSettingsService);
            }

            // KROK 2: Inicjalizacja ConsoleLogger (20%)
            _splashWindow?.UpdateProgress(0.1, "Uruchamianie logowania...");
            await Dispatcher.UIThread.InvokeAsync(() => ConsoleLogger.Initialize());

            // Telemetria jest teraz inicjalizowana po pokazaniu MainWindow (Point 1 optymalizacji)

            await _splashWindow?.AnimateProgressAsync(0.2)!;

            // KROK 3: Tworzenie MainWindow i ViewModel (40%)
            _splashWindow?.UpdateProgress(0.2, "Ładowanie interfejsu...");
            MainWindow? mainWindow = null;
            MainWindowViewModel? viewModel = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel = new MainWindowViewModel();
                mainWindow = new MainWindow(viewModel);
            });

            // Inicjalizuj ciężkie serwisy w tle (ClearEpicLogsOnStartup itd.)
            if (viewModel != null)
            {
                _ = viewModel.InitializeServicesAsync();
            }

            await _splashWindow?.AnimateProgressAsync(0.4)!;

            if (mainWindow == null || viewModel == null)
            {
                throw new InvalidOperationException("Nie udało się utworzyć MainWindow lub ViewModel");
            }

            // KROK 4: Asynchroniczna inicjalizacja danych w ViewModel (90%)
            _splashWindow?.UpdateProgress(0.4, "Ładowanie konfiguracji...");
            await viewModel.InitializeApplicationAsync((progress, status) =>
            {
                // Callback do aktualizacji progressu (0.0 - 1.0 mapowane na 40% - 90%)
                var mappedProgress = 0.4 + (progress * 0.5);
                _splashWindow?.UpdateProgress(mappedProgress, status);
            });
            await _splashWindow?.AnimateProgressAsync(0.9)!;

            // KROK 5: Zamiana okien (100%)
            _splashWindow?.UpdateProgress(0.9, "Finalizacja...");
            await _splashWindow?.AnimateProgressAsync(1.0)!;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                // Zamknij splash z fade out
                if (_splashWindow != null)
                {
                    await _splashWindow.CloseWithFadeAsync();
                }

                // Inicjalizuj SystemTrayService po pokazaniu MainWindow
                mainWindow.InitializeSystemTray();
                // Podepnij podpięcie aktualizacji modów w tray
                mainWindow.UpdateTrayModsList();

                // Po załadowaniu głównego okna uruchom zadania post-startowe.
                _ = RunPostStartupTasksAsync(mainWindow, viewModel, userSettingsService);

                // Inicjalizuj telemetrię i wyślij heartbeat w tle (tylko Windows)
                // Przeniesione z KROK 2 - WMI queries w HardwareIdProvider trwają 1-5s
                if (OperatingSystem.IsWindows())
                {
                    _ = Task.Run(InitializeTelemetryAndSendHeartbeatAsync);
                }
            });

            // Hook do zamknięcia aplikacji - wyślij końcowy heartbeat (tylko Windows)
            if (OperatingSystem.IsWindows())
            {
                desktop.ShutdownRequested += OnShutdownRequested;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Błąd podczas inicjalizacji aplikacji: {ex.Message}");

            // W przypadku błędu, pokaż error i zamknij
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _splashWindow?.Close();
                // TODO: Pokaż dialog błędu
            });
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void InitializeTelemetry()
    {
        try
        {
            var configuration = GetService<IConfiguration>();
            _telemetryService = new TelemetryService(configuration);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize telemetry: {ex.Message}");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task InitializeTelemetryAndSendHeartbeatAsync()
    {
        InitializeTelemetry();
        if (_telemetryService != null)
            await _telemetryService.SendHeartbeatAsync();
    }

    private async Task RunPostStartupTasksAsync(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        UserSettingsService userSettingsService)
    {
        try
        {
            await ShowAntivirusWarningIfNeededAsync(mainWindow, userSettingsService);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Błąd podczas sprawdzania antywirusa: {ex.Message}");
        }

        _ = viewModel.CheckForUpdatesAfterMainWindowLoadAsync();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task ShowAntivirusWarningIfNeededAsync(MainWindow mainWindow, UserSettingsService userSettingsService)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Task.Delay(1200);

        var antivirusDetectionService = new AntivirusDetectionService();
        var result = await Task.Run(antivirusDetectionService.DetectInstalledThirdPartyAntivirus);
        if (!result.HasThirdPartyAntivirus)
        {
            return;
        }

        var userSettings = userSettingsService.LoadUserSettings();
        if (string.Equals(
                userSettings.AntivirusWarningAcknowledgedSignature,
                result.Signature,
                StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine("[App] Ostrzeżenie antywirusowe już potwierdzone dla bieżącego zestawu AV.");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (mainWindow.ViewModel == null)
            {
                return;
            }

            await mainWindow.ViewModel.ShowAntivirusWarningAsync(result.ProductNames, PathSettings.ModsInstallPath);
            userSettingsService.UpdateUserSetting(settings =>
                settings.AntivirusWarningAcknowledgedSignature = result.Signature);
        });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Wyślij końcowy heartbeat przed zamknięciem
        if (_telemetryService != null)
        {
            await _telemetryService.SendShutdownHeartbeatAsync();
            _telemetryService.Dispose();
        }
    }

    /// <summary>
    /// Weryfikuje logowanie do Epic Games przy starcie aplikacji w trybie Epic.
    /// Pobiera legendary.exe jeśli potrzeba, sprawdza status sesji,
    /// i w pętli wyświetla EpicLoginRequiredDialog aż użytkownik się zaloguje,
    /// przełączy na Steam lub zamknie aplikację.
    /// </summary>
    private async Task HandleEpicAuthenticationAsync(UserSettingsService userSettingsService)
    {
        try
        {
            _splashWindow?.UpdateProgress(0.1, "Sprawdzanie logowania Epic Games...");

            var diagnostics = new StartupDiagnosticsOutput();
            var userInteraction = new StartupEpicUserInteraction(_splashWindow);
            var epicManager = new EpicVersionManager(diagnostics, userInteraction);

            // Pobierz legendary.exe jeśli nie istnieje
            _splashWindow?.UpdateProgress(0.1, "Przygotowywanie Epic Games...");
            await epicManager.EnsureLegendaryExistsAsync();

            // Sprawdź status logowania
            _splashWindow?.UpdateProgress(0.12, "Weryfikacja sesji Epic Games...");
            bool isLoggedIn = await epicManager.CheckAuthStatusAsync();

            if (isLoggedIn)
            {
                Debug.WriteLine("[App] Epic Games: sesja aktywna, kontynuuję start aplikacji.");
                return;
            }

            // Sesja nieaktywna – pokaż dialog w pętli
            Debug.WriteLine("[App] Epic Games: brak aktywnej sesji, wyświetlam EpicLoginRequiredDialog.");

            while (true)
            {
                string? dialogResult = null;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialog = new EpicLoginRequiredDialog();
                    if (_splashWindow != null)
                        dialogResult = await dialog.ShowDialog<string>(_splashWindow);
                    else
                        dialogResult = "close";
                });

                if (dialogResult == "login")
                {
                    // Próba logowania przez EpicAuthDialog
                    _splashWindow?.UpdateProgress(0.12, "Logowanie do Epic Games...");
                    bool loginSuccess = await epicManager.LoginAsync();

                    if (loginSuccess)
                    {
                        Debug.WriteLine("[App] Epic Games: logowanie pomyślne.");
                        return;
                    }

                    // Logowanie nie powiodło się - pokaż dialog ponownie (pętla kontynuuje)
                    Debug.WriteLine("[App] Epic Games: logowanie nieudane, wyświetlam dialog ponownie.");
                }
                else if (dialogResult == "steam")
                {
                    // Zmień tryb na Steam
                    userSettingsService.UpdateUserSetting(settings => settings.Mode = "steam");
                    Debug.WriteLine("[App] Zmieniono tryb na Steam.");
                    return;
                }
                else
                {
                    // "close" lub null (dialog zamknięty) – zamknij aplikację
                    Debug.WriteLine("[App] Użytkownik wybrał zamknięcie aplikacji.");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                            desktop.Shutdown();
                        else
                            Environment.Exit(0);
                    });
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Błąd podczas weryfikacji Epic auth: {ex.Message}");
            // Nie blokujemy startu aplikacji w przypadku błędu weryfikacji
        }
    }

    // -------------------------------------------------------------------------
    // Adaptery uproszczone na potrzeby wczesnego startu (przed MainWindowViewModel)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Uproszczony IDiagnosticsOutput używany podczas startup – loguje do Debug.
    /// </summary>
    private sealed class StartupDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message)
        {
            Debug.WriteLine($"[EpicStartup] {message}");
        }
    }

    /// <summary>
    /// Uproszczony IEpicUserInteraction używany podczas startup.
    /// ShowEpicAuthDialogAsync pokazuje EpicAuthDialog z splash window jako parentem.
    /// </summary>
    private sealed class StartupEpicUserInteraction : IEpicUserInteraction
    {
        private readonly SplashWindow? _parentWindow;

        public StartupEpicUserInteraction(SplashWindow? parentWindow)
        {
            _parentWindow = parentWindow;
        }

        public bool Confirm(string message) => true;

        public void ShowError(string message)
        {
            Debug.WriteLine($"[EpicStartup] Error: {message}");
        }

        public string? Prompt(string message, string title = "") => null;

        public async Task<string?> ShowEpicAuthDialogAsync(string browserUrl)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new EpicAuthDialog(browserUrl);
                bool? dialogResult;
                if (_parentWindow != null)
                    dialogResult = await dialog.ShowDialog<bool?>(_parentWindow);
                else
                    dialogResult = await dialog.ShowDialog<bool?>(dialog);

                if (dialogResult == true && dialog.DataContext is EpicAuthDialogViewModel vm)
                    return vm.Result;

                return null;
            });
        }
    }
}
