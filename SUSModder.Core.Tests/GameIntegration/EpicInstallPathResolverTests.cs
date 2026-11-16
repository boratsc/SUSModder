using SUSModder.Core.GameIntegration;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public sealed class EpicInstallPathResolverTests
{
    [Fact]
    public void ExpectedPathDoesNotDependOnFileExistence()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Equal(Path.Combine(path, "AmongUs", "Among Us.exe"), EpicInstallPathResolver.GetExpectedGameExePath(path));
        Assert.False(File.Exists(EpicInstallPathResolver.GetExpectedGameExePath(path)));
    }

    [Fact]
    public void VanillaPathIsExact() =>
        Assert.Equal(Path.Combine("mods", "Among Us - Vanilla", "AmongUs", "Among Us.exe"),
            EpicInstallPathResolver.GetVanillaExpectedExePath("mods"));

    [Theory]
    [InlineData("C:\\Games\\AmongUs\\", "C:\\Games")]
    [InlineData("C:\\Games\\", "C:\\Games")]
    public void SavedPathIsTrimmed(string saved, string expected) =>
        Assert.Equal(expected, EpicInstallPathResolver.GetBaseInstallDirectory(saved, "mods", "mod"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingSavedPathUsesModsAndModName(string? saved) =>
        Assert.Equal(Path.Combine("mods", "mod"), EpicInstallPathResolver.GetBaseInstallDirectory(saved, "mods", "mod"));

    [Fact]
    public void ResolverKeepsLegacyFallbackAndPrefersNewLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var preferred = EpicInstallPathResolver.GetExpectedGameExePath(root);
            Assert.Equal(Path.Combine(root, "Among Us.exe"), EpicVersionManager.ResolveEpicGameExePath(root));
            Directory.CreateDirectory(Path.GetDirectoryName(preferred)!);
            File.WriteAllText(preferred, "");
            Assert.Equal(preferred, EpicVersionManager.ResolveEpicGameExePath(root));
            File.WriteAllText(Path.Combine(root, "Among Us.exe"), "");
            Assert.Equal(preferred, EpicVersionManager.ResolveEpicGameExePath(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
