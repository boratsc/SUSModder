using SUSModder.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SUSModder.Core.Services.Discord;

/// <summary>
/// Serwis do komunikacji z Clair API (endpointy /api/susmodder/*).
/// Odpowiada za pobieranie konfiguracji OAuth, listy serwerów Discord i credentials SUSTATS.
/// </summary>
public interface IClairDiscordService
{
    /// <summary>
    /// Pobiera konfigurację OAuth2 z Clair API (GET /api/susmodder/config).
    /// Zawiera discord_client_id i endpointy potrzebne do flow OAuth.
    /// </summary>
    Task<ClairOAuthConfig> GetOAuthConfigAsync();

    /// <summary>
    /// Pobiera listę serwerów Discord, do których użytkownik ma dostęp SUSTATS.
    /// Wymaga ważnego Discord access_token.
    /// </summary>
    /// <param name="accessToken">Discord OAuth2 access token (plaintext)</param>
    Task<List<DiscordGuildInfo>> GetAccessibleGuildsAsync(string accessToken);

    /// <summary>
    /// Pobiera credentials SUSTATS (token + secret) dla wybranego serwera Discord.
    /// Wymaga ważnego Discord access_token.
    /// </summary>
    /// <param name="accessToken">Discord OAuth2 access token (plaintext)</param>
    /// <param name="guildId">ID serwera Discord (Snowflake)</param>
    Task<SustatsCredentials> GetCredentialsAsync(string accessToken, string guildId);
}
