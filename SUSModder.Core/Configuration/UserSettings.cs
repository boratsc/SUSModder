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
        /// Motyw aplikacji: "dark", "light", "pink", "glass"
        /// </summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";

        /// <summary>
        /// Wymusza nieprzezroczysty fallback w motywie Szklany (lepsza czytelność / dostępność).
        /// </summary>
        [JsonPropertyName("glassReduceTransparency")]
        public bool GlassReduceTransparency { get; set; } = false;

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

        /// <summary>
        /// Podpis zestawu antywirusów, dla którego użytkownik potwierdził ostrzeżenie.
        /// Gdy wykryty zestaw się zmieni, ostrzeżenie powinno zostać pokazane ponownie.
        /// </summary>
        [JsonPropertyName("antivirusWarningAcknowledgedSignature")]
        public string AntivirusWarningAcknowledgedSignature { get; set; } = string.Empty;

        /// <summary>
        /// Ostatnia wersja aplikacji, którą użytkownik widział w changelogu.
        /// Puste = nigdy nie pokazano. Służy do wyświetlania "Co nowego" po aktualizacji.
        /// </summary>
        [JsonPropertyName("lastSeenVersion")]
        public string LastSeenVersion { get; set; } = string.Empty;

        /// <summary>
        /// Czy minimalizować do zasobnika systemowego zamiast zamykania aplikacji.
        /// Domyślnie włączone od wersji 2.4.0 (migracja: UserSettingsService.RunMigrations).
        /// </summary>
        [JsonPropertyName("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>
        /// Czy pokazywać szybkie uruchamianie (3 ostatnie mody) w menu zasobnika.
        /// Domyślnie włączone od wersji 2.4.0 (migracja: UserSettingsService.RunMigrations).
        /// </summary>
        [JsonPropertyName("showQuickLaunchInTray")]
        public bool ShowQuickLaunchInTray { get; set; } = true;

        /// <summary>
        /// Czy dymek informacyjny przy pierwszym minimalizowaniu został już pokazany.
        /// </summary>
        [JsonPropertyName("trayFirstMinimizeShown")]
        public bool TrayFirstMinimizeShown { get; set; } = false;

        /// <summary>
        /// Wersja schematu ustawień. Używana do migracji przy aktualizacjach.
        /// 0 = brak/niewersjonowany, 1 = pierwsza wersjonowana migracja (v2.4.0: domyślnie włączone tray).
        /// </summary>
        [JsonPropertyName("settingsVersion")]
        public int SettingsVersion { get; set; } = 0;

        /// <summary>
        /// ID aktywnego serwera Discord dla SUSTATS (GuildId).
        /// Ustawiane po wyborze serwera w UI. Null = brak wybranego serwera.
        /// Mapowane na kolumnę active_sustats_guild_id w SQLite.
        /// </summary>
        [JsonPropertyName("activeSustatsGuildId")]
        public string? ActiveSustatsGuildId { get; set; }

        /// <summary>
        /// Czy pokazywać opcje udostępniania zestawów modów.
        /// </summary>
        [JsonPropertyName("modPacksEnabled")]
        public bool ModPacksEnabled { get; set; } = true;

        /// <summary>
        /// Czy po deep linku od razu pokazywać instalację (bez dodatkowego kroku — nadal wymaga potwierdzenia w preview).
        /// </summary>
        [JsonPropertyName("modPacksAutoInstall")]
        public bool ModPacksAutoInstall { get; set; } = false;
    }
}
