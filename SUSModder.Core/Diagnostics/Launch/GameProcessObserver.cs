using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Obserwuje proces gry po nazwie (i opcjonalnie ścieżce exe).
/// Obsługuje restart bootstrappera Unity/BepInEx oraz wolny cold-start (Epic/Legendary).
/// </summary>
internal static class GameProcessObserver
{
    public const string DefaultProcessName = "Among Us";

    /// <summary>
    /// Czeka na pojawienie się procesu gry. Kończy wcześniej, gdy <paramref name="abortWhenCompleted"/>
    /// zakończy się i po krótkiej grace nadal nie ma procesu (np. Legendary wrócił z błędem).
    /// </summary>
    public static async Task<Process?> WaitForProcessAppearAsync(
        string processName,
        string? exePathHint,
        TimeSpan appearTimeout,
        Task? abortWhenCompleted,
        TimeSpan postAbortGrace,
        CancellationToken cancellationToken)
    {
        var appearDeadline = DateTimeOffset.UtcNow + appearTimeout;
        DateTimeOffset? abortGraceDeadline = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var process = FindGameProcess(processName, exePathHint);
            if (process != null)
                return process;

            var now = DateTimeOffset.UtcNow;
            if (now >= appearDeadline)
                return null;

            if (abortWhenCompleted is { IsCompleted: true })
            {
                abortGraceDeadline ??= now + postAbortGrace;
                if (now >= abortGraceDeadline)
                    return null;
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    /// <summary>
    /// Czeka aż pojawi się proces gry, potem obserwuje go przez <paramref name="stabilityWindow"/>.
    /// Krótki restart w <paramref name="restartGrace"/> nie jest traktowany jako early-exit.
    /// </summary>
    public static async Task<(bool Found, bool ExitedEarly, int? ProcessId)> WaitAndObserveAsync(
        string processName,
        string? exePathHint,
        TimeSpan appearTimeout,
        TimeSpan stabilityWindow,
        TimeSpan restartGrace,
        Task? abortAppearWhenCompleted,
        TimeSpan postAbortGrace,
        CancellationToken cancellationToken)
    {
        var process = await WaitForProcessAppearAsync(
            processName,
            exePathHint,
            appearTimeout,
            abortAppearWhenCompleted,
            postAbortGrace,
            cancellationToken);

        if (process == null)
            return (false, false, null);

        var processId = SafeGetId(process);
        var exitedEarly = await ObserveWithRestartGraceAsync(
            process,
            processName,
            exePathHint,
            stabilityWindow,
            restartGrace,
            cancellationToken);

        process.Dispose();
        return (true, exitedEarly, processId);
    }

    /// <summary>
    /// Obserwuje już znany proces; toleruje krótki restart pod tą samą nazwą.
    /// </summary>
    public static async Task<bool> ObserveWithRestartGraceAsync(
        Process process,
        string processName,
        string? exePathHint,
        TimeSpan window,
        TimeSpan restartGrace,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + window;
        var current = process;

        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!HasExited(current))
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                var restarted = await WaitForProcessAsync(
                    processName,
                    exePathHint,
                    restartGrace,
                    cancellationToken);

                if (restarted == null)
                    return true; // exited early

                if (!ReferenceEquals(current, process))
                    current.Dispose();

                current = restarted;
                await Task.Delay(1000, cancellationToken);
            }

            return false; // nadal działa po oknie
        }
        finally
        {
            if (!ReferenceEquals(current, process))
                current.Dispose();
        }
    }

    private static async Task<Process?> WaitForProcessAsync(
        string processName,
        string? exePathHint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = FindGameProcess(processName, exePathHint);
            if (process != null)
                return process;

            await Task.Delay(500, cancellationToken);
        }

        return null;
    }

    private static Process? FindGameProcess(string processName, string? exePathHint)
    {
        // Nie używamy MainModule.FileName — na Windows często rzuca FileNotFoundException
        // (bitness / uprawnienia) i zaśmieca debugger bez realnej korzyści.
        _ = exePathHint;

        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(processName);
        }
        catch
        {
            return null;
        }

        if (candidates.Length == 0)
            return null;

        return PickNewest(candidates);
    }

    private static Process PickNewest(Process[] candidates)
    {
        Process? best = null;
        var bestStart = DateTime.MinValue;

        foreach (var candidate in candidates)
        {
            DateTime start;
            try { start = candidate.StartTime; }
            catch { start = DateTime.MinValue; }

            if (best == null || start >= bestStart)
            {
                best?.Dispose();
                best = candidate;
                bestStart = start;
            }
            else
            {
                candidate.Dispose();
            }
        }

        return best!;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            process.Refresh();
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static int? SafeGetId(Process process)
    {
        try { return process.Id; }
        catch { return null; }
    }
}
