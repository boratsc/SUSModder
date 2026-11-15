using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SUSModder.Tests.Themes;

public class PaletteRefreshTests
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex DynamicResourceRegex = new(@"\{DynamicResource\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    [Theory]
    [InlineData("AccentPrimaryColor", "#F7AE27")]
    [InlineData("SurfaceBaseColor", "#101113")]
    [InlineData("SurfaceElevatedColor", "#1A1C1F")]
    [InlineData("BorderColor", "#35373C")]
    [InlineData("TextPrimaryColor", "#F4F4F4")]
    [InlineData("TextSecondaryColor", "#BFC1C4")]
    [InlineData("SuccessColor", "#55C58D")]
    [InlineData("DangerColor", "#FF7373")]
    [InlineData("WarningColor", "#FFCB58")]
    public void Palette_has_expected_colors(string key, string expected)
    {
        var theme = LoadTheme();
        Assert.True(theme.TryGetValue(key, out var color), $"Missing {key}");
        Assert.Equal("Color", color!.Name.LocalName);
        Assert.Equal(expected, ToRgb(color.Value));
    }

    [Fact]
    public void Colors_and_brushes_are_consistent_and_keys_unique()
    {
        var theme = LoadTheme();
        var keys = theme.Keys.ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var brush in theme.Values.Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var value = (string?)brush.Attribute("Color");
            if (value?.StartsWith("{StaticResource ", StringComparison.Ordinal) == true)
                Assert.Contains(value[16..^1], keys);
        }
    }

    [Fact]
    public void Dynamic_resources_in_styles_exist_and_main_window_includes_exist()
    {
        var theme = LoadTheme();
        var root = RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "SUSModder", "Styles"), "*.axaml"))
        foreach (Match match in DynamicResourceRegex.Matches(File.ReadAllText(file)))
            Assert.True(theme.ContainsKey(match.Groups[1].Value), $"{Path.GetFileName(file)} missing {match.Groups[1].Value}");
        var window = XDocument.Load(Path.Combine(root, "SUSModder", "Views", "MainWindow.axaml"));
        foreach (var include in window.Descendants().Where(e => e.Name.LocalName == "StyleInclude"))
        {
            var source = (string?)include.Attribute("Source");
            Assert.NotNull(source);
            var path = Path.Combine(root, "SUSModder", "Views", source!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) path = Path.Combine(root, "SUSModder", source.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing style include {source}");
        }
    }

    private static Dictionary<string, XElement> LoadTheme()
    {
        var path = Path.Combine(RepoRoot(), "SUSModder", "Themes", "DarkTheme.axaml");
        var doc = XDocument.Load(path);
        return doc.Root!.Elements().Where(e => (string?)e.Attribute(XName.Get("Key", XamlNamespace)) != null)
            .ToDictionary(e => (string)e.Attribute(XName.Get("Key", XamlNamespace))!, StringComparer.Ordinal);
    }

    private static string ToRgb(string raw)
    {
        var hex = raw.Trim().TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        return "#" + hex.ToUpperInvariant();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Themes", "DarkTheme.axaml"))) return dir.FullName;
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Themes", "DarkTheme.axaml"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}

