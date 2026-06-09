namespace SUSModder.Core.Models;

public enum CatalogSyncStatus
{
    Updated,
    NotModified,
    OfflineUsingCache,
    InvalidResponse,
    Failed,
    SkippedBackoff
}

public sealed class CatalogSyncResult
{
    public CatalogSyncStatus Status { get; init; }
    public string? ETag { get; init; }
    public string? ErrorMessage { get; init; }
    public int ModCount { get; init; }
    public DateTime? LastSuccessUtc { get; init; }

    public bool ConfigChanged => Status == CatalogSyncStatus.Updated;
    public bool IsSuccess => Status is CatalogSyncStatus.Updated or CatalogSyncStatus.NotModified or CatalogSyncStatus.OfflineUsingCache;
}

public sealed class CatalogSnapshotMetadata
{
    public string Key { get; init; } = string.Empty;
    public string? ETag { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? NextAllowedAttemptUtc { get; init; }
    public int FailureCount { get; init; }
}
