using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class InstallationMapManagerTests
{
    [Fact]
    public void ImportDiscoveredMods_DoesNotApplyFullModMapToDllCatalogEntryWithSameId()
    {
        var existingConfigs = new List<ModConfiguration>
        {
            new()
            {
                Id = 10,
                ModName = "CrowdedMod",
                ModType = "dll",
                ModVersion = "2.10.0",
                AmongVersion = "ToU",
                InstallPath = null
            }
        };

        var discoveredMaps = new List<InstallationMap>
        {
            new()
            {
                DisplayName = "ToU - shared",
                FullMod = new FullModInstallation
                {
                    ModId = 10,
                    ModName = "Town of Us",
                    ModVersion = "5.5.0",
                    AmongVersion = "2024.6",
                    InstallPath = @"C:\Users\Bartek\AppData\Roaming\Among Us - Mody\ToU - shared"
                }
            }
        };

        var imported = InstallationMapManager.ImportDiscoveredMods(
            discoveredMaps,
            existingConfigs,
            new TestDiagnosticsOutput());

        Assert.Empty(imported);
        var crowdedMod = Assert.Single(existingConfigs);
        Assert.Equal("CrowdedMod", crowdedMod.ModName);
        Assert.Equal("dll", crowdedMod.ModType);
        Assert.Equal("2.10.0", crowdedMod.ModVersion);
        Assert.Null(crowdedMod.InstallPath);
    }

    [Fact]
    public void ImportDiscoveredMods_UpdatesMatchingFullModCatalogEntry()
    {
        var existingConfigs = new List<ModConfiguration>
        {
            new()
            {
                Id = 1,
                ModName = "Town of Us",
                ModType = "full",
                ModVersion = "5.3.1",
                AmongVersion = "2025-3-31",
                InstallPath = null
            }
        };

        var discoveredPath = @"C:\Users\Bartek\AppData\Roaming\Among Us - Mody\Town of Us";
        var discoveredMaps = new List<InstallationMap>
        {
            new()
            {
                DisplayName = "Town of Us",
                FullMod = new FullModInstallation
                {
                    ModId = 1,
                    ModName = "Town of Us",
                    ModVersion = "5.3.1",
                    AmongVersion = "2025-3-31",
                    InstallPath = discoveredPath
                }
            }
        };

        var imported = InstallationMapManager.ImportDiscoveredMods(
            discoveredMaps,
            existingConfigs,
            new TestDiagnosticsOutput());

        var importedMod = Assert.Single(imported);
        Assert.Equal("Town of Us", importedMod.ModName);
        Assert.Equal(discoveredPath, importedMod.InstallPath);
        Assert.Equal(discoveredPath, existingConfigs.Single().InstallPath);
    }

    [Fact]
    public void ImportDiscoveredMods_SkipsIncompleteMapWithoutBaseModIdentity()
    {
        var existingConfigs = new List<ModConfiguration>();
        var discoveredMaps = new List<InstallationMap>
        {
            new()
            {
                DisplayName = "ToU - copy",
                FullMod = new FullModInstallation
                {
                    ModId = 0,
                    ModName = "",
                    ModVersion = "",
                    AmongVersion = "",
                    InstallPath = @"C:\Users\Bartek\AppData\Roaming\Among Us - Mody\ToU - copy"
                }
            }
        };

        var imported = InstallationMapManager.ImportDiscoveredMods(
            discoveredMaps,
            existingConfigs,
            new TestDiagnosticsOutput());

        Assert.Empty(imported);
        Assert.Empty(existingConfigs);
    }

    private sealed class TestDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string line)
        {
        }
    }
}
