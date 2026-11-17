using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Configuration;
using SUSModder.Services.Localization;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// K2: sortowanie katalogu (4 opcje, remisy, Vanilla na początku, degradacja przy zerowej
/// popularności) i separator „Niezainstalowane" (widoczność także przy filtrach).
/// Bez DI — czyste projekcje, wzorzec BrowserProjectionTests.
/// </summary>
public class ModListSortTests
{
    private static ModItem Mod(int id, string name, int popularity = 0, bool installed = false, string type = "full") =>
        new()
        {
            Id = id,
            Name = name,
            Popularity30d = popularity,
            InstallPath = installed ? $@"C:\Mods\{name}" : null,
            ModType = type
        };

    private static List<int> Order(IEnumerable<ModItem> items, ModSortOption option) =>
        MainWindowViewModel.SortCatalogSnapshot(items, Array.Empty<ModItem>(), option)
            .Select(m => m.Id).ToList();

    private static ModItem[] MixedCatalog() =>
    [
        Mod(0, "AmongUs", type: "Vanilla", installed: true),
        Mod(1, "Alpha", popularity: 10, installed: true),
        Mod(2, "Beta", popularity: 50, installed: true),
        Mod(3, "Gamma", popularity: 99),
        Mod(4, "Delta", popularity: 5)
    ];

    [Fact]
    public void PopularityDesc_SortsWithinGroups_VanillaFirst() =>
        Assert.Equal(new[] { 0, 2, 1, 3, 4 }, Order(MixedCatalog(), ModSortOption.PopularityDesc));

    [Fact]
    public void PopularityAsc_SortsWithinGroups_VanillaFirst() =>
        Assert.Equal(new[] { 0, 1, 2, 4, 3 }, Order(MixedCatalog(), ModSortOption.PopularityAsc));

    [Fact]
    public void NameSorts_IgnorePopularity()
    {
        var items = new[]
        {
            Mod(1, "Zeta", popularity: 100, installed: true),
            Mod(2, "Alpha", popularity: 1, installed: true),
            Mod(3, "Beta", popularity: 7),
            Mod(4, "gamma", popularity: 3)
        };
        Assert.Equal(new[] { 2, 1, 3, 4 }, Order(items, ModSortOption.NameAsc));
        Assert.Equal(new[] { 1, 2, 4, 3 }, Order(items, ModSortOption.NameDesc));
    }

    [Theory]
    [InlineData(ModSortOption.PopularityDesc)]
    [InlineData(ModSortOption.PopularityAsc)]
    public void PopularityTie_FallsBackToNameAsc(ModSortOption option)
    {
        var items = new[]
        {
            Mod(1, "Zeta", popularity: 10, installed: true),
            Mod(2, "Alpha", popularity: 10, installed: true)
        };
        Assert.Equal(new[] { 2, 1 }, Order(items, option));
    }

    [Theory]
    [InlineData(ModSortOption.NameAsc)]
    [InlineData(ModSortOption.NameDesc)]
    [InlineData(ModSortOption.PopularityDesc)]
    public void NameTie_FallsBackToId(ModSortOption option)
    {
        var items = new[]
        {
            Mod(7, "Same", popularity: 10),
            Mod(3, "same", popularity: 10)
        };
        Assert.Equal(new[] { 3, 7 }, Order(items, option));
    }

    [Fact]
    public void NameSort_IsCaseInsensitive()
    {
        var items = new[] { Mod(1, "beta"), Mod(2, "Alpha") };
        Assert.Equal(new[] { 2, 1 }, Order(items, ModSortOption.NameAsc));
    }

    [Theory]
    [InlineData(ModSortOption.PopularityDesc)]
    [InlineData(ModSortOption.PopularityAsc)]
    public void AllZeroPopularity_FallsBackToNameAsc(ModSortOption option)
    {
        var items = new[]
        {
            Mod(1, "Zeta", installed: true),
            Mod(2, "Alpha", installed: true),
            Mod(3, "Beta")
        };
        Assert.Equal(new[] { 2, 1, 3 }, Order(items, option));
    }

    [Fact]
    public void PartialPopularityData_StillSortsByPopularity()
    {
        var items = new[]
        {
            Mod(1, "Alpha", popularity: 0, installed: true),
            Mod(2, "Beta", popularity: 5, installed: true)
        };
        Assert.Equal(new[] { 2, 1 }, Order(items, ModSortOption.PopularityDesc));
    }

    [Fact]
    public void VanillaPinnedFirst_EvenWhenNotInstalled()
    {
        var items = new[]
        {
            Mod(1, "Alpha", popularity: 100, installed: true),
            Mod(0, "AmongUs", type: "Vanilla"),
            Mod(2, "Beta", popularity: 50)
        };
        Assert.Equal(new[] { 0, 1, 2 }, Order(items, ModSortOption.PopularityDesc));
        Assert.Equal(new[] { 0, 1, 2 }, Order(items, ModSortOption.NameAsc));
    }

