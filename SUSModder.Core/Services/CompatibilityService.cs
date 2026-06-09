using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do sprawdzania kompatybilności modów DLL z modami FULL.
    /// Wykorzystuje API v2, SQLite cache i pamięć podręczną.
    /// </summary>
    public class CompatibilityService
    {
        private readonly ISUSModderApiClient _apiClient;
        private readonly IDiagnosticsOutput _log;
        private readonly ICompatibilityCacheRepository? _sqliteCache;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheExpirationMinutes = 10;

        public CompatibilityService(
            IDiagnosticsOutput log,
            ISUSModderApiClient? apiClient = null,
            ICompatibilityCacheRepository? sqliteCache = null)
        {
            _log = log;
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? throw new InvalidOperationException("ISUSModderApiClient nie jest dostępny.");
            _sqliteCache = sqliteCache;
        }

        public Task<CompatibilityInfo?> CheckCompatibilityAsync(
            int dllModId,
            int fullModId,
            CancellationToken cancellationToken = default) =>
            CheckCompatibilityAsync(dllModId, null, fullModId, null, cancellationToken);

        public async Task<CompatibilityInfo?> CheckCompatibilityAsync(
            int dllModId,
            string? dllModVersion,
            int fullModId,
            string? fullModVersion,
            CancellationToken cancellationToken = default)
        {
            var normalizedDllVersion = dllModVersion ?? string.Empty;
            var normalizedFullVersion = fullModVersion ?? string.Empty;
            var cacheKey = $"compat_{dllModId}_{normalizedDllVersion}_{fullModId}_{normalizedFullVersion}";

            if (_cache.TryGetValue<CompatibilityInfo>(cacheKey, out var cachedResult))
                return cachedResult;

            var sqliteEntry = TryGetSqlitePair(fullModId, normalizedFullVersion, dllModId, normalizedDllVersion);
            if (sqliteEntry != null)
            {
                var fromSqlite = sqliteEntry.ToCompatibilityInfo();
                _cache.Set(cacheKey, fromSqlite, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return fromSqlite;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await _apiClient.GetCompatibilityAsync(
                    new CompatibilityQueryParams
                    {
                        DllModId = dllModId,
                        DllModVersion = string.IsNullOrWhiteSpace(dllModVersion) ? null : dllModVersion,
                        FullModId = fullModId,
                        FullModVersion = string.IsNullOrWhiteSpace(fullModVersion) ? null : fullModVersion
                    },
                    cancellationToken: cts.Token);

                if (!response.IsSuccess || response.Data?.Compatibilities is null)
                    return sqliteEntry?.ToCompatibilityInfo();

                var relevant = response.Data.Compatibilities
                    .Where(e => EntryMatchesPair(e, dllModId, fullModId))
                    .ToList();

                var compatInfo = CompatibilityMerger.PickBestFromEntries(
                    relevant.Count > 0 ? relevant : response.Data.Compatibilities);

                if (compatInfo is null)
                    return sqliteEntry?.ToCompatibilityInfo();

                _cache.Set(cacheKey, compatInfo, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return compatInfo;
            }
            catch (OperationCanceledException)
            {
                return sqliteEntry?.ToCompatibilityInfo();
            }
            catch (Exception)
            {
                return sqliteEntry?.ToCompatibilityInfo();
            }
        }

        public Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixAsync(
            int dllModId,
            CancellationToken cancellationToken = default) =>
            GetCompatibilityMatrixAsync(dllModId, null, cancellationToken);

        public async Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixAsync(
            int dllModId,
            string? dllModVersion,
            CancellationToken cancellationToken = default)
        {
            var normalizedDllVersion = dllModVersion ?? string.Empty;
            var cacheKey = $"compat_matrix_{dllModId}_{normalizedDllVersion}";

            if (_cache.TryGetValue<Dictionary<int, CompatibilityInfo>>(cacheKey, out var cachedResult) && cachedResult != null)
                return cachedResult;

            var sqliteMatrix = BuildMatrixFromSqlite(
                _sqliteCache?.GetForDllMod(dllModId, normalizedDllVersion));
            if (sqliteMatrix.Count > 0)
            {
                _cache.Set(cacheKey, sqliteMatrix, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return sqliteMatrix;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await _apiClient.GetCompatibilityAsync(
                    new CompatibilityQueryParams
                    {
                        DllModId = dllModId,
                        DllModVersion = string.IsNullOrWhiteSpace(dllModVersion) ? null : dllModVersion
                    },
                    cancellationToken: cts.Token);

                if (!response.IsSuccess || response.Data?.Compatibilities is null)
                    return sqliteMatrix;

                var result = CompatibilityMerger.BuildMatrixByFullModId(response.Data.Compatibilities);
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return result;
            }
            catch (Exception)
            {
                return sqliteMatrix;
            }
        }

        public Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixForFullModAsync(
            int fullModId,
            CancellationToken cancellationToken = default) =>
            GetCompatibilityMatrixForFullModAsync(fullModId, null, cancellationToken);

        public async Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixForFullModAsync(
            int fullModId,
            string? fullModVersion,
            CancellationToken cancellationToken = default)
        {
            var normalizedFullVersion = fullModVersion ?? string.Empty;
            var cacheKey = $"compat_matrix_full_{fullModId}_{normalizedFullVersion}";

            if (_cache.TryGetValue<Dictionary<int, CompatibilityInfo>>(cacheKey, out var cachedResult) && cachedResult != null)
                return cachedResult;

            var sqliteMatrix = BuildMatrixFromSqliteForFull(
                _sqliteCache?.GetForFullMod(fullModId, normalizedFullVersion));
            if (sqliteMatrix.Count > 0)
            {
                _cache.Set(cacheKey, sqliteMatrix, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return sqliteMatrix;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await _apiClient.GetCompatibilityAsync(
                    new CompatibilityQueryParams
                    {
                        FullModId = fullModId,
                        FullModVersion = string.IsNullOrWhiteSpace(fullModVersion) ? null : fullModVersion
                    },
                    cancellationToken: cts.Token);

                if (!response.IsSuccess || response.Data?.Compatibilities is null)
                    return sqliteMatrix;

                var result = CompatibilityMerger.BuildMatrixByDllModId(response.Data.Compatibilities);
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheExpirationMinutes));
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Error: {ex.Message}");
                return sqliteMatrix;
            }
        }

        private CompatibilityCacheEntry? TryGetSqlitePair(
            int fullModId,
            string fullModVersion,
            int dllModId,
            string dllModVersion)
        {
            if (_sqliteCache is null)
                return null;

            var exact = _sqliteCache.GetPair(fullModId, fullModVersion, dllModId, dllModVersion);
            if (exact != null)
                return exact;

            if (!string.IsNullOrWhiteSpace(fullModVersion) || !string.IsNullOrWhiteSpace(dllModVersion))
                return null;

            return _sqliteCache.GetPair(fullModId, string.Empty, dllModId, string.Empty);
        }

        private static Dictionary<int, CompatibilityInfo> BuildMatrixFromSqlite(IReadOnlyList<CompatibilityCacheEntry>? entries)
        {
            var result = new Dictionary<int, CompatibilityInfo>();
            if (entries is null)
                return result;

            foreach (var group in entries.GroupBy(e => e.FullModId))
            {
                var exact = group.FirstOrDefault(e => e.IsExactVersion) ?? group.First();
                result[group.Key] = exact.ToCompatibilityInfo();
            }

            return result;
        }

        private static Dictionary<int, CompatibilityInfo> BuildMatrixFromSqliteForFull(IReadOnlyList<CompatibilityCacheEntry>? entries)
        {
            var result = new Dictionary<int, CompatibilityInfo>();
            if (entries is null)
                return result;

            foreach (var group in entries.GroupBy(e => e.DllModId))
            {
                var exact = group.FirstOrDefault(e => e.IsExactVersion) ?? group.First();
                result[group.Key] = exact.ToCompatibilityInfo();
            }

            return result;
        }

        private static bool EntryMatchesPair(CompatibilityEntry entry, int dllModId, int fullModId)
        {
            var dllOk = entry.DllMod == null || entry.DllMod.Id == dllModId;
            var fullOk = entry.FullMod == null || entry.FullMod.Id == fullModId;
            return dllOk && fullOk;
        }

        public void ClearCache()
        {
        }

        public static bool ShouldShowWarning(CompatibilityInfo? compatibility)
        {
            if (compatibility == null)
                return false;

            return compatibility.Status == CompatibilityStatus.NotWork ||
                   compatibility.Status == CompatibilityStatus.NotTested;
        }

        public static bool ShouldBlockInstallation(CompatibilityInfo? compatibility)
        {
            if (compatibility == null)
                return false;

            return false;
        }
    }
}
