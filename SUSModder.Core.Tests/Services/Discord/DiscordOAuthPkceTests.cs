using System.Security.Cryptography;
using System.Text;
using SUSModder.Core.Services.Discord;

namespace SUSModder.Core.Tests.Services.Discord;

public sealed class DiscordOAuthPkceTests
{
    [Fact]
    public void GenerateCodeVerifier_ReturnsUrlSafeBase64WithoutPadding()
    {
        var verifier = DiscordOAuthPkce.GenerateCodeVerifier();

        Assert.False(string.IsNullOrWhiteSpace(verifier));
        Assert.DoesNotContain('=', verifier);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesUniqueValues()
    {
        var a = DiscordOAuthPkce.GenerateCodeVerifier();
        var b = DiscordOAuthPkce.GenerateCodeVerifier();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateCodeChallenge_IsDeterministicForSameVerifier()
    {
        const string verifier = "test-verifier-12345";
        var challenge1 = DiscordOAuthPkce.GenerateCodeChallenge(verifier);
        var challenge2 = DiscordOAuthPkce.GenerateCodeChallenge(verifier);

        Assert.Equal(challenge1, challenge2);
        Assert.DoesNotContain('=', challenge1);
    }

    [Fact]
    public void GenerateCodeChallenge_MatchesPkceSpec()
    {
        const string verifier = "test-verifier";
        var expectedBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var expected = Convert.ToBase64String(expectedBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, DiscordOAuthPkce.GenerateCodeChallenge(verifier));
    }

    [Fact]
    public void GenerateState_Returns64CharLowerHex()
    {
        var state = DiscordOAuthPkce.GenerateState();

        Assert.Equal(64, state.Length);
        Assert.Matches("^[0-9a-f]{64}$", state);
    }

    [Fact]
    public void ValidateState_MatchingStates_ReturnsValid()
    {
        const string state = "abc123";
        var result = DiscordOAuthPkce.ValidateState(state, state);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateState_MissingExpected_ReturnsInvalid()
    {
        var result = DiscordOAuthPkce.ValidateState(null, "abc");

        Assert.False(result.IsValid);
        Assert.Contains("Sesja OAuth", result.ErrorMessage);
    }

    [Fact]
    public void ValidateState_MissingCallback_ReturnsInvalid()
    {
        var result = DiscordOAuthPkce.ValidateState("abc", null);

        Assert.False(result.IsValid);
        Assert.Contains("state", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateState_Mismatch_ReturnsInvalid()
    {
        var result = DiscordOAuthPkce.ValidateState("expected", "other");

        Assert.False(result.IsValid);
        Assert.Contains("Niezgodność", result.ErrorMessage);
    }
}
