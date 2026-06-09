using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Api;

public sealed class SUSModderApiClient : ISUSModderApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly IDiagnosticsOutput _log;
    private readonly bool _ownsHttpClient;

    public string BaseUrl { get; }

    public string StaticAssetsBaseUrl { get; }

    public SUSModderApiClient(IConfiguration configuration, IDiagnosticsOutput log, HttpClient? httpClient = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var configSection = configuration.GetSection("Configuration");
        var configured = configSection["ApiV2BaseUrl"]
            ?? "https://api.susmodder-cdn.ovh/v2";
        BaseUrl = configured.TrimEnd('/');
        StaticAssetsBaseUrl = (configSection["BaseUrl"] ?? "https://susmodder.app/").TrimEnd('/');

        if (httpClient is null)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public string BuildModDownloadUrl(int modId, string version, string platform, string arch = "x86")
    {
        var normalizedPlatform = platform.Equals("epic", StringComparison.OrdinalIgnoreCase) ? "epic" : "steam";
        return $"{BaseUrl}/downloads/mod/{modId}/{Uri.EscapeDataString(version)}?platform={normalizedPlatform}&arch={arch}";
    }

    public async Task<SusModderApiResult<List<CatalogItemDto>>> GetCatalogAsync(
        CatalogQuery? query = null,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new CatalogQuery();
        var queryParams = new Dictionary<string, string?>
        {
            ["offset"] = query.Offset.ToString(),
            ["limit"] = query.Limit.ToString()
        };

        if (!string.IsNullOrWhiteSpace(query.ModType))
            queryParams["modType"] = query.ModType;
        if (!string.IsNullOrWhiteSpace(query.AmongVersion))
            queryParams["amongVersion"] = query.AmongVersion;

        return await GetEnvelopeAsync<List<CatalogItemDto>>("catalog", queryParams, ifNoneMatch, cancellationToken);
    }

    public async Task<List<ModConfiguration>> GetCatalogAsModConfigurationsAsync(
        CancellationToken cancellationToken = default)
    {
        var allItems = new List<CatalogItemDto>();
        const int pageSize = 200;
        var offset = 0;
        int? total = null;

        while (true)
        {
            var page = await GetCatalogAsync(
                new CatalogQuery { Offset = offset, Limit = pageSize },
                cancellationToken: cancellationToken);

            if (!page.IsSuccess || page.Data is null)
            {
                if (allItems.Count > 0)
                    break;

                _log.Write($"[ApiClient] GetCatalog failed: HTTP {page.StatusCode}");
                return [];
            }

            allItems.AddRange(page.Data);
            total ??= page.Meta?.Total;
            if (page.Data.Count == 0 ||
                page.Data.Count < pageSize ||
                (total.HasValue && allItems.Count >= total.Value))
                break;

            offset += pageSize;
        }

        return allItems
            .Select(item => CatalogMapper.ToModConfiguration(item, this))
            .ToList();
    }

    public Task<SusModderApiResult<CatalogMetaDto>> GetCatalogMetaAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<CatalogMetaDto>("catalog-meta", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<CatalogModDetailDto>> GetCatalogModDetailAsync(
        int modId,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<CatalogModDetailDto>($"catalog/{modId}", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<CatalogVersionsDto>> GetCatalogVersionsAsync(
        int modId,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<CatalogVersionsDto>($"catalog/{modId}/versions", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<CompatibilityDataDto>> GetCompatibilityAsync(
        CompatibilityQueryParams query,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new Dictionary<string, string?>();
        if (query.FullModId.HasValue)
            queryParams["fullModId"] = query.FullModId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(query.FullModVersion))
            queryParams["fullModVersion"] = query.FullModVersion;
        if (query.DllModId.HasValue)
            queryParams["dllModId"] = query.DllModId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(query.DllModVersion))
            queryParams["dllModVersion"] = query.DllModVersion;
        if (!string.IsNullOrWhiteSpace(query.Status))
            queryParams["status"] = query.Status;
        if (query.IncludeUntested.HasValue)
            queryParams["includeUntested"] = query.IncludeUntested.Value ? "true" : "false";

        return GetEnvelopeAsync<CompatibilityDataDto>("compatibility", queryParams, ifNoneMatch, cancellationToken);
    }

    public Task<SusModderApiResult<CompatibilitySnapshotDto>> GetCompatibilitySnapshotAsync(
        bool onlyCurrentVersions = true,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new Dictionary<string, string?>
        {
            ["onlyCurrentVersions"] = onlyCurrentVersions ? "true" : "false"
        };
        return GetEnvelopeAsync<CompatibilitySnapshotDto>("compatibility/snapshot", queryParams, ifNoneMatch, cancellationToken);
    }

    public Task<SusModderApiResult<List<AmongUsVersionDto>>> GetAmongUsVersionsAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<List<AmongUsVersionDto>>("versions", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<AmongUsVersionDto>> GetAmongUsVersionAsync(
        string dbValue,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<AmongUsVersionDto>($"versions/{Uri.EscapeDataString(dbValue)}", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<JsonElement>> GetRolesAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<JsonElement>("roles", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<JsonElement>> GetDiscordFavoritesPublicAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<JsonElement>("discord/favs/public", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<JsonElement>> GetDiscordServerCountsAsync(
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<JsonElement>("discord/server-counts", null, ifNoneMatch, cancellationToken);

    public Task<SusModderApiResult<OnlineUsersDto>> GetOnlineAsync(
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<OnlineUsersDto>("online", null, null, cancellationToken);

    public Task<SusModderApiResult<JsonElement>> GetReleasesAsync(
        string? channel = null,
        string? ifNoneMatch = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string?>? query = null;
        if (!string.IsNullOrWhiteSpace(channel))
            query = new Dictionary<string, string?> { ["channel"] = channel };

        return GetEnvelopeAsync<JsonElement>("releases", query, ifNoneMatch, cancellationToken);
    }

    public async Task SendHeartbeatAsync(object payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new SusModderApiRequest
        {
            Method = HttpMethod.Post,
            RelativePath = "telemetry/heartbeat",
            Content = content
        };

        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            _log.Write($"[ApiClient] Heartbeat failed: HTTP {(int)response.StatusCode}");
        }
    }

    public async Task<HttpResponseMessage> SendAsync(
        SusModderApiRequest request,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(request.RelativePath, request.Query);
        using var httpRequest = new HttpRequestMessage(request.Method, url);

        httpRequest.Headers.TryAddWithoutValidation("User-Agent", "SUSModder/3.0");
        if (!string.IsNullOrWhiteSpace(request.IfNoneMatch))
            httpRequest.Headers.TryAddWithoutValidation("If-None-Match", request.IfNoneMatch);
        if (!string.IsNullOrWhiteSpace(request.UserHash))
            httpRequest.Headers.TryAddWithoutValidation("X-User-Hash", request.UserHash);
        if (request.IncludeAuthToken)
            httpRequest.Headers.TryAddWithoutValidation("Authorization", SecretProvider.GetDownloadToken());

        if (request.Content is not null)
            httpRequest.Content = request.Content;

        return await _httpClient.SendAsync(httpRequest, cancellationToken);
    }

    private async Task<SusModderApiResult<T>> GetEnvelopeAsync<T>(
        string relativePath,
        IDictionary<string, string?>? query,
        string? ifNoneMatch,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new SusModderApiRequest
        {
            Method = HttpMethod.Get,
            RelativePath = relativePath,
            Query = query,
            IfNoneMatch = ifNoneMatch
        }, cancellationToken);

        var etag = response.Headers.ETag?.Tag?.Trim('"');

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new SusModderApiResult<T>
            {
                StatusCode = 304,
                ETag = etag
            };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            SusModderApiError? error = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var errorElement))
                    error = JsonSerializer.Deserialize<SusModderApiError>(errorElement.GetRawText(), JsonOptions);
            }
            catch
            {
                // ignore parse errors for error body
            }

            return new SusModderApiResult<T>
            {
                StatusCode = (int)response.StatusCode,
                ETag = etag,
                Error = error
            };
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new SusModderApiResult<T>
            {
                StatusCode = (int)response.StatusCode,
                ETag = etag,
                Data = default
            };
        }

        using var envelope = JsonDocument.Parse(body);
        var root = envelope.RootElement;

        T? data = default;
        SusModderApiMeta? meta = null;

        if (root.TryGetProperty("data", out var dataElement))
            data = JsonSerializer.Deserialize<T>(dataElement.GetRawText(), JsonOptions);

        if (root.TryGetProperty("meta", out var metaElement))
            meta = JsonSerializer.Deserialize<SusModderApiMeta>(metaElement.GetRawText(), JsonOptions);

        return new SusModderApiResult<T>
        {
            StatusCode = (int)response.StatusCode,
            ETag = etag,
            Data = data,
            Meta = meta
        };
    }

    private string BuildUrl(string relativePath, IDictionary<string, string?>? query)
    {
        var path = relativePath.TrimStart('/');
        var url = $"{BaseUrl}/{path}";

        if (query is null || query.Count == 0)
            return url;

        var parts = query
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");

        var queryString = string.Join("&", parts);
        return string.IsNullOrEmpty(queryString) ? url : $"{url}?{queryString}";
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
