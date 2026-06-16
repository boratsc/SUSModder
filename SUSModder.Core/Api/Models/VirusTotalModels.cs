using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Models;

/// <summary>
/// Raport VirusTotal dla konkretnego wariantu moda (GET /downloads/mod/:id/:version/virustotal).
/// </summary>
public sealed class ModVariantVirusTotalReportDto
{
    [JsonPropertyName("modId")]
    public int ModId { get; init; }

    [JsonPropertyName("modVersion")]
    public string ModVersion { get; init; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>
    /// Status skanu: clean / suspicious / malicious / pending / scanning / error / unknown.
    /// </summary>
    [JsonPropertyName("scanStatus")]
    public string ScanStatus { get; init; } = "unknown";

    /// <summary>
    /// Link do pełnego raportu VirusTotal.
    /// </summary>
    [JsonPropertyName("vtPermalink")]
    public string? VtPermalink { get; init; }

    [JsonPropertyName("vtLastAnalysisDate")]
    public string? VtLastAnalysisDate { get; init; }

    [JsonPropertyName("vtLastCheckedAt")]
    public string? VtLastCheckedAt { get; init; }

    [JsonPropertyName("lastAnalysisStats")]
    public VirusTotalStatsDto? LastAnalysisStats { get; init; }

    /// <summary>
    /// Status AI review: ai_review_not_needed / ai_review_pending / ai_review_false_positive_likely / ai_review_risk_confirmed / ai_review_inconclusive.
    /// </summary>
    [JsonPropertyName("aiReviewStatus")]
    public string? AiReviewStatus { get; init; }

    /// <summary>
    /// Czytelny opis wniosku AI (tekst danych z backendu, nie lokalizowany po stronie klienta).
    /// </summary>
    [JsonPropertyName("aiReviewSummary")]
    public string? AiReviewSummary { get; init; }
}

/// <summary>
/// Statystyki detekcji VirusTotal.
/// </summary>
public sealed class VirusTotalStatsDto
{
    [JsonPropertyName("malicious")]
    public int Malicious { get; init; }

    [JsonPropertyName("suspicious")]
    public int Suspicious { get; init; }

    [JsonPropertyName("undetected")]
    public int Undetected { get; init; }

    [JsonPropertyName("harmless")]
    public int Harmless { get; init; }

    [JsonPropertyName("timeout")]
    public int Timeout { get; init; }
}

/// <summary>
/// Stałe dla statusu skanu VT – używane w UI i logice ostrzeżeń.
/// </summary>
public static class VirusTotalScanStatus
{
    public const string Clean = "clean";
    public const string Suspicious = "suspicious";
    public const string Malicious = "malicious";
    public const string Pending = "pending";
    public const string Scanning = "scanning";
    public const string Error = "error";
    public const string Unknown = "unknown";

    public static bool IsRisky(string? status) =>
        string.Equals(status, Suspicious, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Malicious, StringComparison.OrdinalIgnoreCase);

    public static bool IsClean(string? status) =>
        string.Equals(status, Clean, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnknownLike(string? status) =>
        string.IsNullOrEmpty(status) ||
        string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Scanning, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Error, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Unknown, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Stałe dla statusu AI review.
/// </summary>
public static class VirusTotalAiReviewStatus
{
    public const string NotNeeded = "ai_review_not_needed";
    public const string Pending = "ai_review_pending";
    public const string FalsePositiveLikely = "ai_review_false_positive_likely";
    public const string RiskConfirmed = "ai_review_risk_confirmed";
    public const string Inconclusive = "ai_review_inconclusive";

    /// <summary>
    /// Czy AI review sugeruje false positive (łagodniejsze ostrzeżenie).
    /// </summary>
    public static bool IsFalsePositiveLikely(string? status) =>
        string.Equals(status, FalsePositiveLikely, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Czy AI review potwierdza realne ryzyko (najmocniejsze ostrzeżenie).
    /// </summary>
    public static bool IsRiskConfirmed(string? status) =>
        string.Equals(status, RiskConfirmed, StringComparison.OrdinalIgnoreCase);
}
