using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModInstanceUpdateCheckerTests
{
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
