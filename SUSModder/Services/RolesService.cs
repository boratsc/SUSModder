using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Models;

namespace SUSModder.Services
{
    public class RolesService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _rolesEndpoint;

        public RolesService()
        {
            _httpClient = new HttpClient();

            // Wczytaj konfigurację
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _baseUrl = configuration["Configuration:BaseUrl"] ?? "https://susfuckr.boracik.pl/";
            _rolesEndpoint = configuration["Configuration:RolesEndpoint"] ?? "/api/roles";
        }

        public async Task<List<Role>> GetRolesAsync(int configId)
        {
            try
            {
                var url = $"{_baseUrl.TrimEnd('/')}{_rolesEndpoint}?configId={configId}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                var roles = JsonSerializer.Deserialize<List<Role>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return roles ?? new List<Role>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching roles: {ex.Message}");
                return new List<Role>();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
