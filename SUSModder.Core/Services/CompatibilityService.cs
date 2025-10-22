using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do sprawdzania kompatybilności modów DLL z modami FULL.
    /// Wykorzystuje API i cache do optymalizacji.
    /// </summary>
    public class CompatibilityService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IDiagnosticsOutput _log;
        private readonly string _baseUrl;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheExpirationMinutes = 10;

        public CompatibilityService(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _log = log;

            var baseUrl = configuration.GetSection("Configuration")["BaseUrl"]
                ?? "https://susmodder.boracik.pl/";
            _baseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Sprawdza kompatybilność konkretnego moda DLL z konkretnym modem FULL.
        /// </summary>
        /// <param name="dllModId">ID moda DLL</param>
        /// <param name="fullModId">ID moda FULL</param>
        /// <param name="cancellationToken">Token anulowania</param>
        /// <returns>Informacja o kompatybilności lub null w przypadku błędu</returns>
        public async Task<CompatibilityInfo?> CheckCompatibilityAsync(
            int dllModId, 
            int fullModId, 
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"compat_{dllModId}_{fullModId}";

            // Sprawdź cache
            if (_cache.TryGetValue<CompatibilityInfo>(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            try
            {
                var url = $"{_baseUrl}/api/compatibility?dllModId={dllModId}&fullModId={fullModId}";
                
                // Dodaj token autoryzacji
                string token = SecretProvider.GetDownloadToken();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                
                // Timeout 10 sekund
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await _httpClient.GetAsync(url, cts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var apiResponse = JsonSerializer.Deserialize<CompatibilityResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.FirstCompatibility == null)
                {
                    return null;
                }

                // Konwertuj CompatibilityEntry na CompatibilityInfo
                var entry = apiResponse.FirstCompatibility;
                var compatInfo = new CompatibilityInfo
                {
                    Id = entry.Id,
                    StatusCode = entry.Status,
                    TestedDate = DateTime.TryParse(entry.TestedDate, out var date) ? date : null,
                    TestedBy = entry.TestedBy,
                    AmongUsVersion = entry.AmongUsVersion,
                    Notes = entry.Notes,
                    IssuesUrl = entry.IssuesUrl,
                    IsCurrentVersion = entry.IsCurrentVersion,
                    Warning = entry.Warning
                };

                // Cache'uj wynik
                _cache.Set(cacheKey, compatInfo, TimeSpan.FromMinutes(CacheExpirationMinutes));

                return compatInfo;
            }
            catch (OperationCanceledException)
            {
                // Timeout lub anulowanie
                return null;
            }
            catch (HttpRequestException)
            {
                // Błąd połączenia
                return null;
            }
            catch (JsonException)
            {
                // Błąd parsowania JSON
                return null;
            }
            catch (Exception)
            {
                // Inne błędy
                return null;
            }
        }

        /// <summary>
        /// Pobiera macierz kompatybilności dla danego moda DLL ze wszystkimi modami FULL.
        /// </summary>
        /// <param name="dllModId">ID moda DLL</param>
        /// <param name="cancellationToken">Token anulowania</param>
        /// <returns>Słownik [fullModId -> CompatibilityInfo] lub pusty słownik w przypadku błędu</returns>
        public async Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixAsync(
            int dllModId, 
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"compat_matrix_{dllModId}";

            // Sprawdź cache
            if (_cache.TryGetValue<Dictionary<int, CompatibilityInfo>>(cacheKey, out var cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                // API endpoint używa query parametru dllModId zamiast path parametru
                var url = $"{_baseUrl}/api/compatibility?dllModId={dllModId}";
                
                // Dodaj token autoryzacji
                string token = SecretProvider.GetDownloadToken();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                
                // Timeout 15 sekund (większa odpowiedź)
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await _httpClient.GetAsync(url, cts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    return new Dictionary<int, CompatibilityInfo>();
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                
                // API zwraca strukturę { success, compatibilities: [...] }
                var apiResponse = JsonSerializer.Deserialize<CompatibilityResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse == null || !apiResponse.Success || apiResponse.Compatibilities == null)
                {
                    return new Dictionary<int, CompatibilityInfo>();
                }

                // Konwertuj z CompatibilityEntry na Dictionary<fullModId, CompatibilityInfo>
                var result = new Dictionary<int, CompatibilityInfo>();
                
                foreach (var entry in apiResponse.Compatibilities)
                {
                    if (entry.FullMod == null) continue; // Pomijamy wpisy bez FullMod
                    
                    result[entry.FullMod.Id] = new CompatibilityInfo
                    {
                        Id = entry.Id,
                        StatusCode = entry.Status,
                        TestedDate = string.IsNullOrEmpty(entry.TestedDate) ? null : DateTime.TryParse(entry.TestedDate, out var date) ? date : null,
                        TestedBy = entry.TestedBy,
                        AmongUsVersion = entry.AmongUsVersion,
                        Notes = entry.Notes,
                        IssuesUrl = entry.IssuesUrl,
                        IsCurrentVersion = entry.IsCurrentVersion,
                        Warning = entry.Warning
                    };
                }

                // Cache'uj wynik
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheExpirationMinutes));

                return result;
            }
            catch (OperationCanceledException)
            {
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (HttpRequestException)
            {
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (JsonException)
            {
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (Exception)
            {
                return new Dictionary<int, CompatibilityInfo>();
            }
        }

        /// <summary>
        /// Pobiera macierz kompatybilności dla danego moda FULL ze wszystkimi modami DLL.
        /// </summary>
        /// <param name="fullModId">ID moda FULL</param>
        /// <param name="cancellationToken">Token anulowania</param>
        /// <returns>Słownik [dllModId -> CompatibilityInfo] lub pusty słownik w przypadku błędu</returns>
        public async Task<Dictionary<int, CompatibilityInfo>> GetCompatibilityMatrixForFullModAsync(
            int fullModId, 
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"compat_matrix_full_{fullModId}";

            // Sprawdź cache
            if (_cache.TryGetValue<Dictionary<int, CompatibilityInfo>>(cacheKey, out var cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            try
            {
                // API endpoint używa query parametru fullModId zamiast path parametru
                var url = $"{_baseUrl}/api/compatibility?fullModId={fullModId}";
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Pobieranie macierzy dla FULL mod ID={fullModId}, URL: {url}");
                
                // Dodaj token autoryzacji
                string token = SecretProvider.GetDownloadToken();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                
                // Timeout 15 sekund
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await _httpClient.GetAsync(url, cts.Token);
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Status odpowiedzi: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ⚠️ API zwróciło status: {response.StatusCode}");
                    return new Dictionary<int, CompatibilityInfo>();
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Otrzymano JSON (długość: {json.Length})");
                
                // API zwraca strukturę { success, compatibilities: [...] }
                var apiResponse = JsonSerializer.Deserialize<CompatibilityResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse == null || !apiResponse.Success || apiResponse.Compatibilities == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ⚠️ Deserializacja zwróciła null lub brak compatibilities");
                    return new Dictionary<int, CompatibilityInfo>();
                }

                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Deserializowano {apiResponse.Compatibilities.Count} wpisów");

                // Konwertuj z CompatibilityEntry na Dictionary<dllModId, CompatibilityInfo>
                var result = new Dictionary<int, CompatibilityInfo>();
                
                foreach (var entry in apiResponse.Compatibilities)
                {
                    if (entry.DllMod == null) continue; // Pomijamy wpisy bez DllMod
                    
                    result[entry.DllMod.Id] = new CompatibilityInfo
                    {
                        Id = entry.Id,
                        StatusCode = entry.Status, // Ustaw StatusCode, a Status/Emoji/Description są wyliczane automatycznie
                        TestedDate = string.IsNullOrEmpty(entry.TestedDate) ? null : DateTime.TryParse(entry.TestedDate, out var date) ? date : null,
                        TestedBy = entry.TestedBy,
                        AmongUsVersion = entry.AmongUsVersion,
                        Notes = entry.Notes,
                        IssuesUrl = entry.IssuesUrl,
                        IsCurrentVersion = entry.IsCurrentVersion,
                        Warning = entry.Warning
                    };
                }

                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ✅ Zwracam {result.Count} wpisów kompatybilności");

                // Cache'uj wynik
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheExpirationMinutes));

                return result;
            }
            catch (OperationCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ❌ Timeout: {ex.Message}");
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ❌ HTTP Error: {ex.Message}");
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ❌ JSON Parse Error: {ex.Message}");
                return new Dictionary<int, CompatibilityInfo>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] ❌ Unexpected Error: {ex.Message}");
                return new Dictionary<int, CompatibilityInfo>();
            }
        }

        /// <summary>
        /// Czyści cache kompatybilności (np. po aktualizacji modów).
        /// </summary>
        public void ClearCache()
        {
            // MemoryCache nie ma prostego sposobu na czyszczenie całego cache,
            // ale możemy to rozszerzyć w przyszłości jeśli potrzeba
        }

        /// <summary>
        /// Sprawdza czy dana kompatybilność powinna pokazywać ostrzeżenie użytkownikowi.
        /// </summary>
        public static bool ShouldShowWarning(CompatibilityInfo? compatibility)
        {
            if (compatibility == null)
            {
                return false; // Brak danych - nie pokazujemy ostrzeżenia
            }

            return compatibility.Status == CompatibilityStatus.NotWork ||
                   compatibility.Status == CompatibilityStatus.NotTested;
        }

        /// <summary>
        /// Sprawdza czy instalacja powinna być zablokowana.
        /// </summary>
        public static bool ShouldBlockInstallation(CompatibilityInfo? compatibility)
        {
            if (compatibility == null)
            {
                return false; // Brak danych - nie blokujemy
            }

            // Opcjonalnie: możemy zablokować instalację dla NotWorking
            return false; // Na razie tylko ostrzegamy, nie blokujemy
        }

        /// <summary>
        /// Zwalnia zasoby (HttpClient)
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
