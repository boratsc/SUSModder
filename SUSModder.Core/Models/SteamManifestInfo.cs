using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Models;

public sealed class SteamManifestInfo
{
    public string AmongVersion { get; init; } = string.Empty;
    public string? EpicVersion { get; init; }
    public string? StorageVersion { get; init; }
    public int DepotId { get; init; } = 945361;
    public string ManifestId { get; init; } = string.Empty;
    public string? BuildId { get; init; }
    public long? SizeBytes { get; init; }
}

public enum VanillaAcquireSource
{
    CacheHit,
    DepotDownloader,
    Fallback7z,
    Fallback7zCache
}

public sealed class VanillaAcquireResult
{
    public bool Success { get; init; }
    public VanillaAcquireSource? Source { get; init; }
    public string? ErrorMessage { get; init; }

    public static VanillaAcquireResult Ok(VanillaAcquireSource source) =>
        new() { Success = true, Source = source };

    public static VanillaAcquireResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public sealed class SteamQrDownloadContext
{
    public required string ExtractedCachePath { get; init; }
    public required string ManifestId { get; init; }
    public required string AmongVersion { get; init; }
    public required IProgressReporter Progress { get; init; }
    public required IDiagnosticsOutput Log { get; init; }
    public Action<DepotDownloadProgress>? OnDepotProgress { get; init; }
}

public sealed record DepotDownloadProgress(
    int FilesDownloaded,
    string? LastFileName,
    double? Percent = null);
