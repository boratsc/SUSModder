using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    /// <summary>K2: opcje sortowania katalogu modów (wartość zapisywana w ustawieniach jako nazwa).</summary>
    public enum ModSortOption
    {
        PopularityDesc,
        PopularityAsc,
        NameAsc,
        NameDesc
    }

    /// <summary>K2: opcja sortowania w menu przycisku sortowania — Option to wartość, Label to tekst
    /// tłumaczony, IsSelected steruje znacznikiem aktywnej opcji (MenuItem.IsChecked).</summary>
    public sealed class ModSortOptionItem : ReactiveObject
    {
        private string _label;
        private bool _isSelected;

        public ModSortOptionItem(ModSortOption option, string label)
        {
            Option = option;
            _label = label;
        }

        public ModSortOption Option { get; }
        public string Label
        {
            get => _label;
            set => this.RaiseAndSetIfChanged(ref _label, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
    }

    /// <summary>K2: klucz sortowania wiersza katalogu (wspólny dla ModConfiguration i ModItem).</summary>
    public readonly record struct CatalogSortKey(bool IsVanilla, bool IsInstalled, int Popularity, string Name, int Id);

    /// <summary>
    /// K2: czyste funkcje porządkowania katalogu — Vanilla na początku, potem zainstalowane,
    /// separator „Niezainstalowane", wybrana opcja sortowania wewnątrz grup. Bez Avalonii i DI,
    /// testowalne jak RolesBrowserProjection.
    /// </summary>
    public static class CatalogBrowserProjection
    {
        /// <summary>Nieznana/pusta wartość ustawienia = domyślne (Popularne, malejąco).</summary>
        public static ModSortOption ParseSortOption(string? value) =>
            Enum.TryParse<ModSortOption>(value, ignoreCase: true, out var option)
                ? option
                : ModSortOption.PopularityDesc;

        /// <summary>
        /// Porządkuje wpisy: Vanilla → zainstalowane → niezainstalowane; w grupach wybrana opcja.
        /// Remis popularności → nazwa A–Z; remis nazwy → Id. Gdy wszystkie popularności = 0
        /// (backend jeszcze nie wysyła danych), opcje popularności sortują jak nazwa A–Z.
        /// </summary>
        public static List<T> OrderCatalog<T>(IEnumerable<T> source, ModSortOption option, Func<T, CatalogSortKey> keySelector)
        {
            var entries = source.Select(item => (Item: item, Key: keySelector(item))).ToList();
            bool popularityAvailable = entries.Any(e => e.Key.Popularity > 0);
            var comparer = Comparer<(T Item, CatalogSortKey Key)>.Create((x, y) =>
            {
                int groupX = Group(x.Key), groupY = Group(y.Key);
                return groupX != groupY
                    ? groupX.CompareTo(groupY)
                    : CompareWithinGroup(x.Key, y.Key, option, popularityAvailable);
            });
            return entries.Order(comparer).Select(e => e.Item).ToList();
        }

        /// <summary>
        /// Oznacza pierwszy niezainstalowany wiersz nagłówkiem „Niezainstalowane" — tylko gdy
        /// istnieje też grupa pierwsza (Vanilla lub zainstalowane). Ukrywa znacznik w przeciwnym razie.
        /// </summary>
        public static void MarkNotInstalledSeparator(IEnumerable<ModItem> ordered)
        {
            bool hasFirstGroup = false;
            bool separatorPlaced = false;
            ModItem? previous = null;
            foreach (var item in ordered)
            {
                item.ShowRowSeparator = true;
                if (item.IsVanilla || item.IsInstalled)
                {
                    hasFirstGroup = true;
                    item.ShowNotInstalledSeparator = false;
                }
                else
                {
                    item.ShowNotInstalledSeparator = hasFirstGroup && !separatorPlaced;
                    separatorPlaced |= item.ShowNotInstalledSeparator;
                    // Nagłówek ma własną linię — schowaj dolną linię poprzedniego wiersza (podwójna linia).
                    if (item.ShowNotInstalledSeparator && previous is not null)
                        previous.ShowRowSeparator = false;
                }
                previous = item;
            }
        }

        private static int Group(CatalogSortKey key) => key.IsVanilla ? 0 : key.IsInstalled ? 1 : 2;

        private static int CompareWithinGroup(CatalogSortKey a, CatalogSortKey b, ModSortOption option, bool popularityAvailable)
        {
            if (popularityAvailable && option is ModSortOption.PopularityDesc or ModSortOption.PopularityAsc)
            {
                int popularity = option == ModSortOption.PopularityDesc
                    ? b.Popularity.CompareTo(a.Popularity)
                    : a.Popularity.CompareTo(b.Popularity);
                if (popularity != 0)
                    return popularity;
            }

            int name = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
            if (option == ModSortOption.NameDesc)
                name = -name;
            return name != 0 ? name : a.Id.CompareTo(b.Id);
        }
    }
}