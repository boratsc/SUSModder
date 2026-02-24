using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    public class DiscordFavoritesResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("discordFavs")]
        public List<DiscordServerData> DiscordFavs { get; set; } = new();
    }

    public class DiscordServerData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("link")]
        public string Link { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }
}
