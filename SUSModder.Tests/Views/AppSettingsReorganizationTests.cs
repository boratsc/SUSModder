using System.Xml.Linq;
using Xunit;

namespace SUSModder.Tests.Views;

/// <summary>
/// Reorganizacja panelu Ustawienia: proste opcje (folder biblioteki, język, zachowanie okna)
/// na wierzchu, reszta w Expanderze „Zaawansowane”; karty statyczne usunięte, kanał aktualizacji
/// scalony, ścieżka modów bez duplikatu. Test strukturalny AXAML — wzorzec: AppSettingsPanelHostingTests.
/// </summary>
public class AppSettingsReorganizationTests
{
    private static readonly XNamespace XamlNs = "https://github.com/avaloniaui";

    [Fact]
    public void StaticUpdatesAndAppearanceCards_AreRemoved()
    {
        var text = File.ReadAllText(SettingsViewPath());
        Assert.DoesNotContain("Settings.Main.Updates", text);
        Assert.DoesNotContain("Settings.Main.Appearance", text);
    }

    [Fact]
    public void LanguageAndWindowBehavior_AreOutsideExpander_ChatAnchorIsInside()
    {
        var doc = XDocument.Load(SettingsViewPath());
        var expander = doc.Descendants(XamlNs + "Expander").Single();

        // Język i zachowanie okna na wierzchu (poza Expanderem).
        Assert.DoesNotContain(expander.DescendantsAndSelf(),
            e => e.Attributes().Any(a => a.Value.Contains("SelectedLanguage")));
        Assert.DoesNotContain(expander.DescendantsAndSelf(),
            e => e.Attributes().Any(a => a.Value.Contains("MinimizeToTray")));
        Assert.Single(doc.Descendants(XamlNs + "ComboBox"),
            e => (string?)e.Attribute("SelectedItem") == "{Binding SelectedLanguage, Mode=TwoWay}");
        Assert.Single(doc.Descendants(XamlNs + "ToggleSwitch"),
            e => (string?)e.Attribute("IsChecked") == "{Binding MinimizeToTray}");

        // Zakotwiczenie czatu w zaawansowanych; stary przełącznik nakładki usunięty z widoku.
        Assert.Single(expander.Descendants(XamlNs + "ToggleSwitch"),
            e => (string?)e.Attribute("IsChecked") == "{Binding ChatAnchored}");
        Assert.DoesNotContain("ChatOverlayOpen", File.ReadAllText(SettingsViewPath()));
    }

    [Fact]
    public void UpdateChannel_IsSingleMergedCard_WithoutCurrentTitleCard()
    {
        var doc = XDocument.Load(SettingsViewPath());
        var expander = doc.Descendants(XamlNs + "Expander").Single();

        // Dokładnie jeden ComboBox kanału w Expanderze i podsumowanie bieżącego kanału w tej samej karcie.
        Assert.Single(expander.Descendants(XamlNs + "ComboBox"),
            e => (string?)e.Attribute("SelectedItem") == "{Binding SelectedUpdateChannel, Mode=TwoWay}");
        Assert.Single(expander.Descendants(),
            e => (string?)e.Attribute("Text") == "{Binding CurrentUpdateChannelSummary}");
        Assert.DoesNotContain("Settings.UpdateChannel.CurrentTitle", File.ReadAllText(SettingsViewPath()));
    }

    [Fact]
    public void ModsInstallPath_EditableTextBox_AppearsExactlyOnce()
    {
        var doc = XDocument.Load(SettingsViewPath());

        Assert.Single(doc.Descendants(XamlNs + "TextBox"),
            e => (string?)e.Attribute("Text") == "{Binding ModsInstallPath}");
        // Bez duplikatu przycisków przeglądania/resetu ze starej sekcji „Ścieżki instalacji”.
        Assert.Single(doc.Descendants(XamlNs + "Button"),
            e => (string?)e.Attribute("Command") == "{Binding BrowseFolderCommand}");
        Assert.Single(doc.Descendants(XamlNs + "Button"),
            e => (string?)e.Attribute("Command") == "{Binding ResetToDefaultCommand}");
    }

    [Fact]
    public void ExpanderContentChrome_IsFullyInvisible()
    {
        var doc = XDocument.Load(SettingsViewPath());

        // Artefakt rogów: motyw Fluent (:down/:expanded:down) narzuca Border#ExpanderContent
        // obramowanie i rogi; lokalny styl z aktywatorem :expanded musi je zerować.
        var style = doc.Descendants(XamlNs + "Style")
            .Single(e => (string?)e.Attribute("Selector") ==
                "Expander.modal-details:expanded /template/ Border#ExpanderContent");
        Assert.Equal("Transparent", ValueOf(style, "Background"));
        Assert.Equal("Transparent", ValueOf(style, "BorderBrush"));
        Assert.Equal("0", ValueOf(style, "BorderThickness"));
        Assert.Equal("0", ValueOf(style, "CornerRadius"));
    }

    private static string ValueOf(XElement style, string property) =>
        style.Elements(XamlNs + "Setter")
            .Single(s => (string?)s.Attribute("Property") == property)
            .Attribute("Value")!.Value;

    private static string SettingsViewPath() =>
        Path.Combine(RepoRoot(), "SUSModder", "Views", "AppSettingsView.axaml");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Views", "MainWindow.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}