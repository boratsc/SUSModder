using System.Globalization;
using System.Text;
using SUSModder.Core.Services;
using SUSModder.ViewModels.Helpers;
using SUSModder.Views;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Faza 3b: czysta logika narzędzia firewalla — ownership pre-check, lista kandydatów
/// (vanilla steam/epic, predykcja epic, gate steam) i mapowanie wyników. Bez netsh i UI.
/// </summary>
public class FirewallToolPlanTests
{
    private const string Target = @"C:\Gry\Among Us\Among Us.exe";
    private const string Foreign = @"C:\Inne\Among Us.exe";

    private static string ShowOutput(string programPath) =>
        $"Rule Name: Among Us - Test (Inbound)\r\nEnabled: Yes\r\nProgram: {programPath}\r\n";

    // --- Decide: ownership pre-check ---

    [Fact]
    public void Decide_BothNull_Add() =>
        Assert.Equal(FirewallToolDecision.Add, FirewallToolPlan.Decide(null, null, Target));

    [Fact]
    public void Decide_InboundOwn_OutboundNull_Replace() =>
        Assert.Equal(FirewallToolDecision.Replace, FirewallToolPlan.Decide(ShowOutput(Target), null, Target));

    [Fact]
    public void Decide_InboundNull_OutboundOwn_Replace() =>
        Assert.Equal(FirewallToolDecision.Replace, FirewallToolPlan.Decide(null, ShowOutput(Target), Target));

    [Fact]
    public void Decide_BothOwn_Replace() =>
        Assert.Equal(FirewallToolDecision.Replace, FirewallToolPlan.Decide(ShowOutput(Target), ShowOutput(Target), Target));

    [Fact]
    public void Decide_InboundForeign_Conflict() =>
        Assert.Equal(FirewallToolDecision.Conflict, FirewallToolPlan.Decide(ShowOutput(Foreign), null, Target));

    [Fact]
    public void Decide_OutboundForeign_Conflict() =>
        Assert.Equal(FirewallToolDecision.Conflict, FirewallToolPlan.Decide(null, ShowOutput(Foreign), Target));

    [Fact]
    public void Decide_OwnAndForeign_Conflict() =>
        Assert.Equal(FirewallToolDecision.Conflict, FirewallToolPlan.Decide(ShowOutput(Target), ShowOutput(Foreign), Target));

    [Fact]
    public void Decide_PathComparisonIsCaseInsensitive_Replace() =>
        Assert.Equal(FirewallToolDecision.Replace,
            FirewallToolPlan.Decide(ShowOutput(Target.ToUpperInvariant()), null, Target));

    // --- BuildCandidates ---

    private static Func<string, bool> Exists(params string[] paths) =>
        path => paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    private static List<FirewallModItem> Build(
        string mode,
        string? steamVanillaDir,
        Func<string, bool> fileExists,
        params (string Name, string? InstallPath)[] mods) =>
        FirewallToolPlan.BuildCandidates(mode, steamVanillaDir, @"C:\Mody", mods, "Among Us (podstawowa gra)", fileExists);

    [Fact]
    public void Steam_VanillaExeExists_IncludedFirst_NotPredicted()
    {
        var vanillaDir = @"C:\Steam\steamapps\common\Among Us";
        var items = Build("steam", vanillaDir, Exists(Path.Combine(vanillaDir, "Among Us.exe")));

        var vanilla = Assert.Single(items);
        Assert.True(vanilla.IsVanilla);
        Assert.False(vanilla.IsPredicted);
        Assert.Equal("Among Us (podstawowa gra)", vanilla.Name);
        Assert.Equal(Path.Combine(vanillaDir, "Among Us.exe"), vanilla.ExePath);
    }

    [Fact]
    public void Steam_VanillaExeMissing_Excluded()
    {
        var items = Build("steam", @"C:\Steam\steamapps\common\Among Us", Exists());
        Assert.Empty(items);
    }

    [Fact]
    public void Steam_NoVanillaDirectory_NoVanillaEntry()
    {
        var items = Build("steam", null, Exists());
        Assert.Empty(items);
    }

    [Fact]
    public void Epic_VanillaExeMissing_IncludedAsPrediction()
    {
        var items = Build("epic", null, Exists());

        var vanilla = Assert.Single(items);
        Assert.True(vanilla.IsVanilla);
        Assert.True(vanilla.IsPredicted);
        Assert.Equal(@"C:\Mody\Among Us - Vanilla\AmongUs\Among Us.exe", vanilla.ExePath);
    }

    [Fact]
    public void Epic_VanillaExeExists_NotPredicted()
    {
        var items = Build("epic", null, Exists(@"C:\Mody\Among Us - Vanilla\AmongUs\Among Us.exe"));

        var vanilla = Assert.Single(items);
        Assert.False(vanilla.IsPredicted);
    }

