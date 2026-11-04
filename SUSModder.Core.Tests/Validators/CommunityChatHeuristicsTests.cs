using SUSModder.Core.Validators;
using Xunit;

namespace SUSModder.Core.Tests.Validators;

public class CommunityChatHeuristicsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_EmptyContent_HasNoSignals(string? content)
    {
        var result = CommunityChatHeuristics.Analyze(content);

        Assert.False(result.ShouldWarn);
        Assert.Empty(result.SignalCodes);
    }

    [Theory]
    [InlineData("Szukam ekipy do Town of Us, piszcie na Discordzie.")]
    [InlineData("Cześć :) miło was widzieć")]
    [InlineData("TO JEST WAZNE OGLOSZENIE")]
    [InlineData("jedno przekleństwo chuj nie jest spamem")]
    [InlineData("ok ok ok")]
    public void Analyze_NormalMessage_HasNoSignals(string content)
    {
        var result = CommunityChatHeuristics.Analyze(content);

        Assert.False(result.ShouldWarn);
    }

    [Theory]
    [InlineData("chujchujchuj")]
    [InlineData("aaaaaaaaaa")]
    [InlineData("hahaha")]
    [InlineData("abcabcabc")]
    public void Analyze_RepeatedUnit_FlagsRepeatedCharacters(string content)
    {
        var result = CommunityChatHeuristics.Analyze(content);

        Assert.True(result.ShouldWarn);
        Assert.Contains(CommunityChatHeuristics.RepeatedCharacters, result.SignalCodes);
    }

    [Fact]
    public void Analyze_SameWordRepeated_FlagsRepeatedWords()
    {
        var result = CommunityChatHeuristics.Analyze("siema siema siema siema");

        Assert.True(result.ShouldWarn);
        Assert.Contains(CommunityChatHeuristics.RepeatedWords, result.SignalCodes);
    }

    [Fact]
    public void Analyze_ExcessivePunctuation_FlagsPunctuation()
    {
        var result = CommunityChatHeuristics.Analyze("Kto gra??????");

        Assert.True(result.ShouldWarn);
        Assert.Contains(CommunityChatHeuristics.ExcessivePunctuation, result.SignalCodes);
    }

    [Fact]
    public void Analyze_UrlWithRepeatedCharacters_IsIgnored()
    {
        var result = CommunityChatHeuristics.Analyze("https://example.com/aaaaaaaaaa");

        Assert.False(result.ShouldWarn);
    }

    [Fact]
    public void Analyze_EmailWithRepeatedCharacters_IsIgnored()
    {
        var result = CommunityChatHeuristics.Analyze("napisz na aaaaaaaaaa@example.com");

        Assert.False(result.ShouldWarn);
    }

    [Fact]
    public void Analyze_ResultCodesAreStable()
    {
        var result = CommunityChatHeuristics.Analyze("!!!!!!");

        Assert.Equal(new[] { CommunityChatHeuristics.ExcessivePunctuation }, result.SignalCodes);
    }
}
