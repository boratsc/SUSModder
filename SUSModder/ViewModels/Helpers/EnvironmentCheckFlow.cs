using System;
using System.Linq;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels.Helpers;

/// <summary>Decyzja koordynatora startowego (App.axaml.cs).</summary>
public enum EnvironmentCheckFlowDecision
{
    Nothing,
    ShowProposal,
    ShowAvWarning
}

/// <summary>
/// Czysta logika jednorazowej propozycji sprawdzenia środowiska (wariant C) i legacy
/// ostrzeżenia AV — testowalna bez UI i bazy. Zapisy wykonuje koordynator w App.
/// </summary>
public static class EnvironmentCheckFlow
{
    public static EnvironmentCheckFlowDecision Decide(UserSettings settings, AntivirusDetectionResult avResult)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(avResult);

        if (settings.EnvironmentCheckPromptVersion < EnvironmentCheckPrompt.CurrentVersion)
            return EnvironmentCheckFlowDecision.ShowProposal;

        return ShouldShowAntivirusWarning(settings, avResult)
            ? EnvironmentCheckFlowDecision.ShowAvWarning
            : EnvironmentCheckFlowDecision.Nothing;
    }

    /// <summary>Legacy warunek karty AV: wykryto inny antywirus i sygnatura zestawu nie została potwierdzona.</summary>
    public static bool ShouldShowAntivirusWarning(UserSettings settings, AntivirusDetectionResult avResult) =>
        avResult.HasThirdPartyAntivirus && !SignatureAcknowledged(settings, avResult);

    /// <summary>
    /// Po pokazaniu raportu z checkiem AV: potwierdź sygnaturę tylko, gdy raport naprawdę
    /// pokazał wykryty antywirus (env.av.detected) i sygnatura różni się od potwierdzonej.
    /// Dzięki temu nie ma osobnego modala AV, a ostrzeżenie wróci po zmianie zestawu AV.
    /// </summary>
    public static bool ShouldAcknowledgeAntivirusAfterReport(
        SelfCheckReport report, AntivirusDetectionResult avResult, UserSettings settings) =>
        report.Results.Any(r => r.CheckId == "av.thirdparty" && r.Code == "env.av.detected")
        && ShouldShowAntivirusWarning(settings, avResult);

    private static bool SignatureAcknowledged(UserSettings settings, AntivirusDetectionResult avResult) =>
        string.Equals(
            settings.AntivirusWarningAcknowledgedSignature,
            avResult.Signature,
            StringComparison.OrdinalIgnoreCase);
}
