using System.Text.RegularExpressions;

namespace SUSModder.Core.Validators
{
    /// <summary>
    /// Walidacja kliencka wpisów lobby board — pierwsza linia obrony przed wysłaniem
    /// nieprawidłowych danych do API. Backend i tak weryfikuje niezależnie.
    /// </summary>
    public static class LobbyEntryValidator
    {
        // Kody Among Us: 4-6 znaków A-Z, 0-9
        private static readonly Regex LobbyCodeRegex =
            new(@"^[A-Z0-9]{4,6}$", RegexOptions.Compiled);

        // Tylko discord.gg linki są dozwolone
        private static readonly Regex DiscordInviteRegex =
            new(@"discord\.gg/[a-zA-Z0-9]+", RegexOptions.Compiled);

        // Wykrywanie dowolnych URL-i (do blokowania)
        private static readonly Regex AnyUrlRegex =
            new(@"https?://[^\s]+|www\.[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const int MessageMinLength = 10;
        private const int MessageMaxLength = 280;
        private const int MaxDiscordLinks = 1;

        /// <summary>
        /// Waliduje kod lobby. Zwraca (true, null) gdy poprawny, (false, errorCode) gdy nie.
        /// </summary>
        public static (bool IsValid, string? ErrorCode) ValidateCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (false, "INVALID_LOBBY_CODE");

            code = code.Trim().ToUpperInvariant();

            if (!LobbyCodeRegex.IsMatch(code))
                return (false, "INVALID_LOBBY_CODE");

            return (true, null);
        }

        /// <summary>
        /// Waliduje treść wiadomości. Zwraca (true, null) gdy poprawna, (false, errorCode) gdy nie.
        /// </summary>
        public static (bool IsValid, string? ErrorCode) ValidateMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (false, "CONTENT_TOO_SHORT");

            content = content.Trim();

            if (content.Length < MessageMinLength)
                return (false, "CONTENT_TOO_SHORT");

            if (content.Length > MessageMaxLength)
                return (false, "CONTENT_TOO_LONG");

            // Control chars check
            foreach (char c in content)
            {
                if ((c >= '\u0000' && c <= '\u001F') || (c >= '\u007F' && c <= '\u009F'))
                    return (false, "CONTENT_TOO_SHORT"); // control chars = rejected
            }

            // URL check — dozwolone tylko discord.gg
            var allUrls = AnyUrlRegex.Matches(content);
            if (allUrls.Count > 0)
            {
                var discordLinks = DiscordInviteRegex.Matches(content);
                if (discordLinks.Count != allUrls.Count)
                    return (false, "DISALLOWED_URL");
                if (discordLinks.Count > MaxDiscordLinks)
                    return (false, "TOO_MANY_LINKS");
            }

            return (true, null);
        }

        /// <summary>
        /// Normalizuje treść do porównania duplicate detection (lowercase + strip whitespace + strip interpunkcja).
        /// </summary>
        public static string NormalizeContent(string s)
        {
            return Regex.Replace(s.ToLowerInvariant().Trim(), @"[\s\p{P}]", "");
        }
    }
}
