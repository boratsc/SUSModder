using System.Collections.Generic;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Interfejs repozytorium dla zapisanych konfiguracji ToU (tabela tou_configs).
    /// Zastępuje touConfigsBase.json.
    /// </summary>
    public interface ITouConfigRepository
    {
        /// <summary>
        /// Pobiera wszystkie zapisane konfiguracje, posortowane od najnowszych.
        /// </summary>
        List<TouConfig> GetAllConfigs();

        /// <summary>
        /// Dodaje nową konfigurację (hash + data).
        /// </summary>
        void AddConfig(string hash);

        /// <summary>
        /// Usuwa wszystkie konfiguracje.
        /// </summary>
        void ClearAll();
    }
}
