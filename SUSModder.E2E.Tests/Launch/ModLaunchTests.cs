using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Diagnostics.Launch;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests.Launch;

/// <summary>
/// E2E launch tests for full mods. These tests:
/// 1. Install the mod to an isolated directory
/// 2. Launch Among Us through SteamLaunchSupervisor (Steam) or EpicVersionManager (Epic)
/// 3. Collect BepInEx logs
/// 4. Analyze logs for critical errors
/// 5. Report results
///
/// NOTE: These tests require Steam and/or Epic to be installed and logged in.
/// They will be skipped (not failed) if the game cannot be launched.
/// Set SUSMODDER_E2E_LAUNCH=1 to enable launch tests.
/// Set SUSMODDER_E2E_OBSERVATION_SECONDS to override observation window (default 45).
/// </summary>
public sealed class ModLaunchTests : IDisposable
{
    private readonly E2ETestContext _ctx;
    private readonly E2EDiagnosticsOutput _log;
    private readonly IConfiguration _config;
    private ISUSModderApiClient? _client;
    private readonly bool _launchEnabled;
    private readonly TimeSpan _observationWindow;

    public ModLaunchTests()
    {
        _ctx = new E2ETestContext("launch");
        _log = new E2EDiagnosticsOutput();

        _launchEnabled = Environment.GetEnvironmentVariable("SUSMODDER_E2E_LAUNCH") == "1";
        var obsSeconds = Environment.GetEnvironmentVariable("SUSMODDER_E2E_OBSERVATION_SECONDS");
        _observationWindow = int.TryParse(obsSeconds, out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromSeconds(45);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.susmodder-cdn.ovh/v2"
            })
            .Build();

