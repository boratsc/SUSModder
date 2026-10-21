using SUSModder.Core.GameIntegration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class ModsInstallPathGuardTests
{
    [Fact]
    public void Validate_DefaultModsPath_IsAccepted()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, "Among Us - Mody");

        var result = ModsInstallPathGuard.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal(Path.GetFullPath(path), result.Message);
    }

    [Theory]
    [InlineData(@"C:\Program Files\SUSModder")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Among Us")]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us_Data")]
    public void Validate_ProtectedPaths_AreRejected(string path)
    {
        var result = ModsInstallPathGuard.Validate(path, probeWriteAccess: false);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public void ResolveSafeModsInstallPath_InvalidConfiguredPath_FallsBackToDefault()
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Among Us - Mody");

        var resolved = ModsInstallPathGuard.ResolveSafeModsInstallPath(
            @"C:\Program Files (x86)\Steam\steamapps\common\Among Us\Among Us_Data",
            defaultPath);

        Assert.Equal(Path.GetFullPath(defaultPath), Path.GetFullPath(resolved));
    }

    [Fact]
    public void TryFindAmongUsInstallRoot_DataSubfolder_ReturnsGameRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "susmodder-guard-test-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Among Us_Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(root, AmongUsPathDiscovery.GameExeName), string.Empty);

        try
        {
            var found = ModsInstallPathGuard.TryFindAmongUsInstallRoot(dataDir, out var gameRoot);

            Assert.True(found);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(gameRoot));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Validate_ErrorCodes_AreStable()
    {
        Assert.Equal("empty", ModsInstallPathGuard.ErrorEmpty);
        Assert.Equal("invalid_path", ModsInstallPathGuard.ErrorInvalidPath);
        Assert.Equal("program_files", ModsInstallPathGuard.ErrorProgramFiles);
        Assert.Equal("steam_game_install", ModsInstallPathGuard.ErrorSteamGameInstall);
        Assert.Equal("game_install_directory", ModsInstallPathGuard.ErrorGameInstallDirectory);
        Assert.Equal("not_writable", ModsInstallPathGuard.ErrorNotWritable);
    }

    [Fact]
    public void Validate_EmptyPath_ReturnsEnglishFallback()
    {
        var result = ModsInstallPathGuard.Validate(null);

        Assert.False(result.IsValid);
        Assert.Equal("empty", result.ErrorCode);
        Assert.Contains("cannot be empty", result.Message);
        Assert.DoesNotContain("nie może być pusta", result.Message);
    }
}
