using System.Text.Json;
using System.Text.RegularExpressions;
using SUSModder.Services.Localization;
using Xunit;

namespace SUSModder.Tests.Localization;

/// <summary>
/// E9c (M2): każdy klucz lokalizacji używany z C# (literały Get/GetFormatted)
/// i z AXAML (loc:Localize) musi istnieć w PL i EN z identycznymi placeholderami.
/// Łapie błędy klasy brakujących Security.*/Updates.* (klucz wołany, ale nieobecny w JSON).
/// </summary>
public class LocalizationKeyUsageTests
{
    private static readonly Regex CsharpKeyRegex = new(@"(?:Get|GetFormatted)\(\s*""([A-Za-z0-9_.]+)""", RegexOptions.Compiled);
    private static readonly Regex XamlKeyRegex = new(@"\{loc:Localize\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled);

    [Fact]
    public void AllKeysUsedFromCodeAndXamlExistInBothLocalesWithMatchingPlaceholders()
    {
        var root = RepoRoot();
        var pl = Read("pl");
        var en = Read("en");
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles(root, "*.cs"))
        foreach (Match match in CsharpKeyRegex.Matches(File.ReadAllText(file)))
            used.Add(match.Groups[1].Value);

        foreach (var file in SourceFiles(root, "*.axaml"))
        foreach (Match match in XamlKeyRegex.Matches(File.ReadAllText(file)))
            used.Add(match.Groups[1].Value);

        Assert.NotEmpty(used);
        foreach (var key in used.Order())
        {
            Assert.True(pl.TryGetValue(key, out var polish), $"Missing Polish: {key}");
            Assert.True(en.TryGetValue(key, out var english), $"Missing English: {key}");
            Assert.False(string.IsNullOrWhiteSpace(polish), key);
            Assert.False(string.IsNullOrWhiteSpace(english), key);
            Assert.Equal(Placeholders(polish!), Placeholders(english!));
        }
    }

    [Fact]
    public void SingularUnreadKeysRenderCorrectlyInBothLocales()
    {
        var loc = new LocalizationService();
        Assert.Equal("Czat społeczności — 1 nowa wiadomość", loc.GetFormatted("UI.TopBar.ChatToggleUnreadOne", 1));
        Assert.Equal("1 nowa wiadomość", loc.GetFormatted("UI.CommunityChat.NewMessagesOne", 1));
        loc.ChangeCulture("en");
        Assert.Equal("Community chat — 1 new message", loc.GetFormatted("UI.TopBar.ChatToggleUnreadOne", 1));
        Assert.Equal("1 new message", loc.GetFormatted("UI.CommunityChat.NewMessagesOne", 1));
    }

    private static IEnumerable<string> SourceFiles(string root, string pattern) =>
        Directory.GetFiles(Path.Combine(root, "SUSModder"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{([A-Za-z0-9_]+)(?:[},:])")
            .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();

    private static Dictionary<string, string> Read(string locale)
    {
        var assembly = typeof(LocalizationService).Assembly;
        using var stream = assembly.GetManifestResourceStream($"SUSModder.Localization.{locale}.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>();
        Flatten(document.RootElement, string.Empty, result);
        return result;
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
                Flatten(property.Value, key, result);
            else if (property.Value.ValueKind == JsonValueKind.String)
                result[key] = property.Value.GetString()!;
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Localization", "pl.json"))) return dir.FullName;
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SUSModder", "Localization", "pl.json"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}
