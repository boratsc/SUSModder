using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using SUSModder.Core.Data;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private IFavoriteModsRepository? _favoritesRepository;
    private ReactiveCommand<ModItem, Unit>? _toggleFavoriteCommand;
    private bool _favoritesReadErrorShown;
    private ReactiveCommand<Unit, Unit>? _toggleFavoritesFilterCommand;
    public ReactiveCommand<Unit, Unit> ToggleFavoritesFilterCommand =>
        _toggleFavoritesFilterCommand ??= ReactiveCommand.Create(() => { BrowserFavoritesOnly = !BrowserFavoritesOnly; });

    public ReactiveCommand<ModItem, Unit> ToggleFavoriteCommand =>
        _toggleFavoriteCommand ??= ReactiveCommand.Create<ModItem>(ToggleFavorite);

    private IFavoriteModsRepository? GetFavoritesRepository()
    {
        if (_favoritesRepository != null) return _favoritesRepository;
        try { return _favoritesRepository = App.GetService<IFavoriteModsRepository>(); }
        catch (InvalidOperationException) { return null; } // Designer / startup before service initialization.
    }

    private void ApplyFavoriteStates(IEnumerable<ModItem> items)
    {
        var snapshot = GetFavoritesRepository()?.GetSnapshot();
        if (snapshot == null) return;
        if (!snapshot.IsLoaded)
        {
            if (!_favoritesReadErrorShown)
            {
                _favoritesReadErrorShown = true;
                ToastService.ShowWarning(_localizationService.Get(FavoriteErrorKey(snapshot.ErrorCode, reading: true)));
            }
            return;
        }
        _favoritesReadErrorShown = false;
        var ids = snapshot.ModIds.ToHashSet();
        foreach (var item in items)
            item.IsFavorite = ids.Contains(item.Id);
    }

    private void ToggleFavorite(ModItem item)
    {
        if (item == null || !item.CanFavorite) return;
        var desired = !item.IsFavorite;
        var result = GetFavoritesRepository()?.SetFavorite(item.Id, desired);
        if (result?.Success != true)
        {
            item.RaisePropertyChanged(nameof(ModItem.IsFavorite));
            ToastService.ShowError(_localizationService.Get(FavoriteErrorKey(result?.ErrorCode)));
            return;
        }

        foreach (var mod in _catalogModsSnapshot.Concat(_dllModsSnapshot).Concat(Mods).Concat(DllMods)
                     .Where(mod => mod.Id == item.Id).Distinct())
            mod.IsFavorite = desired;
        item.IsFavorite = desired;
        ApplyBrowserSearchFilter();
    }

    internal static string FavoriteErrorKey(string? errorCode, bool reading = false) => errorCode switch
    {
        FavoriteModsErrorCodes.InvalidModId => "UI.Favorites.InvalidMod",
        FavoriteModsErrorCodes.StorageUnavailable or null => "UI.Favorites.StorageUnavailable",
        _ => reading ? "UI.Favorites.ReadFailed" : "UI.Favorites.WriteFailed"
    };
}
