using System.Text.Json;
using System.Text.RegularExpressions;
using SUSModder.Services.Localization;
using Xunit;

namespace SUSModder.Tests.Localization;

public class RoadmapLocalizationTests
{
    [Fact]
    public void RoadmapKeysHaveBothLocalesAndMatchingPlaceholders()
    {
        var pl = Read("pl");
        var en = Read("en");
        var keys = pl.Keys.Concat(en.Keys).Where(IsRoadmapKey).Distinct().ToArray();
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

    private static bool IsRoadmapKey(string key) =>
        key.StartsWith("UI.CommunityChat.", StringComparison.Ordinal) ||
        key.StartsWith("UI.Favorites.", StringComparison.Ordinal) ||
        key.StartsWith("UI.Storage.", StringComparison.Ordinal) ||
        key.StartsWith("UI.UpdatePreview.", StringComparison.Ordinal) ||
        key.StartsWith("Dialogs.UpdateMod.Preview.", StringComparison.Ordinal) ||
        key.StartsWith("Dialogs.BulkUpdate.", StringComparison.Ordinal) ||
        key.StartsWith("Toast.ModUpdateTarget", StringComparison.Ordinal) ||
        key == "Updates.TargetDrift" ||
        key.StartsWith("ModPacks.Wizard.", StringComparison.Ordinal);

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
                if (result.ContainsKey(key) && IsRoadmapKey(key))
                    Assert.Fail($"Duplicate roadmap translation: {key}");
                // Legacy resources contain unrelated duplicates; retain the runtime's last-value semantics.
                result[key] = property.Value.GetString()!;
            }
        }
    }
}
