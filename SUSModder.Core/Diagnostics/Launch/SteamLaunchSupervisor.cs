using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Steam-specific LaunchSupervisor.
/// Zapisuje steam_appid.txt, uruchamia steam://, czeka, startuje Among Us.exe.
/// </summary>
public sealed class SteamLaunchSupervisor : LaunchSupervisor
{
    private const string SteamAppId = "945360";
    private static readonly TimeSpan SteamDelay = TimeSpan.FromSeconds(1);

    protected override async Task OnBeforeLaunchAsync(LaunchContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.InstallPath))
            throw new InvalidOperationException("InstallPath cannot be empty.");

        var modPath = GetActualModPath(context.InstallPath);
        var steamAppIdPath = Path.Combine(modPath, "steam_appid.txt");
        await File.WriteAllTextAsync(steamAppIdPath, SteamAppId, ct);
    }

    protected override async Task<Process?> StartProcessAsync(LaunchContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.ExePath) || !File.Exists(context.ExePath))
            return null;

        // Uruchom Steam
        Process.Start(new ProcessStartInfo("steam://") { UseShellExecute = true });

        // Poczekaj chwilę
        await Task.Delay(SteamDelay, ct);

        // Uruchom Among Us.exe
        var psi = new ProcessStartInfo(context.ExePath, context.Arguments ?? string.Empty)
        {
            UseShellExecute = true
        };

        return Process.Start(psi);
    }

    private static string GetActualModPath(string installPath)
    {
        var epicSubDir = Path.Combine(installPath, "AmongUs");
        if (Directory.Exists(epicSubDir))
            return epicSubDir;
        return installPath;
    }
}
