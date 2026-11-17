using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.GameIntegration.Steam;
using SUSModder.Core.Services;

namespace SUSModder.Core.Diagnostics.Environment;

/// <summary>Runs read-only environment probes. Callers must not overlap invocations; the UI serializes them.</summary>
public sealed class EnvironmentSelfCheckService
{
    private readonly Func<int?> _sac;
    private readonly Func<int> _build;
    private readonly Func<FirewallProfilesSnapshot?> _firewall;
    private readonly Func<IReadOnlyList<string>?> _thirdPartyFirewall;
    private readonly Func<AntivirusDetectionResult> _av;
    private readonly Func<DefenderSnapshot?> _defender;
    private readonly Func<IReadOnlyList<(string InstallDir, string ManifestPath)>> _steam;
    private readonly TimeSpan _timeout;

    public EnvironmentSelfCheckService(Func<int?>? sacStateProbe = null, Func<int>? osBuildProbe = null,
        Func<FirewallProfilesSnapshot?>? firewallProbe = null, Func<IReadOnlyList<string>?>? thirdPartyFirewallProbe = null,
        Func<AntivirusDetectionResult>? avProbe = null, Func<DefenderSnapshot?>? defenderProbe = null,
        Func<IReadOnlyList<(string InstallDir, string ManifestPath)>>? steamInstallsProbe = null,
        TimeSpan probeTimeout = default)
    {
        _sac = sacStateProbe ?? (() => OperatingSystem.IsWindows() ? ProbeSac() : null);
        _build = osBuildProbe ?? new Func<int>(() => System.Environment.OSVersion.Version.Build);
        _firewall = firewallProbe ?? (() => OperatingSystem.IsWindows() ? ProbeFirewall() : null);
        _thirdPartyFirewall = thirdPartyFirewallProbe ?? (() => OperatingSystem.IsWindows() ? ProbeThirdPartyFirewall() : null);
        _av = avProbe ?? (() => new AntivirusDetectionService().DetectInstalledThirdPartyAntivirus());
        _defender = defenderProbe ?? (() => OperatingSystem.IsWindows() ? ProbeDefender() : null);
        _steam = steamInstallsProbe ?? AmongUsPathDiscovery.TryFindSteamInstallsWithManifest;
        _timeout = probeTimeout == default ? TimeSpan.FromSeconds(5) : probeTimeout;
    }

    public async Task<SelfCheckReport> RunAsync(SelfCheckContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var results = new SelfCheckResult[9];
        var snapshot = await RunValueProbeAsync(_defender, ct).ConfigureAwait(false);
        IReadOnlyList<(string InstallDir, string ManifestPath)>? steamSnapshot;
        try { steamSnapshot = await Task.Run(_steam, CancellationToken.None).WaitAsync(_timeout, ct).ConfigureAwait(false); }
        catch { steamSnapshot = null; }
        var jobs = new (string Id, string UnknownCode, Func<SelfCheckResult> Job)[]
        {
            ("sac", "env.sac.unknown", () => Sac(context)), ("firewall.profiles", "env.firewall.unknown", Firewall),
            ("firewall.thirdparty", "env.firewall.thirdparty_unknown", ThirdPartyFirewall), ("av.thirdparty", "env.av.unknown", Antivirus),
            ("defender.realtime", "env.defender.unknown", () => DefenderRealtime(snapshot)),
            ("defender.cfa", "env.defender.cfa_unknown", () => DefenderCfa(snapshot)),
            ("defender.tamper", "env.defender.tamper_unknown", () => DefenderTamper(snapshot)),
             ("steam.installed", "env.steam.unknown", () => SteamInstalled(context, steamSnapshot)), ("steam.beta", "env.steam.beta_unknown", () => SteamBeta(context, steamSnapshot))
        };
        var tasks = jobs.Select(job => RunProbeAsync(job.Id, job.UnknownCode, job.Job, ct)).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
        for (var i = 0; i < results.Length; i++) results[i] = tasks[i].Result;
        return new(DateTimeOffset.UtcNow, results);
    }

    private async Task<SelfCheckResult> RunProbeAsync(string id, string unknownCode, Func<SelfCheckResult> probe, CancellationToken ct)
    {
        // shortcut: WaitAsync nie anuluje sondy systemowej (WMI/COM/netsh); po timeoutcie porzucone zadanie leci dalej, wynik Unknown. Upgrade: provider-respektowane timeouty.
        try { return await Task.Run(probe, CancellationToken.None).WaitAsync(_timeout, ct).ConfigureAwait(false); }
        catch { return Unknown(id, unknownCode); }
    }
    private async Task<DefenderSnapshot?> RunValueProbeAsync(Func<DefenderSnapshot?> probe, CancellationToken ct)
    {
        try { return await Task.Run(probe, CancellationToken.None).WaitAsync(_timeout, ct).ConfigureAwait(false); }
        catch { return null; }
    }

