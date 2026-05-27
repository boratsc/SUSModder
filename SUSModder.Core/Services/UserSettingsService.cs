using System;
using System.IO;
using System.Text.Json;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Zarządza ustawieniami użytkownika.
    /// Preferuje SQLite (przez IUserSettingsRepository) gdy dostępne.
    /// Fallback do user-settings.json dla backward compatibility.
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

        private readonly IUserSettingsRepository? _repository;
        private UserSettings? _cachedSettings;
        private AppVersion? _cachedVersion;

        /// <summary>
        /// Domyślne repozytorium używane gdy tworzona jest instancja bez parametrów.
        /// Ustawiane przez App.axaml.cs podczas inicjalizacji.
        /// </summary>
        private static IUserSettingsRepository? _defaultRepository;

        /// <summary>
        /// Ustawia domyślne repozytorium dla wszystkich instancji tworzonych bez parametrów.
        /// </summary>
        public static void SetDefaultRepository(IUserSettingsRepository repository)
        {
            _defaultRepository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Konstruktor bezparametrowy - używa domyślnego repozytorium (SQLite) jeśli ustawione,
        /// w przeciwnym razie fallback do JSON.
        /// </summary>
        public UserSettingsService() : this(_defaultRepository)
        {
        }

        /// <summary>
        /// Konstruktor z repozytorium SQLite - preferowana ścieżka.
        /// </summary>
        /// <param name="repository">Repozytorium ustawień (null = fallback do JSON).</param>
        public UserSettingsService(IUserSettingsRepository? repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Czy używamy SQLite do przechowywania ustawień.
        /// </summary>
        private bool UseSqlite => _repository != null;

        /// <summary>
        /// Ścieżka do katalogu z danymi aplikacji w %APPDATA%
        /// </summary>
        public static string GetAppDataFolder() => AppDataFolder;

        /// <summary>
        /// Wczytuje ustawienia użytkownika.
        /// SQLite: z bazy danych.
        /// JSON: z pliku user-settings.json (fallback).
        /// </summary>
        public UserSettings LoadUserSettings()
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            try
            {
                if (UseSqlite)
                {
                    _cachedSettings = _repository!.LoadSettings();
                    DetectAndUpdateChannelIfNeeded(_cachedSettings);
                    RunMigrations(_cachedSettings);
                }
                else
                {
                    EnsureAppDataFolderExists();

                    if (File.Exists(UserSettingsPath))
                    {
                        var json = File.ReadAllText(UserSettingsPath);
                        _cachedSettings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();

                        DetectAndUpdateChannelIfNeeded(_cachedSettings);
                        RunMigrations(_cachedSettings);
                    }
                    else
                    {
                        _cachedSettings = MigrateFromAppSettings() ?? new UserSettings();
                        DetectAndUpdateChannelIfNeeded(_cachedSettings);
                        SaveUserSettings(_cachedSettings);
                    }
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
        /// Zapisuje ustawienia użytkownika.
        /// SQLite: do bazy danych.
        /// JSON: do pliku user-settings.json (fallback).
        /// </summary>
        public void SaveUserSettings(UserSettings settings)
        {
            try
            {
                if (UseSqlite)
                {
                    _repository!.SaveSettings(settings);
                }
                else
                {
                    EnsureAppDataFolderExists();

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(settings, options);
                    File.WriteAllText(UserSettingsPath, json);

                    System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Zapisano ustawienia do JSON: {UserSettingsPath}");
                }

                _cachedSettings = settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd zapisu ustawień: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Wczytuje informacje o wersji aplikacji z version.json.
        /// (NIE migruje do SQLite - version.json zostaje jako plik)
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
        /// (NIE migruje do SQLite - version.json zostaje jako plik)
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
        /// Zapisuje ostatnią widzianą wersję changeloga (co nowego).
        /// </summary>
        public void SaveLastSeenVersion(string version)
        {
            UpdateUserSetting(s => s.LastSeenVersion = version);
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

            if (UseSqlite)
            {
                _repository!.ClearCache();
            }
        }

        /// <summary>
        /// Wykrywa kanał z aktualnej wersji aplikacji i aktualizuje ustawienia jeśli potrzeba.
        /// WYŁĄCZONE: Użytkownik powinien mieć pełną kontrolę nad kanałem aktualizacji.
        /// </summary>
        private void DetectAndUpdateChannelIfNeeded(UserSettings settings)
        {
            // DISABLED: Auto-detection prevents manual channel switching
        }

        /// <summary>
        /// Uruchamia migracje konfiguracji użytkownika po aktualizacji aplikacji.
        /// Każda migracja jest identyfikowana przez SettingsVersion.
        /// </summary>
        private void RunMigrations(UserSettings settings)
        {
            try
            {
                bool changed = false;

                // Migration 1 (v2.4.0): Domyślnie włącz opcje system tray
                while (settings.SettingsVersion < 1)
                {
                    System.Diagnostics.Debug.WriteLine("[UserSettingsService] Migracja ustawień do wersji 1: włączanie domyślnych opcji tray");

                    if (!settings.MinimizeToTray)
                    {
                        settings.MinimizeToTray = true;
                        changed = true;
                    }
                    if (!settings.ShowQuickLaunchInTray)
                    {
                        settings.ShowQuickLaunchInTray = true;
                        changed = true;
                    }

                    settings.SettingsVersion = 1;
                    changed = true;
                }

                // --- przyszłe migracje (SettingsVersion < 2, < 3, ...) dodawać tutaj ---

                if (changed)
                {
                    SaveUserSettings(settings);
                    System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Migracja ustawień zakończona (SettingsVersion={settings.SettingsVersion})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd migracji ustawień: {ex.Message}");
            }
        }

        /// <summary>
        /// Migruje ustawienia z appsettings.json przy pierwszym uruchomieniu.
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

                if (root.TryGetProperty("AppSettings", out var appSettings))
                {
                    if (appSettings.TryGetProperty("DefaultModsPath", out var pathProp))
                        settings.ModsInstallPath = Environment.ExpandEnvironmentVariables(pathProp.GetString() ?? string.Empty);
                }

                System.Diagnostics.Debug.WriteLine("[UserSettingsService] Zmigrowano ustawienia z appsettings.json");
                return settings;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd migracji z appsettings: {ex.Message}");
                return null;
            }
        }

        private void EnsureAppDataFolderExists()
        {
            if (!Directory.Exists(AppDataFolder))
                Directory.CreateDirectory(AppDataFolder);
        }

        /// <summary>
        /// Migruje wersję z appsettings.json do version.json.
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
                    var version = new AppVersion();
                    if (config.TryGetProperty("CurrentVersion", out var verProp))
                        version.CurrentVersion = verProp.GetString() ?? string.Empty;

                    System.Diagnostics.Debug.WriteLine("[UserSettingsService] Zmigrowano wersję z appsettings.json");
                    return version;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserSettingsService] Błąd migracji wersji: {ex.Message}");
                return null;
            }
        }
    }
}
