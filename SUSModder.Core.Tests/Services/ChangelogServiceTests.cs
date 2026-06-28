using SUSModder.Core.Services;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class ChangelogServiceTests
{
    [Fact]
    public void IsNewerVersion_EmptyLastSeen_ReturnsFalse()
    {
        var service = new ChangelogService();
        Assert.False(service.IsNewerVersion("3.0.0", string.Empty));
    }

    [Fact]
    public void IsNewerVersion_UpgradeFrom290To300_ReturnsTrue()
    {
        var service = new ChangelogService();
        Assert.True(service.IsNewerVersion("3.0.0", "2.9.0"));
    }

    [Fact]
    public void IsNewerVersion_SameVersion_ReturnsFalse()
    {
        var service = new ChangelogService();
        Assert.False(service.IsNewerVersion("3.0.0", "3.0.0"));
    }
}
