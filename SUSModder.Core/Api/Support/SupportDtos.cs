using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Support;

// ── Request DTOs ─────────────────────────────────────────

/// <summary>Request body dla POST /api/v2/support/query.</summary>
public sealed class SupportQueryRequest
{
    [JsonPropertyName("language")]
    [JsonRequired]
    public string Language { get; set; } = "pl";

    [JsonPropertyName("problem")]
    [JsonRequired]
    public string Problem { get; set; } = string.Empty;

    [JsonPropertyName("categoryHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CategoryHint { get; set; }

    [JsonPropertyName("app")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SupportAppInfo? App { get; set; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SupportDiagnosticsInfo? Diagnostics { get; set; }
}

public sealed class SupportAppInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("platformMode")]
    public string PlatformMode { get; set; } = string.Empty;

    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get; set; } = string.Empty;
}

public sealed class SupportDiagnosticsInfo
{
    [JsonPropertyName("diagnosisCodes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DiagnosisCodes { get; set; }

    [JsonPropertyName("modTypes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ModTypes { get; set; }

    [JsonPropertyName("amongUsVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AmongUsVersion { get; set; }

    [JsonPropertyName("wasRunAsAdmin")]
    public bool WasRunAsAdmin { get; set; }

    [JsonPropertyName("firewallExceptionExists")]
    public bool FirewallExceptionExists { get; set; }

    [JsonPropertyName("defenderEventCodes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? DefenderEventCodes { get; set; }

    [JsonPropertyName("bepInExSummary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? BepInExSummary { get; set; }
}

// ── Response DTOs ────────────────────────────────────────

public sealed class SupportKnowledgeMeta
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("locales")]
    public List<string> Locales { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("articleCount")]
    public int ArticleCount { get; set; }

    [JsonPropertyName("loadedAt")]
    public string LoadedAt { get; set; } = string.Empty;
}

public sealed class SupportQueryResponse
{
    [JsonPropertyName("supportSessionId")]
    public string SupportSessionId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "knowledge_base";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<SupportStep> Steps { get; set; } = [];

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Warnings { get; set; }

    [JsonPropertyName("matchedArticles")]
    public List<SupportArticle> MatchedArticles { get; set; } = [];

    [JsonPropertyName("needsDiagnosticReport")]
    public bool NeedsDiagnosticReport { get; set; }

    [JsonPropertyName("discordRecommended")]
    public bool DiscordRecommended { get; set; }

    [JsonPropertyName("safetyNotice")]
    public string SafetyNotice { get; set; } = string.Empty;
}

public sealed class SupportStep
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("actionCode")]
    public string ActionCode { get; set; } = "none";

    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; set; }
}

public sealed class SupportArticle
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("score")]
    public float Score { get; set; }
}

public sealed class SupportQueryMeta
{
    [JsonPropertyName("kbVersion")]
    public string KbVersion { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("llmUsed")]
    public bool LlmUsed { get; set; }

    [JsonPropertyName("cached")]
    public bool Cached { get; set; }
}

public sealed class SupportFeedbackRequest
{
    [JsonPropertyName("supportSessionId")]
    [JsonRequired]
    public string SupportSessionId { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    [JsonRequired]
    public string Result { get; set; } = "not_helped";

    [JsonPropertyName("articleIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ArticleIds { get; set; }

    [JsonPropertyName("diagnosisCodes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DiagnosisCodes { get; set; }

    [JsonPropertyName("language")]
    [JsonRequired]
    public string Language { get; set; } = "pl";

    [JsonPropertyName("optionalComment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OptionalComment { get; set; }
}

public sealed class SupportReportMetadataRequest
{
    [JsonPropertyName("supportSessionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportSessionId { get; set; }

    [JsonPropertyName("articleCount")]
    public int ArticleCount { get; set; }

    [JsonPropertyName("diagnosisCodes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DiagnosisCodes { get; set; }

    [JsonPropertyName("language")]
    [JsonRequired]
    public string Language { get; set; } = "pl";
}

public sealed class SupportHealthInfo
{
    [JsonPropertyName("kbLoaded")]
    public bool KbLoaded { get; set; }

    [JsonPropertyName("kbVersion")]
    public string KbVersion { get; set; } = string.Empty;

    [JsonPropertyName("articleCount")]
    public int ArticleCount { get; set; }

    [JsonPropertyName("llmEnabled")]
    public bool LlmEnabled { get; set; }

    [JsonPropertyName("aiProviderReachable")]
    public bool AiProviderReachable { get; set; }
}

// ── Action codes (allowlist) ──────────────────────────────

/// <summary>Mapowanie actionCode z API na akcje klienta.</summary>
public static class SupportActionCode
{
    public const string None = "none";
    public const string OpenLogs = "open_logs";
    public const string OpenModFolder = "open_mod_folder";
    public const string OpenFirewallRepair = "open_firewall_repair";
    public const string OpenDefenderInstructions = "open_defender_instructions";
    public const string GenerateReport = "generate_report";
    public const string OpenDiscord = "open_discord";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        None, OpenLogs, OpenModFolder, OpenFirewallRepair,
        OpenDefenderInstructions, GenerateReport, OpenDiscord
    };

    public static bool IsValid(string code) => All.Contains(code);
}
