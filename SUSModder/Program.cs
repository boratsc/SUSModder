using System;
using System.Text;
using Avalonia;
using Avalonia.ReactiveUI;
using Velopack;

namespace SUSModder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

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
