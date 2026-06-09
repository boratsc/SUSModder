using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Models;

namespace SUSModder.Services
{
    public class RolesService
    {
        private readonly ISUSModderApiClient _apiClient;

        private static List<Role>? _cachedRoles = null;
        private static readonly object _cacheLock = new object();

        public RolesService(ISUSModderApiClient? apiClient = null)
        {
            if (apiClient is not null)
            {
                _apiClient = apiClient;
                return;
            }

            _apiClient = SUSModderApiClientProvider.TryGetDefault()
                ?? CreateFallbackClient();
        }

        private static ISUSModderApiClient CreateFallbackClient()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            return new SUSModderApiClient(configuration, new NullRolesDiagnostics());
        }

        public async Task<List<Role>> GetRolesAsync(int configId)
        {
            try
            {
                lock (_cacheLock)
                {
                    if (_cachedRoles != null)
                    {
                        Console.WriteLine($"[RolesService] Using cached roles ({_cachedRoles.Count} roles)");
                        return _cachedRoles;
                    }
                }

                Console.WriteLine("[RolesService] Fetching roles from API v2 /roles");
                var response = await _apiClient.GetRolesAsync();
                if (!response.IsSuccess)
                {
                    Console.WriteLine($"[RolesService] API returned HTTP {response.StatusCode}");
                    return new List<Role>();
                }

                var roles = JsonSerializer.Deserialize<List<Role>>(response.Data.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<Role>();

                lock (_cacheLock)
                {
                    _cachedRoles = roles;
                }

                return roles;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RolesService] Error fetching roles: {ex.Message}");
                return new List<Role>();
            }
        }

        public async Task<bool> CheckIfHasRolesAsync(int configId)
        {
            try
            {
                var allRoles = await GetRolesAsync(configId);
                return allRoles.Any(role => role.IsAssociatedWithMod(configId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking roles availability: {ex.Message}");
                return false;
            }
        }

        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedRoles = null;
            }
        }

        private sealed class NullRolesDiagnostics : IDiagnosticsOutput
        {
            public void Write(string message) { }
        }
    }
}
