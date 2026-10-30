using SUSModder.Core.Models;
using SUSModder.Core.Validators;
using Xunit;

namespace SUSModder.Core.Tests.Validators;

public class CommunityChatMessageValidatorTests
{
    private const string ValidText = "abcdefghij";

    // ── Długość (10–280 jednostek UTF-16 po trim) ───────────────────────────

    [Fact]
    public void Validate_NineCharacters_ReturnsTooShort()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abcdefghi");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void Validate_TenCharacters_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(ValidText);

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_TwoHundredEightyCharacters_ReturnsValid()
    {
        var content = new string('a', 280);

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_TwoHundredEightyOneCharacters_ReturnsTooLong()
    {
        var content = new string('a', 281);

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooLong, errorCode);
    }

    [Fact]
    public void Validate_WhitespaceAroundContent_MeasuresTrimmedLength()
    {
        var content = "   " + ValidText + "   ";

        Assert.Equal(10, CommunityChatMessageValidator.MeasureLength(content));

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_OnlyWhitespace_ReturnsTooShort()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("        ");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void Validate_EmptyString_ReturnsTooShort()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(string.Empty);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void Validate_Null_ReturnsTooShort()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(null);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void MeasureLength_Emoji_CountsUtf16CodeUnits()
    {
        // 5 emoji = 10 jednostek UTF-16 (każde emoji to para surrogate).
        var fiveEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 5));

