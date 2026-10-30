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
        state.ModId = IsCatalogTab ? SelectedMod?.Id : IsDllAddonsTab ? SelectedDllMod?.Id : null;
        state.InstanceId = IsMyPacksTab ? SelectedPackInstance?.InstanceId : null;
        BrowserScrollCaptureRequested?.Invoke(ActiveBrowserTab);
    }

    private void RestoreBrowserTabFilters()
    {
        var state = _browserSession.For(ActiveBrowserTab);
        _browserSearchText = state.Query;
        _browserFavoritesOnly = state.FavoritesOnly;
        this.RaisePropertyChanged(nameof(BrowserSearchText));
        this.RaisePropertyChanged(nameof(BrowserFavoritesOnly));
    }

    private void RestoreBrowserTabSelection()
    {
        var tab = ActiveBrowserTab;
        var state = _browserSession.For(tab);
        if (IsCatalogTab)
            SelectedMod = Mods.FirstOrDefault(m => m.Id == state.ModId);
        else if (IsDllAddonsTab)
            SelectedDllMod = DllMods.FirstOrDefault(m => m.Id == state.ModId);
        else
            SelectedPackInstance = PackInstances.FirstOrDefault(p => p.InstanceId == state.InstanceId);

        BrowserScrollRestoreRequested?.Invoke(tab, state.ScrollOffset);
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
