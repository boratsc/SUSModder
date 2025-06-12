using Avalonia;
using Avalonia.ReactiveUI;
using System;
using SUSModder; // <-- DODAJ TĘ LINIĘ

namespace SUSModder; // Ta nazwa może się różnić, to nie jest kluczowe

class Program
{
    // ... reszta kodu
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Tutaj używana jest klasa "App"
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>() // <-- Błąd pojawia się tutaj
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}