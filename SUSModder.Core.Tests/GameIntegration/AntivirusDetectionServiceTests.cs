using SUSModder.Core.Services;
using System.Runtime.Versioning;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

[SupportedOSPlatform("windows")]
public sealed class AntivirusDetectionServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SourcesAvailableReflectsEitherSource(bool security, bool registry)
    {
        var result = new AntivirusDetectionService().DetectInstalledThirdPartyAntivirusWindows(
            _ => security, _ => registry);
        Assert.Equal(security || registry, result.SourcesAvailable);
        if (!security && !registry) Assert.Empty(result.ProductNames);
    }

    [Fact]
    public void SignatureDoesNotDependOnSourceAvailability()
    {
        Assert.Equal(new AntivirusDetectionResult(new[] { "Avast" }, true).Signature,
            new AntivirusDetectionResult(new[] { "Avast" }, false).Signature);
        Assert.Same(AntivirusDetectionResult.Empty, AntivirusDetectionResult.Empty);
    }
}
