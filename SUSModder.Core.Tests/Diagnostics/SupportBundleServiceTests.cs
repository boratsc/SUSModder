using SUSModder.Core.Diagnostics;
using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Tests.Diagnostics;

public sealed class SupportBundleServiceTests
{
    private readonly SupportBundleService _service = new();

    [Fact]
    public void RedactPath_RedactsUserProfile()
    {
        var path = @"C:\Users\JohnDoe\AppData\Roaming\SUSModder\mods\TownOfUs";
        var result = SupportBundleService.RedactPath(path);
        Assert.DoesNotContain("JohnDoe", result);
        Assert.Contains("<redacted>", result);
    }

    [Fact]
    public void RedactPath_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SupportBundleService.RedactPath(null));
    }

    [Fact]
    public void RedactPath_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SupportBundleService.RedactPath(""));
    }

    [Fact]
    public void RedactLine_RedactsEmail()
    {
        var line = "Error from user@example.com in path";
        var result = SupportBundleService.RedactLine(line);
        Assert.DoesNotContain("user@example.com", result);
        Assert.Contains("<email-redacted>", result);
    }

    [Fact]
    public void RedactLine_RedactsDiscordToken()
    {
        var line = "token: NzQwMTIzNDU2Nzg5MDEyMzQ1Njc4OTAx.G4B5C6.D7E8F9G0H1I2J3K4L5M6N7O8P9Q0R1S2";
        var result = SupportBundleService.RedactLine(line);
        Assert.Contains("<discord-token-redacted>", result);
    }

    [Fact]
    public void RedactLine_RedactsBearerToken()
    {
        var line = "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0";
        var result = SupportBundleService.RedactLine(line);
        Assert.DoesNotContain("eyJhbGciOi", result);
        Assert.Contains("<redacted>", result);
    }

    [Fact]
    public void RedactLine_NoSensitiveData_ReturnsSame()
    {
        var line = "[Info   :   BepInEx] Chainloader started";
        var result = SupportBundleService.RedactLine(line);
        Assert.Equal(line, result);
    }

    [Fact]
    public async Task GenerateBundle_CreatesZipFile()
    {
        var result = new LaunchResult
        {
            Attempt = new LaunchAttempt
            {
                ModName = "TestMod",
                ModType = "full",
                PlatformMode = "steam",
                InstallPath = @"C:\TestMod"
            },
            DiagnosisCodes = { "launch.bepinex.log_missing" },
            BepInExCriticalLines = { "[Error] Test error line" },
            PluginSnapshot =
            {
                new PluginFileSnapshot { FileName = "test.dll", SizeBytes = 1024 }
            }
        };

        var outputDir = Path.Combine(Path.GetTempPath(), "susmodder-test-bundle");
        Directory.CreateDirectory(outputDir);

        try
        {
            var zipPath = await _service.GenerateBundleAsync(result, outputDir, anonymize: true);
            Assert.NotNull(zipPath);
            Assert.True(File.Exists(zipPath));
            Assert.True(new FileInfo(zipPath).Length > 0);
        }
        finally
        {
            try { Directory.Delete(outputDir, true); } catch { }
        }
    }

    [Fact]
    public void ComputeSha256_ReturnsCorrectHash()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var hash = SupportBundleService.ComputeSha256(tempFile);
            Assert.Equal(64, hash.Length);
            Assert.True(hash.All(c => char.IsAsciiHexDigit(c)));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
