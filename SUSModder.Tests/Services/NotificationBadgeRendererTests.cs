using SUSModder.Services;
using Xunit;

namespace SUSModder.Tests.Services;

public class NotificationBadgeRendererTests
{
    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(100, "9+")]
    public void Maps_count_to_badge_label(int count, string? expected) =>
        Assert.Equal(expected, NotificationBadgeRenderer.GetLabel(count));
}
