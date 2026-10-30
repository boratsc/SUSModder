using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SUSModder.Tests.Themes;

/// <summary>
/// Regresja odświeżenia palety (Dark + Szklany) — testy czytają źródłowe pliki
/// motywów/stylek z repozytorium przez XDocument (bez inicjalizacji Avalonii
/// i bez nowych zależności pakietów).
///
/// Zakres (wąski):
///  1. tokeny palety użytkownika w Dark i Szklany: akcent #20C5C5, tło #101820,
///     panele #172630, tekst #F0EEE8/#97AAB8, statusy success/warning/danger;
///  2. pędzle wiążą się z tokenami kolorów ({StaticResource ...});
///  3. nowe współdzielone klucze DynamicResource istnieją we WSZYSTKICH czterech
///     motywach (Light/Pink mają historycznie niepełny zestaw kluczy legacy —
///     dlatego sprawdzamy tylko jawnie nowe, ograniczone tokeny);
///  4. motywy nie mają duplikatów kluczy x:Key w korzeniu słownika.
/// </summary>
public class PaletteRefreshTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex DynamicResourceRegex =
        new(@"\{DynamicResource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    /// <summary>Wszystkie cztery motywy aplikacji.</summary>
    private static readonly string[] ThemeFiles =
    {
        "DarkTheme.axaml",
        "LightTheme.axaml",
        "PinkTheme.axaml",
        "SzklanyTheme.axaml",
    };

    /// <summary>
    /// Ograniczony zestaw nowych kluczy współdzielonych (wprowadzonych w iteracji
    /// palety), które MUSZĄ istnieć w każdym motywie — bo są używane przez
    /// wspólne style (DynamicResource) niezależnie od motywu.
    /// </summary>
    private static readonly string[] SharedNewDynamicKeys =
    {
        "OnAccentTextBrush",
        "TileCornerRadius",
        "WarningTextBrush",
        "InfoChipFillBrush",
        "ModDetailBorderGradientBrush",
        "ModDetailIconGlowBrush",
        "BadgeAlertBrush",
        "BulkChipActiveFillBrush",
    };

    /// <summary>
    /// Pliki stylek dotknięte iteracją palety + oczekiwane nowe tokeny wspólne.
    /// Pliki zależne od motywu (glass / dark) sprawdzamy osobno.
    /// </summary>
    public static TheoryData<string, string[]> TouchedStyleFiles => new()
    {
        { "BrowserCardStyle.axaml", new[] { "TileCornerRadius", "OnAccentTextBrush" } },
        { "BulkModeChipStyle.axaml", new[] { "BulkChipActiveFillBrush" } },
        { "FabButtonStyle.axaml", new[] { "OnAccentTextBrush", "BadgeAlertBrush" } },
        { "ModDetailPanelStyle.axaml", new[] { "ModDetailBorderGradientBrush", "ModDetailIconGlowBrush", "OnAccentTextBrush" } },
        { "PanelStyles.axaml", new[] { "OnAccentTextBrush" } },
    };

    /// <summary>Paleta użytkownika wspólna dla Dark i Szklany (bez alf — porównanie RGB).</summary>
    public static TheoryData<string, string> SharedPaletteTokens => new()
    {
        { "AccentPrimaryColor", "#20C5C5" },
        { "AccentColor", "#20C5C5" },
        { "SelectionBarColor", "#20C5C5" },
        { "IconAccentColor", "#20C5C5" },
        { "PrimaryActionColor", "#20C5C5" },
        { "PrimaryActionHoverColor", "#17A2A2" },
        { "SurfaceBaseColor", "#101820" },
        { "PrimaryBackgroundColor", "#101820" },
        { "OnAccentTextColor", "#101820" },
        { "SecondaryBackgroundColor", "#172630" },
        { "SurfaceElevatedColor", "#172630" },
        { "SurfaceCardColor", "#172630" },
        { "TextPrimaryColor", "#F0EEE8" },
        { "TextSecondaryColor", "#97AAB8" },
        { "SuccessColor", "#45B889" },
        { "PositiveActionColor", "#45B889" },
        { "WarningColor", "#D9A441" },
        { "StatusUpdateColor", "#D9A441" },
        { "UpdateAlertColor", "#D9A441" },
        { "WarningTextColor", "#D9A441" },
        { "DangerColor", "#D95C48" },
        { "StatusMissingTextColor", "#D95C48" },
        { "StatusInstalledColor", "#45B889" },
    };

    // ── Paleta: Dark + Szklany ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SharedPaletteTokens))]
    public void DarkAndSzklany_UseUserPaletteTokens(string key, string expectedRgb)
    {
        foreach (var themeFile in new[] { "DarkTheme.axaml", "SzklanyTheme.axaml" })
        {
            var theme = LoadTheme(themeFile);
            Assert.True(theme.Resources.TryGetValue(key, out var element), $"[{themeFile}] brak tokenu '{key}'");
            Assert.Equal("Color", element!.Name.LocalName);
            Assert.Equal(expectedRgb, ToRgb(element.Value));
        }
    }

    [Fact]
    public void DarkTheme_KeepsOpaqueBaseAndFlatSurfaces()
    {
        var theme = LoadTheme("DarkTheme.axaml");

        // Tło aplikacji jest płaskie i w pełni kryjące (bez alf jak w szkle).
        AssertColor(theme, "SurfaceBaseColor", "#101820", 0xFF);
        AssertColor(theme, "PrimaryBackgroundColor", "#101820", 0xFF);
        AssertColor(theme, "WindowBackgroundColor", "#101820", 0xFF);
        AssertColor(theme, "SecondaryBackgroundColor", "#172630", 0xFF);
        AssertColor(theme, "MenuBackgroundColor", "#172630", 0xFF);
        AssertColor(theme, "PaneBackgroundColor", "#172630", 0xFF);
        AssertColor(theme, "CardBackgroundColor", "#172630", 0xFF);
        AssertColor(theme, "ModCardBackgroundColor", "#172630", 0xFF);
        AssertColor(theme, "TertiaryBackgroundColor", "#243B53", 0xFF);
        AssertColor(theme, "BorderColor", "#243B53", 0xFF);
        AssertColor(theme, "ModCardBorderColor", "#243B53", 0xFF);
        AssertColor(theme, "GridSplitterColor", "#243B53", 0xFF);
        AssertColor(theme, "AccentMutedColor", "#177079", 0xFF);
        AssertColor(theme, "BulkSelectionColor", "#17A2A2", 0xFF);

        // Kafelki: r10 (Light/Pink zostają przy r12 — to decyzja wyglądu, nie regresja).
        Assert.Equal("10", theme.Resources["TileCornerRadius"].Value.Trim());
    }

    [Fact]
    public void SzklanyTheme_KeepsGlassAlphaOverTheSameBaseColor()
    {
        var theme = LoadTheme("SzklanyTheme.axaml");

        // Szkło: te same odcienie palety, ale z alfą (tło jest przezroczyste).
        AssertColor(theme, "SurfaceBaseColor", "#101820", 0x99);
        AssertColor(theme, "PrimaryBackgroundColor", "#101820", 0x99);
        AssertColor(theme, "SecondaryBackgroundColor", "#172630", 0xB3);
        AssertColor(theme, "SurfaceElevatedColor", "#172630", 0xB3);
        AssertColor(theme, "SurfaceCardColor", "#172630", 0x59);

        // Akcent i statusy bez zmian względem Dark.
        AssertColor(theme, "AccentPrimaryColor", "#20C5C5", 0xFF);
        AssertColor(theme, "AccentColor", "#20C5C5", 0xFF);
        AssertColor(theme, "SelectionBarColor", "#20C5C5", 0xFF);
        AssertColor(theme, "TextPrimaryColor", "#F0EEE8", 0xFF);
        AssertColor(theme, "TextSecondaryColor", "#97AAB8", 0xFF);
        AssertColor(theme, "SuccessColor", "#45B889", 0xFF);
        AssertColor(theme, "WarningColor", "#D9A441", 0xFF);
        AssertColor(theme, "DangerColor", "#D95C48", 0xFF);

        Assert.Equal("10", theme.Resources["TileCornerRadius"].Value.Trim());
    }

    [Theory]
    [InlineData("DarkTheme.axaml")]
    [InlineData("SzklanyTheme.axaml")]
    public void PaletteBrushes_AreBoundToTheirColorTokens(string themeFile)
    {
        var theme = LoadTheme(themeFile);

        (string Brush, string Color)[] pairs =
        {
            ("AccentPrimaryBrush", "AccentPrimaryColor"),
            ("AccentBrush", "AccentColor"),
            ("PrimaryBackgroundBrush", "PrimaryBackgroundColor"),
            ("SurfaceBaseBrush", "SurfaceBaseColor"),
            ("SurfaceElevatedBrush", "SurfaceElevatedColor"),
            ("TextPrimaryBrush", "TextPrimaryColor"),
            ("TextSecondaryBrush", "TextSecondaryColor"),
            ("SelectionBarBrush", "SelectionBarColor"),
            ("WarningBrush", "WarningColor"),
            ("DangerBrush", "DangerColor"),
            ("SuccessBrush", "SuccessColor"),
            ("OnAccentTextBrush", "OnAccentTextColor"),
            ("WarningTextBrush", "WarningTextColor"),
            ("InfoChipFillBrush", "InfoChipFillColor"),
        };

        foreach (var (brush, color) in pairs)
        {
            Assert.True(theme.Resources.TryGetValue(brush, out var element), $"[{themeFile}] brak pędzla '{brush}'");
            Assert.Equal("SolidColorBrush", element!.Name.LocalName);
            Assert.Equal($"{{StaticResource {color}}}", (string?)element.Attribute("Color"));
        }
    }

    // ── Klucze wspólne + pliki stylek ───────────────────────────────────────

    [Theory]
    [InlineData("DarkTheme.axaml")]
    [InlineData("LightTheme.axaml")]
    [InlineData("PinkTheme.axaml")]
    [InlineData("SzklanyTheme.axaml")]
    public void SharedNewDynamicKeys_AreDefinedInEveryTheme(string themeFile)
    {
        var theme = LoadTheme(themeFile);

        foreach (var key in SharedNewDynamicKeys)
        {
            Assert.True(
                theme.Resources.ContainsKey(key),
                $"[{themeFile}] brak współdzielonego klucza '{key}' (używany przez wspólne style przez DynamicResource)");
        }
    }

    [Theory]
    [MemberData(nameof(TouchedStyleFiles))]
    public void TouchedStyleFiles_UseSharedTokensDefinedInAllThemes(string fileName, string[] expectedSharedKeys)
    {
        var referenced = LoadStyleDynamicResources(fileName);

        foreach (var key in expectedSharedKeys)
        {
            Assert.Contains(key, referenced);
        }

        foreach (var key in referenced)
        {
            if (!SharedNewDynamicKeys.Contains(key))
            {
                continue; // klucze legacy/hurtowniowe — sprawdzane osobno (test niżej)
            }

            foreach (var themeFile in ThemeFiles)
            {
                Assert.True(
                    LoadTheme(themeFile).Resources.ContainsKey(key),
                    $"[{themeFile}] brak klucza '{key}' używanego w {fileName}");
            }
        }
    }

    [Theory]
    [InlineData("GlassSurfaceStyles.axaml", "SzklanyTheme.axaml")]
    [InlineData("GlassFlyoutStyles.axaml", "SzklanyTheme.axaml")]
    [InlineData("DarkSurfaceStyles.axaml", "DarkTheme.axaml")]
    public void ThemeScopedStyleFiles_ReferenceTokensDefinedInTheirTheme(string fileName, string themeFile)
    {
        var theme = LoadTheme(themeFile);

        foreach (var key in LoadStyleDynamicResources(fileName))
        {
            Assert.True(
                theme.Resources.ContainsKey(key),
                $"[{themeFile}] brak klucza '{key}' używanego w {fileName}");
        }
    }

    // ── Higiena słowników motywów ───────────────────────────────────────────

    [Theory]
    [InlineData("DarkTheme.axaml")]
    [InlineData("LightTheme.axaml")]
    [InlineData("PinkTheme.axaml")]
    [InlineData("SzklanyTheme.axaml")]
    public void ThemeFiles_HaveNoDuplicateRootKeys(string themeFile)
    {
        var theme = LoadTheme(themeFile);

        Assert.Empty(theme.DuplicateKeys);
    }

    [Fact]
    public void ThemeChanges_DeferChromeRefreshUntilResourceSwapCompletes()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SUSModder", "Views", "MainWindow.GlassTheme.cs"));
        var subscription = source[source.IndexOf("private void SubscribeGlassThemeChanges", StringComparison.Ordinal)..];
        subscription = subscription[..subscription.IndexOf(".DisposeWith(disposables)", StringComparison.Ordinal)];

        Assert.Contains("Dispatcher.UIThread.Post(", subscription);
        Assert.Contains("RefreshGlassWindowChrome,", subscription);
        Assert.DoesNotContain(".Subscribe(_ => RefreshGlassWindowChrome())", subscription);
    }

    // ── Pomocnicze ───────────────────────────────────────────────────────────

    private static void AssertColor(ThemeCatalog theme, string key, string expectedRgb, byte expectedAlpha)
    {
        Assert.True(theme.Resources.TryGetValue(key, out var element), $"[{theme.FileName}] brak tokenu '{key}'");
        var (alpha, rgb) = ParseColor(element!.Value);
        Assert.Equal(expectedRgb, rgb);
        Assert.Equal(expectedAlpha, alpha);
    }

    /// <summary>Zwraca "#RRGGBB" z "#RRGGBB" lub "#AARRGGBB" (kolejność Avalonia: AARRGGBB).</summary>
    private static string ToRgb(string raw)
    {
        var (_, rgb) = ParseColor(raw);
        return rgb;
    }

    private static (byte Alpha, string Rgb) ParseColor(string raw)
    {
        var hex = raw.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        if (hex.Length != 8 ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"Niepoprawny kolor w XAML: '{raw}'");
        }

        var alpha = (byte)((value >> 24) & 0xFF);
        var red = (byte)((value >> 16) & 0xFF);
        var green = (byte)((value >> 8) & 0xFF);
        var blue = (byte)(value & 0xFF);
        return (alpha, $"#{red:X2}{green:X2}{blue:X2}");
    }

    private static IReadOnlyList<string> LoadStyleDynamicResources(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "SUSModder", "Styles", fileName);
        Assert.True(File.Exists(path), $"Brak pliku stylu: {path}");

        var document = XDocument.Load(path);
        var keys = new List<string>();
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                foreach (Match match in DynamicResourceRegex.Matches(attribute.Value))
                {
                    keys.Add(match.Groups[1].Value);
                }
            }
        }

        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static ThemeCatalog LoadTheme(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "SUSModder", "Themes", fileName);
        Assert.True(File.Exists(path), $"Brak pliku motywu: {path}");
        return new ThemeCatalog(fileName, path);
    }

    /// <summary>
    /// Znajduje katalog repozytorium (wzgórzem od AppContext.BaseDirectory),
    /// żeby testy czytały źródłowe pliki XAML, a nie kopie w katalogu wyjściowym.
    /// </summary>
    private static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SUSModder", "Themes", "DarkTheme.axaml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Nie znaleziono katalogu repozytorium (oczekiwano SUSModder/Themes/DarkTheme.axaml) od " +
            AppContext.BaseDirectory);
    }

    private sealed class ThemeCatalog
    {
        public ThemeCatalog(string fileName, string path)
        {
            FileName = fileName;

            var resources = new Dictionary<string, XElement>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            var document = XDocument.Load(path);
            foreach (var element in document.Root!.Elements())
            {
                if (element.Name.NamespaceName != AvaloniaNamespace)
                {
                    continue;
                }

                var key = (string?)element.Attribute(XName.Get("Key", XamlNamespace));
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (resources.ContainsKey(key!))
                {
                    duplicates.Add(key!);
                }

                resources[key!] = element;
            }

            Resources = resources;
            DuplicateKeys = duplicates;
        }

        public string FileName { get; }

        public IReadOnlyDictionary<string, XElement> Resources { get; }

        public IReadOnlyList<string> DuplicateKeys { get; }
    }
}
