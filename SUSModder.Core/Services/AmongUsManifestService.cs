using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services;

public sealed class AmongUsManifestService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ISUSModderApiClient _apiClient;

    private List<SteamManifestInfo>? _listCache;
    private DateTimeOffset _listCacheExpiry;

    public AmongUsManifestService(
        IConfiguration configuration,
        ISUSModderApiClient? apiClient = null,
        IDiagnosticsOutput? diagnostics = null)
    {
        _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
            ?? new SUSModderApiClient(configuration, diagnostics ?? new NullAmongUsDiagnostics());
    }

    public async Task<SteamManifestInfo?> GetManifestForVersionAsync(string amongVersion, CancellationToken ct = default)
    {
        var normalized = AmongUsVersionHelper.NormalizeAmongVersion(amongVersion);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        try
        {
            var response = await _apiClient.GetAmongUsVersionAsync(normalized, cancellationToken: ct);
            if (response.IsSuccess && response.Data is not null)
                return MapDto(response.Data, normalized);
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
            var response = await _apiClient.GetAmongUsVersionsAsync(cancellationToken: ct);
            _listCache = (response.Data ?? [])
                .Select(dto => MapDto(dto, AmongUsVersionHelper.NormalizeAmongVersion(dto.DbValue)))
                .Where(m => !string.IsNullOrWhiteSpace(m.AmongVersion) && !string.IsNullOrWhiteSpace(m.ManifestId))
                .ToList();
        }
        catch
        {
            _listCache ??= [];
        }

        _listCacheExpiry = now + CacheDuration;
        return _listCache;
    }

    /// <summary>
    /// Lista wersji Among Us z API (również bez Steam ManifestId — wystarczy do paczek 7z).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAmongUsVersionValuesAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _apiClient.GetAmongUsVersionsAsync(cancellationToken: ct);
            return (response.Data ?? [])
                .Select(dto => AmongUsVersionHelper.NormalizeAmongVersion(
                    string.IsNullOrWhiteSpace(dto.DbValue) ? dto.Label : dto.DbValue))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static SteamManifestInfo MapDto(AmongUsVersionDto dto, string amongVersion)
    {
        var resolvedAmong = AmongUsVersionHelper.NormalizeAmongVersion(
            string.IsNullOrWhiteSpace(amongVersion) ? dto.DbValue : amongVersion);

        var steam = dto.Steam;
        return new SteamManifestInfo
        {
            AmongVersion = resolvedAmong,
            EpicVersion = dto.EpicVersion,
            StorageVersion = dto.StorageVersion ?? AmongUsVersionHelper.ToStorageVersion(resolvedAmong),
            DepotId = steam?.DepotId > 0 ? steam.DepotId : 945361,
            ManifestId = steam?.ManifestId ?? string.Empty,
            BuildId = steam?.BuildId,
            SizeBytes = steam?.SizeBytes
        };
    }

    private sealed class NullAmongUsDiagnostics : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }
}
