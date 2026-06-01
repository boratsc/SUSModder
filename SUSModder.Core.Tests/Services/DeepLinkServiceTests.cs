using System.Runtime.Versioning;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

[SupportedOSPlatform("windows")]

public class DeepLinkServiceTests
{
    [Fact]
    public void ParseDeepLink_ValidUri_ReturnsValid()
    {
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-JKLM");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
        Assert.False(result.AutoInstall);
    }

    [Fact]
    public void ParseDeepLink_ValidUriWithAutoInstall_ReturnsAutoInstallTrue()
    {
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-JKLM?install=1");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
        Assert.True(result.AutoInstall);
    }

    [Fact]
    public void ParseDeepLink_ValidUriWithInstall0_ReturnsAutoInstallFalse()
    {
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-JKLM?install=0");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
        Assert.False(result.AutoInstall);
    }

    [Fact]
    public void ParseDeepLink_PlainCode_ReturnsValid()
    {
        var result = DeepLinkService.ParseDeepLink("ABCD-EFGH-JKLM");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
        Assert.False(result.AutoInstall);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-pack-code")]
    [InlineData("susmodder://not-pack/CODE")]
    [InlineData("https://susmodder.app/pack/CODE")]
    [InlineData("ABCD-EFGH")] // za krotki
    public void ParseDeepLink_InvalidInput_ReturnsInvalid(string? input)
    {
        var result = DeepLinkService.ParseDeepLink(input);

        Assert.False(result.IsValid);
        Assert.Null(result.PackCode);
        Assert.False(result.AutoInstall);
    }

    [Fact]
    public void ParseDeepLink_PlainCodeLowercase_NotValidWithoutNormalize()
    {
        // IsValid() sprawdza regex uppercase-only przed normalizacją.
        // Używamy kodu z 'o' (→O, które jest wykluczone z alfabetu).
        var result = DeepLinkService.ParseDeepLink("abcd-efgh-opqr");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParseDeepLink_UriLowercaseCode_NormalizedByUriParser()
    {
        // Przez URI: parser zachowuje case, ale TryExtractPackCode + IsValid odrzuca.
        // Używamy kodu z 'o' (→O, które jest wykluczone z alfabetu).
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/abcd-efgh-opqr");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParseDeepLink_UriWithExtraQueryParams_ReturnsValid()
    {
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-JKLM?foo=bar&install=1&baz=qux");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
        Assert.True(result.AutoInstall);
    }

    [Fact]
    public void ParseDeepLink_ArgFromRegistry_QuotesRemoved()
    {
        // Rejestr Windows przekazuje argument w cudzysłowie: "susmodder://pack/CODE"
        var result = DeepLinkService.ParseDeepLink("\"susmodder://pack/ABCD-EFGH-JKLM\"");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
    }

    [Fact]
    public void ParseDeepLink_UriWithTrailingSlash_ReturnsValid()
    {
        // susmodder://pack/CODE/ — z koñcowym slash
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-JKLM/");

        Assert.True(result.IsValid);
        Assert.Equal("ABCD-EFGH-JKLM", result.PackCode);
    }

    [Fact]
    public void ParseDeepLink_InvalidCharsInCode_ReturnsInvalid()
    {
        // I, O, 0, 1 s¹ niedozwolone w kodzie
        var result = DeepLinkService.ParseDeepLink("susmodder://pack/ABCD-EFGH-IJKO");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ParseDeepLink_ProtocolScheme_IsCorrect()
    {
        Assert.Equal("susmodder", DeepLinkService.ProtocolScheme);
    }
}
