using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Velopack;

namespace SUSModder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle Velopack activation hooks before any other startup logic
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}