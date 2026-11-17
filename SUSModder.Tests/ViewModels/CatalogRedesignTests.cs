using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SUSModder.Converters;
using SUSModder.Core.Configuration;
using SUSModder.Services.Localization;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E3 (katalog jako lista): badge typu i podtytuł wiersza na istniejących stanach ModItem.
/// Bez inicjalizacji lokalizacji — testujemy polski fallback (zgodnie z zasadą PL fallback).
/// </summary>
public class CatalogRedesignTests
{
    private static ModItem Item(string type, string version = "1.0.0", string among = "2026-9-29") =>
        ModItemAdapter.FromConfig(new ModConfiguration
        {
            ModType = type,
            ModVersion = version,
            AmongVersion = among
        });

    [Fact]
    public void VanillaBadgeIsGame() => Assert.Equal("Gra", Item("Vanilla").CatalogTypeBadge);

    [Fact]
    public void FullBadgeIsFullMod() => Assert.Equal("Pełny mod", Item("full").CatalogTypeBadge);

    [Fact]
    public void DllBadgeIsDll() => Assert.Equal("DLL", Item("dll").CatalogTypeBadge);

    [Fact]
    public void InstalledBetaShowsBetaBadge()
    {
        var item = Item("full");
        item.IsInstalledBeta = true;
        Assert.Equal("BETA", item.CatalogTypeBadge);
    }

    [Fact]
    public void NewerBetaShowsBetaBadge()
    {
        var item = Item("full");
        item.LatestStableVersion = "1.7.3";
        item.LatestBetaVersion = "1.7.9";
        Assert.True(item.ShowNewerBetaOnCard);
        Assert.Equal("BETA", item.CatalogTypeBadge);
    }

    [Fact]
    public void RowSubtitleCombinesVersionAndDescription()
    {
        var item = Item("full");
        item.Description = "Nowe role i tryby gry.";
        Assert.Equal("v1.0.0 · 2026-9-29 · Nowe role i tryby gry.", item.CatalogRowSubtitle);
    }

    [Fact]
    public void RowSubtitleWithoutDescriptionKeepsVersion() =>
        Assert.Equal("v1.0.0 · 2026-9-29", Item("full").CatalogRowSubtitle);

    [Fact]
    public void RowSubtitleWithoutVersionKeepsDescription()
    {
        var item = Item("full", version: "", among: "");
        item.Description = "Opis moda.";
        Assert.Equal("Opis moda.", item.CatalogRowSubtitle);
    }
}

/// <summary>E3: nowe klucze i18n istnieją w PL i EN z identycznymi placeholderami.</summary>
public class CatalogRedesignLocalizationTests
{
    private static readonly string[] NewKeys =
    [
        "UI.Browser.NoResultsHint",
        "UI.Browser.ClearFilters",
        "UI.Inspector.CompatCheck",
        "UI.Inspector.CompatReady",
        "UI.Labels.Type",
        "UI.Labels.GameCompatibility",
        "UI.Labels.LastUpdated",
        "UI.Labels.Repository",
        "ModTypes.Game",
        "UI.Buttons.Install",
        "UI.Buttons.Launch",
        "UI.Beta.InstallStable",
        "UI.Beta.InstallBeta",
        "UI.Browser.Sort.Label",
        "UI.Browser.Sort.ButtonLabel",
        "UI.Browser.Sort.PopularityDesc",
        "UI.Browser.Sort.PopularityAsc",
        "UI.Browser.Sort.NameAsc",
        "UI.Browser.Sort.NameDesc",
        "UI.Browser.NotInstalledSeparator"
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

/// <summary>
/// Szybkie akcje wiersza katalogu: warunki widoczności przycisków oparte na stanach ModItem
/// (bindingi w MainWindow.axaml łączą te same właściwości).
/// </summary>
public class CatalogRowQuickActionTests
{
    private static ModItem Item(string type, string version = "1.0.0") =>
        ModItemAdapter.FromConfig(new ModConfiguration
        {
            ModType = type,
            ModVersion = version,
            AmongVersion = "2026-9-29"
        });

    [Fact]
    public void NotInstalledWithoutBeta_ShowsSingleInstall()
    {
        var item = Item("full");
        Assert.True(item.CanInstall);          // "Instaluj" widoczny
        Assert.False(item.HasAvailableBeta);   // brak przycisków stabilna/beta
        Assert.False(item.IsInstalled);        // brak "Uruchom"
    }

    [Fact]
    public void NotInstalledWithBeta_ShowsStableAndBeta()
    {
        var item = Item("full");
        item.LatestBetaVersion = "1.1.0";
        Assert.True(item.CanInstall);
        Assert.True(item.HasAvailableBeta);    // "Instaluj stabilną" + "Instaluj beta"
    }

    [Fact]
    public void InstalledMod_ShowsLaunchInsteadOfInstall()
    {
        var item = Item("full");
        item.InstallPath = @"C:\Mods\Test";
        Assert.True(item.IsInstalled);         // "Uruchom" widoczny
        Assert.False(item.CanInstall);         // przyciski instalacji ukryte
    }

    [Fact]
    public void InstallingMod_HidesInstallButtons()
    {
        var item = Item("full");
        item.IsInstalling = true;
        Assert.False(item.CanInstall);
    }

    [Fact]
    public void VanillaNeverOffersBetaInstall()
    {
        var item = Item("Vanilla");
        item.LatestBetaVersion = "1.1.0";
        Assert.True(item.IsVanilla);           // wiersz pokazuje tylko "Uruchom"
        Assert.False(item.HasAvailableBeta);
    }

    [Fact]
    public void InstalledVersionEqualToLatestBeta_HidesBetaOption()
    {
        var item = Item("full", version: "1.1.0");
        item.LatestBetaVersion = "1.1.0";
        Assert.False(item.HasAvailableBeta);
    }
}

/// <summary>ReferenceEqualsMultiConverter z parametrem "Invert" (wiersz != zaznaczony mod).</summary>
public class ReferenceEqualsMultiConverterTests
{
    private static readonly ReferenceEqualsMultiConverter Converter = new();

    private static object? Convert(object? a, object? b, string? parameter = null) =>
        Converter.Convert(new[] { a, b }, typeof(bool), parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void SameReference_EqualsTrue_InvertFalse()
    {
        var item = new ModItem();
        Assert.Equal(true, Convert(item, item));
        Assert.Equal(false, Convert(item, item, "Invert"));
    }

    [Fact]
    public void DifferentReference_EqualsFalse_InvertTrue()
    {
        Assert.Equal(false, Convert(new ModItem(), new ModItem()));
        Assert.Equal(true, Convert(new ModItem(), new ModItem(), "Invert"));
    }

    [Fact]
    public void NoSelection_InvertTrue()
    {
        Assert.Equal(false, Convert(new ModItem(), null));
        Assert.Equal(true, Convert(new ModItem(), null, "Invert"));
    }

    [Fact]
    public void MissingValues_StillFalse()
    {
        Assert.Equal(false, Converter.Convert(new object?[] { new ModItem() }, typeof(bool), "Invert", CultureInfo.InvariantCulture));
    }
}