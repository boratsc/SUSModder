using System.Text.RegularExpressions;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Services.Localization;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Faza 3a: prezentacja raportu środowiska — komplet kluczy PL/EN dla WSZYSTKICH kodów Core
/// (klucze dynamiczne, których nie skanuje LocalizationKeyUsageTests), formatowanie Data,
/// akcje per kod, ukrywanie „Nie dotyczy" i kolejność problems-first.
/// </summary>
public class EnvironmentCheckPresentationTests
{
    /// <summary>Pełny kontrakt kodów Core (10 checków, 39 kodów) — przypięty na stałe.</summary>
    private static readonly string[] AllCodes =
    {
        "env.sac.not_applicable", "env.sac.off", "env.sac.enforce", "env.sac.evaluation", "env.sac.unknown",
        "env.firewall.unknown", "env.firewall.disabled", "env.firewall.enabled",
        "env.firewall.thirdparty_unknown", "env.firewall.thirdparty_none", "env.firewall.thirdparty_detected",
        "env.av.unknown", "env.av.detected", "env.av.none",
        "env.defender.unknown", "env.defender.realtime_on", "env.defender.realtime_off",
        "env.defender.cfa_unknown", "env.defender.cfa_off", "env.defender.cfa_on",
        "env.defender.tamper_unknown", "env.defender.tamper_on", "env.defender.tamper_off",
        "env.steam.not_applicable", "env.steam.unknown", "env.steam.installed", "env.steam.multiple",
        "env.steam.files_only", "env.steam.not_found",
        "env.steam.beta_not_applicable", "env.steam.beta_default", "env.steam.beta_nondefault",
        "env.steam.beta_inconsistent", "env.steam.beta_unknown",
        "env.firewall.rules_not_applicable", "env.firewall.rules_missing", "env.firewall.rules_drift",
        "env.firewall.rules_ok", "env.firewall.rules_unknown"
    };

    private static readonly string[] AllCheckIds =
    {
        "sac", "firewall.profiles", "firewall.thirdparty", "av.thirdparty", "defender.realtime",
        "defender.cfa", "defender.tamper", "steam.installed", "steam.beta", "firewall.rules"
    };

    private static readonly string[] StaticKeys =
    {
        "UI.EnvironmentCheck.Title", "UI.EnvironmentCheck.ReadOnlyNote", "UI.EnvironmentCheck.Running",
        "UI.EnvironmentCheck.Recheck", "UI.EnvironmentCheck.Timestamp", "UI.EnvironmentCheck.Error",
        "UI.EnvironmentCheck.SummaryOk", "UI.EnvironmentCheck.SummaryProblems", "UI.EnvironmentCheck.ShowRest",
        "UI.EnvironmentCheck.Proposal.Title", "UI.EnvironmentCheck.Proposal.Message",
        "UI.EnvironmentCheck.Proposal.Check", "UI.EnvironmentCheck.Proposal.Skip",
        "UI.EnvironmentCheck.Action.OpenWindowsSecurity", "UI.EnvironmentCheck.Action.OpenSteam",
        "UI.EnvironmentCheck.Action.OpenFirewallTool", "UI.EnvironmentCheck.Action.SacInstruction",
        "UI.EnvironmentCheck.Action.SteamBetaInstruction", "UI.EnvironmentCheck.Action.AvInstruction",
        "UI.EnvironmentCheck.Action.CfaInstruction",
        "UI.EnvironmentCheck.Detail.Code", "UI.EnvironmentCheck.Detail.Path",
        "UI.EnvironmentCheck.Detail.RulePath", "UI.EnvironmentCheck.Detail.TargetPath",
        "UI.EnvironmentCheck.Detail.Branch",
        "UI.Repair.EnvironmentCheck.Title", "UI.Repair.EnvironmentCheck.Description",
        "UI.Repair.SteamLoginCheck.BetaBranchMismatchTitle", "UI.Repair.SteamLoginCheck.BetaBranchMismatch",
        "UI.Repair.SteamLoginCheck.BetaToastHint",
        "UI.Repair.SteamLoginCheck.BranchUnknownTitle", "UI.Repair.SteamLoginCheck.BranchUnknown",
        "UI.Repair.SteamLoginCheck.BranchUnknownOverrideConfirm"
    };

