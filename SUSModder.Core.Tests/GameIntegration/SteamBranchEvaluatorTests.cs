using SUSModder.Core.GameIntegration.Steam;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public sealed class SteamBranchEvaluatorTests
{
    private static SteamBranchResult Evaluate(string? user, string? mounted = null) =>
        SteamBranchEvaluator.Evaluate(new SteamAppManifest { UserConfigBetaKey = user, MountedConfigBetaKey = mounted });

    [Fact]
    public void NullIsUnknown() => Assert.Equal(SteamBranchStatus.Unknown, SteamBranchEvaluator.Evaluate(null).Status);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("public")]
    public void MissingOrPublicIsDefault(string? branch) => Assert.Equal(SteamBranchStatus.Default, Evaluate(branch).Status);

    [Theory]
    [InlineData("public-beta")]
    [InlineData("public-previous")]
    [InlineData("named")]
    public void NamedBranchIsNonDefault(string branch)
    {
        var result = Evaluate(branch);
        Assert.Equal(SteamBranchStatus.NonDefault, result.Status);
        Assert.Equal(branch, result.BranchName);
    }

    [Theory]
    [InlineData(null, "public-beta")]
    [InlineData("public-beta", "")]
    [InlineData("public-beta", "public")]
    public void ConflictingMountedConfigIsInconsistent(string? user, string mounted) =>
        Assert.Equal(SteamBranchStatus.Inconsistent, Evaluate(user, mounted).Status);

    [Fact]
    public void EqualPublicConfigsAreDefault() => Assert.Equal(SteamBranchStatus.Default, Evaluate("public", "public").Status);

    [Fact]
    public void BranchComparisonIsCaseInsensitive() =>
        Assert.Equal(SteamBranchStatus.NonDefault, Evaluate("public-beta", "PUBLIC-BETA").Status);
}
