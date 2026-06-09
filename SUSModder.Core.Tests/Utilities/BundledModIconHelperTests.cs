using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;
using Xunit;

namespace SUSModder.Core.Tests.Utilities;

public class BundledModIconHelperTests
{
    [Fact]
    public void NormalizeVanillaIconReference_RewritesCdnUrlToLocalFileName()
    {
        var normalized = BundledModIconHelper.NormalizeVanillaIconReference(
            "https://susmodder.app/icons/Vanilla.png");

        Assert.Equal("Vanilla.png", normalized);
    }

    [Fact]
    public void IsVanillaMod_MatchesIdZeroAndModType()
    {
        Assert.True(BundledModIconHelper.IsVanillaMod(new ModConfiguration
        {
            Id = 0,
            ModName = "AmongUs",
            ModType = "Vanilla"
        }));

        Assert.False(BundledModIconHelper.IsVanillaMod(new ModConfiguration
        {
            Id = 1,
            ModName = "Town of Us",
            ModType = "full"
        }));
    }
}
