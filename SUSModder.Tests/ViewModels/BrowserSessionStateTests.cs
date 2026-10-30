using System;
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
}
