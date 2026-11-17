using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Regresja: zmiana opcji sortowania nie może gubić zaznaczonego moda (panel szczegółów
/// ma zostać otwarty). MainWindowViewModel nie da się tu skonstruować (konstruktor wymaga
/// DI z App.GetService i bootstrapu Avalonii — projekt testowy świadomie tego nie inicjuje),
/// więc testujemy kontrakt obserwowalny, który chroni flaga _suppressSelectedModPanelReset
/// i przywrócenie zaznaczenia po Id: po przesorowaniu snapshotu i Clear+re-add na Mods
/// (co w Avalonii zeruje ListBox.SelectedItem) ten sam egzemplarz moda nadal daje się
/// odzyskać przez Mods.FirstOrDefault(m => m.Id == selected.Id) — dokładnie tym wyrażeniem
/// posługuje się wspólny helper ResortCatalogKeepSelection.
/// </summary>
public class SortChangeSelectionTests
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

    private static List<ModItem> Catalog() =>
    [
        Mod(0, "AmongUs", type: "Vanilla", installed: true),
        Mod(1, "Alpha", popularity: 10, installed: true),
        Mod(2, "Zeta", popularity: 99, installed: true),
        Mod(3, "Beta", popularity: 50)
    ];

    /// <summary>Lustro kroków ResortCatalogKeepSelection na tych samych wewnętrznych seamach.</summary>
    private static (List<ModItem> Snapshot, ModItem? Restored) ResortLikeVm(
        List<ModItem> snapshot, ObservableCollection<ModItem> mods, ModSortOption option,
        ModItem? selected, string? query = null)
    {
        var sorted = MainWindowViewModel.SortCatalogSnapshot(snapshot, mods, option);
        MainWindowViewModel.ApplyFilterToCollection(mods, sorted, query,
            (m, q) => string.IsNullOrEmpty(q) || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        CatalogBrowserProjection.MarkNotInstalledSeparator(mods);
        return (sorted, selected == null ? null : mods.FirstOrDefault(m => m.Id == selected.Id));
    }

    [Fact]
    public void SortChange_ReordersList_SelectedModRestoredAsSameInstance()
    {
        var items = Catalog();
        // Stan wyjściowy VM: Mods posortowane aktualną opcją (PopularityDesc).
        var initial = MainWindowViewModel.SortCatalogSnapshot(items, Array.Empty<ModItem>());
        var mods = new ObservableCollection<ModItem>(initial);
        var selected = mods.First(m => m.Id == 2); // Zeta: indeks 1

        var (snapshot, restored) = ResortLikeVm(initial, mods, ModSortOption.NameAsc, selected);

        // Kolejność realnie się zmieniła → ApplyFilterToCollection zrobił Clear+re-add
        // (w Avalonii ListBox.SelectedItem chwilowo null — bez guardu panel by się zamknął).
        Assert.Equal(new[] { 0, 2, 1, 3 }, initial.Select(m => m.Id));
        Assert.Equal(new[] { 0, 1, 2, 3 }, mods.Select(m => m.Id));
        Assert.Same(selected, restored);
        Assert.Equal(4, snapshot.Count); // snapshot nietknięty przez filtr/projekcję
    }

    [Fact]
    public void SortChange_WithActiveSearch_RestoresSameInstanceAndKeepsFullSnapshot()
    {
        var items = Catalog();
        var mods = new ObservableCollection<ModItem>(items);
        var selected = mods.First(m => m.Id == 2); // Zeta pasuje do "et"

        var (snapshot, restored) = ResortLikeVm(items.ToList(), mods, ModSortOption.NameAsc, selected, query: "et");

        Assert.Equal(new[] { 2, 3 }, mods.Select(m => m.Id));
        Assert.Same(selected, restored);
        Assert.Equal(4, snapshot.Count); // pełny snapshot, nie podzbiór z filtra
    }

    [Fact]
    public void SortChange_SelectedModFilteredOut_RestoreYieldsNull()
    {
        var items = Catalog();
        var mods = new ObservableCollection<ModItem>(items);
        var selected = mods.First(m => m.Id == 1); // Alpha nie pasuje do "et"

        var (_, restored) = ResortLikeVm(items.ToList(), mods, ModSortOption.PopularityDesc, selected, query: "et");

        // null → ClearFilteredBrowserSelection czyści zaznaczenie (mod naprawdę zniknął z listy).
        Assert.Null(restored);
        Assert.Equal(new[] { 2, 3 }, mods.Select(m => m.Id));
    }

    [Fact]
    public void SortChange_WithoutSelection_ResortsAllItems()
    {
        var items = Catalog();
        var mods = new ObservableCollection<ModItem>(items);

        var (snapshot, restored) = ResortLikeVm(items.ToList(), mods, ModSortOption.NameAsc, selected: null);

        Assert.Null(restored);
        Assert.Equal(new[] { 0, 1, 2, 3 }, mods.Select(m => m.Id));
        Assert.Equal(4, snapshot.Count);
    }
}
