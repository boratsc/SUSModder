using SUSModder.Core.Api.Support;
using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Tests.Api.Support;

public sealed class SupportDiagnosticContextBuilderTests
{
    private readonly SupportDiagnosticContextBuilder _builder = new();

    [Fact]
    public void BuildFrom_WithCodes_ReturnsDiagnostics()
    {
        var result = new LaunchResult
        {
            Attempt = new LaunchAttempt { ModType = "full", PlatformMode = "steam" },
            DiagnosisCodes = { "launch.bepinex.log_missing", "launch.bepinex.log_stale" },
            BepInExCriticalLines = { "[Error  :  BepInEx] FileNotFoundException: mod.dll" }
        };

        var info = _builder.BuildFrom(result);

        Assert.NotNull(info.DiagnosisCodes);
        Assert.Equal(2, info.DiagnosisCodes.Count);
        Assert.Equal("full", info.ModTypes![0]);
        Assert.NotNull(info.BepInExSummary);
        Assert.Single(info.BepInExSummary);
    }

    [Fact]
    public void BuildFrom_LimitsCodes_ToMax10()
    {
        var result = new LaunchResult
        {
            Attempt = new LaunchAttempt(),
            DiagnosisCodes = Enumerable.Range(0, 15).Select(i => $"code_{i}").ToList()
        };

        var info = _builder.BuildFrom(result);
        Assert.Equal(10, info.DiagnosisCodes!.Count);
    }

    [Fact]
    public void BuildFrom_LimitsBepInExLines()
    {
        var lines = Enumerable.Range(0, 30).Select(i => $"line {i}").ToList();
        var result = new LaunchResult
        {
            Attempt = new LaunchAttempt(),
            BepInExCriticalLines = lines
        };

        var info = _builder.BuildFrom(result);
        Assert.True(info.BepInExSummary!.Count <= 20);
        Assert.True(info.BepInExSummary.All(l => l.Length <= 300));
    }

    [Fact]
    public void RedactProblem_RedactsUserPaths()
    {
        var problem = "Error in C:\\Users\\John\\AppData\\Roaming\\SUSModder\\mods";
        var result = SupportDiagnosticContextBuilder.RedactProblem(problem);
        Assert.DoesNotContain("John", result);
    }

    [Fact]
    public void NormalizeLanguage_Pl_ReturnsPl()
    {
        Assert.Equal("pl", SupportDiagnosticContextBuilder.NormalizeLanguage("pl"));
        Assert.Equal("pl", SupportDiagnosticContextBuilder.NormalizeLanguage("PL"));
    }

    [Fact]
    public void NormalizeLanguage_En_ReturnsEn()
    {
        Assert.Equal("en", SupportDiagnosticContextBuilder.NormalizeLanguage("en"));
        Assert.Equal("en", SupportDiagnosticContextBuilder.NormalizeLanguage("EN"));
        Assert.Equal("en", SupportDiagnosticContextBuilder.NormalizeLanguage("en-US"));
    }

    [Fact]
    public void NormalizeLanguage_Unknown_ReturnsPl()
    {
        Assert.Equal("pl", SupportDiagnosticContextBuilder.NormalizeLanguage("de"));
        Assert.Equal("pl", SupportDiagnosticContextBuilder.NormalizeLanguage(""));
        Assert.Equal("pl", SupportDiagnosticContextBuilder.NormalizeLanguage(null));
    }
}
