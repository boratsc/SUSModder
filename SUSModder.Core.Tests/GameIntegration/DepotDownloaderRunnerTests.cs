using SUSModder.Core.GameIntegration;
using SUSModder.Core.GameIntegration.Steam;

namespace SUSModder.Core.Tests.GameIntegration;

public class DepotDownloaderRunnerTests
{
    [Fact]
    public void BuildArgs_WithManifestAndSavedToken_IncludesManifestAndDepot()
    {
        var args = DepotDownloaderRunner.BuildArgs(@"C:\cache\2026317", "1234567890", useQrAuth: false);

        Assert.Contains("-app", args);
        Assert.Contains(DepotDownloaderRunner.SteamAppId, args);
        Assert.Contains("-depot", args);
        Assert.Contains(DepotDownloaderRunner.SteamDepotId, args);
        Assert.Contains("-manifest", args);
        Assert.Contains("1234567890", args);
        Assert.Contains(@"C:\cache\2026317", args);
        Assert.DoesNotContain("-password", args);
    }

    [Fact]
    public void BuildArgs_QrAuth_UsesQrFlagWithoutUsername()
    {
        var args = DepotDownloaderRunner.BuildArgs(@"C:\cache", "999", useQrAuth: true);

        Assert.Contains("-qr", args);
        Assert.Contains("-remember-password", args);
        Assert.DoesNotContain("-username", args);
        Assert.DoesNotContain("-password", args);
    }

    [Fact]
    public void TryExtractQrBlock_RecognizesOemEncodedAsciiQr()
    {
        var encoding = DepotDownloaderRunner.ResolveProcessOutputEncoding();
        var sampleLine = encoding.GetString(new byte[] { 0xDB, 0xDB, 0xDB, 0x20, 0xDB, 0xDB, 0xDB, 0xDB, 0x20, 0xDB, 0xDB, 0xDB });
        var lines = new[]
        {
            "Use the Steam Mobile App to sign in with this QR code:",
            sampleLine,
            sampleLine,
            sampleLine,
            "Done!"
        };

        Assert.True(DepotDownloaderRunner.TryExtractQrBlock(lines, out var qrBlock));
        Assert.Contains("█", qrBlock);
    }

    [Fact]
    public void TryExtractQrBlock_RecognizesUnicodeBlockGlyphs()
    {
        var lines = new[]
        {
            "Use the Steam Mobile App to sign in with this QR code:",
            "█████████████  ██  ████  ████    ████  █████████████",
            "██          ██    ████████  ████████    ██          ██",
            "██████████████  ██  ██  ██  ██  ██  ██  ██████████████",
        };

        Assert.True(DepotDownloaderRunner.TryExtractQrBlock(lines, out var qrBlock));
        Assert.Contains("█████████████", qrBlock);
    }
}

public class AmongUsVersionHelperTests
{
    [Theory]
    [InlineData("2026-3-17", "2026317")]
    [InlineData("2026.3.17", "2026317")]
    public void ToStorageVersion_Normalizes(string input, string expected)
    {
        Assert.Equal(expected, AmongUsVersionHelper.ToStorageVersion(input));
    }

    [Theory]
    [InlineData("2026-3-17", "2026-3-17")]
    [InlineData("2026.3.17", "2026-3-17")]
    [InlineData("2026317", "2026-3-17")]
    public void NormalizeAmongVersion_ParsesCommonFormats(string input, string expected)
    {
        Assert.Equal(expected, AmongUsVersionHelper.NormalizeAmongVersion(input));
    }
}
