using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

public class BrowserProjectionTests
{
    [Fact]
    public void SortingAfterAnOperationNeverCapturesOnlySearchResults()
    {
        var all = new[] { new ModItem { Id = 1, Name = "Alpha" }, new ModItem { Id = 2, Name = "Beta" } };
        var visible = new List<ModItem>(all);
        MainWindowViewModel.ApplyFilterToCollection(visible, all, "Alpha", (item, query) => item.Name.Contains(query!));
        Assert.Single(visible);
        visible[0].InstallPath = @"C:\Tests\Alpha";
        var completeSorted = MainWindowViewModel.SortCatalogSnapshot(all, visible);
        Assert.Equal(2, completeSorted.Count);
        MainWindowViewModel.ApplyFilterToCollection(visible, completeSorted, "", (_, _) => true);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void FavoritesAndQueryAreCombinedWithoutLosingNonFavorites()
    {
        var all = new[]
        {
            new ModItem { Id = 1, Name = "Town", IsFavorite = true },
            new ModItem { Id = 2, Name = "Other", IsFavorite = true },
            new ModItem { Id = 3, Name = "Town extra" }
        };
        var visible = new List<ModItem>(all);
        MainWindowViewModel.ApplyFilterToCollection(visible, all, "Town",
            (item, query) => item.IsFavorite && item.Name.Contains(query!), forcePredicate: true);
        Assert.Equal(1, Assert.Single(visible).Id);
        var complete = MainWindowViewModel.SortCatalogSnapshot(all, visible);
        MainWindowViewModel.ApplyFilterToCollection(visible, complete, "", (item, _) => item.IsFavorite, forcePredicate: true);
        Assert.Equal(2, visible.Count);
        MainWindowViewModel.ApplyFilterToCollection(visible, complete, "", (_, _) => true);
        Assert.Equal(3, visible.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public void OnlyStablePositiveCatalogIdsCanBeFavorited(int id, bool expected) =>
        Assert.Equal(expected, new ModItem { Id = id }.CanFavorite);

    [Theory]
    [InlineData(FavoriteModsErrorCodes.StorageUnavailable, "UI.Favorites.StorageUnavailable")]
    [InlineData(FavoriteModsErrorCodes.InvalidModId, "UI.Favorites.InvalidMod")]
    [InlineData(FavoriteModsErrorCodes.WriteFailed, "UI.Favorites.WriteFailed")]
    [InlineData(null, "UI.Favorites.StorageUnavailable")]
    public void FavoriteFailuresMapToStableLocalizedKeys(string? code, string key) =>
        Assert.Equal(key, MainWindowViewModel.FavoriteErrorKey(code));
}
