using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Tests.Diagnostics.Launch;

public sealed class BepInExLogAnalyzerTests
{
    private readonly BepInExLogAnalyzer _analyzer = new();

    // ── Line classification tests ─────────────────────────────

    [Fact]
    public void ClassifyLine_BenignUnityError_ReturnsInfo()
    {
        var line = "[Error  : Unity Log] MissingFieldException: Field not found";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Info, result);
    }

    [Fact]
    public void ClassifyLine_BenignChainloaderStarted_ReturnsInfo()
    {
        var line = "[Message:   BepInEx] Chainloader started";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Info, result);
    }

    [Fact]
    public void ClassifyLine_BenignBepInExInfo_ReturnsInfo()
    {
        var line = "[Info   :   BepInEx] Loading plugins...";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Info, result);
    }

    [Fact]
    public void ClassifyLine_FileNotFoundException_ReturnsCritical()
    {
        var line = "[Error  :  HarmonyX] FileNotFoundException: Could not load file or assembly 'SomeMod.dll'";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Critical, result);
    }

    [Fact]
    public void ClassifyLine_DllNotFoundException_ReturnsCritical()
    {
        var line = "[Error  :  HarmonyX] DllNotFoundException: Unable to load DLL 'some_native'";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Critical, result);
    }

    [Fact]
    public void ClassifyLine_AccessDenied_ReturnsCritical()
    {
        var line = "[Error  :  BepInEx] UnauthorizedAccessException: Access to the path 'C:\\test' is denied.";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Critical, result);
    }

    [Fact]
    public void ClassifyLine_MissingMethod_ReturnsCritical()
    {
        var line = "[Error  :  BepInEx] MissingMethodException: Method not found: 'Void SomeMod.Init()'";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Critical, result);
    }

    [Fact]
    public void ClassifyLine_TypeLoadException_ReturnsCritical()
    {
        var line = "[Error  :  BepInEx] TypeLoadException: Could not load type 'SomeMod.Plugin'";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Critical, result);
    }

    [Fact]
    public void ClassifyLine_BepInExError_ReturnsWarning()
    {
        var line = "[Error  :   BepInEx] Some non-critical error";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        // Nie-Unity, nie pasuje do krytycznych → warning
        Assert.Equal(LineClassification.Warning, result);
    }

    [Fact]
    public void ClassifyLine_PlainText_ReturnsInfo()
    {
        var line = "Some random log output without error markers";
        var result = BepInExLogAnalyzer.ClassifyLine(line);
        Assert.Equal(LineClassification.Info, result);
    }

    // ── IsBenign tests ────────────────────────────────────────

    [Fact]
    public void IsBenign_UnityErrorLog_ReturnsTrue()
    {
        Assert.True(BepInExLogAnalyzer.IsBenign("[Error  : Unity Log] something"));
    }

    [Fact]
    public void IsBenign_ChainloaderStarted_ReturnsTrue()
    {
        Assert.True(BepInExLogAnalyzer.IsBenign("[Message:   BepInEx] Chainloader started"));
    }

    [Fact]
    public void IsBenign_BepInExInfo_ReturnsTrue()
    {
        Assert.True(BepInExLogAnalyzer.IsBenign("[Info   :   BepInEx] plugins loaded"));
    }

    [Fact]
    public void IsBenign_CriticalError_ReturnsFalse()
    {
        Assert.False(BepInExLogAnalyzer.IsBenign("[Error  :  HarmonyX] FileNotFoundException: mod.dll"));
    }

    // ── Diagnosis code extraction tests ───────────────────────

    [Fact]
    public void ExtractDiagnosisCodes_PluginLoadFailed_VariousExceptions()
    {
        var lines = new List<string>
        {
            "[Error  :  HarmonyX] FileNotFoundException: SomeMod.dll",
            "[Error  :  BepInEx] DllNotFoundException: native.dll",
            "[Error  :  BepInEx] BadImageFormatException: corrupted.dll",
            "[Error  :  BepInEx] MissingMethodException: Init()",
            "[Error  :  BepInEx] TypeLoadException: Plugin type"
        };

        var codes = BepInExLogAnalyzer.ExtractDiagnosisCodes(lines);

        Assert.Contains(DiagnosisCode.BepInExPluginLoadFailed, codes);
        Assert.DoesNotContain(DiagnosisCode.BepInExAccessDenied, codes);
    }

    [Fact]
    public void ExtractDiagnosisCodes_AccessDenied()
    {
        var lines = new List<string>
        {
            "[Error  :  BepInEx] UnauthorizedAccessException: Access to the path 'x' is denied.",
            "[Error  :  BepInEx] Access is denied"
        };

        var codes = BepInExLogAnalyzer.ExtractDiagnosisCodes(lines);

        Assert.Contains(DiagnosisCode.BepInExAccessDenied, codes);
    }

    [Fact]
    public void ExtractDiagnosisCodes_MixedErrors()
    {
        var lines = new List<string>
        {
            "[Error  :  BepInEx] FileNotFoundException: mod.dll",
            "[Error  :  BepInEx] Access is denied"
        };

        var codes = BepInExLogAnalyzer.ExtractDiagnosisCodes(lines);

        Assert.Contains(DiagnosisCode.BepInExPluginLoadFailed, codes);
        Assert.Contains(DiagnosisCode.BepInExAccessDenied, codes);
        Assert.Equal(2, codes.Count);
    }

    [Fact]
    public void ExtractDiagnosisCodes_EmptyLines_ReturnsEmpty()
    {
        var codes = BepInExLogAnalyzer.ExtractDiagnosisCodes([]);
        Assert.Empty(codes);
    }

    [Fact]
    public void ExtractDiagnosisCodes_NoMatchingPatterns_ReturnsEmpty()
    {
        var lines = new List<string> { "Some random line", "Another line" };
        var codes = BepInExLogAnalyzer.ExtractDiagnosisCodes(lines);
        Assert.Empty(codes);
    }

    // ── Full analysis tests ───────────────────────────────────

    [Fact]
    public void Analyze_FileNotFound_ReturnsMissing()
    {
        var result = _analyzer.Analyze(@"C:\nonexistent\LogOutput.log");
        Assert.Equal(BepInExLogStatus.Missing, result.LogStatus);
    }

    [Fact]
    public void Analyze_EmptyPath_ReturnsMissing()
    {
        var result = _analyzer.Analyze("");
        Assert.Equal(BepInExLogStatus.Missing, result.LogStatus);
    }

    [Fact]
    public void Analyze_NullPath_ReturnsMissing()
    {
        var result = _analyzer.Analyze(null!);
        Assert.Equal(BepInExLogStatus.Missing, result.LogStatus);
    }

    // ── DiagnosisCode validation ──────────────────────────────

    [Fact]
    public void DiagnosisCode_All_ContainsExpectedCodes()
    {
        Assert.Contains(DiagnosisCode.ProcessStartFailed, DiagnosisCode.All);
        Assert.Contains(DiagnosisCode.BepInExLogMissing, DiagnosisCode.All);
        Assert.Contains(DiagnosisCode.BepInExPluginLoadFailed, DiagnosisCode.All);
        Assert.Contains(DiagnosisCode.DefenderThreatDetected, DiagnosisCode.All);
        Assert.Contains(DiagnosisCode.FirewallRuleMissingOrBlocked, DiagnosisCode.All);
        Assert.Contains(DiagnosisCode.Unknown, DiagnosisCode.All);
    }

    // ── LaunchAttempt defaults ────────────────────────────────

    [Fact]
    public void LaunchAttempt_Created_HasNonEmptyAttemptId()
    {
        var attempt = new LaunchAttempt();
        Assert.False(string.IsNullOrWhiteSpace(attempt.AttemptId));
        Assert.NotEqual(Guid.Empty.ToString("N"), attempt.AttemptId);
    }

    [Fact]
    public void LaunchAttempt_Created_HasStartedAtUtcSet()
    {
        var attempt = new LaunchAttempt();
        var now = DateTimeOffset.UtcNow;
        Assert.True(attempt.StartedAtUtc <= now);
        Assert.True(attempt.StartedAtUtc > now.AddSeconds(-5));
    }

    // ── LaunchResult defaults ─────────────────────────────────

    [Fact]
    public void LaunchResult_Defaults_NotSuccessful()
    {
        var result = new LaunchResult { Attempt = new LaunchAttempt() };
        Assert.False(result.IsSuccessful);
        Assert.Empty(result.DiagnosisCodes);
        Assert.Equal(DiagnosisSeverity.Unknown, result.Severity);
    }
}
