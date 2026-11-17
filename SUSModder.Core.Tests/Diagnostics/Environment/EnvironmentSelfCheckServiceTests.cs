using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Diagnostics.Environment;

public sealed class EnvironmentSelfCheckServiceTests
{
    private static readonly string[] CheckIds = ["sac", "firewall.profiles", "firewall.thirdparty", "av.thirdparty", "defender.realtime", "defender.cfa", "defender.tamper", "steam.installed", "steam.beta", "firewall.rules"];
    private static SelfCheckContext Context(string mode = "steam") => new() { Mode = mode, ModsInstallPath = Path.GetTempPath() };
    private static EnvironmentSelfCheckService Service(
        Func<int?>? sac = null, Func<int>? build = null, Func<FirewallProfilesSnapshot?>? firewall = null,
        Func<IReadOnlyList<string>?>? firewallNames = null, Func<AntivirusDetectionResult>? av = null,
        Func<DefenderSnapshot?>? defender = null, Func<IReadOnlyList<(string InstallDir, string ManifestPath)>>? steam = null,
        Func<string, string?>? rules = null, TimeSpan timeout = default) =>
        new(sac, build, firewall, firewallNames, av, defender, steam ?? (() => Array.Empty<(string, string)>()), rules ?? (_ => null), timeout);

