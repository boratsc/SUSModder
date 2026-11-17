using System.Text.RegularExpressions;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Regresja: ustawienia są panelem treści, nie tool-modalem. MainWindowViewModel nie jest
/// konstruowalny w testach (ciężkie DI/Avalonia), więc pilnujemy reguł na źródle partiali —
/// wzorzec testów strukturalnych repo (RolesPanelEmptyStateTests).
/// </summary>
public class SettingsPanelLayoutGuardTests
{
    [Fact]
    public void ToolModalAggregations_DoNotIncludeSettings()
    {
        var source = Read("MainWindowViewModel.FrontendLayout.cs");

        foreach (var member in new[] { "IsAnyToolModalOpen", "IsAnyNonAiSupportToolModalOpen" })
        {
            var body = Regex.Match(source, member + @"(?:\(\))?\s*=>\s*([^;]+);");
            Assert.True(body.Success, $"Expression body not found: {member}");
            Assert.DoesNotContain("IsAppSettingsVisible", body.Groups[1].Value);
        }
    }

    [Fact]
    public void ToolModalTitle_HasNoSettingsBranch()
    {
        var source = Read("MainWindowViewModel.FrontendLayout.cs");
        Assert.DoesNotContain("if (IsAppSettingsVisible)", source);
    }

    [Fact]
    public void OpeningToolModal_ClosesSettingsPanel()
    {
        // Centralna reguła w NotifyToolModalStateChanged: modal narzędziowy ma pierwszeństwo.
        var source = Read("MainWindowViewModel.FrontendLayout.cs");
        Assert.Matches(@"_isAppSettingsVisible\s*&&\s*IsAnyToolModalOpen", source);
    }

    [Fact]
    public void ClosingToolModal_AlsoClosesSettingsPanel()
    {
        var source = Read("MainWindowViewModel.FrontendLayout.cs");
        var core = source[(source.IndexOf("private void CloseToolModalCore()", StringComparison.Ordinal))..];
        core = core[..core.IndexOf("\n        }", StringComparison.Ordinal)];
        Assert.Contains("IsAppSettingsVisible = false;", core);
    }

    [Fact]
    public void ShowAppSettings_ClosesToolModalsBeforeOpeningPanel()
    {
        var source = Read("MainWindowViewModel.AppSettings.cs");
        var close = source.IndexOf("CloseToolModalCore();", StringComparison.Ordinal);
        var open = source.IndexOf("IsAppSettingsVisible = true;", StringComparison.Ordinal);
        Assert.True(close >= 0, "ShowAppSettings must close tool modals");
        Assert.True(open > close, "Settings panel opens only after modals are closed");
    }

    [Fact]
    public void BrowserDetailPanel_IsHiddenWhileSettingsOpen()
    {
        var source = Read("MainWindowViewModel.cs");
        var body = Regex.Match(source, @"IsBrowserDetailPanelVisible\s*=>\s*([^;]+);");
        Assert.True(body.Success);
        Assert.Contains("!IsAppSettingsVisible", body.Groups[1].Value);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SUSModder", "ViewModels", fileName));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "ViewModels", "MainWindowViewModel.cs"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
