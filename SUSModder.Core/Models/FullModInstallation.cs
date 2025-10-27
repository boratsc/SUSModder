using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Informacja o zainstalowanym modzie FULL
    /// </summary>
    public class FullModInstallation
    {
        /// <summary>
        /// ID moda
        /// </summary>
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        /// <summary>
        /// Nazwa moda
        /// </summary>
        [JsonPropertyName("modName")]
        public string ModName { get; set; } = string.Empty;

        /// <summary>
        /// Wersja moda
        /// </summary>
        [JsonPropertyName("modVersion")]
        public string ModVersion { get; set; } = string.Empty;

        /// <summary>
        /// Wersja Among Us
        /// </summary>
        [JsonPropertyName("amongVersion")]
        public string AmongVersion { get; set; } = string.Empty;

        /// <summary>
        /// Pełna ścieżka instalacji
        /// </summary>
        [JsonPropertyName("installPath")]
        public string InstallPath { get; set; } = string.Empty;

        /// <summary>
        /// URL źródłowy archiwum moda
        /// </summary>
        [JsonPropertyName("installedFrom")]
        public string InstalledFrom { get; set; } = string.Empty;

        /// <summary>
        /// Data i czas ostatniej aktualizacji
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }
}
