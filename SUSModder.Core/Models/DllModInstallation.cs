using System;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Informacja o zainstalowanym modzie DLL
    /// </summary>
    public class DllModInstallation
    {
        /// <summary>
        /// ID moda DLL
        /// </summary>
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        /// <summary>
        /// Nazwa moda DLL
        /// </summary>
        [JsonPropertyName("modName")]
        public string ModName { get; set; } = string.Empty;

        /// <summary>
        /// Wersja moda DLL
        /// </summary>
        [JsonPropertyName("modVersion")]
        public string ModVersion { get; set; } = string.Empty;

        /// <summary>
        /// Ścieżka instalacji relatywna do katalogu moda FULL (np. BepInEx/plugins/AleLuduMod.dll)
        /// </summary>
        [JsonPropertyName("installPath")]
        public string InstallPath { get; set; } = string.Empty;

        /// <summary>
        /// URL źródłowy pliku DLL
        /// </summary>
        [JsonPropertyName("installedFrom")]
        public string InstalledFrom { get; set; } = string.Empty;

        /// <summary>
        /// Data i czas instalacji
        /// </summary>
        [JsonPropertyName("installedAt")]
        public DateTime InstalledAt { get; set; }

        /// <summary>
        /// Data i czas ostatniej aktualizacji
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }
}
