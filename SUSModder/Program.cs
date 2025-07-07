using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.IO;
using SUSModder.Core.Services; 

namespace SUSModder;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Przywracanie ustawień użytkownika po aktualizacji
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppUpdateService.RestoreUserSettingsIfNeeded(appSettingsPath, null);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}