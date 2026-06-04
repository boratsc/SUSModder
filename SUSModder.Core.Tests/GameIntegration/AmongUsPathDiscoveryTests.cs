using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public class AmongUsPathDiscoveryTests
{
    [Fact]
    public void TryParseAppManifestInstallDir_ReadsInstallDirFromAcf()
    {
        const string manifest = """
            "AppState"
            {
                "appid"		"945360"
                "installdir"		"Among Us"
            }
            """;

        var installDir = AmongUsPathDiscovery.TryParseAppManifestInstallDirFromContent(manifest);

        Assert.Equal("Among Us", installDir);
    }

    [Fact]
    public void IsValidInstallDirectory_ReturnsFalseForMissingExe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Assert.False(AmongUsPathDiscovery.IsValidInstallDirectory(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
