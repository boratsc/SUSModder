using System.Net.Http;
using System.Text.Json;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Api;

/// <summary>
/// Jedyny punkt dostępu do API SUSModder v2 w warstwie Core.
/// Wszystkie wywołania HTTP do api.susmodder-cdn.ovh/v2 powinny przechodzić przez ten interfejs.
/// </summary>
public interface ISUSModderApiClient : IDisposable
{
    string BaseUrl { get; }

    string StaticAssetsBaseUrl { get; }

    string BuildModDownloadUrl(int modId, string version, string platform, string arch = "x86");

    Task<SusModderApiResult<List<CatalogItemDto>>> GetCatalogAsync(
        CatalogQuery? query = null,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<List<ModConfiguration>> GetCatalogAsModConfigurationsAsync(
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<CatalogMetaDto>> GetCatalogMetaAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<CatalogModDetailDto>> GetCatalogModDetailAsync(
        int modId,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<CatalogVersionsDto>> GetCatalogVersionsAsync(
        int modId,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<CompatibilityDataDto>> GetCompatibilityAsync(
        CompatibilityQueryParams query,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<CompatibilitySnapshotDto>> GetCompatibilitySnapshotAsync(
        bool onlyCurrentVersions = true,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<List<AmongUsVersionDto>>> GetAmongUsVersionsAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<AmongUsVersionDto>> GetAmongUsVersionAsync(
        string dbValue,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<JsonElement>> GetRolesAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<JsonElement>> GetDiscordFavoritesPublicAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<JsonElement>> GetDiscordServerCountsAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<OnlineUsersDto>> GetOnlineAsync(
        CancellationToken cancellationToken = default);

    Task<SusModderApiResult<JsonElement>> GetReleasesAsync(
        string? channel = null,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default);

    Task SendHeartbeatAsync(object payload, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> SendAsync(
        SusModderApiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SusModderApiRequest
{
    public required HttpMethod Method { get; init; }
    public required string RelativePath { get; init; }
    public IDictionary<string, string?>? Query { get; init; }
    public HttpContent? Content { get; init; }
    public string? IfNoneMatch { get; init; }
    public string? UserHash { get; init; }
    public bool IncludeAuthToken { get; init; }
}
