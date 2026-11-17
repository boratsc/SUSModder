using System.Xml.Linq;
using Xunit;

namespace SUSModder.Tests.Views;

/// <summary>
/// F3f: akcje wiersza Discordów muszą wyglądać identycznie jak wiersze katalogu modów
/// (klasa catalog-row-action, bez lokalnych setterów kolizyjnych), a akcje zestawu
/// mają tooltip z nazwą serwera. Test strukturalny AXAML — wzorzec: RolesPanelEmptyStateTests.
/// </summary>
public class RecommendedDiscordsRowActionTests
{
    [Fact]
    public void RowActionButtonsUseCatalogRowActionClassWithoutLocalLookOverrides()
    {
        var buttons = RowButtons();
        Assert.Equal(3, buttons.Count); // Dołącz + Zainstaluj zestaw + Zaktualizuj zestaw
        Assert.All(buttons, b =>
        {
            Assert.Contains("catalog-row-action", (string?)b.Attribute("Classes") ?? string.Empty);
            // Klasa rządzi wyglądem — lokalne setery psułyby identyczność z katalogiem.
            Assert.Null(b.Attribute("Background"));
            Assert.Null(b.Attribute("Foreground"));
            Assert.Null(b.Attribute("Padding"));
            Assert.Null(b.Attribute("CornerRadius"));
            Assert.Null(b.Attribute("FontWeight"));
        });
    }

    [Fact]
    public void PackActionButtonsBindServerTooltipAndJoinHasNone()
    {
        var buttons = RowButtons();
        var pack = buttons
            .Where(b => (string?)b.Attribute("Command") == "{Binding #Root.DataContext.PackActionCommand}")
            .ToList();
        Assert.Equal(2, pack.Count);
        Assert.All(pack, b => Assert.Equal("{Binding PackActionTooltip}", (string?)b.Attribute("ToolTip.Tip")));

        var join = buttons.Single(b => (string?)b.Attribute("Command") == "{Binding #Root.DataContext.OpenDiscordLinkCommand}");
        Assert.Null(join.Attribute("ToolTip.Tip"));
    }

    private static List<XElement> RowButtons()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "RecommendedDiscordsPanel.axaml"));
        var template = doc.Descendants().Single(e => e.Name.LocalName == "DataTemplate");
        return template.Descendants().Where(e => e.Name.LocalName == "Button").ToList();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "RecommendedDiscordsPanel.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
