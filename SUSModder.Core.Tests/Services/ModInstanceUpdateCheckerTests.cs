using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModInstanceUpdateCheckerTests
{
    [Theory]
    [InlineData("1.7.9", "1.7.5", false)]
    [InlineData("1.7.9-beta", "1.7.5", false)]
    [InlineData("1.7.3", "1.7.5", true)]
    [InlineData("unknown", "1.7.5", true)]
    [InlineData("Nieznana", "1.7.5", true)]
    [InlineData("", "1.7.5", true)]
    [InlineData("1.7.5", "1.7.5", false)]
    public void UpdateChecks_DoNotOfferDowngrades_AndPreserveUnknownFallback(string installed, string available, bool expected)
    {
        var catalog = new ModConfiguration { ModVersion = available };
        Assert.Equal(expected, ModUpdateManager.IsUpdateCandidate(installed, catalog));
        Assert.Equal(expected, ModInstanceUpdateChecker.HasCatalogUpdate(
            new ModInstance { FullModVersion = installed, AutoUpdateEnabled = true }, catalog));
    }

    [Fact]
    public void HasCatalogUpdate_WhenCatalogNewer_ReturnsTrue()
    {
        var instance = new ModInstance
        {
            FullModVersion = "5.4.0",
            AutoUpdateEnabled = true
        };
        var catalog = new ModConfiguration { ModVersion = "5.5.0" };

        Assert.True(ModInstanceUpdateChecker.HasCatalogUpdate(instance, catalog));
    }

    [Fact]
    public void HasCatalogUpdate_WhenPinnedAndAutoUpdateOff_ReturnsFalse()
    {
        var instance = new ModInstance
        {
            FullModVersion = "5.4.0",
            PinnedVersion = "5.4.0",
            AutoUpdateEnabled = false
        };
        var catalog = new ModConfiguration { ModVersion = "5.5.0" };

        Assert.False(ModInstanceUpdateChecker.HasCatalogUpdate(instance, catalog));
    }

    [Fact]
    public void HasCatalogUpdate_WhenVersionsMatch_ReturnsFalse()
    {
        var instance = new ModInstance { FullModVersion = "5.5.0" };
        var catalog = new ModConfiguration { ModVersion = "5.5.0" };

        Assert.False(ModInstanceUpdateChecker.HasCatalogUpdate(instance, catalog));
    }
}
