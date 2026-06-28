using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

public interface ICatalogSyncStateRepository
{
    CatalogSnapshotMetadata? Get(string key);

    void SaveSuccess(string key, string? etag, string? lastModified, DateTime successUtc);

    void SaveFailure(string key, string? errorCode, DateTime attemptUtc, DateTime? nextAllowedAttemptUtc);

    void ClearFailure(string key);
}
