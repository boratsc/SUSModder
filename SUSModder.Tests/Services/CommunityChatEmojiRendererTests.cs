using SUSModder.Services;
using Xunit;

namespace SUSModder.Tests.Services;

public class CommunityChatEmojiRendererTests
{
    [Theory]
    [InlineData("Cześć :)", "Cześć 🙂")]
    [InlineData("Cześć :-)", "Cześć 🙂")]
    [InlineData("Cześć ;)", "Cześć 😉")]
    [InlineData("Cześć ;-)", "Cześć 😉")]
    [InlineData("Cześć :D", "Cześć 😄")]
    [InlineData("Cześć :P", "Cześć 😛")]
    [InlineData("Cześć :(", "Cześć ☹️")]
    [InlineData("Kocham was <3", "Kocham was ❤️")]
    [InlineData("Cześć, :)", "Cześć, 🙂")]
    [InlineData("(:) ", "(🙂 ")]
    public void Render_ConvertsSupportedEmoticons(string input, string expected)
    {
        Assert.Equal(expected, CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_ConvertsMultipleEmoticonsInOneMessage()
    {
        Assert.Equal("🙂 😉 😄", CommunityChatEmojiRenderer.Render(":) ;) :D"));
    }

    [Fact]
    public void Render_KeepsHttpUrlUnchanged()
    {
        const string input = "Wejdź na https://example.com/:)";

        Assert.Equal(input, CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_KeepsWwwUrlUnchanged()
    {
        const string input = "Zobacz www.example.com/:)";

        Assert.Equal(input, CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_KeepsUnknownSchemeUnchanged()
    {
        const string input = "s://abc";

        Assert.Equal(input, CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_KeepsEmailUnchanged()
    {
        const string input = "Napisz na ktos@example.com :)";

        Assert.Equal("Napisz na ktos@example.com 🙂", CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_ConvertsEmoticonNextToUrlButNotInsideIt()
    {
        const string input = "https://discord.gg/abc :)";

        Assert.Equal("https://discord.gg/abc 🙂", CommunityChatEmojiRenderer.Render(input));
    }

    [Fact]
    public void Render_DoesNotConvertEmoticonGluedToWord()
    {
        Assert.Equal("hej:)", CommunityChatEmojiRenderer.Render("hej:)"));
        Assert.Equal("axD", CommunityChatEmojiRenderer.Render("axD"));
    }

    [Fact]
    public void Render_NormalizesDashVariant()
    {
        Assert.Equal("🙂", CommunityChatEmojiRenderer.Render(":-)"));
        Assert.Equal("☹️", CommunityChatEmojiRenderer.Render(":-("));
    }

    [Fact]
    public void Render_ReturnsSameInstanceWhenNothingChanges()
    {
        const string input = "Zwykła wiadomość bez emotikon";

        Assert.Same(input, CommunityChatEmojiRenderer.Render(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_EmptyInput_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, CommunityChatEmojiRenderer.Render(input));
    }
}
