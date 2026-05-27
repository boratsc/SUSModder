using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla danych uwierzytelniających SUStats (tabela sustats_credentials w SQLite).
    /// Zastępuje pobieranie listy serwerów z susmodder-api przez Discord OAuth flow.
    /// </summary>
    public interface ISustatsCredentialsRepository
    {
        /// <summary>
        /// Zwraca dane uwierzytelniające dla konkretnego serwera Discord (GuildId).
        /// </summary>
        Task<SustatsCredentials?> GetForGuildAsync(string guildId);

        /// <summary>
        /// Zapisuje nowe dane uwierzytelniające (lub aktualizuje istniejące dla tego samego GuildId).
        /// </summary>
        Task SaveAsync(SustatsCredentials creds);

        /// <summary>
        /// Usuwa dane uwierzytelniające dla danego serwera Discord.
        /// </summary>
        Task DeleteAsync(string guildId);

        /// <summary>
        /// Zwraca aktualnie aktywne dane uwierzytelniające.
        /// Używa user_settings.active_sustats_guild_id do określenia aktywnego serwera.
        /// </summary>
        Task<SustatsCredentials?> GetActiveAsync();
    }
}
