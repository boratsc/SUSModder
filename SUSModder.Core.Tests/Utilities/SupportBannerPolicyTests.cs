using System;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Utilities;

public class SupportBannerPolicyTests
{
    [Fact]
    public void ShouldShow_WhenNeverDismissed_ReturnsTrue()
    {
        Assert.True(SupportBannerPolicy.ShouldShow(null));
        Assert.True(SupportBannerPolicy.ShouldShow(""));
        Assert.True(SupportBannerPolicy.ShouldShow("   "));
    }

    [Fact]
    public void ShouldShow_WhenDismissedRecently_ReturnsFalse()
    {
        var now = DateTimeOffset.Parse("2026-07-24T12:00:00Z");
        var dismissed = now.AddDays(-3).ToString("O");
        Assert.False(SupportBannerPolicy.ShouldShow(dismissed, now));
    }

    [Fact]
    public void ShouldShow_WhenDismissedSevenDaysAgo_ReturnsTrue()
    {
        var now = DateTimeOffset.Parse("2026-07-24T12:00:00Z");
        var dismissed = now.AddDays(-7).ToString("O");
        Assert.True(SupportBannerPolicy.ShouldShow(dismissed, now));
    }

    [Fact]
    public void ShouldShow_WhenInvalidTimestamp_ReturnsTrue()
    {
        Assert.True(SupportBannerPolicy.ShouldShow("not-a-date"));
    }
}
