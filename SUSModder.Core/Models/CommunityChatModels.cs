using System;
using System.Collections.Generic;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Kategoria wyniku operacji globalnego komunikatora.
    /// Warstwa UI mapuje status na stan interfejsu (offline, limit, auth, ...),
    /// a szczegóły komunikatu buduje na podstawie <see cref="CommunityChatErrorCodes"/>.
    /// </summary>
    public enum CommunityChatStatus
    {
        /// <summary>Operacja zakończona powodzeniem.</summary>
        Success = 0,

        /// <summary>
        /// Odczyt powiódł się, ale wspólna rozmowa nie zawiera żadnych aktywnych wiadomości.
        /// Różne od awarii — UI pokazuje stan "pusta rozmowa", nie stan błędu.
        /// </summary>
        Empty = 1,

        /// <summary>Treść wiadomości odrzucona po stronie klienta (przed wysłaniem do API).</summary>
        ValidationFailed = 2,

        /// <summary>Brak lub nieprawidłowy token transportowy (HTTP 401).</summary>
        Unauthorized = 3,

        /// <summary>Brak uprawnień do operacji (HTTP 403) — np. usuwanie cudzej wiadomości.</summary>
        Forbidden = 4,

        /// <summary>Limit żądań (HTTP 429). Czas oczekiwania: <c>RetryAfterSeconds</c>.</summary>
        RateLimited = 5,

        /// <summary>Błąd serwera (HTTP 5xx).</summary>
        ServerError = 6,

        /// <summary>Brak łączności lub timeout — wynik operacji zapisu może być niepewny; nie ponawiać automatycznie.</summary>
        NetworkError = 7,

        /// <summary>Odpowiedź serwera nie jest zgodna z oczekiwanym formatem.</summary>
        ProtocolError = 8,

        /// <summary>Operacja anulowana przez aplikację (np. zamknięcie okna).</summary>
        Cancelled = 9,

        /// <summary>Zasób nie istnieje (HTTP 404) — np. wiadomość już wygasła lub usunięta.</summary>
        NotFound = 10,

        /// <summary>Żądanie odrzucone przez serwer (inny HTTP 4xx).</summary>
        RequestFailed = 11,

        /// <summary>Nieznany błąd po stronie serwera.</summary>
        UnknownError = 12
    }

    /// <summary>
    /// Pojedyncza wiadomość we wspólnym kanale czatu.
    /// DTO jest neutralne wobec backendu — nie zawiera ID moda, regionu ani danych gry.
    /// </summary>
    public sealed record CommunityChatMessage
    {
        /// <summary>Identyfikator wiadomości nadany przez serwer (klucz deduplikacji i usuwania).</summary>
        public required string Id { get; init; }

        /// <summary>Treść wiadomości — zawsze tekst, bez HTML/Markdown.</summary>
        public required string Content { get; init; }

        /// <summary>Czas publikacji (null, gdy serwer go nie zwrócił).</summary>
        public DateTimeOffset? PublishedAt { get; init; }

        /// <summary>Czas wygaśnięcia wpisu (null, gdy backend nie stosuje TTL dla tego kanału).</summary>
        public DateTimeOffset? ExpiresAt { get; init; }

        /// <summary>
        /// Czy wiadomość należy do bieżącego użytkownika.
        /// Potwierdzone przez serwer (<c>isOwn</c>); brak pola oznacza false.
        /// </summary>
        public bool IsOwn { get; init; }

        /// <summary>Czy UI powinno umożliwić usunięcie tej wiadomości.</summary>
        public bool CanDelete { get; init; }

        /// <summary>Server-assigned chat author; never used for authorization.</summary>
        public CommunityChatAuthor? Author { get; init; }

        public bool IsComplete => !string.IsNullOrWhiteSpace(Id) &&
            !string.IsNullOrWhiteSpace(Content) && PublishedAt.HasValue && Author?.IsComplete == true;
    }

    public sealed record CommunityChatNickname(string Pl, string En);

    public sealed record CommunityChatAuthor(string Id, CommunityChatNickname Nickname)
    {
        public bool IsComplete => !string.IsNullOrWhiteSpace(Id) && Nickname is not null &&
            !string.IsNullOrWhiteSpace(Nickname.Pl) && !string.IsNullOrWhiteSpace(Nickname.En);
    }

    /// <summary>
    /// Wynik odczytu historii wspólnego kanału czatu.
    /// </summary>
    public sealed record CommunityChatMessagesResult
    {
        /// <summary>Kategoria wyniku operacji.</summary>
        public CommunityChatStatus Status { get; init; } = CommunityChatStatus.Success;

        /// <summary>
        /// True dla <see cref="CommunityChatStatus.Success"/> i <see cref="CommunityChatStatus.Empty"/>.
        /// Pusta rozmowa to sukces — UI rozróżnia ją po <see cref="Status"/>.
        /// </summary>
        public bool IsSuccess => Status is CommunityChatStatus.Success or CommunityChatStatus.Empty;

        /// <summary>Historia wiadomości (najstarsze na początku, najnowsze na końcu).</summary>
        public IReadOnlyList<CommunityChatMessage> Messages { get; init; } = Array.Empty<CommunityChatMessage>();

        /// <summary>Stabilny kod błędu (backendowy lub kliencki) — do zmapowania na tekst UI.</summary>
        public string? ErrorCode { get; init; }

        /// <summary>Techniczny opis błędu (fallback diagnostyczny, nie do pokazywania użytkownikowi).</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>Liczba sekund do ponowienia (serwer HTTP 429, nagłówek Retry-After).</summary>
        public int? RetryAfterSeconds { get; init; }

        /// <summary>Kod HTTP odpowiedzi (null, gdy nie było odpowiedzi).</summary>
        public int? HttpStatusCode { get; init; }
    }

    /// <summary>
    /// Wynik publikacji wiadomości we wspólnym kanale czatu.
    /// </summary>
    public sealed record CommunityChatSendResult
    {
        /// <summary>Kategoria wyniku operacji.</summary>
        public CommunityChatStatus Status { get; init; } = CommunityChatStatus.Success;

        /// <summary>True tylko dla <see cref="CommunityChatStatus.Success"/>.</summary>
        public bool IsSuccess => Status == CommunityChatStatus.Success;

        /// <summary>
        /// Opublikowana wiadomość (null, gdy serwer nie zwrócił ID — UI powinno odświeżyć listę).
        /// </summary>
        public CommunityChatMessage? Message { get; init; }

        /// <summary>Stabilny kod błędu (backendowy lub kliencki) — do zmapowania na tekst UI.</summary>
        public string? ErrorCode { get; init; }

        /// <summary>Techniczny opis błędu (fallback diagnostyczny, nie do pokazywania użytkownikowi).</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>Liczba sekund do ponowienia (serwer HTTP 429, nagłówek Retry-After).</summary>
        public int? RetryAfterSeconds { get; init; }

        /// <summary>Kod HTTP odpowiedzi (null, gdy nie było odpowiedzi).</summary>
        public int? HttpStatusCode { get; init; }

        /// <summary>Serwer oznaczył publikację ostrzeżeniem moderacji (treść przyjęta, ale z flagą).</summary>
        public bool ModerationWarning { get; init; }
    }

    /// <summary>
    /// Wynik operacji moderacyjnej (usunięcie własnej wiadomości lub zgłoszenie wiadomości).
    /// </summary>
    public sealed record CommunityChatModerationResult
    {
        /// <summary>Kategoria wyniku operacji.</summary>
        public CommunityChatStatus Status { get; init; } = CommunityChatStatus.Success;

        /// <summary>True tylko dla <see cref="CommunityChatStatus.Success"/>.</summary>
        public bool IsSuccess => Status == CommunityChatStatus.Success;

        /// <summary>Stabilny kod błędu (backendowy lub kliencki) — do zmapowania na tekst UI.</summary>
        public string? ErrorCode { get; init; }

        /// <summary>Techniczny opis błędu (fallback diagnostyczny, nie do pokazywania użytkownikowi).</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>Liczba sekund do ponowienia (serwer HTTP 429, nagłówek Retry-After).</summary>
        public int? RetryAfterSeconds { get; init; }

        /// <summary>Kod HTTP odpowiedzi (null, gdy nie było odpowiedzi).</summary>
        public int? HttpStatusCode { get; init; }
    }
}
