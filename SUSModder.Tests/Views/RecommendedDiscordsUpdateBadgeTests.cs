using System.Xml.Linq;
using Xunit;

namespace SUSModder.Tests.Views;

/// <summary>
/// F3g: badge „Dostępna aktualizacja" i lista zmian paczki w wierszu Discordów —
/// reuse stylu catalog-row-badge i istniejących kluczy (bez hardcoded hex/emoji).
/// Test strukturalny AXAML — wzorzec: RecommendedDiscordsRowActionTests / RolesPanelEmptyStateTests.
/// </summary>
public class RecommendedDiscordsUpdateBadgeTests
{
    [Fact]
    public void UpdateBadgeUsesCatalogRowBadgeClassAndLocalizedText()
    {
        var badge = BadgeBorder("{Binding ShowPackUpdateBadge}");
        Assert.Contains("catalog-row-badge", (string?)badge.Attribute("Classes") ?? string.Empty);
        // Klasa rządzi wyglądem — bez lokalnych setterów kolorów.
        Assert.Null(badge.Attribute("Background"));
        Assert.Null(badge.Attribute("BorderBrush"));

        var text = badge.Descendants().Single(e => e.Name.LocalName == "TextBlock");
        Assert.Contains("catalog-row-badge-text", (string?)text.Attribute("Classes") ?? string.Empty);
        Assert.Equal("{loc:Localize UI.ModStatus.UpdateAvailable}", (string?)text.Attribute("Text"));
    }

    [Fact]
    public void UpdateBadgeAndUnavailableBadgeAreMutuallyExclusive()
    {
        // Oba badge w tej samej kolumnie — VM gwarantuje wyłączność (ShowPackUpdateBadge zawiera !ShowPackUnavailable).
        var template = RowTemplate();
        var badges = template.Descendants()
            .Where(e => e.Name.LocalName == "Border"
                        && ((string?)e.Attribute("Classes") ?? string.Empty).Contains("catalog-row-badge"))
            .ToList();
        Assert.Equal(2, badges.Count);
        Assert.Contains(badges, b => (string?)b.Attribute("IsVisible") == "{Binding ShowPackUnavailable}");
        Assert.Contains(badges, b => (string?)b.Attribute("IsVisible") == "{Binding ShowPackUpdateBadge}");
    }

    [Fact]
    public void DetailsExpanderShowsPackChangesUnderLocalizedHeader()
    {
        var expander = RowTemplate().Descendants().Single(e => e.Name.LocalName == "Expander");
        var changes = expander.Descendants()
            .Single(e => (string?)e.Attribute("IsVisible") == "{Binding HasPackUpdateChanges}");

        var texts = changes.Descendants().Where(e => e.Name.LocalName == "TextBlock").ToList();
        Assert.Contains(texts, t => (string?)t.Attribute("Text") == "{loc:Localize ModPacks.UpdateChanges}");
        var body = texts.Single(t => (string?)t.Attribute("Text") == "{Binding PackUpdateChangesText}");
        Assert.Equal("Wrap", (string?)body.Attribute("TextWrapping"));
    }

    private static XElement BadgeBorder(string isVisible) =>
        RowTemplate().Descendants()
            .Single(e => e.Name.LocalName == "Border" && (string?)e.Attribute("IsVisible") == isVisible);

    private static XElement RowTemplate()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "RecommendedDiscordsPanel.axaml"));
        return doc.Descendants().Single(e => e.Name.LocalName == "DataTemplate");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "RecommendedDiscordsPanel.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}