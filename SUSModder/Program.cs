using System;
using System.IO;
using Avalonia;
using Avalonia.ReactiveUI;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;
using Velopack;

namespace SUSModder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle Velopack activation hooks before any other startup logic
        VelopackApp.Build().Run();

        RestoreUserSettings();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();

    private static void RestoreUserSettings()
    {
        try
        {
            var appDirPath = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(appDirPath))
            {
                return;
            }

            var appSettingsPath = Path.Combine(appDirPath, "appsettings.json");
            AppUpdateService.RestoreUserSettingsIfNeeded(appSettingsPath, new ConsoleDiagnosticsOutput());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] Failed to restore user settings: {ex.Message}");
        }
    }

    private sealed class ConsoleDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string line)
        {
            Console.WriteLine($"[Startup] {line}");
        }
    }
}