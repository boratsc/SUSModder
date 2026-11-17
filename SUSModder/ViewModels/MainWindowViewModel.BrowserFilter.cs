using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        private string _browserSearchText = string.Empty;
        private bool _browserFavoritesOnly;

        /// <summary>Czyści wyszukiwanie i filtr ulubionych (stan pusty katalogu, makieta 13).</summary>
        public ReactiveCommand<Unit, Unit> ClearBrowserFiltersCommand { get; private set; } = null!;

        public bool BrowserFavoritesOnly
        {
            get => _browserFavoritesOnly;
            set
            {
                if (_browserFavoritesOnly == value) return;
                this.RaiseAndSetIfChanged(ref _browserFavoritesOnly, value);
                ApplyBrowserSearchFilter();
            }
        }
        private List<ModItem> _catalogModsSnapshot = new();
        private List<ModInstanceItem> _packInstancesSnapshot = new();
        private List<ModItem> _dllModsSnapshot = new();

        /// <summary>
        /// E13: szybki dostęp w sidebarze — pełna lista własnych zestawów, niezależna
        /// od filtra wyszukiwania (PackInstances bywa przefiltrowane i zależy od zakładki).
        /// </summary>
        public IReadOnlyList<ModInstanceItem> SidebarPackInstances => _packInstancesSnapshot;

        public string BrowserSearchText
        {
            get => _browserSearchText;
            set
            {
                if (_browserSearchText == value)
                    return;

                this.RaiseAndSetIfChanged(ref _browserSearchText, value);
                ApplyBrowserSearchFilter();
            }
        }

        public bool ShowBrowserNoResults =>
            !IsModsLoading &&
            (!string.IsNullOrWhiteSpace(BrowserSearchText) || BrowserFavoritesOnly) &&
            ((IsCatalogTab && Mods.Count == 0 && _catalogModsSnapshot.Count > 0) ||
             (IsMyPacksTab && PackInstances.Count == 0 && _packInstancesSnapshot.Count > 0) ||
             (IsDllAddonsTab && DllMods.Count == 0 && _dllModsSnapshot.Count > 0));

        private void InitializeBrowserFilter()
        {
            ClearBrowserFiltersCommand = ReactiveCommand.Create(() =>
            {
                BrowserSearchText = string.Empty;
                BrowserFavoritesOnly = false;
            });
            InitializeIconAssets();
            InitializeBrowserSort();
            _localizationService.PropertyChanged += OnLibraryLanguageChanged;
            InitializeStorageOverview();
        }

        internal void CaptureCatalogModsSnapshot()
        {
            _catalogModsSnapshot = Mods.ToList();
            ApplyFavoriteStates(_catalogModsSnapshot);
            ApplyCurrentIconKeys();
            _ = ReconcileLegacyIconReferencesAsync();
            ApplyBrowserSearchFilter();
            // E14: ikony/nazwy modów z rolami pochodzą z katalogu — odśwież po jego załadowaniu.
            if (_rolesLoaded)
                RebuildRoleMods();
        }

        internal void CapturePackInstancesSnapshot()
        {
            _packInstancesSnapshot = PackInstances.ToList();
            this.RaisePropertyChanged(nameof(SidebarPackInstances));
            ApplyCurrentIconKeys();
            ApplyBrowserSearchFilter();
        }

        internal void CaptureDllModsSnapshot()
        {
            _dllModsSnapshot = DllMods.ToList();
            ApplyFavoriteStates(_dllModsSnapshot);
            ApplyCurrentIconKeys();
            ApplyBrowserSearchFilter();
        }

        private void ApplyBrowserSearchFilter()
        {
            var query = BrowserSearchText?.Trim();

            if (IsCatalogTab)
            {
                ApplyFilterToCollection(Mods, _catalogModsSnapshot, query,
                    (mod, text) => (!BrowserFavoritesOnly || mod.IsFavorite) && MatchesCatalogMod(mod, text), BrowserFavoritesOnly);
                // K2: separator „Niezainstalowane" nad pierwszym wierszem drugiej grupy (także przy filtrach).
                CatalogBrowserProjection.MarkNotInstalledSeparator(Mods);
            }
            else if (IsMyPacksTab)
                ApplyFilterToCollection(PackInstances, _packInstancesSnapshot, query, MatchesPackInstance);
            else if (IsDllAddonsTab)
                ApplyFilterToCollection(DllMods, _dllModsSnapshot, query,
                    (mod, text) => (!BrowserFavoritesOnly || mod.IsFavorite) && MatchesDllMod(mod, text), BrowserFavoritesOnly);
            else if (IsRolesTab)
                ApplyRolesFilter();

            ClearFilteredBrowserSelection();

            this.RaisePropertyChanged(nameof(ShowBrowserNoResults));
            this.RaisePropertyChanged(nameof(IsBrowserGridVisible));
            this.RaisePropertyChanged(nameof(IsPackInstancesGridVisible));
            this.RaisePropertyChanged(nameof(IsDllAddonsGridVisible));
            this.RaisePropertyChanged(nameof(ShowDllAddonsEmptyState));
        }

        internal static void ApplyFilterToCollection<T>(
            ICollection<T> target,
            IReadOnlyList<T> source,
            string? query,
            Func<T, string?, bool> matches,
            bool forcePredicate = false)
        {
            var effectiveSource = source;
            if (effectiveSource.Count == 0 && target.Count > 0 && string.IsNullOrWhiteSpace(query))
            {
                // Snapshot nie został jeszcze zapisany (np. pierwsze odświeżenie listy) — nie czyść UI.
                effectiveSource = target.ToList();
            }

            var filtered = string.IsNullOrWhiteSpace(query) && !forcePredicate
                ? effectiveSource
                : effectiveSource.Where(item => matches(item, query)).ToList();

            if (filtered.Count == target.Count && target.Count > 0)
            {
                var unchanged = true;
                using var enumerator = target.GetEnumerator();
                foreach (var item in filtered)
                {
                    if (!enumerator.MoveNext() || !ReferenceEquals(enumerator.Current, item))
                    {
                        unchanged = false;
                        break;
                    }
                }

                if (unchanged)
                    return;
            }

            target.Clear();
            foreach (var item in filtered)
                target.Add(item);
        }

        private static bool MatchesCatalogMod(ModItem mod, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return Contains(mod.Name, query)
                   || Contains(mod.Description, query)
                   || Contains(mod.ModVersion, query)
                   || Contains(mod.AmongVersion, query)
                   || Contains(mod.ModType, query);
        }

        private static bool MatchesPackInstance(ModInstanceItem pack, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return Contains(pack.DisplayName, query)
                   || Contains(pack.BaseModName, query)
                   || Contains(pack.FullModVersion, query)
                   || Contains(pack.Subtitle, query)
                   || Contains(pack.ContentsSummary, query);
        }

        private static bool MatchesDllMod(ModItem dll, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return Contains(dll.Name, query)
                   || Contains(dll.Description, query)
                   || Contains(dll.ModVersion, query)
                   || Contains(dll.InstalledInSummary, query);
        }

        private static bool Contains(string? value, string query) =>
            !string.IsNullOrEmpty(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
