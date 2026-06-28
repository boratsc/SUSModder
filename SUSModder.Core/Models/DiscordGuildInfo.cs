using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Reprezentuje serwer Discord zwrócony przez endpoint /api/susmodder/guilds.
    /// Używany w UI do wyboru serwera przez użytkownika.
    /// </summary>
    public class DiscordGuildInfo
    {
        /// <summary>
        /// ID serwera Discord (Snowflake)
        /// </summary>
        [JsonPropertyName("guild_id")]
        public string GuildId { get; set; } = string.Empty;

        /// <summary>
        /// Nazwa serwera Discord
        /// </summary>
        [JsonPropertyName("guild_name")]
        public string GuildName { get; set; } = string.Empty;

        /// <summary>
        /// Czy serwer ma aktywny SUSTATS token
        /// </summary>
        [JsonPropertyName("has_sustats")]
        public bool HasSustats { get; set; }

        /// <summary>
        /// Nazwa serwera SUSTATS (jeśli has_sustats = true)
        /// </summary>
        [JsonPropertyName("sustats_server_name")]
        public string? SustatsServerName { get; set; }

        /// <summary>
        /// Poziom dostępu użytkownika: "owner", "admin", "role" lub null
        /// </summary>
        [JsonPropertyName("user_access_level")]
        public string? UserAccessLevel { get; set; }
    }
}
