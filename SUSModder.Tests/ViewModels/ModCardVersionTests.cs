using System.Collections.Generic;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

public class ModCardVersionTests
{
    private static ModConfiguration Catalog() => new()
    {
        ModType = "full", ModVersion = "1.7.9", AmongVersion = "2026-9-29",
        LatestStableVersion = "1.7.3", LatestBetaVersion = "1.7.9",
        AvailableReleases = new List<ModRelease>
        {
            new() { Version = "1.7.3", AmongVersion = "2026-8-18" },
            new() { Version = "1.7.9", AmongVersion = "2026-9-29", IsBeta = true }
        }
    };

    [Fact]
    public void UninstalledShowsStableReleaseAndItsGameVersion()
    {
        var item = ModItemAdapter.FromConfig(Catalog());
        Assert.Equal("v1.7.3 · 2026-8-18", item.BrowserSubtitle);
        Assert.True(item.ShowNewerBetaOnCard);
        Assert.False(item.ShowInstallationSubtitle);
        Assert.True(item.ShowBetaInstall);
        Assert.False(item.ShowNormalInstall);
        Assert.Equal("1.7.9", ModItemAdapter.ToConfig(item).ModVersion);
    }

    [Fact]
    public void InstalledMapIsSeparateAndUpdatesSubtitle()
    {
        var config = Catalog();
        config.InstallPath = @"C:\Games\Mira";
        var item = ModItemAdapter.FromConfig(config);
        var notifications = new List<string?>();
        item.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        item.CardInstallation = new FullModInstallation { ModVersion = "1.7.3", AmongVersion = "2026-8-18" };
        Assert.Equal("v1.7.3 · 2026-8-18", item.BrowserSubtitle);
        Assert.True(item.ShowInstallationSubtitle);
        Assert.False(item.ShowBetaInstall);
        Assert.Equal("1.7.9", ModItemAdapter.ToConfig(item).ModVersion);
        item.CardInstallation = new FullModInstallation { ModVersion = "1.7.9", AmongVersion = "2026-9-29", IsBeta = true };
        Assert.Equal("v1.7.9 · 2026-9-29", item.BrowserSubtitle);
        Assert.Contains(nameof(ModItem.BrowserSubtitle), notifications);
        item.InstallPath = null;
        Assert.Null(item.CardInstallation);
        Assert.Equal("v1.7.3 · 2026-8-18", item.BrowserSubtitle);
    }

    [Fact]
    public void MissingMapDoesNotPairUnknownVersionWithBetaGame()
    {
        var config = Catalog();
        config.InstallPath = @"C:\Games\Mira";
        config.ModVersion = "1.6.0";
        Assert.Equal("v1.6.0", ModItemAdapter.FromConfig(config).BrowserSubtitle);
        config.AvailableReleases.Clear();
        config.InstallPath = null;
        Assert.Equal("v1.7.3", ModItemAdapter.FromConfig(config).BrowserSubtitle);
    }

    [Fact]
    public void LegacyCatalogKeepsVersionPair()
    {
        var item = ModItemAdapter.FromConfig(new ModConfiguration { ModVersion = "1.7.3", AmongVersion = "2026-8-18" });
        Assert.Equal("v1.7.3 · 2026-8-18", item.BrowserSubtitle);
        Assert.False(item.ShowNewerBetaOnCard);
    }

    [Theory]
    [InlineData("1.7.3")]
    [InlineData("1.7.2")]
    [InlineData(null)]
    [InlineData("unknown")]
    public void BadgesRequireNewerComparableBeta(string? beta)
    {
        var item = ModItemAdapter.FromConfig(Catalog());
        item.LatestBetaVersion = beta;
        Assert.False(item.ShowNewerBetaOnCard);
        Assert.True(item.ShowInstallationSubtitle);
        Assert.False(item.ShowBetaInstall);
        Assert.True(item.ShowNormalInstall);
    }

    [Fact]
    public void SplitRequiresFullModMatchingReleaseAndIdleState()
    {
        var item = ModItemAdapter.FromConfig(Catalog());
        item.IsInstalling = true;
        Assert.False(item.ShowBetaInstall);
        item.IsInstalling = false;
        item.ModType = "dll";
        Assert.False(item.ShowBetaInstall);
        item.ModType = "full";
        item.AvailableReleases = new List<ModRelease>();
        Assert.False(item.ShowBetaInstall);
        Assert.True(item.ShowNormalInstall);
    }
}
