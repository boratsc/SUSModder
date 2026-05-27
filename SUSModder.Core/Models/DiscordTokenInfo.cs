using System;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Przechowuje zaszyfrowane tokeny Discord OAuth2.
    /// Singleton w bazie SQLite (CHECK id = 1).
    /// Wszystkie tokeny są szyfrowane przed zapisem (ochrona danych wrażliwych).
    /// </summary>
    public class DiscordTokenInfo
    {
        /// <summary>
        /// Zaszyfrowany access token Discord OAuth2
        /// </summary>
        public string AccessTokenEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Zaszyfrowany refresh token Discord OAuth2
        /// </summary>
        public string RefreshTokenEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Typ tokena (domyślnie "Bearer")
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// Data wygaśnięcia access tokena (UTC)
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// ID użytkownika Discord
        /// </summary>
        public string? DiscordUserId { get; set; }

        /// <summary>
        /// Nazwa użytkownika Discord (np. "user#1234")
        /// </summary>
        public string? DiscordUsername { get; set; }

        /// <summary>
        /// Data utworzenia rekordu (ISO 8601)
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Data ostatniej aktualizacji (ISO 8601)
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
