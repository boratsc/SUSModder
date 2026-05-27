using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Konfiguracja OAuth2 pobrana z endpointu GET /api/susmodder/config.
    /// Zawiera dane potrzebne do rozpoczęcia flow Discord OAuth PKCE.
    /// </summary>
    public class ClairOAuthConfig
    {
        /// <summary>
        /// Status odpowiedzi API
        /// </summary>
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        /// <summary>
        /// Client ID aplikacji Discord "Clair"
        /// </summary>
        [JsonPropertyName("discord_client_id")]
        public string DiscordClientId { get; set; } = string.Empty;

        /// <summary>
        /// Bazowy endpoint API Clair (np. "https://clairbot.app/api/susmodder")
        /// </summary>
        [JsonPropertyName("auth_endpoint")]
        public string AuthEndpoint { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint do pobierania listy serwerów (względny lub absolutny)
        /// </summary>
        [JsonPropertyName("guilds_endpoint")]
        public string GuildsEndpoint { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint do pobierania credentials SUSTATS (względny lub absolutny)
        /// </summary>
        [JsonPropertyName("credentials_endpoint")]
        public string CredentialsEndpoint { get; set; } = string.Empty;
    }
}