    [Fact]
    public void InstalledGroupPrecedesNotInstalled_RegardlessOfPopularity()
    {
        var items = new[]
        {
            Mod(1, "Popular", popularity: 1000),
            Mod(2, "Installed", popularity: 1, installed: true)
        };
        Assert.Equal(new[] { 2, 1 }, Order(items, ModSortOption.PopularityDesc));
    }

    [Fact]
    public void DefaultOption_IsPopularityDesc()
    {
        var items = MixedCatalog();
        var viaDefault = MainWindowViewModel.SortCatalogSnapshot(items, Array.Empty<ModItem>())
            .Select(m => m.Id).ToList();
        Assert.Equal(Order(items, ModSortOption.PopularityDesc), viaDefault);
    }

    [Fact]
    public void SortingNeverLosesItems()
    {
        var items = MixedCatalog();
        foreach (var option in Enum.GetValues<ModSortOption>())
            Assert.Equal(items.Length, Order(items, option).Count);
    }
}

/// <summary>K2: separator „Niezainstalowane" — znacznik na pierwszym wierszu drugiej grupy.</summary>
public class NotInstalledSeparatorTests
{
    private static ModItem Mod(int id, string name, bool installed = false, string type = "full") =>
        new()
        {
            Id = id,
            Name = name,
            InstallPath = installed ? $@"C:\Mods\{name}" : null,
            ModType = type
        };

    [Fact]
    public void BothGroups_MarkFirstNotInstalledOnly()
    {
        var vanilla = Mod(0, "AmongUs", installed: true, type: "Vanilla");
        var installed = Mod(1, "Alpha", installed: true);
        var notA = Mod(2, "Beta");
        var notB = Mod(3, "Gamma");
        CatalogBrowserProjection.MarkNotInstalledSeparator(new[] { vanilla, installed, notA, notB });
        Assert.False(vanilla.ShowNotInstalledSeparator);
        Assert.False(installed.ShowNotInstalledSeparator);
        Assert.True(notA.ShowNotInstalledSeparator);
        Assert.False(notB.ShowNotInstalledSeparator);
    }

    [Fact]
    public void OnlyInstalled_NoSeparator()
    {
        var items = new[] { Mod(0, "AmongUs", installed: true, type: "Vanilla"), Mod(1, "Alpha", installed: true) };
        CatalogBrowserProjection.MarkNotInstalledSeparator(items);
        Assert.All(items, m => Assert.False(m.ShowNotInstalledSeparator));
    }

    [Fact]
    public void OnlyNotInstalled_NoSeparator()
    {
        var items = new[] { Mod(1, "Alpha"), Mod(2, "Beta") };
        CatalogBrowserProjection.MarkNotInstalledSeparator(items);
        Assert.All(items, m => Assert.False(m.ShowNotInstalledSeparator));
    }

    [Fact]
    public void NotInstalledVanilla_StillCountsAsFirstGroup()
    {
        var vanilla = Mod(0, "AmongUs", type: "Vanilla");
        var notA = Mod(1, "Alpha");
        CatalogBrowserProjection.MarkNotInstalledSeparator(new[] { vanilla, notA });
        Assert.False(vanilla.ShowNotInstalledSeparator);
        Assert.True(notA.ShowNotInstalledSeparator);
    }

    [Fact]
    public void SearchFilter_HidesSeparatorWhenOneGroupEmpty()
    {
        var installed = Mod(1, "Alpha", installed: true);
        var notA = Mod(2, "Beta");
        var notB = Mod(3, "Gamma");
        var source = new[] { installed, notA, notB };
        var visible = new List<ModItem>(source);

        // Fraza pasuje tylko do niezainstalowanych → brak grupy pierwszej → brak separatora.
        MainWindowViewModel.ApplyFilterToCollection(visible, source, "a",
            (m, q) => m.Name.Contains(q!, StringComparison.OrdinalIgnoreCase) && !m.IsInstalled);
        CatalogBrowserProjection.MarkNotInstalledSeparator(visible);
        Assert.All(visible, m => Assert.False(m.ShowNotInstalledSeparator));

        // Fraza pasuje do obu grup → separator na pierwszym niezainstalowanym wyniku.
        MainWindowViewModel.ApplyFilterToCollection(visible, source, "a",
            (m, q) => m.Name.Contains(q!, StringComparison.OrdinalIgnoreCase));
        CatalogBrowserProjection.MarkNotInstalledSeparator(visible);
        Assert.True(visible.First(m => !m.IsInstalled).ShowNotInstalledSeparator);
        Assert.Equal(1, visible.Count(m => m.ShowNotInstalledSeparator));
    }

    [Fact]
    public void FavoritesFilter_HidesSeparatorWhenOneGroupEmpty()
    {
        var installed = Mod(1, "Alpha", installed: true);
        var notA = Mod(2, "Beta");
        notA.IsFavorite = true;
        var source = new[] { installed, notA };
        var visible = new List<ModItem>(source);
        MainWindowViewModel.ApplyFilterToCollection(visible, source, "",
            (m, _) => m.IsFavorite, forcePredicate: true);
        CatalogBrowserProjection.MarkNotInstalledSeparator(visible);
        Assert.All(visible, m => Assert.False(m.ShowNotInstalledSeparator));
    }

