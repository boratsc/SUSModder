using System.Text.Json.Serialization;
using SUSModder.Core.Models;

namespace SUSModder.Core.Api.Models;

public sealed class CompatibilityDataDto
{
    [JsonPropertyName("query")]
    public CompatibilityQuery? Query { get; init; }

    [JsonPropertyName("compatibilities")]
    public List<CompatibilityEntry> Compatibilities { get; init; } = [];
}

public sealed class CompatibilityQueryParams
{
    public int? FullModId { get; init; }
    public string? FullModVersion { get; init; }
    public int? DllModId { get; init; }
    public string? DllModVersion { get; init; }
    public string? Status { get; init; }
    public bool? IncludeUntested { get; init; }
}

public sealed class CompatibilitySnapshotDto
{
    [JsonPropertyName("revision")]
    public string? Revision { get; init; }

    [JsonPropertyName("generatedAtUtc")]
    public string? GeneratedAtUtc { get; init; }

    [JsonPropertyName("entries")]
    public List<CompatibilitySnapshotEntryDto> Entries { get; init; } = [];
}

public sealed class CompatibilitySnapshotEntryDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("fullModId")]
    public int FullModId { get; init; }

    [JsonPropertyName("fullModName")]
    public string? FullModName { get; init; }

    [JsonPropertyName("fullModVersion")]
    public string FullModVersion { get; init; } = string.Empty;

    [JsonPropertyName("dllModId")]
    public int DllModId { get; init; }

    [JsonPropertyName("dllModName")]
    public string? DllModName { get; init; }

    [JsonPropertyName("dllModVersion")]
    public string DllModVersion { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "NT";

    [JsonPropertyName("isExactVersion")]
    public bool IsExactVersion { get; init; } = true;

    [JsonPropertyName("warning")]
    public string? Warning { get; init; }
}
