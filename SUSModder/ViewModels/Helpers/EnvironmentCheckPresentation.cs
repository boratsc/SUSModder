using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels.Helpers;

/// <summary>Akcja wiersza raportu — jeden przycisk na pozycję wymagającą działania.</summary>
public enum EnvironmentCheckAction
{
    None,
    OpenWindowsSecurity,
    OpenSteam,
    OpenFirewallTool
}

/// <summary>Wiersz raportu: ikona statusu + etykieta tekstowa + tytuł + wyjaśnienie + opcjonalna akcja.</summary>
public sealed class EnvironmentCheckRow
{
    public required string CheckId { get; init; }
    public required SelfCheckStatus Status { get; init; }
    public required string Title { get; init; }
    public required string StatusLabel { get; init; }
    public required string Description { get; init; }
    public string? Instruction { get; init; }
    public EnvironmentCheckAction Action { get; init; }
    public string? ActionLabel { get; init; }
    public bool HasAction => Action != EnvironmentCheckAction.None;
    public bool HasInstruction => !string.IsNullOrEmpty(Instruction);

    /// <summary>Technikalia (ścieżki, nazwa wersji, kod) — tylko pod „Szczegóły", nie w głównym widoku.</summary>
    public string? Details { get; init; }
    public bool HasDetails => !string.IsNullOrEmpty(Details);

    /// <summary>Pełny opis dla czytników ekranu (status tekstem, nie tylko kolorem).</summary>
    public string AutomationName => HasInstruction
        ? $"{Title}. {StatusLabel}. {Description} {Instruction}"
        : $"{Title}. {StatusLabel}. {Description}";
}

/// <summary>
/// Mapowanie SelfCheckResult na zlokalizowane wiersze raportu (klucze UI.EnvironmentCheck.*).
/// „Nie dotyczy" jest ukryte; pozycje wymagające działania są przed informacyjnymi.
/// </summary>
public static class EnvironmentCheckPresentation
{
    public static string TitleKey(string checkId) => "UI.EnvironmentCheck.Checks." + checkId;
    public static string DescriptionKey(string code) => "UI.EnvironmentCheck.Code." + code;
    public static string StatusKey(SelfCheckStatus status) => "UI.EnvironmentCheck.Status." + status;

    public static IReadOnlyList<EnvironmentCheckRow> BuildRows(
        SelfCheckReport report, ILocalizationService loc, string modsInstallPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(loc);

        return report.Results
            .Where(r => r.Status != SelfCheckStatus.NotApplicable)
            .Select(r => BuildRow(r, loc, modsInstallPath))
            .OrderByDescending(Priority) // stabilne: problemy → nie sprawdzono → reszta, w kolejności Core
            .ToList();
    }

    /// <summary>Podsumowanie jednym zdaniem nad listą: problemy + pozycje z akcją/instrukcją.</summary>
    public static string BuildSummary(IReadOnlyList<EnvironmentCheckRow> rows, ILocalizationService loc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(loc);

        var problems = rows.Count(r =>
            r.Status is SelfCheckStatus.Warning or SelfCheckStatus.Error || r.HasAction || r.HasInstruction);
        return problems == 0
            ? loc.Get("UI.EnvironmentCheck.SummaryOk")
            : loc.GetFormatted("UI.EnvironmentCheck.SummaryProblems", problems);
    }

    /// <summary>
    /// Wiersz wymagający uwagi: problem, „nie sprawdzono" albo pozycja z akcją/instrukcją
    /// (także Info, np. wykryty antywirus). Reszta (Info/Ok bez akcji) ląduje pod „Pokaż pozostałe szczegóły".
    /// </summary>
    public static bool NeedsAttention(EnvironmentCheckRow row) =>
        row.Status is SelfCheckStatus.Warning or SelfCheckStatus.Error or SelfCheckStatus.Unknown
        || row.HasAction || row.HasInstruction;

    public static EnvironmentCheckRow BuildRow(SelfCheckResult result, ILocalizationService loc, string modsInstallPath)
    {
        var (action, actionKey, instructionKey, instructionArgs) = MapAction(result, modsInstallPath);
        return new EnvironmentCheckRow
        {
            CheckId = result.CheckId,
            Status = result.Status,
            Title = loc.Get(TitleKey(result.CheckId)),
            StatusLabel = loc.Get(StatusKey(result.Status)),
            Description = FormatDescription(result, loc),
            Instruction = instructionKey == null ? null : Format(loc, instructionKey, instructionArgs),
            Action = action,
            ActionLabel = actionKey == null ? null : loc.Get(actionKey),
            Details = BuildDetails(result, loc)
        };
    }

