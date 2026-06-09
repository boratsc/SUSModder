using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do zarządzania wersjami modów - pobieranie historii, instalacja starszych wersji.
    /// </summary>
    public class ModVersionService
    {
        private readonly ISUSModderApiClient _apiClient;
        private readonly IDiagnosticsOutput _log;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheMinutes = 5;

        public ModVersionService(
            IDiagnosticsOutput log,
            ISUSModderApiClient? apiClient = null)
        {
            _log = log;
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? throw new InvalidOperationException("ISUSModderApiClient nie jest dostępny.");
        }

        public async Task<List<ModVersionHistory>> GetVersionHistoryAsync(int modId)
        {
            string cacheKey = $"version_history_{modId}";

            if (_cache.TryGetValue(cacheKey, out List<ModVersionHistory>? cached))
            {
                _log.Write($"[Cache HIT] Historia wersji dla moda {modId}");
                return cached!;
            }

            try
            {
                _log.Write($"[ModVersionService] Pobieranie historii dla moda {modId}");

                var response = await _apiClient.GetCatalogVersionsAsync(modId);
                if (!response.IsSuccess || response.Data?.Versions is null)
                {
                    _log.Write($"[ModVersionService] Brak wersji dla moda {modId} (HTTP {response.StatusCode})");
                    return new List<ModVersionHistory>();
                }

                var versions = response.Data.Versions
                    .Select(v => CatalogMapper.ToModVersionHistory(v, modId))
                    .ToList();

                _log.Write($"[ModVersionService] Pobrano {versions.Count} wersji dla moda {modId}");
                _cache.Set(cacheKey, versions, TimeSpan.FromMinutes(CacheMinutes));
                return versions;
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Nieoczekiwany błąd w GetVersionHistoryAsync: {ex}");
                throw;
            }
        }

        public async Task<ModVersionHistory?> GetSpecificVersionAsync(int modId, int versionId)
        {
            var versions = await GetVersionHistoryAsync(modId);
            return versions.FirstOrDefault(v => v.VersionId == versionId);
        }

        public async Task<List<(int VersionId, string DisplayText)>> GetAvailableVersionsForUIAsync(int modId)
        {
            try
            {
                var versions = await GetVersionHistoryAsync(modId);
                return versions
                    .Select(v => (v.VersionId, v.DisplayText))
                    .ToList();
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Błąd w GetAvailableVersionsForUIAsync: {ex.Message}");
                return new List<(int, string)>();
            }
        }

        public async Task<bool> IsNewerVersionAvailableAsync(int modId, string currentVersion)
        {
            try
            {
                var versions = await GetVersionHistoryAsync(modId);
                if (versions.Count == 0)
                    return false;

                var latestVersion = versions.First().ModVersion;
                bool isNewer = !string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);

                if (isNewer)
                    _log.Write($"[ModVersionService] Dostępna nowsza wersja dla moda {modId}: {currentVersion} → {latestVersion}");

                return isNewer;
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Błąd w IsNewerVersionAvailableAsync: {ex.Message}");
                return false;
            }
        }

        public void ClearCache(int? modId = null)
        {
            if (modId.HasValue)
            {
                _cache.Remove($"version_history_{modId.Value}");
                _log.Write($"[Cache] Wyczyszczono cache dla moda {modId}");
            }
        }
    }
}
