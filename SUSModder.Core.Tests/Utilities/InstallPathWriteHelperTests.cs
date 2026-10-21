using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class InstallPathWriteHelperTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    [InlineData(typeof(IOException), false)]
    public void IsAccessDeniedException_DetectsUnauthorizedAccess(Type exceptionType, bool expected)
    {
        var message = exceptionType == typeof(IOException)
            ? "The process cannot access the file because it is being used by another process."
            : "Access to the path 'C:\\test' is denied.";

        var ex = (Exception)Activator.CreateInstance(exceptionType, message)!;
        Assert.Equal(expected, InstallPathWriteHelper.IsAccessDeniedException(ex));
    }

    [Fact]
    public void TryGetBlockedPathFromException_ParsesWindowsMessage()
    {
        var ex = new Exception("Access to the path 'C:\\Users\\x\\file.config' is denied.");

        var path = InstallPathWriteHelper.TryGetBlockedPathFromException(ex);

        Assert.Equal(@"C:\Users\x\file.config", path);
    }

    [Fact]
    public void FormatExtractionAccessDeniedMessage_IncludesBlockedFileAndModsPath()
    {
        var message = InstallPathWriteHelper.FormatExtractionAccessDeniedMessage(
            @"C:\Users\Pioko\AppData\Roaming\Among Us - Mody\temp\abc\file.config",
            @"C:\Users\Pioko\AppData\Roaming\Among Us - Mody");

        Assert.Contains("file.config", message);
        Assert.Contains(@"Among Us - Mody", message);
        Assert.Contains("antivirus", message);
    }

    [Fact]
    public void FormatExtractionAccessDeniedMessage_WithDetectedAntivirus_NamesProducts()
    {
        var message = InstallPathWriteHelper.FormatExtractionAccessDeniedMessage(
            @"C:\mods\temp\blocked.config",
            @"C:\mods",
            new[] { "Avast", "Malwarebytes" });

        Assert.Contains("Avast", message);
        Assert.Contains("Malwarebytes", message);
        Assert.Contains(@"C:\mods", message);
        Assert.Contains("antivirus exclusions", message);
        Assert.Contains("Controlled Folder Access", message);
        Assert.DoesNotContain("Najczęstsza przyczyna", message);
    }

    [Fact]
    public void FormatExtractionAccessDeniedMessage_UsesCachedAntivirusDetection()
    {
        AntivirusDetectionService.SetLastDetectionResult(new AntivirusDetectionResult(new[] { "ESET" }));

        try
        {
            var message = InstallPathWriteHelper.FormatExtractionAccessDeniedMessage(
                @"C:\mods\temp\x.dll",
                @"C:\mods");

            Assert.Contains("ESET", message);
            Assert.Contains("antivirus exclusions", message);
        }
        finally
        {
            AntivirusDetectionService.SetLastDetectionResult(AntivirusDetectionResult.Empty);
        }
    }

    [Fact]
    public void RunWithWriteRetry_SucceedsAfterClearingReadOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "susmodder-retry-" + Guid.NewGuid().ToString("N"));
        var targetFile = Path.Combine(root, "blocked.config");
        Directory.CreateDirectory(root);
        File.WriteAllText(targetFile, "old");
        File.SetAttributes(targetFile, FileAttributes.ReadOnly);

        var attempts = 0;

        try
        {
            InstallPathWriteHelper.RunWithWriteRetry(
                () =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new UnauthorizedAccessException($"Access to the path '{targetFile}' is denied.");

                    File.WriteAllText(targetFile, "new");
                },
                root,
                "blocked.config");

            Assert.Equal(2, attempts);
            Assert.Equal("new", File.ReadAllText(targetFile));
        }
        finally
        {
            File.SetAttributes(targetFile, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrepareExtractionDirectoryAsync_CreatesWritableDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "susmodder-prepare-" + Guid.NewGuid().ToString("N"));
        var extractPath = Path.Combine(root, "extractMod");

        try
        {
            Directory.CreateDirectory(extractPath);
            File.WriteAllText(Path.Combine(extractPath, "stale.txt"), "x");

            await InstallPathWriteHelper.PrepareExtractionDirectoryAsync(extractPath);

            Assert.True(Directory.Exists(extractPath));
            Assert.True(InstallPathWriteHelper.TryVerifyWriteAccess(extractPath));
            Assert.False(File.Exists(Path.Combine(extractPath, "stale.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