    private SelfCheckResult Sac(SelfCheckContext _)
    {
        if (!OperatingSystem.IsWindows() || _build() < 22621) return Result("sac", SelfCheckStatus.NotApplicable, "env.sac.not_applicable");
        var value = _sac();
        return value switch
        {
            0 => Result("sac", SelfCheckStatus.Info, "env.sac.off", ("state", "0")),
            1 => Result("sac", SelfCheckStatus.Warning, "env.sac.enforce", ("state", "1")),
            2 => Result("sac", SelfCheckStatus.Info, "env.sac.evaluation", ("state", "2")),
            _ => Result("sac", SelfCheckStatus.Unknown, "env.sac.unknown", ("state", value?.ToString() ?? "null"))
        };
    }
    private SelfCheckResult Firewall()
    {
        var f = _firewall();
        if (f is null) return Unknown("firewall.profiles", "env.firewall.unknown");
        var active = f.CurrentProfileTypes;
        var disabled = ((active & 1) != 0 && !f.DomainEnabled) || ((active & 2) != 0 && !f.PrivateEnabled) || ((active & 4) != 0 && !f.PublicEnabled);
        return Result("firewall.profiles", disabled ? SelfCheckStatus.Warning : SelfCheckStatus.Info, disabled ? "env.firewall.disabled" : "env.firewall.enabled",
            ("activeProfiles", active.ToString()), ("domain", f.DomainEnabled.ToString().ToLowerInvariant()), ("private", f.PrivateEnabled.ToString().ToLowerInvariant()), ("public", f.PublicEnabled.ToString().ToLowerInvariant()));
    }
    private SelfCheckResult ThirdPartyFirewall()
    {
        var names = _thirdPartyFirewall();
        return names is null ? Unknown("firewall.thirdparty", "env.firewall.thirdparty_unknown") :
            names.Count == 0 ? Result("firewall.thirdparty", SelfCheckStatus.Info, "env.firewall.thirdparty_none") :
            Result("firewall.thirdparty", SelfCheckStatus.Info, "env.firewall.thirdparty_detected", ("names", string.Join("|", names)));
    }
    private SelfCheckResult Antivirus()
    {
        var av = _av();
        return !av.SourcesAvailable ? Unknown("av.thirdparty", "env.av.unknown") :
            av.HasThirdPartyAntivirus ? Result("av.thirdparty", SelfCheckStatus.Info, "env.av.detected", ("names", string.Join("|", av.ProductNames))) :
            Result("av.thirdparty", SelfCheckStatus.Ok, "env.av.none");
    }
    private SelfCheckResult DefenderRealtime(DefenderSnapshot? d)
    {
        if (d is null) return Unknown("defender.realtime", "env.defender.unknown");
        return d.RealTimeProtectionEnabled switch
        {
            true => Result("defender.realtime", SelfCheckStatus.Info, "env.defender.realtime_on", ("antivirusEnabled", S(d.AntivirusEnabled))),
            false => Result("defender.realtime", SelfCheckStatus.Info, "env.defender.realtime_off", ("antivirusEnabled", S(d.AntivirusEnabled))),
            _ => Unknown("defender.realtime", "env.defender.unknown", ("antivirusEnabled", S(d.AntivirusEnabled)))
        };
    }
    private SelfCheckResult DefenderCfa(DefenderSnapshot? snapshot)
    {
        var value = snapshot?.EnableControlledFolderAccess;
        return value switch
        {
            0 => Result("defender.cfa", SelfCheckStatus.Ok, "env.defender.cfa_off", ("mode", "0")),
            1 or 2 => Result("defender.cfa", SelfCheckStatus.Info, "env.defender.cfa_on", ("mode", value.Value.ToString())),
            _ => Unknown("defender.cfa", "env.defender.cfa_unknown")
        };
    }
    private SelfCheckResult DefenderTamper(DefenderSnapshot? snapshot)
    {
        var value = snapshot?.IsTamperProtected;
        return value switch
        {
            true => Result("defender.tamper", SelfCheckStatus.Info, "env.defender.tamper_on"),
            false => Result("defender.tamper", SelfCheckStatus.Info, "env.defender.tamper_off"),
            _ => Unknown("defender.tamper", "env.defender.tamper_unknown")
        };
    }
    private SelfCheckResult SteamInstalled(SelfCheckContext c, IReadOnlyList<(string InstallDir, string ManifestPath)>? installs)
    {
        if (!IsSteam(c)) return Result("steam.installed", SelfCheckStatus.NotApplicable, "env.steam.not_applicable");
        if (installs is null) return Unknown("steam.installed", "env.steam.unknown");
        if (installs.Count == 1) return Result("steam.installed", SelfCheckStatus.Ok, "env.steam.installed", ("path", installs[0].InstallDir));
        if (installs.Count > 1) return Result("steam.installed", SelfCheckStatus.Info, "env.steam.multiple", ("count", installs.Count.ToString()));
        if (GameLocator.IsValidAmongUsInstallDirectory(c.VanillaInstallPath) || File.Exists(SafeExe(c.VanillaInstallPath)))
            return Result("steam.installed", SelfCheckStatus.Info, "env.steam.files_only", ("path", c.VanillaInstallPath!));
        return Result("steam.installed", SelfCheckStatus.Warning, "env.steam.not_found");
    }
    private SelfCheckResult SteamBeta(SelfCheckContext c, IReadOnlyList<(string InstallDir, string ManifestPath)>? installs)
    {
        if (!IsSteam(c)) return Result("steam.beta", SelfCheckStatus.NotApplicable, "env.steam.beta_not_applicable");
        if (installs is null) return Unknown("steam.beta", "env.steam.beta_unknown");
        if (installs.Count != 1) return Unknown("steam.beta", "env.steam.beta_unknown", ("reason", installs.Count == 0 ? "no_manifest" : "ambiguous"));
        var branch = SteamBranchEvaluator.Evaluate(SteamAppManifest.TryRead(installs[0].ManifestPath));
        return branch.Status switch
        {
            SteamBranchStatus.Default => Result("steam.beta", SelfCheckStatus.Ok, "env.steam.beta_default"),
            SteamBranchStatus.NonDefault => Result("steam.beta", SelfCheckStatus.Warning, "env.steam.beta_nondefault", ("branch", branch.BranchName ?? "")),
            SteamBranchStatus.Inconsistent => Result("steam.beta", SelfCheckStatus.Warning, "env.steam.beta_inconsistent", ("branch", branch.BranchName ?? "")),
            _ => Unknown("steam.beta", "env.steam.beta_unknown")
        };
    }
    private static bool IsSteam(SelfCheckContext c) => string.Equals(c.Mode, "steam", StringComparison.OrdinalIgnoreCase);
    private static string? SafeExe(string? path) { try { return string.IsNullOrWhiteSpace(path) ? null : Path.Combine(path, "Among Us.exe"); } catch { return null; } }
    private static string S(bool? value) => value?.ToString().ToLowerInvariant() ?? "null";
    private static SelfCheckResult Unknown(string id, string code, params (string Key, string Value)[] data) => Result(id, SelfCheckStatus.Unknown, code, data);
    private static SelfCheckResult Result(string id, SelfCheckStatus status, string code, params (string Key, string Value)[] data) =>
        new(id, status, code, data.ToDictionary(x => x.Key, x => x.Value));