    private static SelfCheckResult R(string id, SelfCheckStatus status, string code, params (string Key, string Value)[] data) =>
        new(id, status, code, data.ToDictionary(x => x.Key, x => x.Value));

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{([A-Za-z0-9_]+)(?:[},:])")
            .Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    [Fact]
    public void AllDynamicAndStaticKeys_ExistInPlAndEn_WithMatchingPlaceholders()
    {
        var loc = new LocalizationService();
        var keys = AllCodes.Select(EnvironmentCheckPresentation.DescriptionKey)
            .Concat(AllCheckIds.Select(EnvironmentCheckPresentation.TitleKey))
            .Concat(Enum.GetValues<SelfCheckStatus>().Select(EnvironmentCheckPresentation.StatusKey))
            .Concat(StaticKeys)
            .ToArray();

        foreach (var key in keys)
        {
            loc.ChangeCulture("pl");
            var pl = loc.Get(key);
            loc.ChangeCulture("en");
            var en = loc.Get(key);

            Assert.NotEqual($"[{key}]", pl);
            Assert.NotEqual($"[{key}]", en);
            Assert.False(string.IsNullOrWhiteSpace(pl), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.Equal(Placeholders(pl), Placeholders(en));
        }
    }

    [Fact]
    public void BuildRows_HidesNotApplicable_AndPutsProblemsFirst()
    {
        var loc = new LocalizationService();
        var report = new SelfCheckReport(DateTimeOffset.UtcNow, new[]
        {
            R("sac", SelfCheckStatus.NotApplicable, "env.sac.not_applicable"),
            R("firewall.profiles", SelfCheckStatus.Info, "env.firewall.enabled"),
            R("steam.beta", SelfCheckStatus.Warning, "env.steam.beta_nondefault", ("branch", "beta")),
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none"),
            R("defender.cfa", SelfCheckStatus.Unknown, "env.defender.cfa_unknown")
        });

        var rows = EnvironmentCheckPresentation.BuildRows(report, loc, @"C:\Mody");

        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "steam.beta", "defender.cfa", "firewall.profiles", "av.thirdparty" },
            rows.Select(r => r.CheckId));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.StatusLabel)));
    }

    [Theory]
    [InlineData("env.sac.enforce")]
    [InlineData("env.sac.evaluation")]
    public void SacEnforceOrEvaluation_OpensWindowsSecurity_WithInstruction(string code)
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("sac", SelfCheckStatus.Warning, code), new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.OpenWindowsSecurity, row.Action);
        Assert.True(row.HasAction);
        Assert.True(row.HasInstruction);
        Assert.False(string.IsNullOrWhiteSpace(row.ActionLabel));
    }

    [Fact]
    public void SacOff_HasNoAction()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("sac", SelfCheckStatus.Info, "env.sac.off"), new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.None, row.Action);
        Assert.False(row.HasAction);
        Assert.False(row.HasInstruction);
    }

    [Theory]
    [InlineData("env.steam.beta_nondefault")]
    [InlineData("env.steam.beta_inconsistent")]
    public void BetaNonDefaultOrInconsistent_OpensSteam_WithAlwaysVisibleInstruction(string code)
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("steam.beta", SelfCheckStatus.Warning, code, ("branch", "publicbeta")),
            new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.OpenSteam, row.Action);
        Assert.True(row.HasInstruction);
        // Nazwa wersji technicznej jest w szczegółach, nie w głównym opisie karty.
        Assert.DoesNotContain("publicbeta", row.Description);
        Assert.True(row.HasDetails);
        Assert.Contains("publicbeta", row.Details);
    }

    [Fact]
    public void BetaDefault_HasNoAction()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("steam.beta", SelfCheckStatus.Ok, "env.steam.beta_default"), new LocalizationService(), @"C:\Mody");

        Assert.False(row.HasAction);
        Assert.False(row.HasInstruction);
    }

    [Theory]
    [InlineData("env.firewall.rules_missing")]
    [InlineData("env.firewall.rules_drift")]
    public void RulesMissingOrDrift_OpensFirewallTool(string code)
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("firewall.rules", SelfCheckStatus.Warning, code,
                ("rulePath", @"C:\Stara\Among Us.exe"), ("targetPath", @"C:\Nowa\Among Us.exe")),
            new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.OpenFirewallTool, row.Action);
        Assert.True(row.HasAction);
    }

    [Fact]
    public void RulesDrift_PathsOnlyInDetails_NotInMainDescription()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("firewall.rules", SelfCheckStatus.Warning, "env.firewall.rules_drift",
                ("rulePath", @"C:\Stara\Among Us.exe"), ("targetPath", @"C:\Nowa\Among Us.exe")),
            new LocalizationService(), @"C:\Mody");

        Assert.DoesNotContain(@"C:\Stara\Among Us.exe", row.Description);
        Assert.DoesNotContain(@"C:\Nowa\Among Us.exe", row.Description);
        Assert.True(row.HasDetails);
        Assert.Contains(@"C:\Stara\Among Us.exe", row.Details);
        Assert.Contains(@"C:\Nowa\Among Us.exe", row.Details);
        Assert.Contains("env.firewall.rules_drift", row.Details);
    }

    [Fact]
    public void AvDetected_InstructionOnly_WithJoinedNamesAndModsPath()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("av.thirdparty", SelfCheckStatus.Info, "env.av.detected", ("names", "Avast|AVG")),
            new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.None, row.Action);
        Assert.False(row.HasAction);
        Assert.True(row.HasInstruction);
        Assert.Contains("Avast, AVG", row.Description);
        Assert.Contains(@"C:\Mody", row.Instruction);
    }

    [Fact]
    public void AvNone_HasNoInstruction()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none"), new LocalizationService(), @"C:\Mody");

        Assert.False(row.HasInstruction);
        Assert.False(row.HasAction);
    }

    [Fact]
    public void SteamInstalled_PathInDetails_Multiple_CountInDescription()
    {
        var loc = new LocalizationService();
        var installed = EnvironmentCheckPresentation.BuildRow(
            R("steam.installed", SelfCheckStatus.Ok, "env.steam.installed", ("path", @"C:\Steam\Among Us")),
            loc, @"C:\Mody");
        var multiple = EnvironmentCheckPresentation.BuildRow(
            R("steam.installed", SelfCheckStatus.Info, "env.steam.multiple", ("count", "3")),
            loc, @"C:\Mody");

        Assert.DoesNotContain(@"C:\Steam\Among Us", installed.Description);
        Assert.True(installed.HasDetails);
        Assert.Contains(@"C:\Steam\Among Us", installed.Details);
        Assert.Contains("3", multiple.Description);
        Assert.False(multiple.HasDetails);
    }

    [Fact]
    public void MissingDataKeys_FallBackToNeutralDash()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("steam.installed", SelfCheckStatus.Ok, "env.steam.installed"),
            new LocalizationService(), @"C:\Mody");

        Assert.Contains("-", row.Details);
        Assert.DoesNotContain("{0}", row.Description);
        Assert.DoesNotContain("{0}", row.Details);
    }

    [Fact]
    public void Summary_CountsProblemsAndActionableRows_NotUnknownOrPlainInfo()
    {
        var loc = new LocalizationService();
        var ok = new SelfCheckReport(DateTimeOffset.UtcNow, new[]
        {
            R("sac", SelfCheckStatus.NotApplicable, "env.sac.not_applicable"),
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none"),
            R("defender.cfa", SelfCheckStatus.Unknown, "env.defender.cfa_unknown")
        });
        var problems = new SelfCheckReport(DateTimeOffset.UtcNow, new[]
        {
            R("sac", SelfCheckStatus.Warning, "env.sac.enforce"),
            R("steam.beta", SelfCheckStatus.Warning, "env.steam.beta_nondefault"),
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none")
        });
        // Pozycja Info z akcją (brak zezwolenia zapory) też wymaga działania — liczy się do podsumowania.
        var actionableInfo = new SelfCheckReport(DateTimeOffset.UtcNow, new[]
        {
            R("firewall.rules", SelfCheckStatus.Info, "env.firewall.rules_missing"),
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none")
        });

        Assert.Equal(loc.Get("UI.EnvironmentCheck.SummaryOk"),
            EnvironmentCheckPresentation.BuildSummary(EnvironmentCheckPresentation.BuildRows(ok, loc, @"C:\Mody"), loc));
        Assert.Contains("2", EnvironmentCheckPresentation.BuildSummary(
            EnvironmentCheckPresentation.BuildRows(problems, loc, @"C:\Mody"), loc));
        Assert.Contains("1", EnvironmentCheckPresentation.BuildSummary(
            EnvironmentCheckPresentation.BuildRows(actionableInfo, loc, @"C:\Mody"), loc));
    }

    [Fact]
    public void NeedsAttention_ProblemsUnknownAndActionable_Visible_PlainInfoOkCollapsed()
    {
        var loc = new LocalizationService();
        var report = new SelfCheckReport(DateTimeOffset.UtcNow, new[]
        {
            R("sac", SelfCheckStatus.Warning, "env.sac.enforce"),
            R("defender.cfa", SelfCheckStatus.Unknown, "env.defender.cfa_unknown"),
            R("firewall.rules", SelfCheckStatus.Info, "env.firewall.rules_missing"),
            R("av.thirdparty", SelfCheckStatus.Info, "env.av.detected", ("names", "Avast")),
            R("firewall.profiles", SelfCheckStatus.Info, "env.firewall.enabled"),
            R("steam.installed", SelfCheckStatus.Ok, "env.steam.installed", ("path", @"C:\Steam\Among Us"))
        });

        var rows = EnvironmentCheckPresentation.BuildRows(report, loc, @"C:\Mody");
        var visible = rows.Where(EnvironmentCheckPresentation.NeedsAttention).Select(r => r.CheckId).ToArray();
        var collapsed = rows.Where(r => !EnvironmentCheckPresentation.NeedsAttention(r)).Select(r => r.CheckId).ToArray();

        Assert.Equal(new[] { "sac", "defender.cfa", "firewall.rules", "av.thirdparty" }, visible);
        Assert.Equal(new[] { "firewall.profiles", "steam.installed" }, collapsed);
    }

    [Fact]
    public void CfaOn_OpensWindowsSecurity_WithInstruction()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("defender.cfa", SelfCheckStatus.Info, "env.defender.cfa_on"),
            new LocalizationService(), @"C:\Mody");

        Assert.Equal(EnvironmentCheckAction.OpenWindowsSecurity, row.Action);
        Assert.True(row.HasInstruction);
        Assert.True(EnvironmentCheckPresentation.NeedsAttention(row));
    }

    [Fact]
    public void SacInstruction_Neutral_NoIrreversibilityScare_NoOffRecommendation()
    {
        var loc = new LocalizationService();
        loc.ChangeCulture("pl");
        var pl = loc.Get("UI.EnvironmentCheck.Action.SacInstruction");
        var plEvaluation = loc.Get("UI.EnvironmentCheck.Code.env.sac.evaluation");
        loc.ChangeCulture("en");
        var en = loc.Get("UI.EnvironmentCheck.Action.SacInstruction");
        var enEvaluation = loc.Get("UI.EnvironmentCheck.Code.env.sac.evaluation");

        // Korekta merytoryczna: nowsze Windows pozwalają ponownie włączyć SAC — bez straszenia reinstalacją.
        Assert.DoesNotContain("nie można włączyć", pl);
        Assert.DoesNotContain("cannot be turned back on", en);
        Assert.DoesNotContain("permanent", en);
        Assert.DoesNotContain("na stałe", pl);
        // Nie rekomendujemy wyłączenia; decyzja należy do użytkownika.
        Assert.DoesNotContain("Wyłącz", pl);
        Assert.DoesNotContain("Warto go wyłączyć", plEvaluation);
        Assert.DoesNotContain("Consider turning it off", enEvaluation);
        Assert.Contains("Decyzja", pl);
        Assert.Contains("decision", en);
        // Instrukcja 1-2-3 zostaje.
        Assert.Contains("1.", pl);
        Assert.Contains("2.", pl);
        Assert.Contains("3.", pl);
    }

    [Fact]
    public void PlainOkRow_HasNoDetails()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none"),
            new LocalizationService(), @"C:\Mody");

        Assert.False(row.HasDetails);
        Assert.Null(row.Details);
    }

    [Fact]
    public void AutomationName_ContainsTitleStatusAndDescription()
    {
        var row = EnvironmentCheckPresentation.BuildRow(
            R("av.thirdparty", SelfCheckStatus.Ok, "env.av.none"), new LocalizationService(), @"C:\Mody");

        Assert.Contains(row.Title, row.AutomationName);
        Assert.Contains(row.StatusLabel, row.AutomationName);
        Assert.Contains(row.Description, row.AutomationName);
    }
}
