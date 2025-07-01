using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    public class AmongTokensResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("tokens")]
        public List<AmongToken> Tokens { get; set; } = new();
    }

    public class AmongToken
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("secret")]
        public string Secret { get; set; } = string.Empty;

        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = string.Empty;
    }
}
