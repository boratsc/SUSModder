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
}
