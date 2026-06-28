using System;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Diagnostics.Launch;

namespace SUSModder.Core.Api.Support;

/// <summary>
/// Buduje kontekst diagnostyczny do wysyłki w POST /api/v2/support/query.
/// Redaguje PII, limituje rozmiary i normalizuje dane.
/// </summary>
public sealed class SupportDiagnosticContextBuilder
{
    private const int MaxBepInExLines = 20;
    private const int MaxBepInExLineLength = 300;
    private const int MaxDiagnosisCodes = 10;

    /// <summary>
    /// Buduje obiekt SupportDiagnosticsInfo z LaunchResult.
    /// </summary>
    public SupportDiagnosticsInfo BuildFrom(LaunchResult launchResult)
    {
        var info = new SupportDiagnosticsInfo
        {
            DiagnosisCodes = launchResult.DiagnosisCodes
                .Take(MaxDiagnosisCodes)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList(),

            ModTypes = string.IsNullOrWhiteSpace(launchResult.Attempt.ModType)
                ? null
                : [launchResult.Attempt.ModType],

            WasRunAsAdmin = false,
            FirewallExceptionExists = false,
        };

        // Redagowane linie BepInEx
        if (launchResult.BepInExCriticalLines.Count > 0)
        {
            info.BepInExSummary = launchResult.BepInExCriticalLines
                .Take(MaxBepInExLines)
                .Select(l =>
                {
                    var redacted = SupportBundleService.RedactLine(l);
                    return redacted.Length > MaxBepInExLineLength
                        ? redacted[..MaxBepInExLineLength]
                        : redacted;
                })
                .ToList();
        }

        return info;
    }

    /// <summary>
    /// Redaguje opis problemu użytkownika (ścieżki, tokeny).
    /// </summary>
    public static string RedactProblem(string problem)
    {
        if (string.IsNullOrWhiteSpace(problem))
            return string.Empty;

        var result = SupportBundleService.RedactLine(problem);

        // Limit do 2000 znaków
        if (result.Length > 2000)
            result = result[..2000];

        return result;
    }

    /// <summary>
    /// Normalizuje język do pl/en.
    /// </summary>
    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "pl";

        var lower = language.Trim().ToLowerInvariant();
        return lower switch
        {
            "en" or "english" or "en-us" or "en-gb" => "en",
            _ => "pl"
        };
    }
}