        Assert.Equal(10, CommunityChatMessageValidator.MeasureLength(fiveEmoji));

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(fiveEmoji);
        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_FourEmoji_ReturnsTooShort_BecauseUtf16Units()
    {
        // 4 emoji = 8 jednostek UTF-16, 4 klastry graficzne — limit liczy UTF-16, nie grafy.
        var fourEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 4));

        Assert.Equal(8, CommunityChatMessageValidator.MeasureLength(fourEmoji));

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(fourEmoji);
        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void Validate_CombiningCharacters_CountsCodeUnits()
    {
        // "e" + combining acute = 2 jednostki UTF-16.
        var content = "e\u0301e\u0301e\u0301e\u0301e\u0301";

        Assert.Equal(10, CommunityChatMessageValidator.MeasureLength(content));

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);
        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    // ── Znaki sterujące ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_ControlCharacter_ReturnsControlCharacterError()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abcdefghij\u0001");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsControlCharacter, errorCode);
    }

    [Fact]
    public void Validate_PastedNewline_ReturnsNewlineError()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abcdefghij\nklmnop");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    [Fact]
    public void Validate_CarriageReturn_ReturnsNewlineError()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abcdefghij\r\nklmnop");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    // ── Reguła linków Discord ───────────────────────────────────────────────

    [Fact]
    public void Validate_HttpsDiscordInvite_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("join discord.gg/abc123 now");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_DiscordInviteWithoutScheme_ReturnsValid()
    {
        // Link bez schematu to nadal link zaproszenia — nie może być odrzucany.
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("join discord.gg/abc123 now");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_DiscordInviteUpperCase_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("JOIN HTTPS://DISCORD.GG/ABC123 NOW");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_SpoofedDomainDiscordGgEvilCom_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg.evil.com/abc123");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_SpoofedDomainNotDiscordGg_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("https://notdiscord.gg/abc123");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_DiscordAppCom_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("https://discordapp.com/invite");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_OtherHostWithDiscordPath_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("https://example.com/discord.gg/abc123");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_RegularUrl_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("https://example.com/abc");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_WwwHost_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("www.example.com");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_TwoDiscordInvites_ReturnsTooManyLinks()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123 and discord.gg/def456");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.TooManyLinks, errorCode);
    }

    [Fact]
    public void Validate_DiscordInviteAndOtherLink_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123 and https://example.com");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_DiscordInviteWithExtraPath_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("https://discord.gg/abc123/extra");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_DiscordHostWithoutCode_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("visit discord.gg now");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_FileNameLikeToken_IsNotTreatedAsLink()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("readme.md describes notes.txt");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_WindowsPath_IsNotTreatedAsLink()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("graj w C:/Users/Player/Game");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_TokenWithUpperCaseSuffix_IsNotTreatedAsLink()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("ends with e.g. and more text");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void FindLinks_CountsBareDiscordInviteOnce()
    {
        var links = CommunityChatMessageValidator.FindLinks("discord.gg/abc123");

        Assert.Single(links);
        Assert.True(CommunityChatMessageValidator.IsDiscordInviteLink(links[0]));
    }

    [Fact]
    public void IsDiscordInviteLink_RejectsUserInfoPrefix()
    {
        Assert.False(CommunityChatMessageValidator.IsDiscordInviteLink("https://user@discord.gg/abc123"));
    }

    [Fact]
    public void IsDiscordInviteLink_RejectsPortInHost()
    {
        Assert.False(CommunityChatMessageValidator.IsDiscordInviteLink("https://discord.gg:443/abc123"));
    }

    [Fact]
    public void IsDiscordInviteLink_RejectsNonHttpScheme()
    {
        Assert.False(CommunityChatMessageValidator.IsDiscordInviteLink("ftp://discord.gg/abc123"));
    }

    // ── Znaki sterujące sprawdzane w oryginalnej treści (przed trimem) ──────

    [Theory]
    [InlineData("\nabcdefghij")]
    [InlineData("abcdefghij\n")]
    [InlineData("\rabcdefghij")]
    [InlineData("abcdefghij\r")]
    [InlineData("\r\nabcdefghij")]
    [InlineData("abcdefghij\r\n")]
    [InlineData("\nabcdefghij\n")]
    public void Validate_LeadingOrTrailingNewline_IsNotSilentlyTrimmed(string content)
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    [Theory]
    [InlineData("\tabcdefghij")]
    [InlineData("abcdefghij\t")]
    [InlineData("\tabcdefghij\t")]
    public void Validate_LeadingOrTrailingTab_IsNotSilentlyTrimmed(string content)
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsControlCharacter, errorCode);
    }

    [Fact]
    public void Validate_InnerTab_ReturnsControlCharacterError()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abc\tdefghij");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsControlCharacter, errorCode);
    }

    [Fact]
    public void Validate_TooShortWithTrailingNewline_ReturnsNewlineError()
    {
        // Kontrola znaków sterujących ma pierwszeństwo przed pomiarem długości.
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("abc\n");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    [Fact]
    public void Validate_WhitespaceOnly_ReturnsTooShort()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("     ");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentTooShort, errorCode);
    }

    [Fact]
    public void Validate_NewlineOnly_ReturnsNewlineError()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("\n");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    [Fact]
    public void Validate_SpacesAroundValidMessage_ReturnsValid()
    {
        // Zwykłe spacje nadal są obcinane i nie liczone w długości.
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("   abcdefghij   ");

        Assert.True(isValid);
        Assert.Null(errorCode);
        Assert.Equal(10, CommunityChatMessageValidator.MeasureLength("   abcdefghij   "));
    }

    [Fact]
    public void Validate_TooLongWithTrailingNewline_ReturnsNewlineError()
    {
        var content = new string('a', 281) + "\n";

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.ContentContainsNewline, errorCode);
    }

    // ── Domeny z cyframi i myślnikami (obejście reguły allowlist) ───────────

    [Fact]
    public void Validate_BareDomainWithHyphen_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("odwiedz evil-site.com teraz");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_BareDomainStartingWithDigits_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("odwiedz 123example.com teraz");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_BareDomainWithHyphenAndPath_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("odwiedz evil-site.com/path teraz");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_SpoofedDiscordDomainWithDigits_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("123discord.gg/abc123");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void Validate_SpoofedDiscordDomainWithHyphen_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord-gg.com/abc123");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    // ── Interpunkcja przy zaproszeniu ───────────────────────────────────────

    [Fact]
    public void Validate_TwoInvitesSeparatedByComma_ReturnsTooManyLinks()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123,discord.gg/def456");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.TooManyLinks, errorCode);
    }

    [Fact]
    public void Validate_TwoInvitesSeparatedByCommaAndSpace_ReturnsTooManyLinks()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123, discord.gg/def456");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.TooManyLinks, errorCode);
    }

    [Fact]
    public void Validate_TwoInvitesSeparatedBySemicolon_ReturnsTooManyLinks()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123;discord.gg/def456");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.TooManyLinks, errorCode);
    }

    [Fact]
    public void Validate_OneInviteFollowedByComma_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("join discord.gg/abc123, thanks");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_OneInviteFollowByPeriod_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("zaproszenie discord.gg/abc123.");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_OneInviteInParentheses_ReturnsValid()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("zaproszenie (discord.gg/abc123)");

        Assert.True(isValid);
        Assert.Null(errorCode);
    }

    [Fact]
    public void Validate_OneInviteFollowedByDisallowedLink_ReturnsDisallowedUrl()
    {
        var (isValid, errorCode) = CommunityChatMessageValidator.Validate("discord.gg/abc123,evil-site.com");

        Assert.False(isValid);
        Assert.Equal(CommunityChatErrorCodes.DisallowedUrl, errorCode);
    }

    [Fact]
    public void FindLinks_SplitsInvitesSeparatedByPunctuation()
    {
        var links = CommunityChatMessageValidator.FindLinks("discord.gg/abc123,discord.gg/def456");

        Assert.Equal(2, links.Count);
    }
}
