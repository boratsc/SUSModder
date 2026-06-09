using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Configuration
{
    public class DiscordFavoritesService
    {
        private readonly ISUSModderApiClient _apiClient;
        private readonly IDiagnosticsOutput _diagnosticsOutput;

        public DiscordFavoritesService(
            IConfiguration configuration,
            IDiagnosticsOutput diagnosticsOutput,
            ISUSModderApiClient? apiClient = null)
        {
            _diagnosticsOutput = diagnosticsOutput;
            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? new SUSModderApiClient(configuration, diagnosticsOutput);
        }

        public async Task<List<DiscordServerData>> GetDiscordFavoritesAsync()
        {
            try
            {
                _diagnosticsOutput.Write("=== DISCORD SERVICE START ===");
                _diagnosticsOutput.Write("Pobieranie ulubionych serwerów Discord (API v2)...");

                var response = await _apiClient.GetDiscordFavoritesPublicAsync();
                if (!response.IsSuccess)
                {
                    _diagnosticsOutput.Write($"ERROR: HTTP {response.StatusCode}");
                    return new List<DiscordServerData>();
                }

                return ParseDiscordFavorites(response.Data);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Unexpected Error: {ex.Message}");
                return new List<DiscordServerData>();
            }
        }

        public async Task<Dictionary<string, int>> GetDiscordServerCountsAsync()
        {
            try
            {
                var response = await _apiClient.GetDiscordServerCountsAsync();
                if (!response.IsSuccess)
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                return ParseServerCounts(response.Data);
            }
            catch (Exception ex)
            {
                _diagnosticsOutput.Write($"Discord counts fetch failed: {ex.Message}");
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private List<DiscordServerData> ParseDiscordFavorites(JsonElement data)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (data.ValueKind == JsonValueKind.Array)
            {
                // v2 /discord/favs/public zwraca tylko aktywne serwery; pole is_active nie występuje
                return JsonSerializer.Deserialize<List<DiscordServerData>>(data.GetRawText(), options)
                    ?? new List<DiscordServerData>();
            }

            if (data.TryGetProperty("discordFavs", out _))
            {
                var wrapped = JsonSerializer.Deserialize<DiscordFavoritesResponse>(data.GetRawText(), options);
                if (wrapped?.Success == true && wrapped.DiscordFavs is not null)
                    return wrapped.DiscordFavs.Where(s => s.IsActive).ToList();
            }

            return new List<DiscordServerData>();
        }

        private static Dictionary<string, int> ParseServerCounts(JsonElement data)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("counts", out var countsElement) && countsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in countsElement.EnumerateObject())
                        TryAddCount(counts, property.Name, property.Value);
                    return counts;
                }

                foreach (var property in data.EnumerateObject())
                    TryAddCount(counts, property.Name, property.Value);
            }

            return counts;
        }

        private static void TryAddCount(Dictionary<string, int> counts, string key, JsonElement element)
        {
            var memberCount = ReadCountValue(element);
            if (memberCount.HasValue && !string.IsNullOrWhiteSpace(key))
                counts[key.Trim()] = memberCount.Value;
        }

        private static int? ReadCountValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numberCount))
                return numberCount;

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out var stringCount))
                return stringCount;

            return null;
        }
    }
}
