using SUSModder.Services.Localization;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Przycisk „Narzędzie firewalla" w panelu diagnostyki launcha (reaktywnie — przy problemie
/// z uruchomieniem). MainWindowViewModel nie jest konstruowalny w testach (ciężkie DI/Avalonia,
/// brak headless) — wiring pilnowany na źródle, wzorzec: ChatAnchoringGuardTests.
/// </summary>
public sealed class LaunchDiagnosticsFirewallToolTests
{
    [Fact]
    public void ButtonLabel_IsLocalizedInPlAndEn()
    {
        var loc = new LocalizationService();
        loc.ChangeCulture("pl");
        Assert.Equal("Narzędzie firewalla", loc.Get("LaunchDiagnostics.Actions.OpenFirewallTool"));
        loc.ChangeCulture("en");
        Assert.Equal("Firewall tool", loc.Get("LaunchDiagnostics.Actions.OpenFirewallTool"));
    }

    [Fact]
    public void PanelButton_BindsLocalizedLabel_AndDispatchesFirewallRepair()
    {
        var axaml = ReadView("MainWindow.axaml");
        Assert.Contains("Content=\"{Binding LaunchDiagBtnFirewallTool}\" Click=\"OnOpenFirewallToolClicked\"", axaml);

        var codeBehind = ReadView("MainWindow.axaml.cs");
        Assert.Contains("await ViewModel.OpenFirewallToolAsync();", codeBehind);

        var vm = ReadViewModel("MainWindowViewModel.LaunchDiagnostics.cs");
        Assert.Contains("LaunchDiagBtnFirewallTool = _localizationService.Get(\"LaunchDiagnostics.Actions.OpenFirewallTool\");", vm);
        // Ten sam flow co panel napraw — bez duplikowania logiki narzędzia.
        Assert.Contains("public Task OpenFirewallToolAsync() => ExecuteRepairOptionAsync(RepairOption.Firewall);", vm);
    }

    [Fact]
    public void EnvironmentCheckReport_NoLongerOffersFirewallToolProactively()
    {
        Assert.DoesNotContain("FirewallToolRequested", ReadView("EnvironmentCheckReportDialog.axaml.cs"));
        Assert.DoesNotContain("FirewallToolRequested", ReadViewModel("MainWindowViewModel.EnvironmentCheck.cs"));
        Assert.DoesNotContain("OpenFirewallTool", ReadViewModel("Helpers/EnvironmentCheckPresentation.cs"));
    }

    private static string ReadViewModel(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "ViewModels", fileName));

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "Views", fileName));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "MainWindow.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
