namespace SUSModder.Core.Utilities;

public sealed class ModDownloadResolution
{
    public required string Url { get; init; }
    public string? ExpectedSha256 { get; init; }
}
