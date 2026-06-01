using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

public sealed class AmongUsManifestService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    private List<SteamManifestInfo>? _listCache;
    private DateTimeOffset _listCacheExpiry;

    public AmongUsManifestService(IConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<SteamManifestInfo?> GetManifestForVersionAsync(string amongVersion, CancellationToken ct = default)
    {
        var normalized = AmongUsVersionHelper.NormalizeAmongVersion(amongVersion);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        try
        {
            var direct = await GetAsync<SteamManifestApiDto>(
                $"among-us-steam-manifests/{Uri.EscapeDataString(normalized)}", ct);

            if (direct is not null && !string.IsNullOrWhiteSpace(direct.ManifestId))
                return MapDto(direct, normalized);
        }
        catch
        {
            // Fallback to list lookup below.
        }

        var list = await GetManifestListAsync(ct);
        return list.FirstOrDefault(m =>
            string.Equals(AmongUsVersionHelper.NormalizeAmongVersion(m.AmongVersion), normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.StorageVersion, AmongUsVersionHelper.ToStorageVersion(normalized), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<SteamManifestInfo>> GetManifestListAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_listCache is not null && now < _listCacheExpiry)
            return _listCache;

        try
        {
            var wrapped = await GetAsync<SteamManifestListApiDto>("among-us-steam-manifests", ct);
            var manifests = wrapped?.Manifests ?? wrapped?.Versions ?? [];
            _listCache = manifests
                .Where(m => !string.IsNullOrWhiteSpace(m.ManifestId))
                .Select(m => MapDto(m, AmongUsVersionHelper.NormalizeAmongVersion(
                    m.Version ?? m.DbValue ?? m.VersionAlias ?? string.Empty)))
                .Where(m => !string.IsNullOrWhiteSpace(m.AmongVersion))
                .ToList();
        }
        catch
        {
            _listCache ??= [];
        }

        _listCacheExpiry = now + CacheDuration;
        return _listCache;
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken ct)
    {
        var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"] ?? "https://susmodder.app/";
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        var url = $"{baseUrl}api/{relativePath.TrimStart('/')}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", SecretProvider.GetDownloadToken());

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return default;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private static SteamManifestInfo MapDto(SteamManifestApiDto dto, string amongVersion)
    {
        var resolvedAmong = AmongUsVersionHelper.NormalizeAmongVersion(
            dto.Version ?? dto.DbValue ?? dto.VersionAlias ?? amongVersion);

        return new SteamManifestInfo
        {
            AmongVersion = resolvedAmong,
            EpicVersion = dto.EpicVersion,
            StorageVersion = dto.StorageVersion ?? AmongUsVersionHelper.ToStorageVersion(resolvedAmong),
            DepotId = dto.DepotId > 0 ? dto.DepotId : 945361,
            ManifestId = dto.ManifestId ?? string.Empty,
            BuildId = dto.BuildId,
            SizeBytes = dto.SizeBytes
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class SteamManifestListApiDto
    {
        [JsonPropertyName("manifests")]
        public List<SteamManifestApiDto> Manifests { get; init; } = [];

        [JsonPropertyName("versions")]
        public List<SteamManifestApiDto> Versions { get; init; } = [];
    }

    private sealed class SteamManifestApiDto
    {
        [JsonPropertyName("amongVersion")]
        public string? Version { get; init; }

        [JsonPropertyName("version")]
        public string? VersionAlias { get; init; }

        [JsonPropertyName("dbValue")]
        public string? DbValue { get; init; }

        [JsonPropertyName("storageVersion")]
        public string? StorageVersion { get; init; }

        [JsonPropertyName("epicVersion")]
        public string? EpicVersion { get; init; }

        [JsonPropertyName("depotId")]
        public int DepotId { get; init; }

        [JsonPropertyName("manifestId")]
        public string? ManifestId { get; init; }

        [JsonPropertyName("buildId")]
        public string? BuildId { get; init; }

        [JsonPropertyName("sizeBytes")]
        public long? SizeBytes { get; init; }
    }
}
