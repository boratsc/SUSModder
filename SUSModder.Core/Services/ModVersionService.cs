using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do zarządzania wersjami modów - pobieranie historii, instalacja starszych wersji
    /// </summary>
    public class ModVersionService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IDiagnosticsOutput _log;
        private readonly string _apiBaseUrl;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheMinutes = 5;

        public ModVersionService(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _log = log;

            var baseUrl = configuration.GetSection("Configuration")["BaseUrl"]
                ?? "https://susmodder.boracik.pl/";
            _apiBaseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Pobierz historię wersji dla moda
        /// </summary>
        public async Task<List<ModVersionHistory>> GetVersionHistoryAsync(int modId)
        {
            string cacheKey = $"version_history_{modId}";

            // Sprawdź cache
            if (_cache.TryGetValue(cacheKey, out List<ModVersionHistory>? cached))
            {
                _log.Write($"[Cache HIT] Historia wersji dla moda {modId}");
                return cached!;
            }

            try
            {
                var url = $"{_apiBaseUrl}/api/susmodder-config-versions?modId={modId}";
                _log.Write($"[ModVersionService] Pobieranie historii z: {url}");

                // Dodaj token autoryzacji
                string token = SecretProvider.GetDownloadToken();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var result = JsonSerializer.Deserialize<ModVersionsResponse>(json, options);

                if (result?.Success == true && result.Versions != null)
                {
                    _log.Write($"[ModVersionService] Pobrano {result.Count} wersji dla moda {modId}");

                    // Cache na 5 minut
                    _cache.Set(cacheKey, result.Versions, TimeSpan.FromMinutes(CacheMinutes));

                    return result.Versions;
                }

                _log.Write($"[ModVersionService] Brak wersji dla moda {modId} (success={result?.Success})");
                return new List<ModVersionHistory>();
            }
            catch (HttpRequestException ex)
            {
                _log.Write($"[ERROR] Błąd HTTP podczas pobierania wersji: {ex.Message}");
                throw new Exception($"Nie można pobrać historii wersji: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _log.Write($"[ERROR] Timeout podczas pobierania wersji: {ex.Message}");
                throw new TimeoutException("Przekroczono czas oczekiwania na odpowiedź API", ex);
            }
            catch (JsonException ex)
            {
                _log.Write($"[ERROR] Błąd parsowania JSON: {ex.Message}");
                throw new Exception($"Nieprawidłowa odpowiedź z API: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Nieoczekiwany błąd w GetVersionHistoryAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Pobierz konkretną wersję moda
        /// </summary>
        public async Task<ModVersionHistory?> GetSpecificVersionAsync(int modId, int versionId)
        {
            var versions = await GetVersionHistoryAsync(modId);
            return versions.FirstOrDefault(v => v.VersionId == versionId);
        }

        /// <summary>
        /// Pobierz wszystkie dostępne wersje dla moda (z nazwami dla UI)
        /// </summary>
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

        /// <summary>
        /// Sprawdź czy istnieje nowsza wersja niż podana
        /// </summary>
        public async Task<bool> IsNewerVersionAvailableAsync(int modId, string currentVersion)
        {
            try
            {
                var versions = await GetVersionHistoryAsync(modId);
                
                if (versions.Count == 0)
                    return false;
                
                // Najnowsza wersja to pierwsza na liście (API sortuje od najnowszej)
                var latestVersion = versions.First().ModVersion;
                
                // Proste porównanie stringów - można rozbudować o semantic versioning
                bool isNewer = !string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
                
                if (isNewer)
                {
                    _log.Write($"[ModVersionService] Dostępna nowsza wersja dla moda {modId}: {currentVersion} → {latestVersion}");
                }
                
                return isNewer;
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Błąd w IsNewerVersionAvailableAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wyczyść cache (użyteczne po aktualizacji danych)
        /// </summary>
        public void ClearCache(int? modId = null)
        {
            if (modId.HasValue)
            {
                _cache.Remove($"version_history_{modId.Value}");
                _log.Write($"[Cache] Wyczyszczono cache dla moda {modId}");
            }
            else
            {
                _log.Write($"[Cache] Brak metody czyszczenia całego cache w MemoryCache");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
