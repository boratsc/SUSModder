using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Bazowa implementacja nadzorcy uruchamiania gry.
/// Wspólna dla Steam i Epic – różnice w sposobie startu procesu są w metodach wirtualnych.
/// </summary>
public abstract class LaunchSupervisor : ILaunchSupervisor
{
    /// <summary>Domyślne okno stabilności procesu po starcie (Steam).</summary>
    protected static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromSeconds(60);

    /// <summary>Górny limit oczekiwania na pojawienie się procesu (Epic cold-start / wolny Unity).</summary>
    protected static readonly TimeSpan ExtendedProcessAppearTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Tolerancja na restart bootstrappera Unity/BepInEx.</summary>
    protected static readonly TimeSpan DefaultRestartGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Grace po zakończeniu Legendary launch — gra (Unity) często startuje dopiero po wyjściu CLI.
    /// </summary>
    private static readonly TimeSpan PostLaunchAbortGrace = TimeSpan.FromMinutes(3);

    public async Task<LaunchResult> LaunchAndObserveAsync(
        LaunchContext context,
        TimeSpan? observationWindow = null,
        CancellationToken cancellationToken = default)
    {
        var window = observationWindow ?? DefaultObservationWindow;

        // 1. Utwórz próbę
        var attempt = new LaunchAttempt
        {
            ModId = context.ModId,
            ModName = context.ModName,
            ModType = context.ModType,
            PlatformMode = context.PlatformMode,
            InstallPath = context.InstallPath,
            ExePath = context.ExePath,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var result = new LaunchResult { Attempt = attempt };

        try
        {
            // 2. Pre-launch hook (steam_appid.txt, SUStats, etc.)
            await OnBeforeLaunchAsync(context, cancellationToken);

            // 3. Start procesu
            Process? process;
            try
            {
                process = await StartProcessAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessStartFailed);
                result.Severity = DiagnosisSeverity.Critical;
                result.TechnicalSummary = $"Process start failed: {ex.Message}";
                result.IsSuccessful = false;
                return result;
            }

            if (process == null)
            {
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessStartFailed);
                result.Severity = DiagnosisSeverity.Critical;
                result.TechnicalSummary = "Process start returned null.";
                result.IsSuccessful = false;
                return result;
            }

            attempt.ProcessId = process.Id;

            // 4. Obserwuj proces (z tolerancją na restart Unity/BepInEx)
            var exitedEarly = await ObserveProcessAsync(process, window, cancellationToken);

            attempt.ElapsedMs = (long)(DateTimeOffset.UtcNow - attempt.StartedAtUtc).TotalMilliseconds;

            if (exitedEarly)
            {
                attempt.ExitedWithinObservationWindow = true;
                try { attempt.ExitCode = process.ExitCode; } catch { /* proces mógł już być disposed */ }
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessExitedEarly);
                result.Severity = DiagnosisSeverity.Critical;
            }

            await FinalizeObservationAsync(attempt, result, exitedEarly, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.DiagnosisCodes.Add(DiagnosisCode.Unknown);
            result.Severity = DiagnosisSeverity.Warning;
            result.TechnicalSummary = "Launch observation was cancelled.";
        }

