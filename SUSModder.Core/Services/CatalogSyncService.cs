using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

public sealed class CatalogSyncService
{
    public const string CatalogStateKey = "catalog.config";
    public const string CompatibilityStateKey = "compatibility.snapshot";

    private static readonly TimeSpan CatalogMinInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CompatibilityMinInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RemoteConfigMemoryTtl = TimeSpan.FromMinutes(2);
    private static readonly MemoryCache RemoteConfigCache = new(new MemoryCacheOptions());

    private readonly ISUSModderApiClient _apiClient;
    private readonly IModRepository _modRepository;
    private readonly ICatalogSyncStateRepository _syncState;
    private readonly ICompatibilityCacheRepository _compatibilityCache;
    private readonly IDiagnosticsOutput _log;
    private readonly SemaphoreSlim _catalogFlight = new(1, 1);
    private readonly SemaphoreSlim _compatibilityFlight = new(1, 1);

    public CatalogSyncService(
        ISUSModderApiClient apiClient,
        IModRepository modRepository,
        ICatalogSyncStateRepository syncState,
        ICompatibilityCacheRepository compatibilityCache,
        IDiagnosticsOutput log)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _modRepository = modRepository ?? throw new ArgumentNullException(nameof(modRepository));
        _syncState = syncState ?? throw new ArgumentNullException(nameof(syncState));
        _compatibilityCache = compatibilityCache ?? throw new ArgumentNullException(nameof(compatibilityCache));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<CatalogSyncResult> RefreshCatalogIfDueAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && IsInBackoff(CatalogStateKey))
        {
            return OfflineResult(CatalogStateKey, CatalogSyncStatus.SkippedBackoff);
        }

        if (!force && !IsDue(CatalogStateKey, CatalogMinInterval))
        {
            var meta = _syncState.Get(CatalogStateKey);
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.NotModified,
                ETag = meta?.ETag,
                ModCount = _modRepository.GetAllMods().Count,
                LastSuccessUtc = meta?.LastSuccessUtc
            };
        }

        if (!await _catalogFlight.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            return new CatalogSyncResult { Status = CatalogSyncStatus.Failed, ErrorMessage = "catalog sync busy" };
        }

        try
        {
            return await RefreshCatalogCoreAsync(force, cancellationToken);
        }
        finally
        {
            _catalogFlight.Release();
        }
    }

    public async Task<CatalogSyncResult> RefreshCompatibilityIfDueAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!force && IsInBackoff(CompatibilityStateKey))
        {
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.SkippedBackoff,
                ModCount = _compatibilityCache.Count()
            };
        }

        if (!force && !IsDue(CompatibilityStateKey, CompatibilityMinInterval) && _compatibilityCache.Count() > 0)
        {
            var meta = _syncState.Get(CompatibilityStateKey);
            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.NotModified,
                ETag = meta?.ETag,
                ModCount = _compatibilityCache.Count(),
                LastSuccessUtc = meta?.LastSuccessUtc
            };
        }

        if (!await _compatibilityFlight.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            return new CatalogSyncResult { Status = CatalogSyncStatus.Failed, ErrorMessage = "compatibility sync busy" };
        }

        try
        {
            return await RefreshCompatibilityCoreAsync(cancellationToken);
        }
        finally
        {
            _compatibilityFlight.Release();
        }
    }

    public async Task<List<ModConfiguration>> EnsureRemoteConfigCachedAsync(CancellationToken cancellationToken = default)
    {
        if (RemoteConfigCache.TryGetValue<List<ModConfiguration>>("remote_catalog", out var cached) && cached != null)
            return cached;

        await RefreshCatalogIfDueAsync(force: false, cancellationToken);
        var mods = _modRepository.GetAllMods();
        RemoteConfigCache.Set("remote_catalog", mods, RemoteConfigMemoryTtl);
        return mods;
    }

    public void InvalidateRemoteConfigCache() => RemoteConfigCache.Remove("remote_catalog");

    private async Task<CatalogSyncResult> RefreshCatalogCoreAsync(bool force, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var state = _syncState.Get(CatalogStateKey);
        var localCount = _modRepository.GetAllMods().Count;

        try
        {
            var fetch = await FetchCatalogAsync(state?.ETag, cancellationToken);

            if (fetch.NotModified)
            {
                _syncState.SaveSuccess(CatalogStateKey, fetch.ETag ?? state?.ETag, null, nowUtc);
                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.NotModified,
                    ETag = fetch.ETag ?? state?.ETag,
                    ModCount = localCount,
                    LastSuccessUtc = nowUtc
                };
            }

            if (fetch.Mods.Count == 0)
            {
                _syncState.SaveFailure(CatalogStateKey, "EMPTY_RESPONSE", nowUtc, ComputeNextAttemptUtc(state));
                if (localCount > 0)
                {
                    return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache,
                        "Pusta odpowiedź API — użyto lokalnego katalogu.");
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.InvalidResponse,
                    ErrorMessage = "Pusta odpowiedź katalogu",
                    ModCount = 0
                };
            }

            if (!ValidateCatalog(fetch.Mods, localCount, out var validationError))
            {
                _syncState.SaveFailure(CatalogStateKey, "INVALID_RESPONSE", nowUtc, ComputeNextAttemptUtc(state));
                if (localCount > 0)
                {
                    return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache, validationError);
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.InvalidResponse,
                    ErrorMessage = validationError,
                    ModCount = localCount
                };
            }

            var changed = await _modRepository.ApplyRemoteCatalogAsync(fetch.Mods);
            _syncState.SaveSuccess(CatalogStateKey, fetch.ETag, null, nowUtc);
            InvalidateRemoteConfigCache();

            return new CatalogSyncResult
            {
                Status = changed ? CatalogSyncStatus.Updated : CatalogSyncStatus.NotModified,
                ETag = fetch.ETag,
                ModCount = fetch.Mods.Count,
                LastSuccessUtc = nowUtc
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] catalog refresh failed: {ex.Message}");
            _syncState.SaveFailure(CatalogStateKey, "EXCEPTION", nowUtc, ComputeNextAttemptUtc(state));

            if (localCount > 0)
            {
                return OfflineResult(CatalogStateKey, CatalogSyncStatus.OfflineUsingCache, ex.Message);
            }

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Failed,
                ErrorMessage = ex.Message,
                ModCount = localCount
            };
        }
    }

    private async Task<CatalogSyncResult> RefreshCompatibilityCoreAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var state = _syncState.Get(CompatibilityStateKey);
        var cachedCount = _compatibilityCache.Count();

        try
        {
            var response = await _apiClient.GetCompatibilitySnapshotAsync(
                onlyCurrentVersions: true,
                ifNoneMatch: state?.ETag,
                cancellationToken: cancellationToken);

            if (response.IsNotModified)
            {
                _syncState.SaveSuccess(CompatibilityStateKey, response.ETag ?? state?.ETag, null, nowUtc);
                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.NotModified,
                    ETag = response.ETag ?? state?.ETag,
                    ModCount = cachedCount,
                    LastSuccessUtc = nowUtc
                };
            }

            if (!response.IsSuccess || response.Data?.Entries is null)
            {
                _syncState.SaveFailure(CompatibilityStateKey, $"HTTP_{response.StatusCode}", nowUtc, ComputeNextAttemptUtc(state));
                if (cachedCount > 0)
                {
                    return new CatalogSyncResult
                    {
                        Status = CatalogSyncStatus.OfflineUsingCache,
                        ModCount = cachedCount,
                        LastSuccessUtc = state?.LastSuccessUtc
                    };
                }

                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.Failed,
                    ErrorMessage = response.Error?.Message ?? $"HTTP {response.StatusCode}",
                    ModCount = 0
                };
            }

            var entries = response.Data.Entries
                .Select(e => new CompatibilityCacheEntry
                {
                    FullModId = e.FullModId,
                    FullModVersion = e.FullModVersion,
                    DllModId = e.DllModId,
                    DllModVersion = e.DllModVersion,
                    Status = CompatibilityMerger.NormalizeStatusCode(e.Status),
                    IsExactVersion = e.IsExactVersion,
                    Warning = e.Warning,
                    SourceUpdatedAt = response.Data.GeneratedAtUtc ?? response.Data.Revision,
                    FetchedAtUtc = nowUtc
                })
                .ToList();

            _compatibilityCache.SaveSnapshot(entries, response.Data.Revision ?? response.ETag, nowUtc);
            _syncState.SaveSuccess(CompatibilityStateKey, response.ETag ?? response.Data.Revision, null, nowUtc);

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Updated,
                ETag = response.ETag ?? response.Data.Revision,
                ModCount = entries.Count,
                LastSuccessUtc = nowUtc
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[CatalogSync] compatibility refresh failed: {ex.Message}");
            _syncState.SaveFailure(CompatibilityStateKey, "EXCEPTION", nowUtc, ComputeNextAttemptUtc(state));

            if (cachedCount > 0)
            {
                return new CatalogSyncResult
                {
                    Status = CatalogSyncStatus.OfflineUsingCache,
                    ModCount = cachedCount,
                    LastSuccessUtc = state?.LastSuccessUtc,
                    ErrorMessage = ex.Message
                };
            }

            return new CatalogSyncResult
            {
                Status = CatalogSyncStatus.Failed,
                ErrorMessage = ex.Message,
                ModCount = 0
            };
        }
    }

    private async Task<(List<ModConfiguration> Mods, string? ETag, bool NotModified)> FetchCatalogAsync(
        string? etag,
        CancellationToken cancellationToken)
    {
        var allItems = new List<CatalogItemDto>();
        const int pageSize = 200;
        var offset = 0;
        int? total = null;
        string? responseEtag = etag;
        var notModified = false;

        while (true)
        {
            var page = await _apiClient.GetCatalogAsync(
                new CatalogQuery { Offset = offset, Limit = pageSize },
                ifNoneMatch: offset == 0 ? etag : null,
                cancellationToken: cancellationToken);

            if (page.IsNotModified)
            {
                notModified = true;
                responseEtag = page.ETag ?? etag;
                break;
            }

            if (!page.IsSuccess || page.Data is null)
            {
                if (allItems.Count > 0)
                    break;

                return ([], page.ETag, false);
            }

            responseEtag = page.ETag ?? responseEtag;
            allItems.AddRange(page.Data);
            total ??= page.Meta?.Total;

            if (page.Data.Count == 0 ||
                page.Data.Count < pageSize ||
                (total.HasValue && allItems.Count >= total.Value))
                break;

            offset += pageSize;
        }

        if (notModified)
            return ([], responseEtag, true);

        var mods = allItems
            .Select(item => CatalogMapper.ToModConfiguration(item, _apiClient))
            .ToList();

        return (mods, responseEtag, false);
    }

    private static bool ValidateCatalog(List<ModConfiguration> mods, int localCount, out string error)
    {
        error = string.Empty;
        if (mods.Count == 0)
        {
            error = "Katalog jest pusty.";
            return false;
        }

        var ids = new HashSet<int>();
        foreach (var mod in mods)
        {
            if (mod.Id <= 0 && !IsVanilla(mod))
            {
                error = $"Niepoprawne Id moda: {mod.Id} ({mod.ModName}).";
                return false;
            }

            if (!ids.Add(mod.Id))
            {
                error = $"Duplikat Id w odpowiedzi API: {mod.Id}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(mod.ModName))
            {
                error = $"Pusty ModName dla Id={mod.Id}.";
                return false;
            }

            if (!IsAllowedType(mod.ModType))
            {
                error = $"Niepoprawny ModType '{mod.ModType}' dla {mod.ModName}.";
                return false;
            }

            if (!IsVanilla(mod) &&
                (mod.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                 mod.ModType.Equals("dll", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(mod.ModVersion))
                {
                    error = $"Brak ModVersion dla {mod.ModName}.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(mod.GitHubRepoOrLink))
                {
                    error = $"Brak linku pobierania dla {mod.ModName}.";
                    return false;
                }
            }
        }

        if (localCount > 0 && mods.Count < localCount * 0.5)
        {
            error = $"Podejrzany spadek liczby modów: {localCount} → {mods.Count}.";
            return false;
        }

        return true;
    }

    private bool IsDue(string key, TimeSpan minInterval)
    {
        var state = _syncState.Get(key);
        if (state?.LastSuccessUtc is null)
            return true;

        return DateTime.UtcNow - state.LastSuccessUtc.Value >= minInterval;
    }

    private bool IsInBackoff(string key)
    {
        var state = _syncState.Get(key);
        return state?.NextAllowedAttemptUtc is DateTime next && next > DateTime.UtcNow;
    }

    private CatalogSyncResult OfflineResult(string key, CatalogSyncStatus status, string? message = null)
    {
        var meta = _syncState.Get(key);
        return new CatalogSyncResult
        {
            Status = status,
            ETag = meta?.ETag,
            ModCount = key == CatalogStateKey ? _modRepository.GetAllMods().Count : _compatibilityCache.Count(),
            LastSuccessUtc = meta?.LastSuccessUtc,
            ErrorMessage = message
        };
    }

    private static DateTime ComputeNextAttemptUtc(CatalogSnapshotMetadata? state)
    {
        var failures = (state?.FailureCount ?? 0) + 1;
        var delayMinutes = failures switch
        {
            1 => 1,
            2 => 2,
            3 => 5,
            _ => 15
        };
        var jitterSeconds = Random.Shared.Next(0, 30);
        return DateTime.UtcNow.AddMinutes(delayMinutes).AddSeconds(jitterSeconds);
    }

    private static bool IsVanilla(ModConfiguration mod) =>
        mod.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
        mod.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedType(string? modType) =>
        modType is "full" or "dll" or "Vanilla";
}
