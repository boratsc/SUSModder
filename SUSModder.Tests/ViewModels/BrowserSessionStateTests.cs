using System;
using SUSModder.Behaviors;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

public class BrowserSessionStateTests
{
    [Fact]
    public void TabsHaveIndependentState()
    {
        var session = new BrowserSessionState();
        var catalog = session.For(MainWindowViewModel.ModBrowserTab.Catalog);
        catalog.Query = "town";
        catalog.FavoritesOnly = true;
        catalog.ModId = 42;
        catalog.ScrollOffset = 120;
        var packs = session.For(MainWindowViewModel.ModBrowserTab.MyPacks);
        packs.Query = "friends";
        packs.InstanceId = "pack-1";
        Assert.Equal("town", session.For(MainWindowViewModel.ModBrowserTab.Catalog).Query);
        Assert.True(catalog.FavoritesOnly);
        Assert.Null(packs.ModId);
        Assert.Equal(string.Empty, session.For(MainWindowViewModel.ModBrowserTab.DllAddons).Query);
    }

    [Fact]
    public void NewSessionDoesNotRestorePreviousProcessState()
    {
        var first = new BrowserSessionState();
        first.For(MainWindowViewModel.ModBrowserTab.Catalog).FavoritesOnly = true;
        first.For(MainWindowViewModel.ModBrowserTab.Catalog).Query = "saved only in memory";
        var next = new BrowserSessionState().For(MainWindowViewModel.ModBrowserTab.Catalog);
        Assert.False(next.FavoritesOnly);
        Assert.Empty(next.Query);
        Assert.Null(next.ModId);
        Assert.Equal(0, next.ScrollOffset);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOffsetsAreSafelyClamped(double offset)
    {
        var state = new BrowserTabState { ScrollOffset = offset };
        Assert.Equal(0, state.ScrollOffset);
    }

    /// <summary>F3d: zakładka Discordy ma własny, pusty stan sesji (nie dziedziczy filtra/zaznaczenia innych zakładek).</summary>
    [Fact]
    public void DiscordsTabStartsWithEmptyIndependentState()
    {
        var session = new BrowserSessionState();
        session.For(MainWindowViewModel.ModBrowserTab.Catalog).Query = "town";
        session.For(MainWindowViewModel.ModBrowserTab.MyPacks).InstanceId = "pack-1";
        var discords = session.For(MainWindowViewModel.ModBrowserTab.Discords);
        Assert.Empty(discords.Query);
        Assert.False(discords.FavoritesOnly);
        Assert.Null(discords.ModId);
        Assert.Null(discords.InstanceId);
        Assert.Equal(0, discords.ScrollOffset);
    }

    /// <summary>
    /// W5: capture dla Discordów nie zapisuje offsetu cudzej listy — zakładka nie ma scroll ownera
    /// (ResolveScrollListName = null), więc stan sesji Discordów pozostaje pusty niezależnie od Moich zestawów.
    /// </summary>
    [Fact]
    public void DiscordsCaptureHasNoScrollOwnerAndDoesNotStoreOtherListOffset()
    {
        Assert.Null(BrowserSessionScrollBehavior.ResolveScrollListName(
            MainWindowViewModel.ModBrowserTab.Discords, selectedRoleModId: null));
        // regression guard: pozostałe zakładki nadal mają swojego scroll ownera
        Assert.Equal("PackInstancesListBox", BrowserSessionScrollBehavior.ResolveScrollListName(
            MainWindowViewModel.ModBrowserTab.MyPacks, selectedRoleModId: null));

        var session = new BrowserSessionState();
        session.For(MainWindowViewModel.ModBrowserTab.MyPacks).ScrollOffset = 120;
        Assert.Equal(0, session.For(MainWindowViewModel.ModBrowserTab.Discords).ScrollOffset);
    }
}
