using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class AnonymousUserHashTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("2a4bb6c85cd24aa280fd33dabe6cf6e8")] // GUID "N" = 32 hex — bug historyczny
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase
    [InlineData("g111111111111111111111111111111111111111111111111111111111111111")] // non-hex
    public void IsValid_RejectsInvalidFormats(string? value)
    {
        Assert.False(AnonymousUserHash.IsValid(value));
    }

    [Fact]
    public void IsValid_AcceptsLowercase64Hex()
    {
        var hash = new string('a', 64);
        Assert.True(AnonymousUserHash.IsValid(hash));
    }

    [Fact]
    public void CreateFallback_ReturnsValid64Hex_NotRawGuid()
    {
        var hash = AnonymousUserHash.CreateFallback();

        Assert.True(AnonymousUserHash.IsValid(hash));
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(32, hash.Length);
    }

    [Fact]
    public void EnsureValid_RehashesInvalidGuidFallback()
    {
        var rawGuid = Guid.NewGuid().ToString("N");
        Assert.Equal(32, rawGuid.Length);

        var fixedHash = AnonymousUserHash.EnsureValid(rawGuid);

        Assert.True(AnonymousUserHash.IsValid(fixedHash));
        Assert.NotEqual(rawGuid, fixedHash);
    }

    [Fact]
    public void EnsureValid_KeepsAlreadyValidHash()
    {
        var valid = new string('b', 64);
        Assert.Equal(valid, AnonymousUserHash.EnsureValid(valid));
    }
}
