using System.Threading.Tasks;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Interfejs repozytorium dla tokenów Discord OAuth2 (tabela discord_auth w SQLite).
    /// Singleton (CHECK id = 1) – przechowuje tylko jeden rekord.
    /// Wszystkie tokeny są szyfrowane przed zapisem.
    /// </summary>
    public interface IDiscordAuthRepository
    {
        /// <summary>
        /// Pobiera zapisane tokeny Discord OAuth2 (jeśli istnieją).
        /// </summary>
        Task<DiscordTokenInfo?> GetTokenInfoAsync();

        /// <summary>
        /// Zapisuje tokeny Discord OAuth2 (INSERT OR REPLACE – upsert singletona).
        /// </summary>
        Task SaveTokenInfoAsync(DiscordTokenInfo info);

        /// <summary>
        /// Czyści zapisane tokeny Discord OAuth2.
        /// </summary>
        Task ClearTokenAsync();
    }
}
