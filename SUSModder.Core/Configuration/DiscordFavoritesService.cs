using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Configuration
{
    public class DiscordFavoritesService
    {
        // Statyczny HttpClient współdzielony przez wszystkie instancje (best practice)
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;

        public DiscordFavoritesService(IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
        }

        public async Task<List<DiscordServerData>> GetDiscordFavoritesAsync()
        {
            try
            {
                _diagnosticsOutput.Write("=== DISCORD SERVICE START ===");
                _diagnosticsOutput.Write("Starting Discord favorites fetch...");

                // Pobierz konfigurację
                var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"];
                var discordEndpoint = _configuration.GetSection("Configuration")["DiscordEndpoint"];

                _diagnosticsOutput.Write($"BaseUrl from config: '{baseUrl}'");
                _diagnosticsOutput.Write($"DiscordEndpoint from config: '{discordEndpoint}'");

                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(discordEndpoint))
                {
                    _diagnosticsOutput.Write("ERROR: Missing BaseUrl or DiscordEndpoint in configuration");
                    return new List<DiscordServerData>();
                }

                var fullUrl = baseUrl.TrimEnd('/') + discordEndpoint;
                _diagnosticsOutput.Write($"Full URL: {fullUrl}");

                // Pobierz token
                var token = SecretProvider.GetDownloadToken();
                _diagnosticsOutput.Write($"Token retrieved, length: {token?.Length ?? 0}");

                if (string.IsNullOrEmpty(token))
                {
                    _diagnosticsOutput.Write("ERROR: Download token is empty");
                    return new List<DiscordServerData>();
                }

                // Skonfiguruj nagłówki - POPRAWKA TUTAJ
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token); // Bez "Bearer"
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                _diagnosticsOutput.Write($"Headers configured - Authorization: {token.Substring(0, 10)}...");

                // Wykonaj żądanie
                var response = await _httpClient.GetAsync(fullUrl);

                _diagnosticsOutput.Write($"HTTP Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _diagnosticsOutput.Write($"ERROR: HTTP {response.StatusCode} - {errorContent}");
                    return new List<DiscordServerData>();
                }

                // Parsuj odpowiedź
                var jsonContent = await response.Content.ReadAsStringAsync();
                _diagnosticsOutput.Write($"Received JSON content length: {jsonContent.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var discordResponse = JsonSerializer.Deserialize<DiscordFavoritesResponse>(jsonContent, options);

                if (discordResponse == null)
                {
                    _diagnosticsOutput.Write("ERROR: Failed to deserialize JSON response");
                    return new List<DiscordServerData>();
                }

                _diagnosticsOutput.Write($"Deserialized response - Success: {discordResponse.Success}, Count: {discordResponse.Count}");

                if (!discordResponse.Success)
                {
                    _diagnosticsOutput.Write("ERROR: API returned success=false");
                    return new List<DiscordServerData>();
                }

                // Filtruj tylko aktywne serwery
                var activeServers = discordResponse.DiscordFavs
                    .Where(server => server.IsActive)
                    .ToList();

                _diagnosticsOutput.Write($"Successfully loaded {activeServers.Count} active Discord servers");

                return activeServers;
            }
            catch (HttpRequestException ex)
            {
                _diagnosticsOutput.Write($"HTTP Request Error: {ex.Message}");
                return new List<DiscordServerData>();
            }
            catch (TaskCanceledException ex)
            {
                _diagnosticsOutput.Write($"Request Timeout: {ex.Message}");
                return new List<DiscordServerData>();
            }
            catch (JsonException ex)
            {
                _diagnosticsOutput.Write($"JSON Parse Error: {ex.Message}");
                return new List<DiscordServerData>();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Unexpected Error: {ex.Message}");
                return new List<DiscordServerData>();
            }
        }
    }
}
