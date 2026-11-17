using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ModUpdateManagerTests
{
    [Fact]
    public void IsPackOriginInstallation_RequiresNonEmptySourcePackCode()
    {
        Assert.True(ModUpdateManager.IsPackOriginInstallation(new InstallationMap { SourcePackCode = "pack-code" }));
        Assert.False(ModUpdateManager.IsPackOriginInstallation(new InstallationMap()));
        Assert.False(ModUpdateManager.IsPackOriginInstallation(null));
    }
}
