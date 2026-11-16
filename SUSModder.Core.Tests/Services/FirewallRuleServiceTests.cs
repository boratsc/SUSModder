using SUSModder.Core.Services;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class FirewallRuleServiceTests
{
    private const string Expected = @"C:\Users\borat\AppData\Local\Temp\opencode\probe-żółć-spacje\future game.exe";
    private static string Block(string name, string program) =>
        $"Rule Name:                            {name}{Environment.NewLine}---{Environment.NewLine}Program:                              {program}{Environment.NewLine}Action:                               Allow{Environment.NewLine}Ok.";

    [Theory]
    [InlineData("Mod", "Among Us - Mod")]
    [InlineData("a\"b\nc", "Among Us - a b c")]
    [InlineData("", "Among Us - Among Us")]
    [InlineData(" \t ", "Among Us - Among Us")]
    public void BuildRuleBaseName(string value, string expected) =>
        Assert.Equal(expected, FirewallRuleService.BuildRuleBaseName(value));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("dir\\x.exe")]
    [InlineData("C:\\bad\"path.exe")] [InlineData("C:\\bad\npath.exe")] [InlineData("C:\\bad\u0001path.exe")]
    public void InvalidPathsRejected(string? path) => Assert.False(FirewallRuleService.IsValidProgramPath(path));

    [Theory]
    [InlineData(@"C:\Games\future game.exe")]
    [InlineData(@"C:\Gry\żółć gra.exe")]
    public void RootedPathAccepted(string path) => Assert.True(FirewallRuleService.IsValidProgramPath(path));

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 4)]
    public void ScriptHasExactCommands(bool delete, int count)
    {
        var lines = FirewallRuleService.BuildNetshScript(new("Among Us - Mod", @"C:\Game\Among Us.exe"), delete).Split(Environment.NewLine);
        var offset = delete ? 2 : 0;
        Assert.Equal(count, lines.Length);
        if (delete)
        {
            Assert.Equal("advfirewall firewall delete rule name=\"Among Us - Mod (Inbound)\"", lines[0]);
            Assert.Equal("advfirewall firewall delete rule name=\"Among Us - Mod (Outbound)\"", lines[1]);
        }
        Assert.Equal("advfirewall firewall add rule name=\"Among Us - Mod (Inbound)\" dir=in action=allow program=\"C:\\Game\\Among Us.exe\" enable=yes", lines[offset]);
        Assert.Equal("advfirewall firewall add rule name=\"Among Us - Mod (Outbound)\" dir=out action=allow program=\"C:\\Game\\Among Us.exe\" enable=yes", lines[offset + 1]);
        Assert.Equal("advfirewall firewall show rule name=\"Among Us - Mod (Inbound)\" verbose", lines[offset + 2]);
        Assert.Equal("advfirewall firewall show rule name=\"Among Us - Mod (Outbound)\" verbose", lines[offset + 3]);
    }

    [Fact] public void InvalidScriptPathThrows() => Assert.Throws<ArgumentException>(() => FirewallRuleService.BuildNetshScript(new("x", "relative"), false));
    [Theory]
    [InlineData("bad\"name")]
    [InlineData("bad\nname")]
    [InlineData("bad\u0001name")]
    [InlineData("")]
    public void InvalidRuleBaseNameThrows(string name) =>
        Assert.Throws<ArgumentException>(() => FirewallRuleService.BuildNetshScript(new(name, @"C:\Game\Among Us.exe"), false));
    [Fact]
    public void ValidUnicodeRuleBaseNameBuildsScript() =>
        Assert.Contains("Żółć i Among Us", FirewallRuleService.BuildNetshScript(new("Żółć i Among Us", @"C:\Game\Among Us.exe"), false));
    [Fact] public void NullScriptRequestThrows() => Assert.Throws<ArgumentNullException>(() => FirewallRuleService.BuildNetshScript(null!, false));
    [Fact] public void ParsesProgramLine() => Assert.Equal(Expected, FirewallRuleService.ParseProgramPath($"Program:                              {Expected}"));
    [Fact] public void MissingProgramIsNull() => Assert.Null(FirewallRuleService.ParseProgramPath("Rule Name: x"));
    [Fact] public void NullProgramOutputIsNull() => Assert.Null(FirewallRuleService.ParseProgramPath(null!));
    [Fact] public void OwnershipRequiresMatchingProgram() => Assert.True(FirewallRuleService.IsOwnRule("Program: C:\\Game\\a.exe", @"c:\game\a.exe"));
    [Fact] public void OwnershipRejectsMismatchOrMissing() { Assert.False(FirewallRuleService.IsOwnRule("Program: C:\\Game\\b.exe", @"c:\game\a.exe")); Assert.False(FirewallRuleService.IsOwnRule(null, @"c:\game\a.exe")); Assert.False(FirewallRuleService.IsOwnRule("Rule Name: x", @"c:\game\a.exe")); }

    [Fact] public void ParsesBothRules() {
        var output = Block("SUSModder (Inbound)", Expected) + Environment.NewLine + Block("SUSModder (Outbound)", Expected);
        Assert.Equal(new FirewallRuleOutcome(true, true, false, output), FirewallRuleService.ParseScriptOutput(output, Expected));
    }
    [Fact] public void ParsesPartialOutput() => Assert.Equal(new FirewallRuleOutcome(true, false, false, Block("SUSModder (Inbound)", Expected)), FirewallRuleService.ParseScriptOutput(Block("SUSModder (Inbound)", Expected), Expected));
    [Fact] public void DetectsProgramConflict() {
        var output = Block("SUSModder (Inbound)", @"C:\Other\game.exe");
        Assert.True(FirewallRuleService.ParseScriptOutput(output, Expected).NameConflict);
    }
    [Theory]
    [InlineData("Ok.\nError adding rule.")]
    [InlineData("No rules match the specified criteria.\nOk.")]
    public void HandlesOutputWithoutOrBeforeRuleBlocks(string output) =>
        Assert.Equal(new FirewallRuleOutcome(false, false, false, output), FirewallRuleService.ParseScriptOutput(output, Expected));
    [Fact] public void ParsesUnicodeExpectedPath() => Assert.True(FirewallRuleService.ParseScriptOutput(Block("SUSModder (Inbound)", Expected), Expected).InboundOk);
}
