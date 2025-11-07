using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
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

namespace SUSModder;

public partial class App : Application
{
    private SplashWindow? _splashWindow;
    private static ServiceProvider? _serviceProvider;
    private TelemetryService? _telemetryService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureServices();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Rejestracja serwisu lokalizacji
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var locService = new LocalizationService();

            // Odczytaj zapisany język z user settings
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
            // KROK 1: Inicjalizacja ustawień (10%)
            _splashWindow?.UpdateProgress(0.0, "Inicjalizacja...");
            await Task.Run(() =>
            {
                // Upewnij się, że TelemetryEnabled istnieje w appsettings.json
                ConfigManager.EnsureTelemetryEnabledExists();
            });
            await _splashWindow?.AnimateProgressAsync(0.1)!;

            // KROK 1.5: Sprawdź czy język jest ustawiony, jeśli nie - pokaż dialog wyboru języka
            var userSettingsService = new UserSettingsService();
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
            }

            // KROK 2: Inicjalizacja ConsoleLogger i Telemetrii (20%)
            _splashWindow?.UpdateProgress(0.1, "Uruchamianie logowania...");
            await Dispatcher.UIThread.InvokeAsync(() => ConsoleLogger.Initialize());

            // Inicjalizuj telemetrię (tylko Windows)
            if (OperatingSystem.IsWindows())
            {
                InitializeTelemetry();
            }

            await _splashWindow?.AnimateProgressAsync(0.2)!;;

            // KROK 3: Tworzenie MainWindow i ViewModel (40%)
            _splashWindow?.UpdateProgress(0.2, "Ładowanie interfejsu...");
            MainWindow? mainWindow = null;
            MainWindowViewModel? viewModel = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel = new MainWindowViewModel();
                mainWindow = new MainWindow
                {
                    DataContext = viewModel
                };
            });
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
            await Task.Delay(200); // Krótka pauza dla płynności
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

                // Po załadowaniu głównego okna sprawdź aktualizacje
                // Fire and forget - nie blokujemy UI
                _ = viewModel.CheckForUpdatesAfterMainWindowLoadAsync();

                // Wyślij pierwszy heartbeat telemetrii (tylko Windows)
                if (OperatingSystem.IsWindows() && _telemetryService != null)
                {
                    _ = _telemetryService.SendHeartbeatAsync();
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
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            var appSettingsPath = Path.Combine(exeDir, "appsettings.json");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .Build();

            _telemetryService = new TelemetryService(configuration);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize telemetry: {ex.Message}");
        }
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
}