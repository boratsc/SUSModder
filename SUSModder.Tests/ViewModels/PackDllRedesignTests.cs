using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Services.Localization;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E4 (zestawy i DLL jako listy): badge, podtytuł i liczniki wiersza zestawu
/// na istniejących stanach ModInstanceItem. Bez inicjalizacji lokalizacji —
/// testujemy polski fallback (zgodnie z zasadą PL fallback).
/// </summary>
public class PackDllRedesignTests
{
    private static ModInstanceItem Pack(int dllCount = 2, bool tou = false, string updated = "2026-09-29T10:00:00Z") =>
        new(new ModInstance
        {
            DisplayName = "Vanilla + ToU",
            BaseModName = "ToU",
            FullModVersion = "7.3.0",
            UpdatedAt = updated
        }, "tou.png", dllCount, tou);

    [Fact]
    public void SavedBadgeIsDefault() => Assert.Equal("Zapisany", Pack().PackStatusBadge);

    [Fact]
    public void UpdateStateShowsUpdateBadge()
    {
        var pack = Pack();
        pack.HasUpdateAvailable = true;
        Assert.Equal("Dostępna aktualizacja", pack.PackStatusBadge);
    }

    [Fact]
    public void ContentsCountShowsSingleModAndDlls() =>
        Assert.Equal("1 mod · 2 DLL", Pack().PackContentsCount);

    [Fact]
    public void RowSubtitleCombinesCountsAndBaseMod() =>
        Assert.Equal("1 mod · 2 DLL · ToU · v7.3.0", Pack().PackRowSubtitle);

    [Fact]
    public void ContentsItemCountIncludesTouConfig()
    {
        Assert.Equal(2, Pack().PackContentsItemCount);
        Assert.Equal(3, Pack(tou: true).PackContentsItemCount);
    }

    [Fact]
    public void LastUpdatedTextFormatsDate() =>
        Assert.Equal("29.09.2026", Pack().LastUpdatedText);

    [Fact]
    public void LastUpdatedTextFallsBackForInvalidDate() =>
        Assert.Equal("—", Pack(updated: "").LastUpdatedText);

    [Fact]
    public void CompatReadyReflectsBusyAndUpdateStates()
    {
        var pack = Pack();
        Assert.True(pack.PackCompatIsReady);

        pack.IsBusy = true;
        Assert.False(pack.PackCompatIsReady);

        pack.IsBusy = false;
        pack.HasUpdateAvailable = true;
        Assert.False(pack.PackCompatIsReady);
    }

    [Fact]
    public void DllRowBadgeStaysDllEvenWithBeta()
    {
        var dll = ModItemAdapter.FromConfig(new ModConfiguration { ModType = "dll", ModVersion = "1.2.3" });
        dll.IsInstalledBeta = true;
        Assert.Equal("DLL", dll.CatalogTypeBadge);
    }
}

/// <summary>E4: nowe klucze i18n istnieją w PL i EN z identycznymi placeholderami.</summary>
public class PackDllRedesignLocalizationTests
{
    private static readonly string[] NewKeys =
    [
        "UI.Packs.SavedBadge",
        "UI.Packs.ContentsCountFormat",
        "UI.Packs.LaunchPack",
        "UI.Packs.ContentsRow",
        "UI.DllManager.AddToPack",
        "UI.DllManager.CompatibleMods",
        "UI.Browser.NoDllsTitle",
        "UI.Browser.NoDllsHint",
        "UI.Inspector.CompatErrorTargets",
        "UI.Inspector.CompatWarningTargets"
    ];

    [Fact]
    public void NewKeysExistInBothLocalesWithMatchingPlaceholders()
    {
        var pl = Read("pl");
        var en = Read("en");
        foreach (var key in NewKeys)
        {
            Assert.True(pl.TryGetValue(key, out var polish), $"Missing Polish: {key}");
            Assert.True(en.TryGetValue(key, out var english), $"Missing English: {key}");
            Assert.False(string.IsNullOrWhiteSpace(polish), key);
            Assert.False(string.IsNullOrWhiteSpace(english), key);
            Assert.Equal(Placeholders(polish!), Placeholders(english!));
        }
    }

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
}