    private static int Priority(EnvironmentCheckRow row) => row.Status switch
    {
        SelfCheckStatus.Warning or SelfCheckStatus.Error => 2,
        SelfCheckStatus.Unknown => 1,
        _ => 0
    };

    private static string FormatDescription(SelfCheckResult result, ILocalizationService loc)
    {
        var key = DescriptionKey(result.Code);
        // Ścieżki i nazwy wersji technicznych nie wchodzą do głównego opisu — trafiają do Details.
        object[] args = result.Code switch
        {
            "env.firewall.thirdparty_detected" or "env.av.detected" => new object[] { Names(result) },
            "env.steam.multiple" => new object[] { Value(result, "count") },
            _ => Array.Empty<object>()
        };
        return Format(loc, key, args);
    }

    /// <summary>Warstwa techniczna pod „Szczegóły": ścieżki/nazwa wersji + surowy kod.</summary>
    private static string? BuildDetails(SelfCheckResult result, ILocalizationService loc)
    {
        var lines = new List<string>();
        switch (result.Code)
        {
            case "env.steam.installed":
            case "env.steam.files_only":
                lines.Add(loc.GetFormatted("UI.EnvironmentCheck.Detail.Path", Value(result, "path")));
                break;
            case "env.firewall.rules_drift":
                lines.Add(loc.GetFormatted("UI.EnvironmentCheck.Detail.RulePath", Value(result, "rulePath")));
                lines.Add(loc.GetFormatted("UI.EnvironmentCheck.Detail.TargetPath", Value(result, "targetPath")));
                break;
            case "env.steam.beta_nondefault":
            case "env.steam.beta_inconsistent":
                lines.Add(loc.GetFormatted("UI.EnvironmentCheck.Detail.Branch", Value(result, "branch")));
                break;
        }

        if (lines.Count == 0)
            return null;

        lines.Add(loc.GetFormatted("UI.EnvironmentCheck.Detail.Code", result.Code));
        return string.Join("\n", lines);
    }

    private static (EnvironmentCheckAction Action, string? ActionKey, string? InstructionKey, object[] Args) MapAction(
        SelfCheckResult result, string modsInstallPath) => (result.CheckId, result.Code) switch
        {
            ("sac", "env.sac.enforce" or "env.sac.evaluation") => (
                EnvironmentCheckAction.OpenWindowsSecurity,
                "UI.EnvironmentCheck.Action.OpenWindowsSecurity",
                "UI.EnvironmentCheck.Action.SacInstruction",
                Array.Empty<object>()),
            ("steam.beta", "env.steam.beta_nondefault" or "env.steam.beta_inconsistent") => (
                EnvironmentCheckAction.OpenSteam,
                "UI.EnvironmentCheck.Action.OpenSteam",
                "UI.EnvironmentCheck.Action.SteamBetaInstruction",
                Array.Empty<object>()),
            ("firewall.rules", "env.firewall.rules_missing" or "env.firewall.rules_drift") => (
                EnvironmentCheckAction.OpenFirewallTool,
                "UI.EnvironmentCheck.Action.OpenFirewallTool",
                null,
                Array.Empty<object>()),
            ("av.thirdparty", "env.av.detected") => (
                EnvironmentCheckAction.None,
                null,
                "UI.EnvironmentCheck.Action.AvInstruction",
                new object[] { modsInstallPath }),
            ("defender.cfa", "env.defender.cfa_on") => (
                EnvironmentCheckAction.OpenWindowsSecurity,
                "UI.EnvironmentCheck.Action.OpenWindowsSecurity",
                "UI.EnvironmentCheck.Action.CfaInstruction",
                Array.Empty<object>()),
            _ => (EnvironmentCheckAction.None, null, null, Array.Empty<object>())
        };

    private static string Format(ILocalizationService loc, string key, object[] args) =>
        args.Length == 0 ? loc.Get(key) : loc.GetFormatted(key, args);

    private static string Value(SelfCheckResult result, string key) =>
        result.Data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "-";

    private static string Names(SelfCheckResult result) =>
        string.Join(", ", Value(result, "names").Split('|', StringSplitOptions.RemoveEmptyEntries));
}
