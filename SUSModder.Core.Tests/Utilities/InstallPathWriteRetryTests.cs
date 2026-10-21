using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class InstallPathWriteRetryTests
{
    [Fact]
    public void RunWithWriteRetry_PersistentAccessDenied_StopsAfterThreeAttempts()
    {
        var attempts = 0;
        var failure = new UnauthorizedAccessException("Access is denied");
        var missingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var thrown = Assert.Throws<UnauthorizedAccessException>(() =>
            InstallPathWriteHelper.RunWithWriteRetry(() =>
            {
                attempts++;
                throw failure;
            }, missingDirectory));

        Assert.Equal(3, attempts);
        Assert.Same(failure, thrown);
    }

    [Fact]
    public void RunWithWriteRetry_OtherFailure_IsNotRetried()
    {
        var attempts = 0;
        var failure = new IOException("File is in use");

        var thrown = Assert.Throws<IOException>(() =>
            InstallPathWriteHelper.RunWithWriteRetry(() =>
            {
                attempts++;
                throw failure;
            }, Path.GetTempPath()));

        Assert.Equal(1, attempts);
        Assert.Same(failure, thrown);
    }
}
