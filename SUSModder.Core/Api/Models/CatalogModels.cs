using System.Text.Json.Serialization;

namespace SUSModder.Core.Api.Models;

public sealed class CatalogItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; init; }

    [JsonPropertyName("installPath")]
    public string? InstallPath { get; init; }

    [JsonPropertyName("dllInstallPath")]
    public string? DllInstallPath { get; init; }

    [JsonPropertyName("gitHubProjectUrl")]
    public string? GitHubProjectUrl { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; init; }

    [JsonPropertyName("amongVersion")]
    public CatalogAmongVersionDto? AmongVersion { get; init; }

    [JsonPropertyName("hasRoles")]
    public bool? HasRoles { get; init; }

    [JsonPropertyName("lobbyRegionBaseUrl")]
    public string? LobbyRegionBaseUrl { get; init; }

    [JsonPropertyName("supportsLobbySharing")]
    public bool SupportsLobbySharing { get; init; }
}

public sealed class CatalogAmongVersionDto
{
    [JsonPropertyName("dbValue")]
    public string? DbValue { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; init; }
}

public sealed class CatalogMetaDto
{
    [JsonPropertyName("catalogRevision")]
    public string? CatalogRevision { get; init; }

    [JsonPropertyName("compatibilityRevision")]
    public string? CompatibilityRevision { get; init; }

    [JsonPropertyName("versionsRevision")]
    public string? VersionsRevision { get; init; }

    [JsonPropertyName("serverTimeUtc")]
    public string? ServerTimeUtc { get; init; }
}

public sealed class CatalogVersionsDto
{
    [JsonPropertyName("modId")]
    public int ModId { get; init; }

    [JsonPropertyName("modName")]
    public string ModName { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("versions")]
    public List<CatalogVersionEntryDto> Versions { get; init; } = [];
}

public sealed class CatalogVersionEntryDto
{
    [JsonPropertyName("versionId")]
    public int VersionId { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("amongVersion")]
    public string AmongVersion { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed class CatalogModDetailDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    [JsonPropertyName("variants")]
    public List<CatalogModVariantDto> Variants { get; init; } = [];
}

public sealed class CatalogModVariantDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("fileSizeBytes")]
    public long? FileSizeBytes { get; init; }
}

public sealed class CatalogQuery
{
    public string? ModType { get; init; }
    public string? AmongVersion { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 200;
}
