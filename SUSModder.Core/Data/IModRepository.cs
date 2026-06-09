using System.Collections.Generic;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Interfejs repozytorium dla katalogu modów (tabela mods w SQLite).
    /// Zastępuje ConfigManager/ConfigRepository do operacji CRUD.
    /// </summary>
    public interface IModRepository
    {
        /// <summary>
        /// Wczytuje wszystkie mody z bazy.
        /// Przy pierwszym wywołaniu ładuje dane do cache.
        /// </summary>
        List<ModConfiguration> GetAllMods();

        /// <summary>
        /// Zapisuje mody do bazy (upsert — nie usuwa wpisów spoza przekazanej listy).
        /// </summary>
        void SaveAllMods(List<ModConfiguration> mods);

        /// <summary>
        /// Aktualizuje pojedynczy mod w bazie.
        /// </summary>
        void UpdateMod(ModConfiguration mod);

        /// <summary>
        /// Dodaje nowy mod do bazy.
        /// </summary>
        void AddMod(ModConfiguration mod);

        /// <summary>
        /// Usuwa mod z bazy po Id.
        /// </summary>
        void DeleteMod(int id);

        /// <summary>
        /// Wstawia lub aktualizuje mod (UPSERT).
        /// </summary>
        void UpsertMod(ModConfiguration mod);

        /// <summary>
        /// Czyści cache w pamięci.
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Asynchronicznie pobiera konfigurację z API i merguje z lokalną.
        /// </summary>
        Task<List<ModConfiguration>> FetchAndMergeFromApiAsync();

        /// <summary>
        /// Odświeża konfigurację z API. Zwraca true jeśli były zmiany.
        /// </summary>
        Task<bool> RefreshFromApiAsync();

        /// <summary>
        /// Stosuje zwalidowany katalog z API (merge lokalnych pól + upsert SQLite).
        /// Zwraca true, jeśli dane w bazie uległy zmianie.
        /// </summary>
        Task<bool> ApplyRemoteCatalogAsync(List<ModConfiguration> apiMods);
    }
}
