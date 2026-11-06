using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Neutralny kontrakt globalnego komunikatora (jeden wspólny kanał dla wszystkich modów).
    ///
    /// Założenia:
    /// - serwis nie przyjmuje wybranego moda, konfiguracji gry, regionu ani danych Among Us;
    /// - kanał jest wspólny i technicznie identyczny dla wszystkich użytkowników;
    /// - walidacja treści jest wspólna z UI (<see cref="SUSModder.Core.Validators.CommunityChatMessageValidator"/>);
    /// - błędy zwracane są jako stabilne kody, nie jako surowe komunikaty serwera.
    /// </summary>
    public interface ICommunityChatService
    {
        /// <summary>
        /// Techniczny identyfikator wspólnego kanału (używany do odczytu i publikacji).
        /// UI nie przekazuje ID zaznaczonego moda.
        /// </summary>
        int ChannelModId { get; }

        /// <summary>
        /// Pobiera historię wspólnego kanału (najnowsze wiadomości na końcu).
        /// Pusta rozmowa to sukces (<see cref="CommunityChatMessagesResult.Status"/> =
        /// <see cref="CommunityChatStatus.Empty"/>), nie błąd.
        /// </summary>
        /// <param name="limit">Maksymalna liczba wiadomości (przycinana do <see cref="CommunityChatLimits.MaxHistorySize"/>).</param>
        /// <param name="ct">Token anulowania operacji.</param>
        Task<CommunityChatMessagesResult> GetMessagesAsync(
            int limit = CommunityChatLimits.MaxHistorySize,
            CancellationToken ct = default);

        /// <summary>
        /// Publikuje wiadomość we wspólnym kanale.
        /// Treść jest walidowana przed wysłaniem; nieprawidłowa treść zwraca
        /// <see cref="CommunityChatStatus.ValidationFailed"/> bez kontaktu z API.
        /// </summary>
        /// <param name="content">Treść wiadomości (jednostki UTF-16, 10–280 po trim).</param>
        /// <param name="ct">Token anulowania operacji.</param>
        Task<CommunityChatSendResult> SendMessageAsync(string content, CancellationToken ct = default);

        /// <summary>
        /// Usuwa własną wiadomość. Uprawnienia potwierdza API (403 = nie Twoja wiadomość).
        /// </summary>
        /// <param name="messageId">Identyfikator wiadomości.</param>
        /// <param name="ct">Token anulowania operacji.</param>
        Task<CommunityChatModerationResult> DeleteMessageAsync(string messageId, CancellationToken ct = default);

        /// <summary>
        /// Zgłasza wiadomość do moderacji. Zgłoszenie nie obiecuje usunięcia treści.
        /// </summary>
        /// <param name="messageId">Identyfikator wiadomości.</param>
        /// <param name="reason">Powód zgłoszenia (opcjonalny — użyty jest domyślny).</param>
        /// <param name="ct">Token anulowania operacji.</param>
        Task<CommunityChatModerationResult> ReportMessageAsync(
            string messageId,
            string? reason = null,
            CancellationToken ct = default);
    }
}
