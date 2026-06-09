using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Models;

public sealed class AmongUsVersionDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("dbValue")]
    public string DbValue { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("storageVersion")]
    public string? StorageVersion { get; init; }

    [JsonPropertyName("epicVersion")]
    public string? EpicVersion { get; init; }

    [JsonPropertyName("steam")]
    public AmongUsPlatformManifestDto? Steam { get; init; }

    [JsonPropertyName("epic")]
    public AmongUsPlatformManifestDto? Epic { get; init; }
}

public sealed class AmongUsPlatformManifestDto
{
    [JsonPropertyName("depotId")]
    public int DepotId { get; init; }

    [JsonPropertyName("manifestId")]
    public string? ManifestId { get; init; }

    [JsonPropertyName("buildId")]
    public string? BuildId { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }
}

public sealed class OnlineUsersDto
{
    [JsonPropertyName("online")]
    public int Online { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }
}
