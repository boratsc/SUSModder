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
        /// Id lokalnej instancji modpacka (format v2). Puste dla legacy map v1.
        /// </summary>
        [JsonPropertyName("instanceId")]
        public string? InstanceId { get; set; }

        /// <summary>
        /// Lokalna nazwa instancji ustawiona przez użytkownika (format v2).
        /// </summary>
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Źródło utworzenia instancji: manual, shared_pack, clone lub legacy (format v2).
        /// </summary>
        [JsonPropertyName("origin")]
        public string? Origin { get; set; }

        /// <summary>
        /// Kod paczki źródłowej, jeśli instancja powstała z importu udostępnionego modpacka.
        /// </summary>
        [JsonPropertyName("sourcePackCode")]
        public string? SourcePackCode { get; set; }

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
