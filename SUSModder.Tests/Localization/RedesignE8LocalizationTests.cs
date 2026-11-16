using System.Text.Json;
using System.Text.RegularExpressions;
using SUSModder.Services.Localization;
using Xunit;

namespace SUSModder.Tests.Localization;

/// <summary>
/// E8: pełne rodziny kluczy redesignu 4.0 (E2–E7) istnieją w PL i EN
/// z identycznymi placeholderami. Zakres prefiksowy — łapie też klucze
/// dodane po testach jednostkowych E3/E4.
/// </summary>
public class RedesignE8LocalizationTests
{
    private static readonly string[] KeyPrefixes =
    [
        "Settings.Main.",
        "UI.TopBar.",
        "UI.Sidebar.",
        "UI.Window.",
        "UI.Browser.",
        "UI.Tabs.",
        "UI.Roles.",
        "UI.Inspector.",
        "UI.Packs.",
        "UI.DllManager.",
        "UI.Bulk.Packs.",
        "UI.CommunityChat.Toast.",
        "Dialogs.PostInstallFailure.",
        "Dialogs.AmongUsNotFound."
    ];

    [Fact]
    public void RedesignKeysHaveBothLocalesAndMatchingPlaceholders()
    {
        var pl = Read("pl");
        var en = Read("en");
        var keys = pl.Keys.Concat(en.Keys).Where(IsRedesignKey).Distinct().ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.True(pl.TryGetValue(key, out var polish), $"Missing Polish: {key}");
            Assert.True(en.TryGetValue(key, out var english), $"Missing English: {key}");
            Assert.False(string.IsNullOrWhiteSpace(polish), key);
            Assert.False(string.IsNullOrWhiteSpace(english), key);
            Assert.Equal(Placeholders(polish!), Placeholders(english!));
        }
    }

    private static bool IsRedesignKey(string key) =>
        KeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

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
            {
                if (result.ContainsKey(key) && IsRedesignKey(key))
                    Assert.Fail($"Duplicate redesign translation: {key}");
                result[key] = property.Value.GetString()!;
            }
        }
    }
}
