using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        private string _browserSearchText = string.Empty;
        private List<ModItem> _catalogModsSnapshot = new();
        private List<ModInstanceItem> _packInstancesSnapshot = new();
        private List<ModItem> _dllModsSnapshot = new();

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
            !string.IsNullOrWhiteSpace(BrowserSearchText) &&
            ((IsCatalogTab && Mods.Count == 0 && _catalogModsSnapshot.Count > 0) ||
             (IsMyPacksTab && PackInstances.Count == 0 && _packInstancesSnapshot.Count > 0) ||
             (IsDllAddonsTab && DllMods.Count == 0 && _dllModsSnapshot.Count > 0));

        private void InitializeBrowserFilter()
        {
        }

        internal void CaptureCatalogModsSnapshot()
        {
            _catalogModsSnapshot = Mods.ToList();
            ApplyBrowserSearchFilter();
        }

        internal void CapturePackInstancesSnapshot()
        {
            _packInstancesSnapshot = PackInstances.ToList();
            ApplyBrowserSearchFilter();
        }

        internal void CaptureDllModsSnapshot()
        {
            _dllModsSnapshot = DllMods.ToList();
            ApplyBrowserSearchFilter();
        }

        private void ApplyBrowserSearchFilter()
        {
            var query = BrowserSearchText?.Trim();

            if (IsCatalogTab)
                ApplyFilterToCollection(Mods, _catalogModsSnapshot, query, MatchesCatalogMod);
            else if (IsMyPacksTab)
                ApplyFilterToCollection(PackInstances, _packInstancesSnapshot, query, MatchesPackInstance);
            else if (IsDllAddonsTab)
                ApplyFilterToCollection(DllMods, _dllModsSnapshot, query, MatchesDllMod);

            this.RaisePropertyChanged(nameof(ShowBrowserNoResults));
            this.RaisePropertyChanged(nameof(IsBrowserGridVisible));
            this.RaisePropertyChanged(nameof(IsPackInstancesGridVisible));
            this.RaisePropertyChanged(nameof(IsDllAddonsGridVisible));
        }

        private static void ApplyFilterToCollection<T>(
            ICollection<T> target,
            IReadOnlyList<T> source,
            string? query,
            Func<T, string?, bool> matches)
        {
            var effectiveSource = source;
            if (effectiveSource.Count == 0 && target.Count > 0 && string.IsNullOrWhiteSpace(query))
            {
                // Snapshot nie został jeszcze zapisany (np. pierwsze odświeżenie listy) — nie czyść UI.
                effectiveSource = target.ToList();
            }

            var filtered = string.IsNullOrWhiteSpace(query)
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
