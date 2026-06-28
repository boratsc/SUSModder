using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Przechowuje dane uwierzytelniające SUSTATS dla serwera Discord.
    /// Token i secret są szyfrowane przed zapisem do bazy SQLite.
    /// Klucz główny: GuildId (Discord Server ID).
    /// </summary>
    public class SustatsCredentials
    {
        /// <summary>
        /// ID serwera Discord (Snowflake)
        /// </summary>
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; } = string.Empty;

        /// <summary>
        /// Nazwa serwera (dla identyfikacji w UI)
        /// </summary>
        [JsonPropertyName("server_name")]
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// Token SUSTATS (plaintext z API, do zaszyfrowania przed zapisem)
        /// </summary>
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Secret SUSTATS (plaintext z API, do zaszyfrowania przed zapisem)
        /// </summary>
        [JsonPropertyName("secret")]
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// Zaszyfrowany token SUSTATS (dla repozytorium)
        /// </summary>
        [JsonIgnore]
        public string TokenEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Zaszyfrowany secret SUSTATS (dla repozytorium)
        /// </summary>
        [JsonIgnore]
        public string SecretEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint API Clair (np. "https://clairbot.app/api/among-data")
        /// </summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Data utworzenia rekordu (ISO 8601)
        /// </summary>
        [JsonIgnore]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Data ostatniej aktualizacji (ISO 8601)
        /// </summary>
        [JsonIgnore]
        public DateTime UpdatedAt { get; set; }
    }
}
