using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Api;
using SUSModder.Models;
using SUSModder.Services;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// E14: zakładka Role — panel w głównym oknie (zamiast osobnego RolesWindow).
    /// Poziom A: mody mające role (wszystkie z danych API, nie tylko zainstalowane);
    /// poziom B: role wybranego moda; prawa kolumna: szczegóły roli (read-only).
    /// Dane z RolesService (cache współdzielony z checkiem HasRoles); grupowanie i filtry w VM.
    /// Stan zakładki (mod/rola/filtry/search) w sesji przez BrowserSessionState — bez SQLite.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private readonly RolesService _rolesService = new();
        private List<Role> _allRoles = new();
        private List<RoleModGroup> _roleModsSnapshot = new();
        private List<RoleListItem> _selectedModRoleItems = new();
        private bool _rolesLoaded;
        private bool _isRolesLoading;
        private bool _isRolesLoadError;
        private int? _selectedRoleModId;
        private RoleListItem? _selectedRoleItem;
        private int? _pendingRoleModPreselect;
        private int _rolesLoadGeneration;
        private RoleFilterOption? _selectedRoleCategoryOption;
        private RoleFilterOption? _selectedRoleTypeOption;

        public ObservableCollection<RoleModGroup> RoleMods { get; } = new();
        public ObservableCollection<RoleListItem> ModRoles { get; } = new();
        public ObservableCollection<RoleFilterOption> RoleCategoryOptions { get; } = new();
        public ObservableCollection<RoleFilterOption> RoleTypeOptions { get; } = new();

        public bool IsRolesTab => ActiveBrowserTab == ModBrowserTab.Roles;

        public bool IsRolesLoading
        {
            get => _isRolesLoading;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isRolesLoading, value);
                RaiseRolesStateProperties();
            }
        }

        public bool IsRolesLoadError
        {
            get => _isRolesLoadError;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isRolesLoadError, value);
                RaiseRolesStateProperties();
            }
        }

        /// <summary>null = poziom A (lista modów); wartość = poziom B (role tego moda).</summary>
        public int? SelectedRoleModId
        {
            get => _selectedRoleModId;
            private set
            {
                if (_selectedRoleModId == value)
                    return;
                this.RaiseAndSetIfChanged(ref _selectedRoleModId, value);
                RaiseRolesStateProperties();
            }
        }

        public RoleListItem? SelectedRoleItem
        {
            get => _selectedRoleItem;
            set
            {
                if (ReferenceEquals(_selectedRoleItem, value))
                    return;
                this.RaiseAndSetIfChanged(ref _selectedRoleItem, value);
                this.RaisePropertyChanged(nameof(IsRolePanelVisible));
                this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));
            }
        }

        public bool IsRolePanelVisible => IsRolesTab && SelectedRoleItem != null && !IsAnyToolModalOpen;

        public string SelectedRoleModName =>
            _selectedRoleModId is int id
                ? _roleModsSnapshot.FirstOrDefault(g => g.ModId == id)?.Name ?? string.Empty
                : string.Empty;

        /// <summary>Podtytuł poziomu B: liczba ról (z pluralizacją) albo „X z Y” przy aktywnym filtrze.</summary>
        public string RolesLevelBSubtitle
        {
            get
            {
                var total = _selectedModRoleItems.Count;
                var shown = ModRoles.Count;
                return shown == total
                    ? _localizationService.GetFormatted(RolesBrowserProjection.RoleCountKey(total), total)
                    : _localizationService.GetFormatted("UI.Roles.ModRoleCount", shown, total);
            }
        }

        public int RoleModsCount => _roleModsSnapshot.Count;
        public bool HasRoleMods => _roleModsSnapshot.Count > 0;
        public string RoleModsCountLabel =>
            _localizationService.GetFormatted("UI.Roles.SidebarCount", _roleModsSnapshot.Count);

        public bool CanShowRoleModInCatalog =>
            _selectedRoleModId is int id && _catalogModsSnapshot.Any(m => m.Id == id);

        public bool IsRoleModsLevelVisible =>
            IsRolesTab && !IsRolesLoading && !IsRolesLoadError && SelectedRoleModId == null;
        public bool IsRoleRolesLevelVisible =>
            IsRolesTab && !IsRolesLoading && !IsRolesLoadError && SelectedRoleModId != null;
        public bool IsRoleModsListVisible => IsRoleModsLevelVisible && RoleMods.Count > 0;
        public bool IsModRolesListVisible => IsRoleRolesLevelVisible && ModRoles.Count > 0;
        public bool ShowRoleModsEmptyState => IsRoleModsLevelVisible && _roleModsSnapshot.Count == 0;
        public bool ShowRoleModsNoResults =>
            IsRoleModsLevelVisible && _roleModsSnapshot.Count > 0 && RoleMods.Count == 0;
        public bool ShowModRolesEmptyState => IsRoleRolesLevelVisible && _selectedModRoleItems.Count == 0;
        public bool ShowModRolesNoResults =>
            IsRoleRolesLevelVisible && _selectedModRoleItems.Count > 0 && ModRoles.Count == 0;

        public RoleFilterOption? SelectedRoleCategoryOption
        {
            get => _selectedRoleCategoryOption;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedRoleCategoryOption, value);
                ApplyRolesFilter();
            }
        }

        public RoleFilterOption? SelectedRoleTypeOption
        {
            get => _selectedRoleTypeOption;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedRoleTypeOption, value);
                ApplyRolesFilter();
            }
        }

        public ReactiveCommand<RoleModGroup, Unit> SelectRoleModCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> BackToRoleModsCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> RetryRolesLoadCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ClearRoleFiltersCommand { get; private set; } = null!;
        /// <summary>E14: deep-link z drawera moda — zakładka Role z preselekcją moda (bez warunku instalacji).</summary>
        public ReactiveCommand<ModItem, Unit> ShowModRolesCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> ShowRoleModInCatalogCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> CloseRoleDetailCommand { get; private set; } = null!;

        private void InitializeRoles()
        {
            SelectRoleModCommand = ReactiveCommand.Create<RoleModGroup>(group => OpenRoleMod(group.ModId));
            BackToRoleModsCommand = ReactiveCommand.Create(BackToRoleMods);
            RetryRolesLoadCommand = ReactiveCommand.Create(() =>
            {
                RolesService.ClearCache();
                _rolesLoaded = false;
                _ = LoadRolesAsync();
            });
            ClearRoleFiltersCommand = ReactiveCommand.Create(() =>
            {
                BrowserSearchText = string.Empty;
                _selectedRoleCategoryOption = RoleCategoryOptions.FirstOrDefault();
                _selectedRoleTypeOption = RoleTypeOptions.FirstOrDefault();
                this.RaisePropertyChanged(nameof(SelectedRoleCategoryOption));
                this.RaisePropertyChanged(nameof(SelectedRoleTypeOption));
                ApplyRolesFilter();
            });
            ShowModRolesCommand = ReactiveCommand.Create<ModItem>(ShowModRoles);
            ShowRoleModInCatalogCommand = ReactiveCommand.Create(ShowRoleModInCatalog);
            CloseRoleDetailCommand = ReactiveCommand.Create(() => { SelectedRoleItem = null; });

            // Opcje filtrów: Value = dane API, Label = tłumaczenie (świeże po zmianie języka).
            RoleCategoryOptions.Add(new RoleFilterOption(RolesBrowserProjection.AllValue, _localizationService.Get("UI.Roles.AllCategories")));
            RoleCategoryOptions.Add(new RoleFilterOption("Crewmate", _localizationService.Get("UI.Roles.CategoryCrewmate")));
            RoleCategoryOptions.Add(new RoleFilterOption("Impostor", _localizationService.Get("UI.Roles.CategoryImpostor")));
            RoleCategoryOptions.Add(new RoleFilterOption("Neutral", _localizationService.Get("UI.Roles.CategoryNeutral")));
            RoleCategoryOptions.Add(new RoleFilterOption("Modifier", _localizationService.Get("UI.Roles.CategoryModifier")));
            RoleTypeOptions.Add(new RoleFilterOption(RolesBrowserProjection.AllValue, _localizationService.Get("UI.Roles.AllTypes")));
            RoleTypeOptions.Add(new RoleFilterOption("Role", _localizationService.Get("UI.Roles.TypeRole")));
            RoleTypeOptions.Add(new RoleFilterOption("Modifier", _localizationService.Get("UI.Roles.TypeModifier")));
            _selectedRoleCategoryOption = RoleCategoryOptions[0];
            _selectedRoleTypeOption = RoleTypeOptions[0];
        }

        /// <summary>Wywoływane z handlera zmiany języka: etykiety filtrów + liczniki.</summary>
        private void RefreshRolesLocalization()
        {
            if (RoleCategoryOptions.Count == 0)
                return;

            SetFilterLabel(RoleCategoryOptions, RolesBrowserProjection.AllValue, "UI.Roles.AllCategories");
            SetFilterLabel(RoleCategoryOptions, "Crewmate", "UI.Roles.CategoryCrewmate");
            SetFilterLabel(RoleCategoryOptions, "Impostor", "UI.Roles.CategoryImpostor");
            SetFilterLabel(RoleCategoryOptions, "Neutral", "UI.Roles.CategoryNeutral");
            SetFilterLabel(RoleCategoryOptions, "Modifier", "UI.Roles.CategoryModifier");
            SetFilterLabel(RoleTypeOptions, RolesBrowserProjection.AllValue, "UI.Roles.AllTypes");
            SetFilterLabel(RoleTypeOptions, "Role", "UI.Roles.TypeRole");
            SetFilterLabel(RoleTypeOptions, "Modifier", "UI.Roles.TypeModifier");

            if (_rolesLoaded)
                RebuildRoleMods();
            this.RaisePropertyChanged(nameof(RolesLevelBSubtitle));
            this.RaisePropertyChanged(nameof(RoleModsCountLabel));
        }

        private void SetFilterLabel(ObservableCollection<RoleFilterOption> options, string value, string key)
        {
            var option = options.FirstOrDefault(o => o.Value == value);
            if (option != null)
                option.Label = _localizationService.Get(key);
        }

        private void ActivateRolesTab()
        {
            IsPaneOpen = false;
            _ = EnsureRolesLoadedAsync();
            RaiseRolesStateProperties();
        }

        private async Task EnsureRolesLoadedAsync()
        {
            if (_rolesLoaded || _isRolesLoading)
                return;
            await LoadRolesAsync();
        }

        private async Task LoadRolesAsync()
        {
            var generation = System.Threading.Interlocked.Increment(ref _rolesLoadGeneration);
            IsRolesLoading = true;
            IsRolesLoadError = false;
            try
            {
                // configId nieistotny — serwis pobiera WSZYSTKIE role z /v2/roles (cache statyczny).
                var roles = await _rolesService.GetRolesAsync(0);
                if (generation != _rolesLoadGeneration)
                    return;

                _allRoles = roles;
                if (RolesBrowserProjection.IsLoadError(roles.Count, ApiStatus == ApiConnectionStatus.Offline))
                {
                    IsRolesLoadError = true;
                    return;
                }

                _rolesLoaded = true;
                RebuildRoleMods();
                ApplyPendingRoleModPreselect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Roles] Load error: {ex.Message}");
                if (generation == _rolesLoadGeneration)
                    IsRolesLoadError = true;
            }
            finally
            {
                if (generation == _rolesLoadGeneration)
                    IsRolesLoading = false;
            }
        }

        private void RebuildRoleMods()
        {
            _roleModsSnapshot = RolesBrowserProjection.BuildModGroups(
                _allRoles,
                _catalogModsSnapshot,
                count => _localizationService.GetFormatted(RolesBrowserProjection.RoleCountKey(count), count),
                count => _localizationService.GetFormatted("UI.Roles.RoleCountLabel", count)).ToList();
            ApplyRolesFilter();
        }

        /// <summary>Preselekcja deep-linku konsumowana po załadowaniu danych (albo od razu, gdy są).</summary>
        private void ApplyPendingRoleModPreselect()
        {
            if (_pendingRoleModPreselect is not int id)
                return;
            _pendingRoleModPreselect = null;
            if (!IsRolesTab)
                return;
            if (RolesBrowserProjection.ResolveModId(id, _roleModsSnapshot) is int modId)
                OpenRoleMod(modId);
            else
                ApplyRolesFilter();
        }

        private void ShowModRoles(ModItem? mod)
        {
            if (mod == null)
                return;

            _pendingRoleModPreselect = mod.Id;
            if (ActiveBrowserTab != ModBrowserTab.Roles)
            {
                ActiveBrowserTab = ModBrowserTab.Roles;
                return;
            }

            if (_rolesLoaded)
                ApplyPendingRoleModPreselect();
        }

        private void OpenRoleMod(int modId, bool clearSearch = true)
        {
            if (_roleModsSnapshot.All(g => g.ModId != modId))
                return;

            SelectedRoleItem = null;
            _selectedModRoleItems = _allRoles
                .Where(r => r.IsAssociatedWithMod(modId))
                .Select(r => new RoleListItem(r))
                .ToList();
            if (clearSearch)
            {
                _browserSearchText = string.Empty;
                this.RaisePropertyChanged(nameof(BrowserSearchText));
            }
            SelectedRoleModId = modId;
            ApplyRolesFilter();
        }

        private void BackToRoleMods()
        {
            SelectedRoleItem = null;
            _selectedModRoleItems = new List<RoleListItem>();
            _browserSearchText = string.Empty;
            this.RaisePropertyChanged(nameof(BrowserSearchText));
            SelectedRoleModId = null;
            ApplyRolesFilter();
        }

        private void ShowRoleModInCatalog()
        {
            if (_selectedRoleModId is not int id)
                return;
            if (_catalogModsSnapshot.All(m => m.Id != id))
                return;

            ActiveBrowserTab = ModBrowserTab.Catalog;
            // Filtr wyszukiwania mógł ukryć mod na liście — wtedy czyścimy wyszukiwanie.
            if (!Mods.Any(m => m.Id == id))
                BrowserSearchText = string.Empty;
            SelectedMod = Mods.FirstOrDefault(m => m.Id == id);
        }

        /// <summary>Filtry poziomu A (search po nazwie moda) i B (search + kategoria/typ po wartościach API).</summary>
        private void ApplyRolesFilter()
        {
            if (!IsRolesTab)
                return;

            var query = BrowserSearchText?.Trim();
            if (SelectedRoleModId == null)
            {
                ApplyFilterToCollection(RoleMods, _roleModsSnapshot, query, RolesBrowserProjection.MatchesRoleMod);
            }
            else
            {
                var category = SelectedRoleCategoryOption?.Value ?? RolesBrowserProjection.AllValue;
                var type = SelectedRoleTypeOption?.Value ?? RolesBrowserProjection.AllValue;
                var keep = SelectedRoleItem;
                ApplyFilterToCollection(ModRoles, _selectedModRoleItems, query,
                    (item, q) => RolesBrowserProjection.MatchesRole(item.Role, q, category, type),
                    forcePredicate: category != RolesBrowserProjection.AllValue || type != RolesBrowserProjection.AllValue);
                // Zachowaj zaznaczenie, jeśli rola nadal pasuje do filtra; w przeciwnym razie zamknij szczegóły.
                if (keep != null && ModRoles.Contains(keep) && SelectedRoleItem == null)
                    SelectedRoleItem = keep;
                else if (SelectedRoleItem != null && !ModRoles.Contains(SelectedRoleItem))
                    SelectedRoleItem = null;
            }

            RaiseRolesStateProperties();
        }

        private void RaiseRolesStateProperties()
        {
            this.RaisePropertyChanged(nameof(IsRoleModsLevelVisible));
            this.RaisePropertyChanged(nameof(IsRoleRolesLevelVisible));
            this.RaisePropertyChanged(nameof(IsRoleModsListVisible));
            this.RaisePropertyChanged(nameof(IsModRolesListVisible));
            this.RaisePropertyChanged(nameof(ShowRoleModsEmptyState));
            this.RaisePropertyChanged(nameof(ShowRoleModsNoResults));
            this.RaisePropertyChanged(nameof(ShowModRolesEmptyState));
            this.RaisePropertyChanged(nameof(ShowModRolesNoResults));
            this.RaisePropertyChanged(nameof(RoleModsCount));
            this.RaisePropertyChanged(nameof(HasRoleMods));
            this.RaisePropertyChanged(nameof(RoleModsCountLabel));
            this.RaisePropertyChanged(nameof(RolesLevelBSubtitle));
            this.RaisePropertyChanged(nameof(SelectedRoleModName));
            this.RaisePropertyChanged(nameof(CanShowRoleModInCatalog));
        }
    }
}