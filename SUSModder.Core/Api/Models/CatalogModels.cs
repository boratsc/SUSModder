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

    [JsonPropertyName("icon")]
    public CatalogIconDto? Icon { get; init; }

    /// <summary>
    /// True, gdy ładunek JSON zawierał człon <c>icon</c> (obiekt albo jawne null).
    /// Rozróżnia „pole nieobecne” (zachowaj ostatnie przypisanie) od
    /// „jawnie usunięte przypisanie” (usuń). Uzupełniane przez warstwę transportową.
    /// </summary>
    [JsonIgnore]
    public bool IconMemberPresent { get; set; }

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

    /// <summary>
    /// True, gdy katalog oznacza <c>currentVersion</c> jako wydanie beta.
    /// Nullable: brak pola oznacza serwer w trybie legacy (tylko stabilne wydania).
    /// </summary>
    [JsonPropertyName("isBeta")]
    public bool? IsBeta { get; init; }

    [JsonPropertyName("latestStableVersion")]
    public string? LatestStableVersion { get; init; }

    [JsonPropertyName("latestBetaVersion")]
    public string? LatestBetaVersion { get; init; }

    /// <summary>
    /// Addytywna lista wydań (stabilne + beta). Brak pola = tryb legacy;
    /// pusta lista = serwer jawnie deklaruje brak wydań poza bieżącym.
    /// </summary>
    [JsonPropertyName("availableReleases")]
    public List<CatalogReleaseDto>? AvailableReleases { get; init; }
}

/// <summary>
/// Pojedyncze wydanie moda z katalogu (3.1-11). <c>releaseId</c> jest nieprzezroczystym,
/// niezmiennym identyfikatorem; numer wersji może się powtarzać w różnych kanałach.
/// </summary>
public sealed class CatalogReleaseDto
{
    [JsonPropertyName("releaseId")]
    public string ReleaseId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("isBeta")]
    public bool IsBeta { get; init; }

    [JsonPropertyName("amongVersion")]
    public string? AmongVersion { get; init; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; init; }

    /// <summary>
    /// Warianty pobierania dla tego wydania. Obecne tylko w odpowiedzi szczegółów
    /// (<c>GET /catalog/{id}</c>); lista katalogu ich nie zwraca.
    /// </summary>
    [JsonPropertyName("variants")]
    public List<CatalogModVariantDto>? Variants { get; init; }
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
    [JsonPropertyName("iconsSchemaVersion")]
    public int? IconsSchemaVersion { get; init; }

    [JsonPropertyName("iconsRevision")]
    public string? IconsRevision { get; init; }

    [JsonPropertyName("icons")]
    public CatalogGlobalIconsDto? Icons { get; init; }

    [JsonPropertyName("catalogRevision")]
    public string? CatalogRevision { get; init; }

    [JsonPropertyName("compatibilityRevision")]
    public string? CompatibilityRevision { get; init; }

    [JsonPropertyName("versionsRevision")]
    public string? VersionsRevision { get; init; }

    [JsonPropertyName("serverTimeUtc")]
    public string? ServerTimeUtc { get; init; }
}

public sealed class CatalogIconDto
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = string.Empty;
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = string.Empty;
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
}

public sealed class CatalogGlobalIconsDto
{
    [JsonPropertyName("vanilla")]
    public CatalogIconDto? Vanilla { get; init; }

    /// <summary>True, gdy ładunek zawierał człon <c>vanilla</c> (także jako jawne null).</summary>
    [JsonIgnore]
    public bool VanillaMemberPresent { get; set; }

    [JsonPropertyName("dll")]
    public CatalogIconDto? Dll { get; init; }

    /// <summary>True, gdy ładunek zawierał człon <c>dll</c> (także jako jawne null).</summary>
    [JsonIgnore]
    public bool DllMemberPresent { get; set; }
}

public sealed class CatalogVersionsDto
{
    [JsonPropertyName("modId")]
    public int ModId { get; init; }

    [JsonPropertyName("modName")]
    public string ModName { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>Najnowsze stabilne wydanie; null gdy serwer w trybie legacy.</summary>
    [JsonPropertyName("latestStableVersion")]
    public string? LatestStableVersion { get; init; }

    /// <summary>Najnowsze wydanie beta; null/brak = brak bety lub tryb legacy.</summary>
    [JsonPropertyName("latestBetaVersion")]
    public string? LatestBetaVersion { get; init; }

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

    /// <summary>Czy ta wersja historyczna jest wydaniem beta (3.1-11).</summary>
    [JsonPropertyName("isBeta")]
    public bool IsBeta { get; init; }

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

    [JsonPropertyName("isBeta")]
    public bool? IsBeta { get; init; }

    [JsonPropertyName("latestStableVersion")]
    public string? LatestStableVersion { get; init; }

    [JsonPropertyName("latestBetaVersion")]
    public string? LatestBetaVersion { get; init; }

    [JsonPropertyName("availableReleases")]
    public List<CatalogReleaseDto>? AvailableReleases { get; init; }

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
