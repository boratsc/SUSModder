using System;
using System.Linq;
using ReactiveUI;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private readonly BrowserSessionState _browserSession = new();
    private int _dllBrowserRefreshGeneration;
    public event Action<ModBrowserTab>? BrowserScrollCaptureRequested;
    public event Action<ModBrowserTab, double>? BrowserScrollRestoreRequested;

    public void CaptureBrowserScroll(ModBrowserTab tab, double offset) =>
        _browserSession.For(tab).ScrollOffset = offset;

    private void CaptureBrowserTabState()
    {
        var state = _browserSession.For(ActiveBrowserTab);
        state.Query = BrowserSearchText;
        state.FavoritesOnly = BrowserFavoritesOnly;
        state.ModId = IsCatalogTab ? SelectedMod?.Id : IsDllAddonsTab ? SelectedDllMod?.Id : IsRolesTab ? SelectedRoleModId : null;
        state.InstanceId = IsMyPacksTab ? SelectedPackInstance?.InstanceId : null;
        if (IsRolesTab)
        {
            state.RoleId = SelectedRoleItem?.Role.Id;
            state.RoleCategory = SelectedRoleCategoryOption?.Value;
            state.RoleType = SelectedRoleTypeOption?.Value;
        }
        BrowserScrollCaptureRequested?.Invoke(ActiveBrowserTab);
    }

    private void RestoreBrowserTabFilters()
    {
        var state = _browserSession.For(ActiveBrowserTab);
        _browserSearchText = state.Query;
        _browserFavoritesOnly = state.FavoritesOnly;
        this.RaisePropertyChanged(nameof(BrowserSearchText));
        this.RaisePropertyChanged(nameof(BrowserFavoritesOnly));
        if (IsRolesTab)
        {
            _selectedRoleCategoryOption = RoleCategoryOptions.FirstOrDefault(o => o.Value == state.RoleCategory)
                                        ?? RoleCategoryOptions.FirstOrDefault();
            _selectedRoleTypeOption = RoleTypeOptions.FirstOrDefault(o => o.Value == state.RoleType)
                                      ?? RoleTypeOptions.FirstOrDefault();
            this.RaisePropertyChanged(nameof(SelectedRoleCategoryOption));
            this.RaisePropertyChanged(nameof(SelectedRoleTypeOption));
        }
    }

    private void RestoreBrowserTabSelection()
    {
        var tab = ActiveBrowserTab;
        var state = _browserSession.For(tab);
        if (IsCatalogTab)
            SelectedMod = Mods.FirstOrDefault(m => m.Id == state.ModId);
        else if (IsDllAddonsTab)
            SelectedDllMod = DllMods.FirstOrDefault(m => m.Id == state.ModId);
        else if (IsRolesTab)
            RestoreRolesTabSelection(state);
        else
            SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == state.InstanceId);

        BrowserScrollRestoreRequested?.Invoke(tab, state.ScrollOffset);
    }

    /// <summary>E14: przywrócenie poziomu A/B, roli i filtrów; deep-link (pending) wygrywa z przywracaniem.</summary>
    private void RestoreRolesTabSelection(BrowserTabState state)
    {
        if (_pendingRoleModPreselect is int)
        {
            if (_rolesLoaded)
                ApplyPendingRoleModPreselect();
            return;
        }

        if (!_rolesLoaded)
            return;

        if (RolesBrowserProjection.ResolveModId(state.ModId, _roleModsSnapshot) is int modId)
            OpenRoleMod(modId, clearSearch: false);
        else
            SelectedRoleModId = null;

        SelectedRoleItem = state.RoleId is int roleId
            ? ModRoles.FirstOrDefault(i => i.Role.Id == roleId)
            : null;
    }

    private void ClearFilteredBrowserSelection()
    {
        if (IsCatalogTab && SelectedMod != null && !Mods.Contains(SelectedMod))
            SelectedMod = null;
        if (IsDllAddonsTab && SelectedDllMod != null && !DllMods.Contains(SelectedDllMod))
            SelectedDllMod = null;
        if (IsMyPacksTab && SelectedPackInstance != null && !PackInstances.Contains(SelectedPackInstance))
            SelectedPackInstance = null;
    }
}