    [Fact]
    public void Steam_ModExeExists_Included()
    {
        var items = Build("steam", null, Exists(@"C:\Mody\Mod\Among Us.exe"), ("Mod", @"C:\Mody\Mod"));

        var mod = Assert.Single(items);
        Assert.False(mod.IsVanilla);
        Assert.False(mod.IsPredicted);
        Assert.Equal(@"C:\Mody\Mod\Among Us.exe", mod.ExePath);
    }

    [Fact]
    public void Steam_ModExeMissing_Excluded()
    {
        var items = Build("steam", null, Exists(), ("Mod", @"C:\Mody\Mod"));
        Assert.Empty(items);
    }

    [Fact]
    public void Epic_ModExeMissing_IncludedAsPrediction()
    {
        var items = Build("epic", null, Exists(), ("Mod", @"C:\Mody\Mod"));

        var mod = Assert.Single(items, i => !i.IsVanilla);
        Assert.True(mod.IsPredicted);
        Assert.Equal(@"C:\Mody\Mod\AmongUs\Among Us.exe", mod.ExePath);
    }

    [Fact]
    public void Epic_ModInstallPathEndingWithAmongUs_NormalizedToBase()
    {
        var items = Build("epic", null, Exists(), ("Mod", @"C:\Mody\Mod\AmongUs"));

        var mod = Assert.Single(items, i => !i.IsVanilla);
        Assert.Equal(@"C:\Mody\Mod\AmongUs\Among Us.exe", mod.ExePath);
    }

    [Fact]
    public void EmptyInstallPath_Excluded()
    {
        var items = Build("epic", null, Exists(), ("Mod", ""), ("Mod2", null));
        // Zostaje tylko predykcja vanilla — żaden mod z pustą ścieżką nie wchodzi.
        var vanilla = Assert.Single(items);
        Assert.True(vanilla.IsVanilla);
    }

    [Fact]
    public void VanillaAndMods_VanillaFirst()
    {
        var items = Build("epic", null, Exists(), ("Mod", @"C:\Mody\Mod"));

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsVanilla);
        Assert.False(items[1].IsVanilla);
    }

    // --- OutcomeMessageKey ---

    [Fact]
    public void Outcome_BothOk_Success() =>
        Assert.Equal("UI.Repair.Firewall.Success",
            FirewallToolPlan.OutcomeMessageKey(new FirewallRuleOutcome(true, true, false, "")));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Outcome_Partial_PartialSuccess(bool inbound, bool outbound) =>
        Assert.Equal("UI.Repair.Firewall.PartialSuccess",
            FirewallToolPlan.OutcomeMessageKey(new FirewallRuleOutcome(inbound, outbound, false, "")));

    [Fact]
    public void Outcome_Conflict_TakesPriority() =>
        Assert.Equal("UI.Repair.Firewall.NameConflict",
            FirewallToolPlan.OutcomeMessageKey(new FirewallRuleOutcome(true, true, true, "")));

    // --- ReadOutputFile ---

    [Fact]
    public void ReadOutputFile_MissingFile_Empty() =>
        Assert.Equal(string.Empty, FirewallToolPlan.ReadOutputFile(
            Path.Combine(Path.GetTempPath(), "susmodder-test-" + Guid.NewGuid().ToString("N") + ".out")));

    [Fact]
    public void ReadOutputFile_OemBytes_DecodedWithoutMojibake()
    {
        // cmd przekierowuje stdout bajtami OEM — polskie znaki w ścieżce muszą przetrwać odczyt.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        const string text = "Program: C:\\Gry\\Żółty Mod\\Among Us.exe";
        var path = Path.Combine(Path.GetTempPath(), "susmodder-test-" + Guid.NewGuid().ToString("N") + ".out");
        try
        {
            File.WriteAllText(path, text, oem);
            Assert.Equal(text, FirewallToolPlan.ReadOutputFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadOutputFile_Utf8NoBom_DecodedCorrectly()
    {
        // Acceptance 2026-11-16 (Win11 26300 PL): wyjście netsh to UTF-8 bez BOM.
        const string text = "Program: C:\\Gry\\Żółć\\Among Us.exe";
        var path = Path.Combine(Path.GetTempPath(), "susmodder-test-" + Guid.NewGuid().ToString("N") + ".out");
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Assert.Equal(text, FirewallToolPlan.ReadOutputFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadOutputFile_Oem852Bytes_FallbackDecodedCorrectly()
    {
        // Starsze systemy / locale netsh: bajty OEM (CP852) nie są poprawnym UTF-8 → fallback OEM.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string text = "Program: C:\\Gry\\Żółć\\Among Us.exe";
        var path = Path.Combine(Path.GetTempPath(), "susmodder-test-" + Guid.NewGuid().ToString("N") + ".out");
        try
        {
            File.WriteAllText(path, text, Encoding.GetEncoding(852));
            Assert.Equal(text, FirewallToolPlan.ReadOutputFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
