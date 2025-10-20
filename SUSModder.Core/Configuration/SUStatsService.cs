using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SUSModder.Core.Configuration
{
    public class SUStatsService
    {
        // Statyczny HttpClient współdzielony przez wszystkie instancje (best practice)
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _diagnosticsOutput;

        public SUStatsService(IConfiguration configuration, IDiagnosticsOutput diagnosticsOutput)
        {
            _configuration = configuration;
            _diagnosticsOutput = diagnosticsOutput;
        }

        public async Task<List<AmongToken>> GetSUStatsServersAsync()
        {
            try
            {
                _diagnosticsOutput.Write("Rozpoczynanie pobierania serwerów SUStats...");

                // Pobierz konfigurację - używaj tych samych kluczy co w AmongTokensService
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

                // Pobierz token
                var token = SecretProvider.GetDownloadToken();
                _diagnosticsOutput.Write("Download token retrieved.");

                if (string.IsNullOrEmpty(token))
                {
                    _diagnosticsOutput.Write("BŁĄD: Download token is empty");
                    return new List<AmongToken>();
                }

                // Skonfiguruj nagłówki
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "SUSModder/1.0");

                _diagnosticsOutput.Write("Authorization header configured.");

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

                _diagnosticsOutput.Write($"Successfully loaded {tokensResponse.Tokens.Count} SUStats servers");

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

    }
}
