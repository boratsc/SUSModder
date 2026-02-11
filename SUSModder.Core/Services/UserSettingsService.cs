using System;
using System.IO;
using System.Text.Json;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Zarządza ustawieniami użytkownika przechowywanymi w %APPDATA%/SUSModder/.
    /// Te ustawienia NIE są nadpisywane podczas aktualizacji aplikacji.
    /// </summary>
    public class UserSettingsService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SUSModder"
        );

        private static readonly string UserSettingsPath = Path.Combine(AppDataFolder, "user-settings.json");
        private static readonly string VersionFilePath = Path.Combine(AppContext.BaseDirectory, "version.json");

        private UserSettings? _cachedSettings;
        private AppVersion? _cachedVersion;

        /// <summary>
        /// Ścieżka do katalogu z danymi aplikacji w %APPDATA%
        /// </summary>
        public static string GetAppDataFolder() => AppDataFolder;

        /// <summary>
        /// Wczytuje ustawienia użytkownika. Jeśli plik nie istnieje, tworzy domyślny.
        /// </summary>
        public UserSettings LoadUserSettings()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            try
            {
                EnsureAppDataFolderExists();

                if (File.Exists(UserSettingsPath))
                {
                    var json = File.ReadAllText(UserSettingsPath);
                    _cachedSettings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                    
                    // Auto-detect channel from current version if not set or if mismatch
                    DetectAndUpdateChannelIfNeeded(_cachedSettings);
                }
                else
                {
                    // Pierwsza instalacja - migruj z appsettings.json jeśli istnieje
                    _cachedSettings = MigrateFromAppSettings() ?? new UserSettings();
                    
                    // Auto-detect channel from current version
                    DetectAndUpdateChannelIfNeeded(_cachedSettings);
                    
                    SaveUserSettings(_cachedSettings);
                }

                return _cachedSettings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd wczytywania ustawień: {ex.Message}");
                _cachedSettings = new UserSettings();
                return _cachedSettings;
            }
        }

        /// <summary>
        /// Zapisuje ustawienia użytkownika do pliku.
        /// </summary>
        public void SaveUserSettings(UserSettings settings)
        {
            try
            {
                EnsureAppDataFolderExists();

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(UserSettingsPath, json);

                _cachedSettings = settings;
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Zapisano ustawienia do: {UserSettingsPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd zapisu ustawień: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Wczytuje informacje o wersji aplikacji z version.json.
        /// </summary>
        public AppVersion LoadAppVersion()
        {
            if (_cachedVersion != null)
                return _cachedVersion;

            try
            {
                if (File.Exists(VersionFilePath))
                {
                    var json = File.ReadAllText(VersionFilePath);
                    _cachedVersion = JsonSerializer.Deserialize<AppVersion>(json) ?? new AppVersion();
                }
                else
                {
                    // Fallback - migruj z appsettings.json
                    _cachedVersion = MigrateVersionFromAppSettings() ?? new AppVersion();
                    SaveAppVersion(_cachedVersion);
                }

                return _cachedVersion;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd wczytywania wersji: {ex.Message}");
                _cachedVersion = new AppVersion();
                return _cachedVersion;
            }
        }

        /// <summary>
        /// Zapisuje informacje o wersji aplikacji do version.json.
        /// </summary>
        public void SaveAppVersion(AppVersion version)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(version, options);
                File.WriteAllText(VersionFilePath, json);

                _cachedVersion = version;
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Zapisano wersję do: {VersionFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd zapisu wersji: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Aktualizuje pojedyncze pole w ustawieniach użytkownika.
        /// </summary>
        public void UpdateUserSetting(Action<UserSettings> updateAction)
        {
            var settings = LoadUserSettings();
            updateAction(settings);
            SaveUserSettings(settings);
        }

        /// <summary>
        /// Aktualizuje pole tylko jeśli aktualna wartość jest pusta.
        /// </summary>
        public void UpdateIfEmpty(Func<UserSettings, string?> getCurrentValue, Action<UserSettings, string> setValue, string? newValue)
        {
            if (string.IsNullOrWhiteSpace(newValue))
                return;

            var settings = LoadUserSettings();
            var current = getCurrentValue(settings);
            if (!string.IsNullOrWhiteSpace(current))
                return;

            setValue(settings, newValue);
            SaveUserSettings(settings);
        }

        /// <summary>
        /// Resetuje cache - używaj po aktualizacji aplikacji.
        /// </summary>
        public void ClearCache()
        {
            _cachedSettings = null;
            _cachedVersion = null;
        }

        /// <summary>
        /// Wykrywa kanał z aktualnej wersji aplikacji i aktualizuje ustawienia jeśli potrzeba.
        /// WYŁĄCZONE: Użytkownik powinien mieć pełną kontrolę nad kanałem aktualizacji.
        /// Auto-detekcja uniemożliwiała przełączanie się między kanałami (beta <-> release).
        /// </summary>
        private void DetectAndUpdateChannelIfNeeded(UserSettings settings)
        {
            // DISABLED: Auto-detection prevents manual channel switching
            // User should have full control over update channel selection in settings
            
            // Original logic (disabled):
            // - Detect channel from version (e.g. "2.3.0-beta" -> "beta", "2.2.0" -> "release")
            // - Auto-update settings.UpdateChannel if mismatch
            //
            // Problem: User on release 2.2.2 switches to beta manually,
            // but auto-detection immediately resets it back to "release"
            // because version.json still says "2.2.2" (no "-beta" suffix)
        }

        private void EnsureAppDataFolderExists()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Utworzono katalog: {AppDataFolder}");
            }
        }

        /// <summary>
        /// Migracja ustawień z appsettings.json (dla użytkowników aktualizujących z wersji <= 2.1.5).
        /// </summary>
        private UserSettings? MigrateFromAppSettings()
        {
            try
            {
                var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(appSettingsPath))
                    return null;

                var json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var settings = new UserSettings();

                // Configuration section
                if (root.TryGetProperty("Configuration", out var config))
                {
                    if (config.TryGetProperty("Mode", out var mode))
                        settings.Mode = mode.GetString() ?? string.Empty;  // Pusty = wymusza dialog wyboru platformy

                    if (config.TryGetProperty("lastLaunchId", out var lastLaunch))
                        settings.LastLaunchId = lastLaunch.GetInt32();

                    if (config.TryGetProperty("Theme", out var theme))
                        settings.Theme = theme.GetString() ?? "dark";

                    if (config.TryGetProperty("Language", out var lang))
                        settings.Language = lang.GetString() ?? string.Empty;

                    if (config.TryGetProperty("TelemetryEnabled", out var telemetry))
                        settings.TelemetryEnabled = telemetry.GetBoolean();
                }

                // AppSettings section
                if (root.TryGetProperty("AppSettings", out var appSettings))
                {
                    if (appSettings.TryGetProperty("ModsInstallPath", out var modsPath))
                        settings.ModsInstallPath = modsPath.GetString() ?? string.Empty;
                }

                System.Diagnostics.Debug.WriteLine("[UserSettingsService] Zmigrowano ustawienia z appsettings.json");
                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd migracji z appsettings.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Migracja wersji z appsettings.json.
        /// </summary>
        private AppVersion? MigrateVersionFromAppSettings()
        {
            try
            {
                var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(appSettingsPath))
                    return null;

                var json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Configuration", out var config))
                {
                    if (config.TryGetProperty("CurrentVersion", out var version))
                    {
                        return new AppVersion
                        {
                            CurrentVersion = version.GetString() ?? "2.1.6",
                            LastUpdateDate = DateTime.UtcNow.ToString("O")
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd migracji wersji z appsettings.json: {ex.Message}");
                return null;
            }
        }
    }
}
