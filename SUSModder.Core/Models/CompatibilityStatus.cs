namespace SUSModder.Core.Models
{
    /// <summary>
    /// Status kompatybilności między modem FULL a modem DLL
    /// </summary>
    public enum CompatibilityStatus
    {
        /// <summary>
        /// Favorite (F) - Polecany, działa idealnie
        /// </summary>
        Favorite,

        /// <summary>
        /// Works (W) - Działa poprawnie, bez większych problemów
        /// </summary>
        Works,

        /// <summary>
        /// Not Tested (NT) - Nieprzetestowany, nieznany status
        /// </summary>
        NotTested,

        /// <summary>
        /// Not Work (NW) - Nie działa, niekompatybilny
        /// </summary>
        NotWork
    }

    /// <summary>
    /// Extension methods dla CompatibilityStatus
    /// </summary>
    public static class CompatibilityStatusExtensions
    {
        /// <summary>
        /// Konwersja z kodu API (F/W/NT/NW) na enum
        /// </summary>
        public static CompatibilityStatus FromApiCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return CompatibilityStatus.NotTested;

            return code.Trim().ToUpperInvariant() switch
            {
                "F" or "FAVORITE" => CompatibilityStatus.Favorite,
                "W" or "WORKS" => CompatibilityStatus.Works,
                "NW" or "NOTWORK" or "NOT_WORK" => CompatibilityStatus.NotWork,
                "NT" or "NOTTESTED" or "NOT_TESTED" => CompatibilityStatus.NotTested,
                _ => CompatibilityStatus.NotTested
            };
        }

        /// <summary>
        /// Konwersja z enum na kod API
        /// </summary>
        public static string ToApiCode(this CompatibilityStatus status)
        {
            return status switch
            {
                CompatibilityStatus.Favorite => "F",
                CompatibilityStatus.Works => "W",
                CompatibilityStatus.NotWork => "NW",
                CompatibilityStatus.NotTested => "NT",
                _ => "NT"
            };
        }

        /// <summary>
        /// Opis dla użytkownika
        /// </summary>
        public static string GetDescription(this CompatibilityStatus status)
        {
            return status switch
            {
                CompatibilityStatus.Favorite => "Polecany - działa idealnie",
                CompatibilityStatus.Works => "Kompatybilny - działa poprawnie",
                CompatibilityStatus.NotWork => "Niekompatybilny - nie działa",
                CompatibilityStatus.NotTested => "Nieprzetestowany - brak informacji",
                _ => "Nieznany"
            };
        }

        /// <summary>
        /// Emoji dla statusu
        /// </summary>
        public static string GetEmoji(this CompatibilityStatus status)
        {
            return status switch
            {
                CompatibilityStatus.Favorite => "🟢",
                CompatibilityStatus.Works => "🔵",
                CompatibilityStatus.NotWork => "🔴",
                CompatibilityStatus.NotTested => "⚪",
                _ => "❓"
            };
        }
    }
}
