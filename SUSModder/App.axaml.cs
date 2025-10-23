using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using SUSModder.ViewModels;
using SUSModder.Views;
using SUSModder.Services;
using SUSModder.Core.Services;

namespace SUSModder;

public partial class App : Application
{
    private SplashWindow? _splashWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
            // KROK 1: Przywracanie ustawień użytkownika (10%)
            _splashWindow?.UpdateProgress(0.0, "Inicjalizacja...");
            await Task.Run(() =>
            {
                var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                AppUpdateService.RestoreUserSettingsIfNeeded(appSettingsPath, null);
            });
            await _splashWindow?.AnimateProgressAsync(0.1)!;

            // KROK 2: Inicjalizacja ConsoleLogger (20%)
            _splashWindow?.UpdateProgress(0.1, "Uruchamianie logowania...");
            await Dispatcher.UIThread.InvokeAsync(() => ConsoleLogger.Initialize());
            await _splashWindow?.AnimateProgressAsync(0.2)!;

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
            });
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
}