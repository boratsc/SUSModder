using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Models
{
    /// <summary>
    /// Mapa instalacji moda - plik .susmodder-install.json w katalogu moda
    /// Zawiera pełną informację o zainstalowanym modzie FULL i wszystkich DLL
    /// </summary>
    public class InstallationMap
    {
        /// <summary>
        /// Wersja formatu Installation Map (dla przyszłej kompatybilności)
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Data i czas instalacji
        /// </summary>
        [JsonPropertyName("installedAt")]
        public DateTime InstalledAt { get; set; }

        /// <summary>
        /// Kto/co zainstalowało (np. "SUSModder v2.0.1")
        /// </summary>
        [JsonPropertyName("installedBy")]
        public string InstalledBy { get; set; } = string.Empty;

        /// <summary>
        /// Platforma: "steam" lub "epic"
        /// </summary>
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// Informacja o modzie FULL
        /// </summary>
        [JsonPropertyName("fullMod")]
        public FullModInstallation FullMod { get; set; } = new();

        /// <summary>
        /// Lista zainstalowanych modów DLL w tym modzie FULL
        /// </summary>
        [JsonPropertyName("installedDlls")]
        public List<DllModInstallation> InstalledDlls { get; set; } = new();

        /// <summary>
        /// Metadane instalacji
        /// </summary>
        [JsonPropertyName("metadata")]
        public InstallationMetadata Metadata { get; set; } = new();
    }
}
