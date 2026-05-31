using System;
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

        // Handle Velopack activation hooks before any other startup logic
        VelopackApp.Build().Run();

        // Druga instancja (np. klik susmodder://) — przekaż kod do działającej aplikacji i zakończ
        if (DeepLinkIpc.TryForwardToRunningInstance(args))
            return;

        _instanceMutex = DeepLinkIpc.TryAcquirePrimaryInstanceMutex();
        if (_instanceMutex == null)
        {
            // Główna instancja już działa — ponów IPC (pipe mógł nie być jeszcze gotowy przy starcie)
            DeepLinkIpc.TryForwardToRunningInstance(args, maxAttempts: 50);
            return;
        }

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
