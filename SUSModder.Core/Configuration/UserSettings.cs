using System.Text.Json.Serialization;

namespace SUSModder.Core.Configuration
{
    /// <summary>
    /// Ustawienia użytkownika przechowywane w %APPDATA%/SUSModder/user-settings.json.
    /// Te ustawienia NIE są nadpisywane podczas aktualizacji aplikacji.
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// Tryb gry: "steam" lub "epic" (pusty = nie wybrano, wymusza dialog wyboru)
        /// </summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// ID ostatniego uruchomienia
        /// </summary>
        [JsonPropertyName("lastLaunchId")]
        public int LastLaunchId { get; set; } = 0;

        /// <summary>
        /// Motyw aplikacji: "dark", "light", "pink"
        /// </summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";

        /// <summary>
        /// Język aplikacji (jeśli pusty - automatyczna detekcja)
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Czy telemetria jest włączona
        /// </summary>
        [JsonPropertyName("telemetryEnabled")]
        public bool TelemetryEnabled { get; set; } = true;

        /// <summary>
        /// Ścieżka instalacji modów (jeśli pusta - użyj DefaultModsPath)
        /// </summary>
        [JsonPropertyName("modsInstallPath")]
        public string ModsInstallPath { get; set; } = string.Empty;

        /// <summary>
        /// Czy użytkownik zaakceptował licencję (dla pierwszego uruchomienia)
        /// </summary>
        [JsonPropertyName("licenseAccepted")]
        public bool LicenseAccepted { get; set; } = false;

        /// <summary>
        /// Data pierwszego uruchomienia (dla statystyk)
        /// </summary>
        [JsonPropertyName("firstRunDate")]
        public string FirstRunDate { get; set; } = string.Empty;

        /// <summary>
        /// Kanał aktualizacji: "release" (stabilne wydania) lub "beta" (wersje testowe)
        /// </summary>
        [JsonPropertyName("updateChannel")]
        public string UpdateChannel { get; set; } = "release";

        /// <summary>
        /// Ostatnio wykryta ścieżka do Vanilla Among Us (katalog zawierający Among Us.exe)
        /// </summary>
        [JsonPropertyName("vanillaInstallPath")]
        public string VanillaInstallPath { get; set; } = string.Empty;
    }
}
