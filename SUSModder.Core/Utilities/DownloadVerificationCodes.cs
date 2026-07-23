namespace SUSModder.Core.Utilities;

/// <summary>
/// Stable machine-readable failure codes for download/tool integrity checks.
/// UI maps these to localized strings; Core must not hardcode user-facing language for new failures.
/// </summary>
public static class DownloadVerificationCodes
{
    public const string HashMismatch = "download_hash_mismatch";
    public const string HashMissing = "download_hash_missing";
    public const string ToolHashMismatch = "tool_hash_mismatch";
    public const string ToolDownloadFailed = "tool_download_failed";
    public const string ArtifactVerificationFailed = "artifact_verification_failed";
}
