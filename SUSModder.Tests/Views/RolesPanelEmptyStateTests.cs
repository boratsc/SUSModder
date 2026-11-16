using System.Xml.Linq;
using Xunit;

namespace SUSModder.Tests.Views;

/// <summary>
/// E16 (W1): stan pusty poziomu A musi dawać tę samą akcję co stan błędu (ponów),
/// bo przy online API pusta lista może oznaczać błąd pobierania (RolesService nietknięty).
/// Test strukturalny AXAML — wzorzec: PaletteRefreshTests.
/// </summary>
public class RolesPanelEmptyStateTests
{
    [Fact]
    public void EmptyModsStateHasRetryButtonWithAccessibleName()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "RolesPanel.axaml"));
        var emptyState = doc.Descendants()
            .Single(e => (string?)e.Attribute("IsVisible") == "{Binding ShowRoleModsEmptyState}");
        var retry = emptyState.Descendants().Single(e => e.Name.LocalName == "Button");
        Assert.Equal("{Binding RetryRolesLoadCommand}", (string?)retry.Attribute("Command"));
        Assert.Equal("{loc:Localize UI.Roles.Retry}", (string?)retry.Attribute("AutomationProperties.Name"));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "RolesPanel.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
