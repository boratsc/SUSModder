using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SUSModder.Core.Configuration
{
    public class AmongTokensService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;

        public AmongTokensService(IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Dodaj timeout jak w DiscordFavoritesService
        }

        public async Task<List<AmongToken>> GetAmongTokensAsync()
        {
            try
            {
                _diagnosticsOutput.Write("Rozpoczynanie pobierania tokenów Among Us...");

                // Pobierz konfigurację - używaj tych samych kluczy co w DiscordFavoritesService
                var baseUrl = _configuration.GetSection("Configuration")["BaseUrl"];
                var apiConfig = _configuration.GetSection("Configuration")["ApiConfig"];

                _diagnosticsOutput.Write($"BaseUrl from config: '{baseUrl}'");
                _diagnosticsOutput.Write($"ApiConfig from config: '{apiConfig}'");

                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiConfig))
                {
                    _diagnosticsOutput.Write("BŁĄD: Brak BaseUrl lub ApiConfig w konfiguracji");
                    return new List<AmongToken>();
                }

                var fullUrl = baseUrl.TrimEnd('/') + apiConfig;
                _diagnosticsOutput.Write($"Full URL: {fullUrl}");

                // Pobierz token - używaj tej samej metody co DiscordFavoritesService
                var token = SecretProvider.GetDownloadToken();
                _diagnosticsOutput.Write($"Token retrieved, length: {token?.Length ?? 0}");

                if (string.IsNullOrEmpty(token))
                {
                    _diagnosticsOutput.Write("BŁĄD: Download token is empty");
                    return new List<AmongToken>();
                }

                // Skonfiguruj nagłówki - POPRAWKA: bez "Bearer", tak jak w DiscordFavoritesService
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
                    _diagnosticsOutput.Write($"BŁĄD: HTTP {response.StatusCode} - {errorContent}");
                    return new List<AmongToken>();
                }

                // Parsuj odpowiedź
                var jsonContent = await response.Content.ReadAsStringAsync();
                _diagnosticsOutput.Write($"Received JSON content length: {jsonContent.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var tokensResponse = JsonSerializer.Deserialize<AmongTokensResponse>(jsonContent, options);

                if (tokensResponse == null)
                {
                    _diagnosticsOutput.Write("BŁĄD: Failed to deserialize JSON response");
                    return new List<AmongToken>();
                }

                _diagnosticsOutput.Write($"Deserialized response - Success: {tokensResponse.Success}, Count: {tokensResponse.Count}");

                if (!tokensResponse.Success)
                {
                    _diagnosticsOutput.Write("BŁĄD: API returned success=false");
                    return new List<AmongToken>();
                }

                _diagnosticsOutput.Write($"Successfully loaded {tokensResponse.Tokens.Count} Among Us tokens");

                return tokensResponse.Tokens;
            }
            catch (HttpRequestException ex)
            {
                _diagnosticsOutput.Write($"HTTP Request Error: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (TaskCanceledException ex)
            {
                _diagnosticsOutput.Write($"Request Timeout: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (JsonException ex)
            {
                _diagnosticsOutput.Write($"JSON Parse Error: {ex.Message}");
                return new List<AmongToken>();
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Unexpected Error: {ex.Message}");
                return new List<AmongToken>();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
