using Avalonia;
using Avalonia.ReactiveUI;
using System;

namespace SUSModder;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Usunięto blokujące operacje - teraz wykonywane asynchronicznie w App.axaml.cs
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}