using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class ModDownloadUrlBuilderTests
{
    [Fact]
    public void GetDllFileName_SourceUrlEndsWithVersion_FallsBackToModNameDll()
    {
        var mod = new ModConfiguration
        {
            ModName = "AleLuduMod",
            GitHubRepoOrLink = "https://api.susmodder-cdn.ovh/v2/downloads/mod/5/1.1.2?platform=steam&arch=x86"
        };

        var fileName = ModDownloadUrlBuilder.GetDllFileName(mod, "steam");

        Assert.Equal("AleLuduMod.dll", fileName);
    }

    [Fact]
    public void GetDllFileName_SourceUrlHasDllFile_UsesSourceFileName()
    {
        var mod = new ModConfiguration
        {
            ModName = "AleLuduMod",
            GitHubRepoOrLink = "https://example.com/releases/AleLudu.dll"
        };

        var fileName = ModDownloadUrlBuilder.GetDllFileName(mod, "steam");

        Assert.Equal("AleLudu.dll", fileName);
    }
}
