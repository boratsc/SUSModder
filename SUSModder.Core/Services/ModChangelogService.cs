using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services;

public sealed class ModChangelogResult
{
    public List<CatalogChangelogEntryDto> Entries { get; init; } = [];
    public string? ETag { get; init; }
    public bool IsEmpty { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ModChangelogService : IDisposable
{
    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly ISUSModderApiClient _apiClient;
    private readonly IDiagnosticsOutput _log;

    public ModChangelogService(ISUSModderApiClient apiClient, IDiagnosticsOutput log)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<ModChangelogResult> GetChangelogAsync(
        int modId,
        string lang,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedLang = NormalizeLang(lang);
        var clampedLimit = Math.Clamp(limit, 1, 20);

        var cacheKey = $"mod_changelog_{modId}_{normalizedLang}_{clampedLimit}";
        ETagData? cachedEtag = null;

        if (Cache.TryGetValue(cacheKey, out ETagData? etagData))
        {
            cachedEtag = etagData;
        }

        try
        {
            var ifNoneMatch = cachedEtag?.ETag is not null
                ? EnsureQuoted(cachedEtag.ETag)
                : null;

            var result = await _apiClient.GetCatalogChangelogAsync(
                modId, normalizedLang, clampedLimit, ifNoneMatch, cancellationToken);

            if (result.IsNotModified && cachedEtag?.Entries is not null)
            {
                _log.Write($"[ModChangelog] 304 for mod {modId} — returning cached entries");
                return new ModChangelogResult
                {
                    Entries = cachedEtag.Entries,
                    ETag = result.ETag,
                    IsEmpty = cachedEtag.Entries.Count == 0
                };
            }

            if (result.StatusCode == 404)
            {
                _log.Write($"[ModChangelog] 404 for mod {modId} — no changelog available");
                Cache.Set(cacheKey, new ETagData { ETag = result.ETag, Entries = [] }, CacheTtl);
                return new ModChangelogResult { Entries = [], ETag = result.ETag, IsEmpty = true };
            }

            if (!result.IsSuccess)
            {
                var errorCode = result.Error?.Code ?? "UNKNOWN_ERROR";
                var message = result.Error?.Message ?? $"HTTP {result.StatusCode}";
                _log.Write($"[ModChangelog] API error for mod {modId}: {errorCode} ({message})");
                return new ModChangelogResult
                {
                    Entries = [],
                    ErrorCode = errorCode,
                    ErrorMessage = message
                };
            }

            var entries = result.Data ?? [];
            Cache.Set(cacheKey, new ETagData { ETag = result.ETag, Entries = entries }, CacheTtl);

            var logPreview = entries.Count > 0
                ? $"{entries.Count} entries, first version: {entries[0].Version}"
                : "empty";
            _log.Write($"[ModChangelog] Fetched changelog for mod {modId}: {logPreview}");

            return new ModChangelogResult
            {
                Entries = entries,
                ETag = result.ETag,
                IsEmpty = entries.Count == 0
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Write($"[ModChangelog] Network/exception for mod {modId}: {ex.Message}");

            if (cachedEtag?.Entries is not null)
            {
                return new ModChangelogResult
                {
                    Entries = cachedEtag.Entries,
                    ETag = cachedEtag.ETag,
                    IsEmpty = cachedEtag.Entries.Count == 0
                };
            }

            return new ModChangelogResult
            {
                Entries = [],
                ErrorCode = "NETWORK_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    private static string NormalizeLang(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return "pl";

        var lower = lang.Trim().ToLowerInvariant();

        // MVP: only pl and en are supported by the endpoint.
        return lower switch
        {
            "en" => "en",
            "en-us" => "en",
            "en-gb" => "en",
            _ => "pl"
        };
    }

    /// <summary>
    /// Ensures the ETag value is quoted for If-None-Match.
    /// The API client strips quotes on save; the backend requires them.
    /// </summary>
    internal static string EnsureQuoted(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
            return etag;
        return etag.StartsWith('"') ? etag : $"\"{etag}\"";
    }

    public void Dispose()
    {
        // No managed resources to dispose in MVP.
        // Cache is static and lives for the application lifetime.
    }

    private sealed class ETagData
    {
        public string? ETag { get; init; }
        public List<CatalogChangelogEntryDto>? Entries { get; init; }
    }
}
