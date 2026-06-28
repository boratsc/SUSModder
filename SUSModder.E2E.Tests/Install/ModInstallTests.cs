using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.E2E.Tests.Install;

/// <summary>
/// E2E tests for full mod installation to isolated directories.
/// Uses real ModManager for Steam and EpicModInstaller for Epic.
/// Each mod is installed to an isolated folder, then structure is verified.
/// </summary>
public sealed class ModInstallTests : IDisposable
{
    private readonly E2ETestContext _ctx;
    private readonly E2EDiagnosticsOutput _log;
    private readonly IConfiguration _config;
    private ISUSModderApiClient? _client;

    public ModInstallTests()
    {
        _ctx = new E2ETestContext("install");
        _log = new E2EDiagnosticsOutput();

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
    public async Task Install_EveryFullModSteam_ToIsolatedDirectory()
    {
        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<InstallResult>();
        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            if (detail.Data?.Variants == null ||
                !detail.Data.Variants.Any(v => v.Platform.Equals("steam", StringComparison.OrdinalIgnoreCase)))
                continue;

            var result = new InstallResult
            {
                ModId = mod.Id,
                ModName = mod.Name,
                Platform = "steam"
            };

            try
            {
                var modConfig = new ModConfiguration
                {
                    Id = mod.Id,
                    ModName = mod.Name,
                    ModVersion = detail.Data.CurrentVersion,
                    ModType = "full",
                    AmongVersion = mod.AmongVersion?.DbValue ?? string.Empty
                };

                var installPath = _ctx.GetInstallPath(mod.Name + "_steam");

                _log.Write($"[E2E] Installing Steam mod {mod.Name} to {installPath}");

                var progress = new E2EProgressReporter();
                var userCallbacks = new ModManagerUserCallbacks();

                var installer = new PlatformFullModInstanceInstaller(_config);
                var installResult = await installer.InstallAsync(
                    modConfig,
                    installPath,
                    "steam",
                    progress,
                    _log,
                    userCallbacks);

                if (!installResult.Success)
                {
                    result.Status = $"FAIL_INSTALL: {installResult.ErrorMessage}";
                    results.Add(result);
                    continue;
                }

                // Verify installed structure
                result.InstallPath = installPath;
                result.StructureIssues = VerifyInstalledStructure(installPath, "steam");
                result.Status = result.StructureIssues.Count == 0 ? "PASS" : "FAIL_STRUCTURE";
            }
            catch (Exception ex)
            {
                result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(result);
        }

        WriteInstallReport(results, "steam");

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        var passed = results.Count(r => r.Status == "PASS");

        _log.Write($"[E2E] Steam install: {passed} PASS, {hardFails} FAIL out of {results.Count}");

        // Allow some failures (backend may be incomplete), but most must pass
        Assert.True(hardFails <= results.Count * 0.2,
            $"Too many Steam install failures: {hardFails}/{results.Count}\n" +
            string.Join("\n", results.Where(r => r.Status?.StartsWith("FAIL") == true)
                .Select(r => $"  {r.ModName}: {r.Status}")));
    }

    [Fact]
    public async Task Install_EveryFullModEpic_ToIsolatedDirectory()
    {
        Assert.NotNull(_client);
        var catalog = await _client.GetCatalogAsync(new() { Limit = 200 });
        Assert.NotNull(catalog.Data);

        var results = new List<InstallResult>();
        var fullMods = catalog.Data.Where(m =>
            m.Type.Equals("full", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var mod in fullMods)
        {
            var detail = await _client.GetCatalogModDetailAsync(mod.Id);
            if (detail.Data?.Variants == null ||
                !detail.Data.Variants.Any(v => v.Platform.Equals("epic", StringComparison.OrdinalIgnoreCase)))
                continue;

            var result = new InstallResult
            {
                ModId = mod.Id,
                ModName = mod.Name,
                Platform = "epic"
            };

            try
            {
                var modConfig = new ModConfiguration
                {
                    Id = mod.Id,
                    ModName = mod.Name,
                    ModVersion = detail.Data.CurrentVersion,
                    ModType = "full"
                };

                var installPath = _ctx.GetInstallPath(mod.Name + "_epic");

                _log.Write($"[E2E] Installing Epic mod {mod.Name} to {installPath}");

                var progress = new E2EProgressReporter();
                var epicInstaller = new EpicModInstaller();
                var installResult = await epicInstaller.InstallAsync(
                    modConfig,
                    installPath,
                    progress,
                    _log);

                if (!installResult.Success)
                {
                    result.Status = $"FAIL_INSTALL: {installResult.ErrorMessage}";
                    results.Add(result);
                    continue;
                }

                result.InstallPath = installPath;
                result.StructureIssues = VerifyInstalledStructure(installPath, "epic");
                result.Status = result.StructureIssues.Count == 0 ? "PASS" : "FAIL_STRUCTURE";
            }
            catch (Exception ex)
            {
                result.Status = $"FAIL_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(result);
        }

        WriteInstallReport(results, "epic");

        var hardFails = results.Count(r => r.Status?.StartsWith("FAIL") == true);
        var passed = results.Count(r => r.Status == "PASS");
        _log.Write($"[E2E] Epic install (overlay): {passed} PASS, {hardFails} FAIL out of {results.Count}");

        // Epic installer is overlay-only — all must have valid BepInEx structure
        Assert.True(hardFails == 0,
            $"{hardFails} Epic install failures.\n" +
            string.Join("\n", results.Where(r => r.Status?.StartsWith("FAIL") == true)
                .Select(r => $"  {r.ModName}: {r.Status}")));
    }

    private static List<string> VerifyInstalledStructure(string installPath, string platform)
    {
        var issues = new List<string>();

        var allFiles = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
        var isEpic = platform.Equals("epic", StringComparison.OrdinalIgnoreCase);

        // BepInEx overlay check (primary pattern for mod packages)
        var hasBepInEx = Directory.GetDirectories(installPath, "BepInEx", SearchOption.AllDirectories).Length > 0;
        var hasPluginsDir = Directory.GetDirectories(installPath, "plugins", SearchOption.AllDirectories)
            .Any(d => d.Contains("BepInEx", StringComparison.OrdinalIgnoreCase));
        var hasDoorstop = allFiles.Any(f => Path.GetFileName(f).Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase));
        var hasWinhttp = allFiles.Any(f => Path.GetFileName(f).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));

        // Epic: EpicModInstaller is overlay-only, Among Us.exe comes from EpicVersionManager separately.
        // Steam: PlatformFullModInstanceInstaller merges with vanilla, so Among Us.exe is expected.
        if (!isEpic)
        {
            string[] exeCandidates = ["Among Us.exe"];
            var hasExe = exeCandidates.Any(c => File.Exists(Path.Combine(installPath, c)));
            if (!hasExe)
                hasExe = allFiles.Any(f => Path.GetFileName(f).Equals("Among Us.exe", StringComparison.OrdinalIgnoreCase));
            if (!hasExe)
                issues.Add("Among Us.exe not found after installation (vanilla merge may have failed)");
        }

        if (!hasBepInEx)
            issues.Add("BepInEx directory not found");
        else if (!hasPluginsDir)
            issues.Add("BepInEx/plugins directory not found");
        if (!hasDoorstop)
            issues.Add("doorstop_config.ini not found");
        if (!hasWinhttp)
            issues.Add("winhttp.dll not found");

        return issues;
    }

    private void WriteInstallReport(List<InstallResult> results, string platform)
    {
        var reportLines = new List<string>
        {
            $"# {platform.ToUpperInvariant()} Mod Installation E2E Report",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Total mods installed: {results.Count}",
            "",
            "| Mod | Status | Issues |",
            "|-----|--------|--------|"
        };

        foreach (var r in results)
        {
            var statusIcon = r.Status == "PASS" ? "✅" : "❌";
            var issues = r.StructureIssues?.Count > 0
                ? string.Join("; ", r.StructureIssues)
                : "-";
            reportLines.Add($"| {r.ModName} | {statusIcon} {r.Status} | {issues} |");
        }

        _ctx.WriteArtifact($"install-report-{platform}.md",
            string.Join(Environment.NewLine, reportLines));
    }

    private sealed class InstallResult
    {
        public int ModId { get; set; }
        public string ModName { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? InstallPath { get; set; }
        public List<string> StructureIssues { get; set; } = [];
    }
}
