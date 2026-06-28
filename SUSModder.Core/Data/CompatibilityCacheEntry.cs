using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

public sealed class CompatibilityCacheEntry
{
    public int FullModId { get; init; }
    public string FullModVersion { get; init; } = string.Empty;
    public int DllModId { get; init; }
    public string DllModVersion { get; init; } = string.Empty;
    public string Status { get; init; } = "NT";
    public bool IsExactVersion { get; init; } = true;
    public string? Warning { get; init; }
    public string? SourceUpdatedAt { get; init; }
    public DateTime FetchedAtUtc { get; init; }

    public CompatibilityInfo ToCompatibilityInfo() => new()
    {
        StatusCode = Status,
        Warning = Warning,
        IsCurrentVersion = IsExactVersion
    };
}
