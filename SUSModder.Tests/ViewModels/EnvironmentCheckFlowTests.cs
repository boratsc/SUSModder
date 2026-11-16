using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Services;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Faza 3a: czysta logika koordynatora startowego (propozycja jednorazowa, wariant C).
/// Bez UI i bazy — zapisy wersji/sygnatury wykonuje App na podstawie tych decyzji.
/// </summary>
public class EnvironmentCheckFlowTests
{
    private static UserSettings Settings(int promptVersion, string ackSignature = "") => new()
    {
        EnvironmentCheckPromptVersion = promptVersion,
        AntivirusWarningAcknowledgedSignature = ackSignature
    };

    private static AntivirusDetectionResult Av(params string[] names) => new(names);

    private static SelfCheckReport ReportWithAv(string code) => new(
        DateTimeOffset.UtcNow,
        new[] { new SelfCheckResult("av.thirdparty", SelfCheckStatus.Info, code, new Dictionary<string, string>()) });

    [Fact]
    public void Decide_VersionOutdated_ShowsProposal_EvenWithoutAv()
    {
        var decision = EnvironmentCheckFlow.Decide(Settings(0), Av());
        Assert.Equal(EnvironmentCheckFlowDecision.ShowProposal, decision);
    }

    [Fact]
    public void Decide_VersionOutdated_WithChangedAv_ProposalTakesPriority()
    {
        var decision = EnvironmentCheckFlow.Decide(Settings(0), Av("Avast"));
        Assert.Equal(EnvironmentCheckFlowDecision.ShowProposal, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_NoAv_Nothing()
    {
        var decision = EnvironmentCheckFlow.Decide(Settings(EnvironmentCheckPrompt.CurrentVersion), Av());
        Assert.Equal(EnvironmentCheckFlowDecision.Nothing, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_SourcesUnavailableEmpty_Nothing()
    {
        var unavailable = new AntivirusDetectionResult(Array.Empty<string>(), sourcesAvailable: false);
        var decision = EnvironmentCheckFlow.Decide(Settings(EnvironmentCheckPrompt.CurrentVersion), unavailable);
        Assert.Equal(EnvironmentCheckFlowDecision.Nothing, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_AvSignatureMatch_Nothing()
    {
        var av = Av("Avast", "AVG");
        var decision = EnvironmentCheckFlow.Decide(
            Settings(EnvironmentCheckPrompt.CurrentVersion, av.Signature), av);
        Assert.Equal(EnvironmentCheckFlowDecision.Nothing, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_AvSignatureMatchIsCaseInsensitive()
    {
        var av = Av("Avast");
        var decision = EnvironmentCheckFlow.Decide(
            Settings(EnvironmentCheckPrompt.CurrentVersion, av.Signature.ToUpperInvariant()), av);
        Assert.Equal(EnvironmentCheckFlowDecision.Nothing, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_AvSignatureChanged_ShowsAvWarning()
    {
        var decision = EnvironmentCheckFlow.Decide(
            Settings(EnvironmentCheckPrompt.CurrentVersion, "StaryAntywirus"), Av("Avast"));
        Assert.Equal(EnvironmentCheckFlowDecision.ShowAvWarning, decision);
    }

    [Fact]
    public void Decide_VersionCurrent_AvNeverAcknowledged_ShowsAvWarning()
    {
        var decision = EnvironmentCheckFlow.Decide(
            Settings(EnvironmentCheckPrompt.CurrentVersion), Av("Avast"));
        Assert.Equal(EnvironmentCheckFlowDecision.ShowAvWarning, decision);
    }

    [Fact]
    public void AfterSkip_MarkingVersionDoesNotAcknowledgeAv_LegacyWarningStillArmed()
    {
        // Pomiń zapisuje tylko wersję — sygnatura AV nie jest potwierdzona,
        // więc legacy ostrzeżenie AV nadal zadziała przy zmianie zestawu.
        var settings = Settings(0);
        var av = Av("Avast");
        Assert.Equal(EnvironmentCheckFlowDecision.ShowProposal, EnvironmentCheckFlow.Decide(settings, av));

        settings.EnvironmentCheckPromptVersion = EnvironmentCheckPrompt.CurrentVersion; // jedyny zapis przy Pomiń

        Assert.True(EnvironmentCheckFlow.ShouldShowAntivirusWarning(settings, av));
        Assert.Equal(EnvironmentCheckFlowDecision.ShowAvWarning, EnvironmentCheckFlow.Decide(settings, av));
    }

    [Fact]
    public void AfterReport_AvDetectedAndSignatureChanged_Acknowledges()
    {
        var av = Av("Avast");
        var settings = Settings(EnvironmentCheckPrompt.CurrentVersion);
        Assert.True(EnvironmentCheckFlow.ShouldAcknowledgeAntivirusAfterReport(
            ReportWithAv("env.av.detected"), av, settings));
    }

    [Fact]
    public void AfterReport_AvDetectedButSignatureAlreadyAcknowledged_DoesNotAck()
    {
        var av = Av("Avast");
        var settings = Settings(EnvironmentCheckPrompt.CurrentVersion, av.Signature);
        Assert.False(EnvironmentCheckFlow.ShouldAcknowledgeAntivirusAfterReport(
            ReportWithAv("env.av.detected"), av, settings));
    }

    [Fact]
    public void AfterReport_AvNotDetectedInReport_DoesNotAck()
    {
        var av = Av("Avast");
        var settings = Settings(EnvironmentCheckPrompt.CurrentVersion);
        Assert.False(EnvironmentCheckFlow.ShouldAcknowledgeAntivirusAfterReport(
            ReportWithAv("env.av.none"), av, settings));
        Assert.False(EnvironmentCheckFlow.ShouldAcknowledgeAntivirusAfterReport(
            ReportWithAv("env.av.unknown"), av, settings));
    }

    [Fact]
    public void AfterReport_DetectionEmpty_DoesNotAck()
    {
        var settings = Settings(EnvironmentCheckPrompt.CurrentVersion);
        Assert.False(EnvironmentCheckFlow.ShouldAcknowledgeAntivirusAfterReport(
            ReportWithAv("env.av.detected"), Av(), settings));
    }
}