        return result;
    }

    /// <summary>
    /// Nadzoruje zewnętrzny launch (np. Legendary), który sam startuje grę.
    /// <paramref name="launchAction"/> działa równolegle z oczekiwaniem na proces Among Us —
    /// krytyczne, bo Legendary często blokuje aż do zamknięcia gry.
    /// </summary>
    public async Task<LaunchResult> ObserveExternalLaunchAsync(
        LaunchContext context,
        Func<CancellationToken, Task> launchAction,
        TimeSpan? processAppearTimeout = null,
        TimeSpan? observationWindow = null,
        CancellationToken cancellationToken = default)
    {
        // Przy Epic cold-start (instalacja + pierwszy boot Unity) 5 min bywa za mało,
        // ale gdy Legendary już wrócił z błędem — nie czekamy pełnego extended timeout.
        var appearTimeout = processAppearTimeout ?? ExtendedProcessAppearTimeout;
        var window = observationWindow ?? DefaultObservationWindow;

        var attempt = new LaunchAttempt
        {
            ModId = context.ModId,
            ModName = context.ModName,
            ModType = context.ModType,
            PlatformMode = context.PlatformMode,
            InstallPath = context.InstallPath,
            ExePath = context.ExePath,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var result = new LaunchResult { Attempt = attempt };

        try
        {
            await OnBeforeLaunchAsync(context, cancellationToken);

            // Bez Task.Run — ten sam model co wcześniej (kontynuacje po await na wywołującym kontekście).
            // Task.Run + abort-on-complete przy wyjątku w GetLastLaunchId zabijał cały flow Epic.
            var launchTask = launchAction(cancellationToken);

            var (found, exitedEarly, processId) = await GameProcessObserver.WaitAndObserveAsync(
                GameProcessObserver.DefaultProcessName,
                context.ExePath,
                appearTimeout,
                window,
                DefaultRestartGrace,
                abortAppearWhenCompleted: launchTask,
                postAbortGrace: PostLaunchAbortGrace,
                cancellationToken);

            attempt.ProcessId = processId;
            attempt.ElapsedMs = (long)(DateTimeOffset.UtcNow - attempt.StartedAtUtc).TotalMilliseconds;

            // Jeśli Legendary/launch padł zanim gra wystartowała — preferuj ten błąd.
            if (launchTask.IsCompleted)
            {
                try
                {
                    await launchTask;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!found)
                    {
                        result.DiagnosisCodes.Add(DiagnosisCode.ProcessStartFailed);
                        result.Severity = DiagnosisSeverity.Critical;
                        result.TechnicalSummary = $"External launch failed: {ex.Message}";
                        result.IsSuccessful = false;
                        return result;
                    }
                }
            }

            if (!found)
            {
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessStartFailed);
                result.Severity = DiagnosisSeverity.Critical;
                result.TechnicalSummary =
                    $"Game process did not appear within {appearTimeout.TotalMinutes:0} minutes.";
                result.IsSuccessful = false;
                return result;
            }

            if (exitedEarly)
            {
                attempt.ExitedWithinObservationWindow = true;
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessExitedEarly);
                result.Severity = DiagnosisSeverity.Critical;
            }

            await FinalizeObservationAsync(attempt, result, exitedEarly, cancellationToken);

            // Nie czekamy na launchTask — Legendary często blokuje do zamknięcia gry.
            _ = launchTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        Debug.WriteLine($"[LaunchSupervisor] Background launch faulted: {t.Exception.GetBaseException().Message}");
                },
                TaskScheduler.Default);
        }
        catch (OperationCanceledException)
        {
            result.DiagnosisCodes.Add(DiagnosisCode.Unknown);
            result.Severity = DiagnosisSeverity.Warning;
            result.TechnicalSummary = "Launch observation was cancelled.";
        }

        return result;
    }

    private async Task FinalizeObservationAsync(
        LaunchAttempt attempt,
        LaunchResult result,
        bool exitedEarly,
        CancellationToken cancellationToken)
    {
        // Vanilla nie ma BepInEx — pomiń diagnostykę logów.
        var isVanilla = string.Equals(attempt.ModType, "Vanilla", StringComparison.OrdinalIgnoreCase)
                        || (attempt.ModId == 0 && string.Equals(attempt.ModName, "AmongUs", StringComparison.OrdinalIgnoreCase));

        if (!isVanilla)
            await CollectBepInExLogsAsync(attempt, result, cancellationToken);

        CollectPluginSnapshot(attempt, result);
        CollectWindowsSecurityEvents(attempt, result);
        ClassifyResult(result, exitedEarly);
        result.IsSuccessful = result.Severity < DiagnosisSeverity.Critical;
    }

    /// <summary>
    /// Hook wykonywany przed startem procesu (np. steam_appid.txt).
    /// </summary>
    protected virtual Task OnBeforeLaunchAsync(LaunchContext context, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Startuje proces gry. Implementacja zależna od platformy.
    /// </summary>
    protected abstract Task<Process?> StartProcessAsync(LaunchContext context, CancellationToken ct);

    /// <summary>
    /// Obserwuje proces przez observationWindow. Zwraca true jeśli proces wyszedł przedwcześnie.
    /// Toleruje krótki restart bootstrappera Unity/BepInEx.
    /// </summary>
    protected virtual Task<bool> ObserveProcessAsync(
        Process process,
        TimeSpan window,
        CancellationToken ct)
    {
        return GameProcessObserver.ObserveWithRestartGraceAsync(
            process,
            GameProcessObserver.DefaultProcessName,
            exePathHint: null,
            window,
            DefaultRestartGrace,
            ct);
    }

    /// <summary>
    /// Statyczna metoda pomocnicza do zbierania logów BepInEx – używana przez
    /// LaunchSupervisor oraz zewnętrznych konsumentów (np. Epic flow).
    /// </summary>
    public static void CollectBepInExDiagnostics(LaunchAttempt attempt, LaunchResult result)
    {
        if (string.IsNullOrWhiteSpace(attempt.InstallPath))
            return;

        var modPath = GetActualModPathStatic(attempt.InstallPath);
        var bepInExDir = Path.Combine(modPath, "BepInEx");

        if (!Directory.Exists(bepInExDir))
        {
            attempt.BepInExLogStatus = BepInExLogStatus.Missing;
            result.DiagnosisCodes.Add(DiagnosisCode.BepInExLogMissing);
            return;
        }

        var analyzer = new BepInExLogAnalyzer();

        var logOutputPath = Path.Combine(bepInExDir, "LogOutput.log");
        var logResult = analyzer.Analyze(logOutputPath, attempt.StartedAtUtc);

        attempt.BepInExLogStatus = logResult.LogStatus;

        if (logResult.LogStatus == BepInExLogStatus.Missing)
            result.DiagnosisCodes.Add(DiagnosisCode.BepInExLogMissing);
        else if (logResult.LogStatus == BepInExLogStatus.Stale)
            result.DiagnosisCodes.Add(DiagnosisCode.BepInExLogStale);

        foreach (var code in logResult.DiagnosisCodes)
            result.DiagnosisCodes.Add(code);

        result.BepInExCriticalLines.AddRange(logResult.CriticalLines);

        var errorLogPath = Path.Combine(bepInExDir, "ErrorLog.log");
        var errorResult = analyzer.Analyze(errorLogPath, attempt.StartedAtUtc);
        foreach (var code in errorResult.DiagnosisCodes)
            result.DiagnosisCodes.Add(code);
        result.BepInExCriticalLines.AddRange(errorResult.CriticalLines);
    }

    private static string GetActualModPathStatic(string installPath)
    {
        var epicSubDir = Path.Combine(installPath, "AmongUs");
        if (Directory.Exists(epicSubDir))
            return epicSubDir;
        return installPath;
    }

    /// <summary>
    /// Zbiera i analizuje logi BepInEx po próbie uruchomienia.
    /// </summary>
    protected virtual Task CollectBepInExLogsAsync(
        LaunchAttempt attempt,
        LaunchResult result,
        CancellationToken ct)
    {
        CollectBepInExDiagnostics(attempt, result);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Zbiera snapshot plików w BepInEx\plugins.
    /// </summary>
    protected virtual void CollectPluginSnapshot(LaunchAttempt attempt, LaunchResult result)
    {
        if (string.IsNullOrWhiteSpace(attempt.InstallPath))
            return;

        var modPath = GetActualModPath(attempt.InstallPath);
        var pluginDir = Path.Combine(modPath, "BepInEx", "plugins");

        if (!Directory.Exists(pluginDir))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(pluginDir))
            {
                var fi = new FileInfo(file);
                result.PluginSnapshot.Add(new PluginFileSnapshot
                {
                    FileName = fi.Name,
                    SizeBytes = fi.Length,
                    LastWriteUtc = fi.LastWriteTimeUtc
                });
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Best-effort: odpytuje Windows Defender Event Log o zdarzenia w oknie launchu.
    /// Dodaje kody diagnozy do result. Jeśli brak dostępu – ignoruje.
    /// </summary>
    protected virtual void CollectWindowsSecurityEvents(LaunchAttempt attempt, LaunchResult result)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var modPath = string.IsNullOrWhiteSpace(attempt.InstallPath)
                ? null : GetActualModPath(attempt.InstallPath);

            var diagnostics = new WindowsSecurityDiagnostics();
            var correlation = diagnostics.QueryDefenderEvents(
                modPath,
                attempt.ExePath,
                attempt.StartedAtUtc);

            foreach (var code in correlation.DiagnosisCodes)
                result.DiagnosisCodes.Add(code);
        }
        catch
        {
            // Best-effort – brak wpływu na flow
        }
    }

    /// <summary>
    /// Łączy sygnały w końcową klasyfikację.
    /// </summary>
    protected virtual void ClassifyResult(LaunchResult result, bool exitedEarly)
    {
        // Usuń duplikaty kodów
        result.DiagnosisCodes = new List<string>(new HashSet<string>(result.DiagnosisCodes, StringComparer.Ordinal));

        if (result.DiagnosisCodes.Count == 0)
        {
            result.DiagnosisCodes.Add(exitedEarly ? DiagnosisCode.ProcessExitedEarly : DiagnosisCode.Unknown);
        }

        // Ustal severity
        if (result.DiagnosisCodes.Contains(DiagnosisCode.ProcessStartFailed)
            || result.DiagnosisCodes.Contains(DiagnosisCode.ProcessExitedEarly)
            || result.DiagnosisCodes.Contains(DiagnosisCode.BepInExPluginLoadFailed)
            || result.DiagnosisCodes.Contains(DiagnosisCode.BepInExAccessDenied)
            || result.DiagnosisCodes.Contains(DiagnosisCode.DefenderThreatDetected)
            || result.DiagnosisCodes.Contains(DiagnosisCode.DefenderCfaBlocked))
        {
            result.Severity = DiagnosisSeverity.Critical;
        }
        else if (result.DiagnosisCodes.Contains(DiagnosisCode.BepInExLogMissing)
                 || result.DiagnosisCodes.Contains(DiagnosisCode.BepInExLogStale)
                 || result.DiagnosisCodes.Contains(DiagnosisCode.FirewallRuleMissingOrBlocked)
                 || result.DiagnosisCodes.Contains(DiagnosisCode.ModVersionMismatch)
                 || result.DiagnosisCodes.Contains(DiagnosisCode.DefenderEventsUnavailable))
        {
            result.Severity = DiagnosisSeverity.Warning;
        }
        else
        {
            result.Severity = DiagnosisSeverity.Info;
        }

        result.IsSuccessful = result.Severity < DiagnosisSeverity.Critical;
    }

    private static string GetActualModPath(string installPath)
    {
        // Struktura Epic: {installPath}/AmongUs
        var epicSubDir = Path.Combine(installPath, "AmongUs");
        if (Directory.Exists(epicSubDir))
            return epicSubDir;

        return installPath;
    }
}
