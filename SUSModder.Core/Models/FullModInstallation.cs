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

        /// <summary>
        /// Wyłącz przypomnienia o aktualizacji dla tej instalacji
        /// (używane gdy użytkownik celowo instaluje starszą wersję)
        /// </summary>
        [JsonPropertyName("disableAutoUpdatePrompt")]
        public bool DisableAutoUpdatePrompt { get; set; }

        /// <summary>
        /// Auto-aktualizacja moda — gdy true, aktualizacje są instalowane
        /// automatycznie bez pytania użytkownika o potwierdzenie.
        /// Niezależne od DisableAutoUpdatePrompt/PinnedInstallVersion.
        /// </summary>
        [JsonPropertyName("autoUpdateEnabled")]
        public bool AutoUpdateEnabled { get; set; }

        /// <summary>
        /// Gdy true, dwuetapowy dialog poinstalacyjny (z wyborem
        /// uruchom/dodaj DLL) nie będzie pokazywany dla tego moda.
        /// </summary>
        [JsonPropertyName("dontShowPostInstallDialog")]
        public bool DontShowPostInstallDialog { get; set; }

        /// <summary>
        /// Przypięta wersja instalacji (jeśli DisableAutoUpdatePrompt = true)
        /// </summary>
        [JsonPropertyName("pinnedInstallVersion")]
        public string? PinnedInstallVersion { get; set; }
    }
}
