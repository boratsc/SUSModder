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
    private static readonly TimeSpan DefaultObservationWindow = TimeSpan.FromSeconds(30);

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

            // 4. Obserwuj proces
            var exitedEarly = await ObserveProcessAsync(process, window, cancellationToken);

            attempt.ElapsedMs = (long)(DateTimeOffset.UtcNow - attempt.StartedAtUtc).TotalMilliseconds;

            if (exitedEarly)
            {
                attempt.ExitedWithinObservationWindow = true;
                try { attempt.ExitCode = process.ExitCode; } catch { /* proces mógł już być disposed */ }
                result.DiagnosisCodes.Add(DiagnosisCode.ProcessExitedEarly);
                result.Severity = DiagnosisSeverity.Critical;
            }

            // 5. Zbierz logi BepInEx
            await CollectBepInExLogsAsync(attempt, result, cancellationToken);

            // 6. Zbierz snapshot pluginów
            CollectPluginSnapshot(attempt, result);

            // 7. Best-effort: Windows Defender korelacja
            CollectWindowsSecurityEvents(attempt, result);

            // 8. Klasyfikacja końcowa
            ClassifyResult(result, exitedEarly);

            result.IsSuccessful = result.Severity < DiagnosisSeverity.Critical;
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
    /// Hook wykonywany przed startem procesu (np. steam_appid.txt).
    /// </summary>
    protected virtual Task OnBeforeLaunchAsync(LaunchContext context, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Startuje proces gry. Implementacja zależna od platformy.
    /// </summary>
    protected abstract Task<Process?> StartProcessAsync(LaunchContext context, CancellationToken ct);

    /// <summary>
    /// Obserwuje proces przez observationWindow. Zwraca true jeśli proces wyszedł przedwcześnie.
    /// </summary>
    protected virtual async Task<bool> ObserveProcessAsync(
        Process process,
        TimeSpan window,
        CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(window);

            await process.WaitForExitAsync(cts.Token);
            return true; // proces wyszedł w oknie obserwacji
        }
        catch (OperationCanceledException)
        {
            // Proces nadal działa po oknie – to normalne
            return false;
        }
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
