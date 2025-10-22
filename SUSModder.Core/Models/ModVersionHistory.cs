using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Reprezentuje pojedynczą wersję moda z historii wersji.
    /// Mapuje response z endpointu GET /susmodder-config-versions
    /// </summary>
    public class ModVersionHistory
    {
        /// <summary>
        /// Unikalny identyfikator wersji w tabeli config_versions
        /// </summary>
        [JsonPropertyName("VersionId")]
        public int VersionId { get; set; }

        /// <summary>
        /// ID moda (klucz obcy do tabeli config)
        /// </summary>
        [JsonPropertyName("ModId")]
        public int ModId { get; set; }

        /// <summary>
        /// Wersja moda (np. "5.3.1", "latest", "beta 1.0")
        /// UWAGA: To jest string, nie liczba!
        /// </summary>
        [JsonPropertyName("ModVersion")]
        public string ModVersion { get; set; } = string.Empty;

        /// <summary>
        /// Wersja Among Us (np. "2024.10.29")
        /// </summary>
        [JsonPropertyName("AmongVersion")]
        public string AmongVersion { get; set; } = string.Empty;

        /// <summary>
        /// Link do pobrania dla Steam
        /// </summary>
        [JsonPropertyName("GitHubRepoOrLink")]
        public string? GitHubRepoOrLink { get; set; }

        /// <summary>
        /// Link do pobrania dla Epic Games (opcjonalny)
        /// </summary>
        [JsonPropertyName("EpicGitHubRepoOrLink")]
        public string? EpicGitHubRepoOrLink { get; set; }

        /// <summary>
        /// Data i czas utworzenia wersji
        /// </summary>
        [JsonPropertyName("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Kto utworzył tę wersję (admin/system)
        /// </summary>
        [JsonPropertyName("CreatedBy")]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Notatki o wersji (np. "Version changed from 5.3.1 to 5.4.0")
        /// </summary>
        [JsonPropertyName("Notes")]
        public string? Notes { get; set; }

        /// <summary>
        /// Pomocnicza właściwość dla UI - formatowana data
        /// </summary>
        [JsonIgnore]
        public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// Pomocnicza właściwość dla UI - pełny opis wersji
        /// </summary>
        [JsonIgnore]
        public string DisplayText => $"{ModVersion} (Among Us {AmongVersion}) - {FormattedDate}";

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
