using System.Text.Json;
using SUSModder.Services.Localization;
using Xunit;

namespace SUSModder.Tests.Localization;

/// <summary>
/// E14: migracja tekstów ról do UI.Roles.* — stare sekcje RolesWindow.* i RoleDetail.*
/// usunięte z obu locale; klucz przycisku „Role" w drawerze zostaje.
/// </summary>
public class RolesLocalizationTests
{
    [Fact]
    public void LegacyRolesWindowAndRoleDetailSectionsAreRemoved()
    {
        foreach (var locale in new[] { "pl", "en" })
        {
            var keys = Read(locale);
            Assert.DoesNotContain(keys.Keys, k => k.StartsWith("RolesWindow.", StringComparison.Ordinal));
            Assert.DoesNotContain(keys.Keys, k => k.StartsWith("RoleDetail.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void RolesButtonKeySurvivesMigration()
    {
        var loc = new LocalizationService();
        Assert.Equal("Role", loc.Get("UI.Buttons.Roles"));
        loc.ChangeCulture("en");
        Assert.Equal("Roles", loc.Get("UI.Buttons.Roles"));
    }

    [Fact]
    public void RolesTabAndFilterLabelsRenderInBothLocales()
    {
        var loc = new LocalizationService();
        Assert.Equal("Role", loc.Get("UI.Tabs.Roles"));
        Assert.Equal("Wszystkie kategorie", loc.Get("UI.Roles.AllCategories"));
        Assert.Equal("3 z 12 ról", loc.GetFormatted("UI.Roles.ModRoleCount", 3, 12));
        loc.ChangeCulture("en");
        Assert.Equal("Roles", loc.Get("UI.Tabs.Roles"));
        Assert.Equal("All categories", loc.Get("UI.Roles.AllCategories"));
        Assert.Equal("3 of 12 roles", loc.GetFormatted("UI.Roles.ModRoleCount", 3, 12));
    }

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
}