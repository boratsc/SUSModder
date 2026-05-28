using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis do komunikacji z lobby board API (susmodder.app /api/lobby-board)
    /// oraz live lookupu stanu lobby z serwerów modowanych regionów Among Us.
    /// </summary>
    public interface ILobbyBoardService
    {
        /// <summary>
        /// Publikuje kod lobby na tablicy.
        /// </summary>
        Task<PostEntryResult> PublishCodeAsync(
            string code, int modId, string region,
            int maxPlayers, int? currentPlayers,
            CancellationToken ct = default);

        /// <summary>
        /// Publikuje wiadomość/ogłoszenie na tablicy.
        /// </summary>
        Task<PostEntryResult> PublishMessageAsync(
            string content, int modId,
            CancellationToken ct = default);

        /// <summary>
        /// Pobiera listę wpisów z tablicy.
        /// </summary>
        Task<IReadOnlyList<LobbyBoardEntry>> GetEntriesAsync(
            int? modId = null, LobbyEntryType? type = null,
            string? region = null, int limit = 20,
            CancellationToken ct = default);

        /// <summary>
        /// Usuwa własny wpis.
        /// </summary>
        Task<bool> DeleteOwnEntryAsync(string entryId, CancellationToken ct = default);

        /// <summary>
        /// Aktualizuje własny kod lobby (np. liczbę graczy) — opcjonalny cache.
        /// </summary>
        Task<bool> UpdateCodeEntryAsync(string entryId, int? currentPlayers, int? maxPlayers, CancellationToken ct = default);

        /// <summary>
        /// Live lookup stanu lobby — queryuje bezpośrednio REST API serwera modowanego regionu Among Us.
        /// NIE idzie przez susmodder.app.
        /// </summary>
        /// <param name="code">6-znakowy kod lobby</param>
        /// <param name="regionBaseUrl">URL serwera regionu, np. https://au-eu.duikbo.at</param>
        /// <param name="auth">Dane autoryzacyjne Among Us (idToken, PUID, username, clientVersion)</param>
        /// <param name="modsHeader">Wartość nagłówka Client-Mods, np. "1;2;auavengers.tou.mira=1.5.9"</param>
        Task<LobbyLookupResult?> LookupLobbyStateAsync(
            string code, string regionBaseUrl, AmongUsAuth auth,
            string? modsHeader = null, CancellationToken ct = default);

        /// <summary>
        /// Zgłasza wpis.
        /// </summary>
        Task<bool> ReportEntryAsync(string entryId, string reason, CancellationToken ct = default);
    }
}
