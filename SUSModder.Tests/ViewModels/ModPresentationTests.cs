using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

public class ModPresentationTests
{
    [Fact]
    public void VersionedIconDoesNotReplaceCatalogDownloadReference()
    {
        var item = new ModItem { Id = 12, PngFileName = "https://susmodder.app/icons/old.png" };
        item.VersionedIconKey = "mod:12";
        Assert.Equal("susmodder-icon:mod:12", item.IconSourceKey);
        Assert.Equal("https://susmodder.app/icons/old.png", item.PngFileName);
        item.VersionedIconKey = null;
        Assert.Equal(item.PngFileName, item.IconSourceKey);
    }

    [Fact]
    public void DirectorySizeAvailabilityTracksPresentationWithoutChangingInstallation()
    {
        var item = new ModItem { InstallPath = @"C:\Games\Mod" };
        Assert.False(item.HasDirectorySize);
        item.DirectorySizeText = "1.2 GiB";
        Assert.True(item.HasDirectorySize);
        Assert.Equal(@"C:\Games\Mod", item.InstallPath);
        item.DirectorySizeText = string.Empty;
        Assert.False(item.HasDirectorySize);
    }

    [Fact]
    public void FavoriteIsIndependentOfVersionAndInstallation()
    {
        var item = new ModItem { Id = 12, IsFavorite = true };
        item.ModVersion = "next";
        item.InstallPath = @"C:\Games\Mod";
        item.InstallPath = null;
        item.Name = "Renamed";
        Assert.True(item.IsFavorite);
        Assert.Equal(12, item.Id);
    }

    [Fact]
    public void AvailableBetaIsHiddenOnceItIsTheInstalledVersion()
    {
        var item = new ModItem { Id = 13, ModType = "full", ModVersion = "1.7.3" };
        Assert.False(item.HasAvailableBeta);

        item.LatestBetaVersion = "1.6.3-beta2";
        Assert.True(item.HasAvailableBeta);
        Assert.Equal("Beta available: 1.6.3-beta2", item.AvailableBetaText);

        item.ModVersion = "1.6.3-beta2";
        Assert.False(item.HasAvailableBeta);
    }

    [Fact]
    public void BetaInstalledBadgeRequiresInstallationAndBetaFlag()
    {
        var item = new ModItem { Id = 13, ModType = "full", InstallPath = @"C:\Games\Mira" };
        Assert.False(item.ShowBetaInstalledBadge);

        item.IsInstalledBeta = true;
        Assert.True(item.ShowBetaInstalledBadge);

        item.InstallPath = null;
        Assert.False(item.ShowBetaInstalledBadge);
    }

    [Fact]
    public void VanillaNeverOffersBeta()
    {
        var item = new ModItem { ModType = "Vanilla", LatestBetaVersion = "2026.6" };
        Assert.False(item.HasAvailableBeta);
    }

    [Theory]
    [InlineData("1.7.3", "1.8.0-beta.1", true)]
    [InlineData("v1.7.3", "v1.8.0-beta.1", true)]
    [InlineData("1.7.3", "1.7.3-beta.1", false)]
    [InlineData("1.7.3", "1.7.3", false)]
    [InlineData("1.7.3", "1.6.3-beta.2", false)]
    [InlineData("1.7.3", null, false)]
    [InlineData(null, "1.8.0-beta.1", false)]
    [InlineData("unknown", "1.8.0-beta.1", false)]
    [InlineData("1.7.3", "unknown", false)]
    public void CardShowsOnlyBetaNewerThanCatalogStable(string? stable, string? beta, bool expected)
    {
        var item = new ModItem { ModType = "full", ModVersion = "1.7.3", LatestStableVersion = stable, LatestBetaVersion = beta };
        Assert.Equal(expected, item.ShowNewerBetaOnCard);
        Assert.Equal($"v{(stable ?? "1.7.3").TrimStart('v', 'V')}", item.BrowserSubtitle);
    }

    [Fact]
    public void CardBetaVisibilityUpdatesWhenCatalogVersionsOrTypeChange()
    {
        var item = new ModItem { ModType = "full", LatestBetaVersion = "1.8.0-beta.1" };
        var notifications = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModItem.ShowNewerBetaOnCard)) notifications++; };
        item.LatestStableVersion = "1.7.3";
        Assert.True(item.ShowNewerBetaOnCard);
        item.LatestBetaVersion = "1.6.0-beta.1";
        Assert.False(item.ShowNewerBetaOnCard);
        item.LatestBetaVersion = "1.8.0-beta.1";
        item.ModType = "Vanilla";
        Assert.False(item.ShowNewerBetaOnCard);
        Assert.Equal(4, notifications);
    }
}
