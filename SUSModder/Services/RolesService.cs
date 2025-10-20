using System;
using System.Collections.Generic;
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

    }
}
