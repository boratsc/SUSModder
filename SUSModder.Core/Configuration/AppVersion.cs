using System.Text.Json.Serialization;

namespace SUSModder.Core.Configuration
{
    /// <summary>
    /// Informacje o wersji aplikacji przechowywane w version.json.
    /// Ten plik jest aktualizowany przy każdej aktualizacji przez Velopack.
    /// </summary>
    public class AppVersion
    {
        /// <summary>
        /// Aktualna wersja aplikacji (format: X.Y.Z)
        /// </summary>
        [JsonPropertyName("currentVersion")]
        public string CurrentVersion { get; set; } = "2.1.6";

        /// <summary>
        /// Data ostatniej aktualizacji
        /// </summary>
        [JsonPropertyName("lastUpdateDate")]
        public string LastUpdateDate { get; set; } = string.Empty;

        /// <summary>
        /// Build number (opcjonalny)
        /// </summary>
        [JsonPropertyName("buildNumber")]
        public string BuildNumber { get; set; } = string.Empty;
    }
}