    [Fact]
    public void RemarkClearsStaleFlags_WhenInstallStateChanges()
    {
        var installed = Mod(1, "Alpha", installed: true);
        var notA = Mod(2, "Beta");
        var items = new[] { installed, notA };
        CatalogBrowserProjection.MarkNotInstalledSeparator(items);
        Assert.True(notA.ShowNotInstalledSeparator);

        notA.InstallPath = @"C:\Mods\Beta";
        CatalogBrowserProjection.MarkNotInstalledSeparator(items);
        Assert.All(items, m => Assert.False(m.ShowNotInstalledSeparator));
    }
}

/// <summary>K2: odczyt ustawienia sortowania (nieznana wartość = domyślna) i domyślna wartość ustawienia.</summary>
public class ModSortOptionParsingTests
{
    [Theory]
    [InlineData(null, ModSortOption.PopularityDesc)]
    [InlineData("", ModSortOption.PopularityDesc)]
    [InlineData("bogus", ModSortOption.PopularityDesc)]
    [InlineData("PopularityDesc", ModSortOption.PopularityDesc)]
    [InlineData("PopularityAsc", ModSortOption.PopularityAsc)]
    [InlineData("NameAsc", ModSortOption.NameAsc)]
    [InlineData("nameasc", ModSortOption.NameAsc)]
    [InlineData("NameDesc", ModSortOption.NameDesc)]
    public void ParseSortOption_FallsBackToDefault(string? value, ModSortOption expected) =>
        Assert.Equal(expected, CatalogBrowserProjection.ParseSortOption(value));

    [Fact]
    public void UserSettingsDefault_IsPopularityDesc() =>
        Assert.Equal("PopularityDesc", new UserSettings().ModListSort);
}

/// <summary>
/// K2 (przycisk ikonowy): tooltip/nazwa automatyczna = „Sortowanie: aktywna opcja" w obu językach
/// oraz kontrakt wiązań menu (Label/IsSelected powiadamiają o zmianie). MainWindowViewModel nie da się
/// tu skonstruować (DI + bootstrap Avalonii) — lustro kontraktu SortButtonLabel/RefreshBrowserSortLocalization
/// na tych samych seamach, wzorzec SortChangeSelectionTests.
/// </summary>
public class SortButtonPresentationTests
{
    /// <summary>Lustro InitializeBrowserSort: opcje z etykietami UI.Browser.Sort.*.</summary>
    private static List<ModSortOptionItem> Options(LocalizationService loc) =>
        Enum.GetValues<ModSortOption>()
            .Select(o => new ModSortOptionItem(o, loc.Get($"UI.Browser.Sort.{o}")))
            .ToList();

    /// <summary>Lustro SortButtonLabel.</summary>
    private static string ButtonLabel(LocalizationService loc, ModSortOptionItem? selected) =>
        loc.GetFormatted("UI.Browser.Sort.ButtonLabel", selected?.Label ?? string.Empty);

    [Fact]
    public void ButtonLabel_ReflectsActiveOption_AndLanguageChange()
    {
        var loc = new LocalizationService();
        var options = Options(loc);
        Assert.Equal("Sortowanie: Popularne (najpierw najpopularniejsze)", ButtonLabel(loc, options[0]));
        Assert.Equal("Sortowanie: Nazwa (Z–A)", ButtonLabel(loc, options[3]));
        Assert.Equal("Sortowanie: ", ButtonLabel(loc, null));

        // Handler zmiany języka odświeża etykiety opcji, potem etykietę przycisku.
        loc.ChangeCulture("en");
        foreach (var option in options)
            option.Label = loc.Get($"UI.Browser.Sort.{option.Option}");
        Assert.Equal("Sort: Popular (most first)", ButtonLabel(loc, options[0]));
        Assert.Equal("Sort: Name (Z–A)", ButtonLabel(loc, options[3]));
    }

    [Fact]
    public void ButtonLabel_AllOptions_FormattedWithoutPlaceholderInBothLocales()
    {
        var loc = new LocalizationService();
        foreach (var culture in new[] { "pl", "en" })
        {
            loc.ChangeCulture(culture);
            foreach (var option in Options(loc))
            {
                var label = ButtonLabel(loc, option);
                Assert.DoesNotContain("{0}", label);
                Assert.EndsWith(option.Label, label);
            }
        }
    }

    [Fact]
    public void OptionItem_LabelAndIsSelected_RaisePropertyChanged()
    {
        var item = new ModSortOptionItem(ModSortOption.NameAsc, "Nazwa (A–Z)");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Label = "Name (A–Z)";
        item.IsSelected = true;

        // Bez tych powiadomień nagłówki menu i znacznik aktywnej opcji nie odświeżyłyby się.
        Assert.Equal(new[] { nameof(ModSortOptionItem.Label), nameof(ModSortOptionItem.IsSelected) }, changed);
    }
}