        _client = new SUSModderApiClient(_config, _log);
        SUSModderApiClientProvider.SetDefault(_client);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _ctx.Dispose();
        SUSModderApiClientProvider.ResetForTests();
    }

    [Fact]
    public async Task Launch_SteamFullMods_CollectBepInExLogs()
    {
        if (!_launchEnabled)
        {
            _log.Write("[E2E] Launch tests disabled. Set SUSMODDER_E2E_LAUNCH=1 to enable.");
            return; // Skip, not fail
        }

        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<LaunchTestResult>();
        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).Take(5).ToList(); // Limit to 5 for launch tests

        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            if (detail.Data?.Variants == null ||
                !detail.Data.Variants.Any(v => v.Platform.Equals("steam", StringComparison.OrdinalIgnoreCase)))
                continue;

            var result = new LaunchTestResult
            {
                ModId = mod.Id,
                ModName = mod.Name,
                Platform = "steam"
            };

            try
            {
                // First, install the mod
                var modConfig = new ModConfiguration
                {
                    Id = mod.Id,
                    ModName = mod.Name,
                    ModVersion = detail.Data.CurrentVersion,
                    ModType = "full",
                    AmongVersion = mod.AmongVersion?.DbValue ?? string.Empty
                };

                var installPath = _ctx.GetInstallPath(mod.Name + "_steam_launch");

                _log.Write($"[E2E] Installing {mod.Name} for launch test...");
                var progress = new E2EProgressReporter();
                var installer = new PlatformFullModInstanceInstaller(_config);
                var installResult = await installer.InstallAsync(
                    modConfig, installPath, "steam", progress, _log, new ModManagerUserCallbacks());

                if (!installResult.Success)
                {
                    result.Status = $"SKIP_INSTALL_FAILED: {installResult.ErrorMessage}";
                    results.Add(result);
                    continue;
                }

                result.InstallPath = installPath;

                // Find Among Us.exe
                var exePath = Path.Combine(installPath, "Among Us.exe");
                if (!File.Exists(exePath))
                {
                    result.Status = "FAIL_NO_EXE";
                    results.Add(result);
                    continue;
                }

                // Launch through SteamLaunchSupervisor
                _log.Write($"[E2E] Launching {mod.Name} (Steam)...");
                var context = new LaunchContext
                {
                    ModId = mod.Id,
                    ModName = mod.Name,
                    ModType = "full",
                    PlatformMode = "steam",
                    InstallPath = installPath,
                    ExePath = exePath,
                    WasRunAsAdmin = false
                };

                var supervisor = new SteamLaunchSupervisor();
                var launchResult = await supervisor.LaunchAndObserveAsync(
                    context,
                    observationWindow: _observationWindow,
                    cancellationToken: CancellationToken.None);

                result.IsSuccessful = launchResult.IsSuccessful;
                result.Severity = launchResult.Severity.ToString();
                result.DiagnosisCodes = launchResult.DiagnosisCodes;
                result.TechnicalSummary = launchResult.TechnicalSummary;
                result.BepInExCriticalLines = launchResult.BepInExCriticalLines;
                result.BepInExLogStatus = launchResult.Attempt.BepInExLogStatus.ToString();

                // Determine pass/fail
                if (launchResult.IsSuccessful &&
                    !launchResult.DiagnosisCodes.Contains(DiagnosisCode.BepInExPluginLoadFailed) &&
                    !launchResult.DiagnosisCodes.Contains(DiagnosisCode.ProcessExitedEarly))
                {
                    result.Status = launchResult.DiagnosisCodes.Count == 0 ? "PASS" : "PASS_WITH_INFO";
                }
                else
                {
                    result.Status = "FAIL_LAUNCH";
                }

                // Copy BepInEx logs to artifacts
                CopyBepInExLogs(installPath, mod.Name, "steam");

                // Kill the process if still running
                if (launchResult.Attempt.ProcessId.HasValue)
                {
                    try { System.Diagnostics.Process.GetProcessById(launchResult.Attempt.ProcessId.Value)?.Kill(); }
                    catch { /* already exited */ }
                }
            }
            catch (Exception ex)
            {
                result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(result);
        }

        WriteLaunchReport(results, "steam");

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        _log.Write($"[E2E] Steam launch: {results.Count(r => r.Status == "PASS")} PASS, {hardFails} FAIL, {results.Count(r => r.Status?.StartsWith("SKIP") == true)} SKIP out of {results.Count}");

        // Launch tests are inherently semi-manual — hard failures are blockers for release
        Assert.True(hardFails == 0,
            $"{hardFails} Steam launch failures detected.\n" +
            string.Join("\n", results.Where(r => r.Status?.StartsWith("FAIL") == true)
                .Select(r => $"  {r.ModName}: {r.Status}\n    {r.TechnicalSummary}")));
    }

    private void CopyBepInExLogs(string installPath, string modName, string platform)
    {
        var bepInExDirs = Directory.GetDirectories(installPath, "BepInEx", SearchOption.AllDirectories);
        foreach (var dir in bepInExDirs)
        {
            var logOutput = Path.Combine(dir, "LogOutput.log");
            if (File.Exists(logOutput))
            {
                var dest = _ctx.GetLogPath($"{platform}_{modName}_LogOutput.log");
                File.Copy(logOutput, dest, true);
            }
            var errorLog = Path.Combine(dir, "ErrorLog.log");
            if (File.Exists(errorLog))
            {
                var dest = _ctx.GetLogPath($"{platform}_{modName}_ErrorLog.log");
                File.Copy(errorLog, dest, true);
            }
        }
    }

    private void WriteLaunchReport(List<LaunchTestResult> results, string platform)
    {
        var reportLines = new List<string>
        {
            $"# {platform.ToUpperInvariant()} Launch E2E Report",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Observation window: {_observationWindow.TotalSeconds}s",
            $"Total mods launched: {results.Count}",
            "",
            "| Mod | Status | Severity | BepInEx Status | Codes | Critical Lines |",
            "|-----|--------|----------|---------------|-------|---------------|"
        };

        foreach (var r in results)
        {
            var statusIcon = r.Status switch
            {
                "PASS" => "✅",
                "PASS_WITH_INFO" => "✅",
                var s when s?.StartsWith("FAIL") == true => "❌",
                _ => "⏭️"
            };
            var codes = r.DiagnosisCodes.Count > 0
                ? string.Join(", ", r.DiagnosisCodes)
                : "-";
            var criticalLines = r.BepInExCriticalLines.Count > 0
                ? string.Join(" | ", r.BepInExCriticalLines.Take(3))
                : "-";
            reportLines.Add(
                $"| {r.ModName} | {statusIcon} {r.Status} | {r.Severity} | {r.BepInExLogStatus} | {codes} | {criticalLines} |");
        }

        _ctx.WriteArtifact($"launch-report-{platform}.md",
            string.Join(Environment.NewLine, reportLines));
    }

    private sealed class LaunchTestResult
    {
        public int ModId { get; set; }
        public string ModName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? InstallPath { get; set; }
        public bool IsSuccessful { get; set; }
        public string Severity { get; set; } = string.Empty;
        public List<string> DiagnosisCodes { get; set; } = [];
        public string TechnicalSummary { get; set; } = string.Empty;
        public List<string> BepInExCriticalLines { get; set; } = [];
        public string BepInExLogStatus { get; set; } = string.Empty;
    }
}
