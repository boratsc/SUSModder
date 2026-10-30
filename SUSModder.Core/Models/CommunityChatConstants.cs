namespace SUSModder.Core.Models
{
    /// <summary>
    /// Stałe kontraktu globalnego komunikatora wspólne dla Core i UI.
    /// </summary>
    public static class CommunityChatConstants
    {
        /// <summary>
        /// Techniczny identyfikator wspólnego kanału czatu.
        /// Legacy parametr akceptowany przez globalny zasób chat; nie wybiera osobnego kanału.
        /// </summary>
        public const int ChannelModId = 1;

        /// <summary>Typ wpisu wczytywany i publikowany w kanale.</summary>
        public const string EntryType = "message";

        /// <summary>Względna ścieżka zasobu przy istniejącym bazowym URL API v2.</summary>
        public const string ResourcePath = "chat";

        /// <summary>Domyślny powód zgłoszenia wiadomości (może zostać rozszerzony przez UI).</summary>
        public const string DefaultReportReason = "spam";
    }

    /// <summary>
    /// Limity treści i historii wspólnego kanału — jedyne źródło prawdy dla Core i UI.
    /// Liczba znaków jest liczona w jednostkach UTF-16 po usunięciu spacji brzegowych.
    /// </summary>
    public static class CommunityChatLimits
    {
        /// <summary>Minimalna długość treści wiadomości (jednostki UTF-16 po trim).</summary>
        public const int MinMessageLength = 10;

        /// <summary>Maksymalna długość treści wiadomości (jednostki UTF-16 po trim).</summary>
        public const int MaxMessageLength = 280;

        /// <summary>Maksymalna liczba wiadomości pobieranych w jednym odczycie historii.</summary>
        public const int MaxHistorySize = 50;

        /// <summary>Maksymalna liczba zaproszeń Discord w jednej wiadomości.</summary>
        public const int MaxDiscordLinks = 1;
    }

    /// <summary>
    /// Stabilne kody błędów komunikatora — wspólne dla walidacji klienckiej i odpowiedzi backendu.
    /// UI mapuje kod na zlokalizowany tekst; surowych kodów nie pokazuje użytkownikowi.
    /// </summary>
    public static class CommunityChatErrorCodes
    {
        // ── Walidacja treści (klient) ───────────────────────────────────────────
        /// <summary>Treść pusta lub krótsza niż <see cref="CommunityChatLimits.MinMessageLength"/>.</summary>
        public const string ContentTooShort = "CONTENT_TOO_SHORT";

        /// <summary>Treść dłuższa niż <see cref="CommunityChatLimits.MaxMessageLength"/>.</summary>
        public const string ContentTooLong = "CONTENT_TOO_LONG";

        /// <summary>Treść zawiera znak nowej linii (wklejone znaki nowej linii nie są obcinane).</summary>
        public const string ContentContainsNewline = "CONTENT_CONTAINS_NEWLINE";

        /// <summary>Treść zawiera niedozwolone znaki sterujące.</summary>
        public const string ContentContainsControlCharacter = "CONTENT_CONTAINS_CONTROL_CHARACTER";

        /// <summary>Treść zawiera link inny niż zaproszenie discord.gg.</summary>
        public const string DisallowedUrl = "DISALLOWED_URL";

        /// <summary>Treść zawiera więcej niż <see cref="CommunityChatLimits.MaxDiscordLinks"/> linków.</summary>
        public const string TooManyLinks = "TOO_MANY_LINKS";

        /// <summary>Treść nie przeszła moderacji po stronie serwera (backend: CONTENT_BLOCKED).</summary>
        public const string ContentBlocked = "CONTENT_BLOCKED";

        // ── Transport / serwer ─────────────────────────────────────────────────
        /// <summary>Brak lub nieprawidłowy token transportowy (HTTP 401).</summary>
        public const string Unauthorized = "CHAT_UNAUTHORIZED";

        /// <summary>Brak uprawnień (HTTP 403).</summary>
        public const string Forbidden = "CHAT_FORBIDDEN";

        /// <summary>Limit żądań (HTTP 429).</summary>
        public const string RateLimited = "CHAT_RATE_LIMITED";

        /// <summary>Błąd serwera (HTTP 5xx).</summary>
        public const string ServerError = "CHAT_SERVER_ERROR";

        /// <summary>Brak łączności lub timeout.</summary>
        public const string NetworkError = "CHAT_NETWORK_ERROR";

        /// <summary>Nieoczekiwany format odpowiedzi serwera.</summary>
        public const string ProtocolError = "CHAT_PROTOCOL_ERROR";

        /// <summary>Operacja anulowana przez aplikację.</summary>
        public const string Cancelled = "CHAT_CANCELLED";

        /// <summary>Wiadomość nie istnieje (HTTP 404).</summary>
        public const string NotFound = "CHAT_NOT_FOUND";

        /// <summary>Żądanie odrzucone (inny HTTP 4xx).</summary>
        public const string RequestFailed = "CHAT_REQUEST_FAILED";

        /// <summary>Nieznany błąd serwera.</summary>
        public const string UnknownError = "CHAT_UNKNOWN_ERROR";
    }
}
