using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Models;

namespace SUSModder.Services
{
    public class RolesService
    {
        // Statyczny HttpClient współdzielony przez wszystkie instancje (best practice)
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _baseUrl;
        private readonly string _rolesEndpoint;

        // Statyczny cache dla wszystkich ról
        private static List<Role>? _cachedRoles = null;
        private static readonly object _cacheLock = new object();

        public RolesService()
        {
            // Wczytaj konfigurację
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _baseUrl = configuration["Configuration:BaseUrl"] ?? "https://dev.susmodder.boracik.pl/";
            _rolesEndpoint = configuration["Configuration:RolesEndpoint"] ?? "/api/roles";
        }

        public async Task<List<Role>> GetRolesAsync(int configId)
        {
            try
            {
                // Sprawdź czy mamy już cache
                lock (_cacheLock)
                {
                    if (_cachedRoles != null)
                    {
                        Console.WriteLine($"[RolesService] Using cached roles ({_cachedRoles.Count} roles)");
                        return _cachedRoles;
                    }
                }

                var url = $"{_baseUrl.TrimEnd('/')}{_rolesEndpoint}?id={configId}";
                Console.WriteLine($"[RolesService] Fetching roles from: {url}");

                var response = await _httpClient.GetAsync(url);
                Console.WriteLine($"[RolesService] Response status: {response.StatusCode}");
                
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[RolesService] Response content: {jsonContent.Substring(0, Math.Min(200, jsonContent.Length))}...");
                
                // Sprawdź czy odpowiedź to błąd
                if (jsonContent.Contains("\"error\""))
                {
                    Console.WriteLine($"[RolesService] API returned error for configId {configId}");
                    return new List<Role>();
                }
                
                var roles = JsonSerializer.Deserialize<List<Role>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var resultList = roles ?? new List<Role>();
                var resultCount = resultList.Count;
                Console.WriteLine($"[RolesService] Deserialized {resultCount} roles");
                
                // Zapisz do cache
                lock (_cacheLock)
                {
                    _cachedRoles = resultList;
                    Console.WriteLine($"[RolesService] Roles cached ({_cachedRoles.Count} roles)");
                }
                
                return resultList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RolesService] Error fetching roles: {ex.Message}");
                return new List<Role>();
            }
        }

        /// <summary>
        /// Sprawdza czy mod ma dostępne opisy ról w API
        /// </summary>
        /// <param name="configId">ID moda z konfiguracji</param>
        /// <returns>True jeśli mod ma role, false w przeciwnym wypadku</returns>
        public async Task<bool> CheckIfHasRolesAsync(int configId)
        {
            try
            {
                Console.WriteLine($"[RolesService] Checking roles for configId: {configId}");
                var allRoles = await GetRolesAsync(configId);
                
                // Filtruj role dla tego konkretnego moda po Id (API zwraca Id jako ID moda!)
                // Zgodnie z implementacją w RolesWindow.axaml.cs, filtrujemy po role.Id
                var rolesForMod = allRoles.Where(role => role.Id == configId).ToList();
                
                Console.WriteLine($"[RolesService] Got {rolesForMod.Count} roles for configId: {configId} (from {allRoles.Count} total)");
                return rolesForMod.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking roles availability: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Czyści cache ról - przydatne gdy chcemy wymusić ponowne pobranie danych z API
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedRoles = null;
                Console.WriteLine($"[RolesService] Cache cleared");
            }
        }

    }
}
