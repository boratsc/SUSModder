using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.ReactiveUI;
using SUSModder.Core.Services;
using Velopack;

namespace SUSModder;

internal static class Program
{
    private static Mutex? _instanceMutex;

    [STAThread]
    [SupportedOSPlatform("windows")]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Loguj argumenty startowe dla diagnostyki deep linków
        if (args.Length > 0)
        {
            Debug.WriteLine($"[Program] Argumenty startowe ({args.Length}): {string.Join(" | ", args)}");
        }

        // Handle Velopack activation hooks before any other startup logic
        VelopackApp.Build().Run();

        _instanceMutex = DeepLinkIpc.TryAcquirePrimaryInstanceMutex();
        if (_instanceMutex == null)
        {
            // Główna instancja już działa — ponów IPC (pipe mógł nie być jeszcze gotowy przy starcie).
            // Bez deep linka wysyłamy aktywację, aby przywrócić okno z tray lub nadać mu focus.
            var forwarded = DeepLinkIpc.TryForwardToRunningInstance(args, maxAttempts: 50);
            Debug.WriteLine(forwarded
                ? "[Program] IPC: przekazano do działającej instancji."
                : "[Program] IPC: nie udało się przekazać do działającej instancji (fallback zapisany jeśli był pack code).");
            return;
        }

        Debug.WriteLine("[Program] Uzyskano mutex — start jako główna instancja.");
        ParseStartupDeepLink(args);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
    }

    private static void ParseStartupDeepLink(string[] args)
    {
        if (args == null || args.Length == 0)
            return;

        foreach (var arg in args)
        {
            var parsed = DeepLinkService.ParseDeepLink(arg);
            if (parsed.IsValid && !string.IsNullOrEmpty(parsed.PackCode))
            {
                App.PendingModPackCode = parsed.PackCode;
                App.PendingModPackAutoInstall = parsed.AutoInstall;
                Debug.WriteLine($"[Program] Deep link wykryty: {parsed.PackCode} (autoInstall={parsed.AutoInstall})");
                break;
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
