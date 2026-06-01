using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Utilities;

public class ModPackCodeValidatorTests
{
    [Theory]
    [InlineData("ABCD-EFGH-JKLM")]
    [InlineData("MNPQ-RSTU-VWXY")]
    [InlineData("K7FG-8H2J-3L5N")]
    [InlineData("AAAA-BBBB-CCCC")]
    [InlineData("2222-3333-4444")]
    public void IsValid_ValidCode_ReturnsTrue(string code)
    {
        Assert.True(ModPackCodeValidator.IsValid(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC-DEF-GHI")] // za krotkie grupy
    [InlineData("ABCD-EFGH-IJKL-MNOP")] // za duzo grup
    [InlineData("ABCDE-FGHI-JKLM")] // za dluga grupa
    [InlineData("ABCD-EFGH-ijk")] // niellegalne znaki
    [InlineData("ABCD-EF00-OOOO")] // 0 i O to niedozwolone znaki
    [InlineData("ABCD-EF11-1111")] // 1 to niedozwolony znak
    [InlineData("ABCD-EFGH-I")] // za krotki
    public void IsValid_InvalidCode_ReturnsFalse(string? code)
    {
        Assert.False(ModPackCodeValidator.IsValid(code));
    }

    [Theory]
    [InlineData("abcd-efgh-jklm", "ABCD-EFGH-JKLM")]
    [InlineData("  ABCD-EFGH-JKLM  ", "ABCD-EFGH-JKLM")]
    [InlineData("MnPq-RsTu-VwXy", "MNPQ-RSTU-VWXY")]
    public void Normalize_ConvertsToUpperAndTrims(string input, string expected)
    {
        Assert.Equal(expected, ModPackCodeValidator.Normalize(input));
    }

    [Fact]
    public void IsValid_LowercaseRejected_WithoutNormalize()
    {
        // lowercase bez Normalize() powinien byc odrzucony przez IsValid
        // (uzywamy kodu z 'o', bo O jest wykluczone z alfabetu)
        Assert.False(ModPackCodeValidator.IsValid("abcd-efgh-opqr"));
    }

    [Fact]
    public void IsValid_ValidCodeAfterNormalize_ReturnsTrue()
    {
        var normalized = ModPackCodeValidator.Normalize("abcd-efgh-jklm");
        Assert.True(ModPackCodeValidator.IsValid(normalized));
    }

    [Theory]
    [InlineData("III-III-III")] // I = niedozwolone
    [InlineData("OOOO-OOOO-OOOO")] // O = niedozwolone
    [InlineData("0000-0000-0000")] // 0 = niedozwolone
    [InlineData("1111-1111-1111")] // 1 = niedozwolone
    public void IsValid_AmbiguousCharacters_ReturnsFalse(string code)
    {
        Assert.False(ModPackCodeValidator.IsValid(code));
    }
}
