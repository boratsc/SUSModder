using SUSModder.Core.GameIntegration.Steam;
using Xunit;

namespace SUSModder.Core.Tests.GameIntegration;

public sealed class SteamAppManifestTests
{
    private static SteamAppManifest? Parse(string body) => SteamAppManifest.TryParse(body);
    private static string Wrap(string body) => $"\"AppState\" {{ {body} }}";

    [Theory]
    [InlineData("", null)]
    [InlineData("\"betakey\" \"\"", "")]
    [InlineData("\"betakey\" \"public\"", "public")]
    [InlineData("\"betakey\" \"public-beta\"", "public-beta")]
    [InlineData("\"betakey\" \"public-previous\"", "public-previous")]
    [InlineData("\"betakey\" \"mystery\"", "mystery")]
    public void ReadsUserConfigBetaKey(string value, string? expected)
    {
        var manifest = Parse(Wrap($"\"UserConfig\" {{ {value} }}"));
        Assert.Equal(expected, manifest?.UserConfigBetaKey);
    }

    [Fact]
    public void ParsesCaseInsensitivelyAndReadsConfigsSeparately()
    {
        var manifest = Parse("\"appstate\" { \"APPID\" \"945360\" \"USERCONFIG\" { \"BETAKEY\" \"test\" } \"mountedconfig\" { \"betakey\" \"other\" } }");
        Assert.Equal("945360", manifest?.AppId);
        Assert.Equal("test", manifest?.UserConfigBetaKey);
        Assert.Equal("other", manifest?.MountedConfigBetaKey);
    }

    [Theory]
    [InlineData("\"StateFlags\" \"4\"", true)]
    [InlineData("\"StateFlags\" \"6\"", true)]
    [InlineData("\"StateFlags\" \"1026\"", false)]
    [InlineData("", false)]
    public void FullyInstalledUsesStateFlagFour(string flags, bool expected) =>
        Assert.Equal(expected, Parse(Wrap(flags))?.IsFullyInstalled);

    [Fact]
    public void ParsesEscapesAndNestedObjects()
    {
        var manifest = Parse("\"AppState\" { \"installdir\" \"C:\\\\Games\\\\A \\\"B\\\"\" \"MountedDepots\" { \"1\" { \"manifest\" \"x\" } } }");
        Assert.Equal("C:\\Games\\A \"B\"", manifest?.InstallDir);
    }

    [Theory]
    [InlineData("\"BetaKey\" \"ignored\" \"AppState\" { \"appid\" \"1\" }")]
    [InlineData("\"AppState\" { \"appid\" \"1\"")]
    [InlineData("\"AppState\" { \"x\" \"unterminated }")]
    [InlineData("garbage")]
    [InlineData("")]
    public void InvalidOrOutOfScopeDataIsHandled(string content)
    {
        if (content.Contains("BetaKey"))
            Assert.Null(Parse(content)?.UserConfigBetaKey);
        else
            Assert.Null(Parse(content));
    }

    [Fact]
    public void TryReadMissingFileReturnsNull() =>
        Assert.Null(SteamAppManifest.TryRead(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
}