    [SupportedOSPlatform("windows")]
    private static int? ProbeSac()
    {
        try { if (!OperatingSystem.IsWindows()) return null; using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"); var value = key?.GetValue("VerifiedAndReputablePolicyState"); return value is null ? null : Convert.ToInt32(value); }
        catch { return null; }
    }
    [SupportedOSPlatform("windows")]
    private static FirewallProfilesSnapshot? ProbeFirewall()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return null;
            dynamic policy = Activator.CreateInstance(type)!;
            try
            {
                int profiles = policy.CurrentProfileTypes;
                return new(profiles, policy.FirewallEnabled(1), policy.FirewallEnabled(2), policy.FirewallEnabled(4));
            }
            finally { if (System.Runtime.InteropServices.Marshal.IsComObject(policy)) System.Runtime.InteropServices.Marshal.ReleaseComObject(policy); }
        }
        catch { return null; }
    }
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string>? ProbeThirdPartyFirewall()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var searcher = new ManagementObjectSearcher(@"\\.\root\SecurityCenter2", "SELECT displayName FROM FirewallProduct");
            using var items = searcher.Get();
            var names = new List<string>();
            foreach (ManagementObject item in items)
            {
                using (item)
                    if (item["displayName"]?.ToString() is { } name && !string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            return names;
        }
        catch { return null; }
    }
    [SupportedOSPlatform("windows")]
    private static DefenderSnapshot? ProbeDefender()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            bool? av = null, realtime = null, tamper = null;
            int? cfa = null;
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, IsTamperProtected FROM MSFT_MpComputerStatus");
                using var rows = searcher.Get();
                using var row = rows.Cast<ManagementObject>().FirstOrDefault();
                if (row is not null) { av = VBool(row["AntivirusEnabled"]); realtime = VBool(row["RealTimeProtectionEnabled"]); tamper = VBool(row["IsTamperProtected"]); }
            } catch { }
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT EnableControlledFolderAccess FROM MSFT_MpPreference");
                using var rows = searcher.Get();
                using var row = rows.Cast<ManagementObject>().FirstOrDefault();
                cfa = row?["EnableControlledFolderAccess"] is { } value ? Convert.ToInt32(value) : null;
            } catch { }
            return av is null && realtime is null && tamper is null && cfa is null ? null : new(av, realtime, tamper, cfa);
        }
        catch { return null; }
    }
    private static bool? VBool(object? value) => value is null ? null : Convert.ToBoolean(value);
}
