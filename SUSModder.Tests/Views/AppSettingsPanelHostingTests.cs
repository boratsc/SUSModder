using System.Xml.Linq;
using Xunit;

namespace SUSModder.Tests.Views;

/// <summary>
/// Ustawienia jako panel treści (nie modal): AppSettingsView jest hostowany w ContentGrid
/// po prawej stronie sidebara (kolumna listy + szczegółów), poza tool-modal-overlay.
/// Test strukturalny AXAML — wzorzec: RolesPanelEmptyStateTests / PaletteRefreshTests.
/// </summary>
public class AppSettingsPanelHostingTests
{
    private static readonly XNamespace XamlNs = "https://github.com/avaloniaui";
    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SettingsPanel_IsHostedInContentGrid_AcrossListAndDetailsColumns()
    {
        var doc = LoadMainWindow();
        var contentGrid = doc.Descendants(XamlNs + "Grid")
            .Single(e => (string?)e.Attribute(XNs + "Name") == "ContentGrid");

        var host = contentGrid.Descendants(XamlNs + "Grid")
            .Single(e => (string?)e.Attribute(XNs + "Name") == "AppSettingsHost");

        Assert.Equal("1", (string?)host.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)host.Attribute("Grid.ColumnSpan"));
        Assert.Equal("{Binding IsAppSettingsVisible}", (string?)host.Attribute("IsVisible"));
        Assert.Single(host.Descendants(), e => e.Name.LocalName == "AppSettingsView");
    }

    [Fact]
    public void SettingsPanel_IsNotInsideToolModalOverlay()
    {
        var doc = LoadMainWindow();
        var overlay = doc.Descendants(XamlNs + "Border")
            .Single(e => (string?)e.Attribute("Classes") == "tool-modal-overlay"
                      && (string?)e.Attribute("IsVisible") == "{Binding IsAnyToolModalOpen}");

        Assert.DoesNotContain(overlay.Descendants(), e => e.Name.LocalName == "AppSettingsView");
        // Dokładnie jedna instancja ustawień w całym oknie (bez dublowania modal/panel).
        Assert.Single(doc.Descendants(), e => e.Name.LocalName == "AppSettingsView");
    }

    [Fact]
    public void ListColumn_IsHiddenWhenSettingsOpen()
    {
        var doc = LoadMainWindow();
        var contentGrid = doc.Descendants(XamlNs + "Grid")
            .Single(e => (string?)e.Attribute(XNs + "Name") == "ContentGrid");

        var listGrid = contentGrid.Elements(XamlNs + "Grid")
            .Single(e => (string?)e.Attribute("Grid.Column") == "1"
                      && (string?)e.Attribute("RowDefinitions") == "Auto,*");

        Assert.Equal("{Binding IsAppSettingsVisible, Converter={x:Static BoolConverters.Not}}",
            (string?)listGrid.Attribute("IsVisible"));
    }

    [Fact]
    public void SidebarSettingsButton_KeepsActiveStateCommandAndAccessibleName()
    {
        var doc = LoadMainWindow();
        var button = doc.Descendants(XamlNs + "Button")
            .Single(e => (string?)e.Attribute(XNs + "Name") == "SettingsSidebarButton");

        Assert.Equal("{Binding IsAppSettingsVisible}", (string?)button.Attribute("Classes.active"));
        Assert.Equal("{Binding ShowAppSettingsCommand}", (string?)button.Attribute("Command"));
        Assert.Equal("{loc:Localize UI.Sidebar.Settings}", (string?)button.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void SidebarTabs_UseSettingsAwareActiveBindings()
    {
        var doc = LoadMainWindow();
        var tabButtons = doc.Descendants(XamlNs + "Button")
            .Where(e => (string?)e.Attribute("Command") == "{Binding SelectBrowserTabCommand}")
            .ToArray();

        Assert.Equal(5, tabButtons.Length);
        foreach (var button in tabButtons)
        {
            var parameter = (string?)button.Attribute("CommandParameter") ?? "";
            var tab = parameter.Split('.').Last().TrimEnd('}');
            Assert.Equal($"{{Binding Is{tab}TabActive}}", (string?)button.Attribute("Classes.active"));
        }
    }

    [Fact]
    public void SettingsView_IsFocusableAndHasAccessibleName()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "AppSettingsView.axaml"));
        var root = doc.Root!;

        Assert.Equal("True", (string?)root.Attribute("Focusable"));
        Assert.Equal("{local:Localize Settings.Main.Title}", (string?)root.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void SettingsView_HeaderAndWidthMatchContentPanelPattern()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "AppSettingsView.axaml"));

        // Nagłówek jak panele treści: 22 SemiBold + podtytuł 12 muted.
        var headerStyle = doc.Descendants(XamlNs + "Style")
            .Single(e => (string?)e.Attribute("Selector") == "TextBlock.settings-header");
        Assert.Equal("22", ValueOf(headerStyle, "FontSize"));
        Assert.Equal("SemiBold", ValueOf(headerStyle, "FontWeight"));

        var subtitleStyle = doc.Descendants(XamlNs + "Style")
            .Single(e => (string?)e.Attribute("Selector") == "TextBlock.settings-subtitle");
        Assert.Equal("12", ValueOf(subtitleStyle, "FontSize"));

        // Treść: marginesy nagłówka listy (20,20,16,12) + czytelna szerokość na szerokim oknie.
        var content = doc.Descendants(XamlNs + "ScrollViewer").Single()
            .Element(XamlNs + "StackPanel")!;
        Assert.Equal("20,20,16,12", (string?)content.Attribute("Margin"));
        Assert.Equal("760", (string?)content.Attribute("MaxWidth"));
        Assert.Equal("Left", (string?)content.Attribute("HorizontalAlignment"));
    }

    [Fact]
    public void SettingsView_ButtonsAndInputs_UseContentPanelTheme()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "AppSettingsView.axaml"));

        // Przyciski jak w panelach treści (klasy katalogu), nie stare klasy modala.
        foreach (var button in doc.Descendants(XamlNs + "Button"))
        {
            var classes = (string?)button.Attribute("Classes") ?? "";
            Assert.DoesNotContain("modal-secondary", classes);
            Assert.DoesNotContain("modal-primary", classes);
        }

        var save = doc.Descendants(XamlNs + "Button")
            .Single(e => (string?)e.Attribute("Command") == "{Binding SaveCommand}");
        Assert.Equal("catalog-detail-cta", (string?)save.Attribute("Classes"));

        var close = doc.Descendants(XamlNs + "Button")
            .Single(e => (string?)e.Attribute("Click") == "CancelButton_Click");
        Assert.Equal("catalog-detail-secondary", (string?)close.Attribute("Classes"));

        // Inputy na podwyższonym surface (#202226) jak sortowanie katalogu, nie w tle wiersza.
        foreach (var input in doc.Descendants(XamlNs + "ComboBox").Concat(doc.Descendants(XamlNs + "TextBox")))
            Assert.Equal("{DynamicResource TertiaryBackgroundBrush}", (string?)input.Attribute("Background"));
    }

    private static string ValueOf(XElement style, string property) =>
        style.Elements(XamlNs + "Setter")
            .Single(s => (string?)s.Attribute("Property") == property)
            .Attribute("Value")!.Value;

    private static XDocument LoadMainWindow() =>
        XDocument.Load(Path.Combine(RepoRoot(), "SUSModder", "Views", "MainWindow.axaml"));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "MainWindow.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