    [Fact]
    public async Task RunsChecksInStableOrderAndMapsFirewallAndSac()
    {
        var svc = Service(() => 1, () => 22621, () => new(5, false, true, false), () => Array.Empty<string>(),
            () => new(Array.Empty<string>()), () => new(true, false, true, 0), () => Array.Empty<(string, string)>(),
            _ => null);
        var report = await svc.RunAsync(Context("epic"), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "sac", "firewall.profiles", "firewall.thirdparty", "av.thirdparty", "defender.realtime", "defender.cfa", "defender.tamper", "steam.installed", "steam.beta", "firewall.rules" }, report.Results.Select(x => x.CheckId));
        Assert.Equal("env.sac.enforce", report.Results[0].Code);
        Assert.Equal("5", report.Results[1].Data["activeProfiles"]);
        Assert.Equal(SelfCheckStatus.Warning, report.Results[1].Status);
        Assert.Equal(SelfCheckStatus.NotApplicable, report.Results[7].Status);
        Assert.NotEqual(default, report.CheckedAtUtc);
    }

    [Fact]
    public async Task ProbeFailuresAndTimeoutAreUnknownWithoutBlockingReport()
    {
        var svc = Service(sac: () => throw new InvalidOperationException(), timeout: TimeSpan.FromMilliseconds(100));
        var report = await svc.RunAsync(Context("epic"), TestContext.Current.CancellationToken);
        Assert.Equal(SelfCheckStatus.Unknown, report.Results[0].Status);
        Assert.Equal(10, report.Results.Count);
    }

    [Fact]
    public async Task MapsAvDefenderAndFirewallRegistryData()
    {
        var svc = Service(build: () => 22621, firewall: () => null, firewallNames: () => new[] { "Other Firewall" },
            av: () => new(new[] { "Example AV" }), defender: () => new(true, true, false, 2),
            steam: () => Array.Empty<(string, string)>());
        var report = await svc.RunAsync(Context("epic"), TestContext.Current.CancellationToken);
        Assert.Equal("Example AV", report.Results[3].Data["names"]);
        Assert.Equal("env.defender.realtime_on", report.Results[4].Code);
        Assert.Equal("env.defender.cfa_on", report.Results[5].Code);
        Assert.Equal("env.defender.tamper_off", report.Results[6].Code);
        Assert.Equal("env.firewall.thirdparty_detected", report.Results[2].Code);
        Assert.Equal(SelfCheckStatus.Unknown, report.Results[1].Status);
    }

    [Fact]
    public void JsonSerializationRoundTripsReport()
    {
        var expected = new SelfCheckReport(DateTimeOffset.UtcNow,
            new[] { new SelfCheckResult("x", SelfCheckStatus.Warning, "env.test", new Dictionary<string, string> { ["key"] = "value" }) });
        var json = SelfCheckReportJson.Serialize(expected);
        Assert.Contains("\"status\":\"warning\"", json);
        var actual = SelfCheckReportJson.Deserialize(json);
        Assert.NotNull(actual);
        Assert.Equal(expected.Results[0].Code, actual.Results[0].Code);
        Assert.Equal(expected.Results[0].CheckId, actual.Results[0].CheckId);
        Assert.Equal(expected.CheckedAtUtc, actual.CheckedAtUtc);
        Assert.Equal("value", actual.Results[0].Data["key"]);
    }

    [Fact]
    public async Task EpicMode_SteamInstalledAndBeta_NotApplicable()
    {
        var r = await Service().RunAsync(Context("epic"), TestContext.Current.CancellationToken);
        Assert.Equal(("env.steam.not_applicable", "env.steam.beta_not_applicable"),
            (r.Results[7].Code, r.Results[8].Code));
        Assert.Equal(SelfCheckStatus.NotApplicable, r.Results[7].Status);
        Assert.Equal(SelfCheckStatus.NotApplicable, r.Results[8].Status);
    }

    [Fact]
    public async Task SingleManifestInstall_Ok_WithPath()
    {
        var r = await Service(steam: () => new[] { ("install", "manifest") }).RunAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal(SelfCheckStatus.Ok, r.Results[7].Status);
        Assert.Equal("env.steam.installed", r.Results[7].Code);
        Assert.Equal("install", r.Results[7].Data["path"]);
    }

    [Fact]
    public async Task MultipleInstalls_Info_WithCount()
    {
        var r = await Service(steam: () => new[] { ("a", "1"), ("b", "2") }).RunAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal(SelfCheckStatus.Info, r.Results[7].Status);
        Assert.Equal("env.steam.multiple", r.Results[7].Code);
        Assert.Equal("2", r.Results[7].Data["count"]);
    }

    [Fact]
    public async Task NoManifest_ValidVanillaExe_InfoFilesOnly()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "Among Us.exe"), "");
            var r = await Service(steam: () => Array.Empty<(string, string)>()).RunAsync(new() { Mode = "steam", ModsInstallPath = dir.FullName, VanillaInstallPath = dir.FullName }, TestContext.Current.CancellationToken);
            Assert.Equal(SelfCheckStatus.Info, r.Results[7].Status);
            Assert.Equal("env.steam.files_only", r.Results[7].Code);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task NoManifest_NoValidVanilla_WarningNotFound()
    {
        var r = await Service(steam: () => Array.Empty<(string, string)>()).RunAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal(SelfCheckStatus.Warning, r.Results[7].Status);
        Assert.Equal("env.steam.not_found", r.Results[7].Code);
    }

    private static string Manifest(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
    private const string AcfStart = "\"AppState\" { \"appid\" \"945360\" \"installdir\" \"Among Us\" ";
    private static async Task<SelfCheckResult> Beta(string content)
    {
        var path = Manifest(AcfStart + content + " }");
        try { return (await Service(steam: () => new[] { ("install", path) }).RunAsync(Context())).Results[8]; }
        finally { File.Delete(path); }
    }

    [Fact] public async Task DefaultBranch_Ok() { var r = await Beta("\"UserConfig\" { \"betakey\" \"public\" }"); Assert.Equal(SelfCheckStatus.Ok, r.Status); Assert.Equal("env.steam.beta_default", r.Code); }
    [Fact] public async Task NonDefaultBranch_Warning_WithBranch() { var r = await Beta("\"UserConfig\" { \"betakey\" \"public-beta\" }"); Assert.Equal(SelfCheckStatus.Warning, r.Status); Assert.Equal("env.steam.beta_nondefault", r.Code); Assert.Equal("public-beta", r.Data["branch"]); }
    [Fact] public async Task InconsistentSections_Warning() { var r = await Beta("\"UserConfig\" { \"betakey\" \"public-beta\" } \"MountedConfig\" { \"betakey\" \"public\" }"); Assert.Equal(SelfCheckStatus.Warning, r.Status); Assert.Equal("env.steam.beta_inconsistent", r.Code); }
    [Fact] public async Task CorruptManifest_Unknown() { var r = await Beta("not valid acf"); Assert.Equal(SelfCheckStatus.Unknown, r.Status); Assert.Equal("env.steam.beta_unknown", r.Code); }

    [Fact] public async Task NoManifest_Unknown_ReasonNoManifest() { var r = (await Service(steam: () => Array.Empty<(string, string)>()).RunAsync(Context(), TestContext.Current.CancellationToken)).Results[8]; Assert.Equal(SelfCheckStatus.Unknown, r.Status); Assert.Equal("no_manifest", r.Data["reason"]); }
    [Fact] public async Task AmbiguousInstalls_Unknown_ReasonAmbiguous() { var r = (await Service(steam: () => new[] { ("a", "1"), ("b", "2") }).RunAsync(Context(), TestContext.Current.CancellationToken)).Results[8]; Assert.Equal(SelfCheckStatus.Unknown, r.Status); Assert.Equal("ambiguous", r.Data["reason"]); }

    private static string Readback(string path) => $"Rule Name: Among Us - Vanilla (Inbound)\nEnabled: Yes\nDirection: In\nProgram: {path}\nAction: Allow\nOk.";
    [Fact]
    public async Task Epic_MatchingReadback_Ok()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var target = SUSModder.Core.GameIntegration.EpicInstallPathResolver.GetVanillaExpectedExePath(dir.FullName);
            var r = await Service(rules: _ => Readback(target)).RunAsync(new() { Mode = "epic", ModsInstallPath = dir.FullName }, TestContext.Current.CancellationToken);
            Assert.Equal(SelfCheckStatus.Ok, r.Results[9].Status); Assert.Equal("env.firewall.rules_ok", r.Results[9].Code);
        }
        finally { dir.Delete(true); }
    }
    [Fact]
    public async Task Epic_Drift_Warning_WithData()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var r = await Service(rules: _ => Readback("C:\\other\\Among Us.exe")).RunAsync(new() { Mode = "epic", ModsInstallPath = dir.FullName }, TestContext.Current.CancellationToken);
            Assert.Equal(SelfCheckStatus.Warning, r.Results[9].Status); Assert.Equal("env.firewall.rules_drift", r.Results[9].Code);
            Assert.Equal("C:\\other\\Among Us.exe", r.Results[9].Data["rulePath"]);
            Assert.Equal(SUSModder.Core.GameIntegration.EpicInstallPathResolver.GetVanillaExpectedExePath(dir.FullName), r.Results[9].Data["targetPath"]);
        }
        finally { dir.Delete(true); }
    }
    [Fact] public async Task BothReadbacksNull_InfoMissing() { var r = await Service(rules: _ => null).RunAsync(new() { Mode = "epic", ModsInstallPath = Path.GetTempPath() }, TestContext.Current.CancellationToken); Assert.Equal(SelfCheckStatus.Info, r.Results[9].Status); Assert.Equal("env.firewall.rules_missing", r.Results[9].Code); }
    [Fact]
    public async Task Steam_SingleInstall_ExistingExe_Ok()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var exe = Path.Combine(dir.FullName, "Among Us.exe"); File.WriteAllText(exe, "");
            var r = await Service(steam: () => new[] { (dir.FullName, "manifest") }, rules: _ => Readback(exe)).RunAsync(Context(), TestContext.Current.CancellationToken);
            Assert.Equal(SelfCheckStatus.Ok, r.Results[9].Status); Assert.Equal("env.firewall.rules_ok", r.Results[9].Code);
        }
        finally { dir.Delete(true); }
    }
    [Fact] public async Task Steam_NoTarget_NotApplicable() { var r = await Service(steam: () => Array.Empty<(string, string)>()).RunAsync(Context(), TestContext.Current.CancellationToken); Assert.Equal(SelfCheckStatus.NotApplicable, r.Results[9].Status); Assert.Equal("env.firewall.rules_not_applicable", r.Results[9].Code); }

    [Fact]
    public async Task ThrowingProbe_Unknown_PreservesCheckId()
    {
        var r = await Service(sac: () => throw new InvalidOperationException(), build: () => 22621).RunAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal("sac", r.Results[0].CheckId); Assert.Equal(SelfCheckStatus.Unknown, r.Results[0].Status); Assert.Equal("env.sac.unknown", r.Results[0].Code);
        Assert.Equal(10, r.Results.Count); Assert.Equal(CheckIds, r.Results.Select(x => x.CheckId));
    }
    [Fact]
    public async Task SlowProbe_Timeout_Unknown_PreservesCheckId()
    {
        var r = await Service(firewall: () => { Thread.Sleep(500); return null; }, timeout: TimeSpan.FromMilliseconds(100)).RunAsync(Context(), TestContext.Current.CancellationToken);
        Assert.Equal("firewall.profiles", r.Results[1].CheckId); Assert.Equal(SelfCheckStatus.Unknown, r.Results[1].Status); Assert.Equal("env.firewall.unknown", r.Results[1].Code);
        Assert.Equal(10, r.Results.Count);
    }
    [Fact] public async Task SacNullValue_Unknown() { var r = await Service(sac: () => null, build: () => 22621).RunAsync(Context(), TestContext.Current.CancellationToken); Assert.Equal(SelfCheckStatus.Unknown, r.Results[0].Status); Assert.Equal("env.sac.unknown", r.Results[0].Code); }
    [Fact] public async Task SacOldBuild_NotApplicable() { var r = await Service(sac: () => 0, build: () => 22000).RunAsync(Context(), TestContext.Current.CancellationToken); Assert.Equal(SelfCheckStatus.NotApplicable, r.Results[0].Status); }
    [Fact] public async Task Report_AlwaysTenResults_StableCheckIds() { var r = await Service().RunAsync(Context("epic"), TestContext.Current.CancellationToken); Assert.Equal(10, r.Results.Count); Assert.Equal(CheckIds, r.Results.Select(x => x.CheckId)); }
}
