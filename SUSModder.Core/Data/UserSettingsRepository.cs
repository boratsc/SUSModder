using System;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla ustawień użytkownika (tabela user_settings).
    /// Singleton (CHECK id = 1), cache w pamięci.
    /// </summary>
    public class UserSettingsRepository : IUserSettingsRepository
    {
        private readonly DatabaseService _db;
        private UserSettings? _cachedSettings;
        private readonly object _cacheLock = new();

        public UserSettingsRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc/>
        public UserSettings LoadSettings()
        {
            // Zwróć z cache jeśli dostępne
            if (_cachedSettings != null)
                return _cachedSettings;

            lock (_cacheLock)
            {
                if (_cachedSettings != null)
                    return _cachedSettings;

                var conn = _db.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM user_settings WHERE id = 1;";

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    _cachedSettings = MapFromReader(reader);
                }
                else
                {
                    // Wstaw domyślny wiersz jeśli nie istnieje
                    _cachedSettings = new UserSettings();
                    SaveSettings(_cachedSettings);
                }

                return _cachedSettings;
            }
        }

        /// <inheritdoc/>
        public void SaveSettings(UserSettings settings)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO user_settings (
                    id, mode, last_launch_id, theme, language, telemetry_enabled,
                    mods_install_path, license_accepted, first_run_date, update_channel,
                    vanilla_install_path, av_warning_sig, last_seen_version,
                    minimize_to_tray, show_quick_launch_tray, tray_first_minimize_shown,
                    settings_version, active_sustats_guild_id,
                    mod_packs_enabled, mod_packs_auto_install, glass_reduce_transparency
                ) VALUES (
                    1, @mode, @last_launch_id, @theme, @language, @telemetry_enabled,
                    @mods_install_path, @license_accepted, @first_run_date, @update_channel,
                    @vanilla_install_path, @av_warning_sig, @last_seen_version,
                    @minimize_to_tray, @show_quick_launch_tray, @tray_first_minimize_shown,
                    @settings_version, @active_sustats_guild_id,
                    @mod_packs_enabled, @mod_packs_auto_install, @glass_reduce_transparency
                )
                ON CONFLICT(id) DO UPDATE SET
                    mode = excluded.mode,
                    last_launch_id = excluded.last_launch_id,
                    theme = excluded.theme,
                    language = excluded.language,
                    telemetry_enabled = excluded.telemetry_enabled,
                    mods_install_path = excluded.mods_install_path,
                    license_accepted = excluded.license_accepted,
                    first_run_date = excluded.first_run_date,
                    update_channel = excluded.update_channel,
                    vanilla_install_path = excluded.vanilla_install_path,
                    av_warning_sig = excluded.av_warning_sig,
                    last_seen_version = excluded.last_seen_version,
                    minimize_to_tray = excluded.minimize_to_tray,
                    show_quick_launch_tray = excluded.show_quick_launch_tray,
                    tray_first_minimize_shown = excluded.tray_first_minimize_shown,
                    settings_version = excluded.settings_version,
                    active_sustats_guild_id = excluded.active_sustats_guild_id,
                    mod_packs_enabled = excluded.mod_packs_enabled,
                    mod_packs_auto_install = excluded.mod_packs_auto_install,
                    glass_reduce_transparency = excluded.glass_reduce_transparency;";

            cmd.Parameters.AddWithValue("@mode", settings.Mode ?? string.Empty);
            cmd.Parameters.AddWithValue("@last_launch_id", settings.LastLaunchId);
            cmd.Parameters.AddWithValue("@theme", settings.Theme ?? "dark");
            cmd.Parameters.AddWithValue("@language", settings.Language ?? string.Empty);
            cmd.Parameters.AddWithValue("@telemetry_enabled", settings.TelemetryEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@mods_install_path", settings.ModsInstallPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@license_accepted", settings.LicenseAccepted ? 1 : 0);
            cmd.Parameters.AddWithValue("@first_run_date", settings.FirstRunDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@update_channel", settings.UpdateChannel ?? "release");
            cmd.Parameters.AddWithValue("@vanilla_install_path", settings.VanillaInstallPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@av_warning_sig", settings.AntivirusWarningAcknowledgedSignature ?? string.Empty);
            cmd.Parameters.AddWithValue("@last_seen_version", settings.LastSeenVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@minimize_to_tray", settings.MinimizeToTray ? 1 : 0);
            cmd.Parameters.AddWithValue("@show_quick_launch_tray", settings.ShowQuickLaunchInTray ? 1 : 0);
            cmd.Parameters.AddWithValue("@tray_first_minimize_shown", settings.TrayFirstMinimizeShown ? 1 : 0);
            cmd.Parameters.AddWithValue("@settings_version", settings.SettingsVersion);
            cmd.Parameters.AddWithValue("@active_sustats_guild_id", settings.ActiveSustatsGuildId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@mod_packs_enabled", settings.ModPacksEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@mod_packs_auto_install", settings.ModPacksAutoInstall ? 1 : 0);
            cmd.Parameters.AddWithValue("@glass_reduce_transparency", settings.GlassReduceTransparency ? 1 : 0);

            cmd.ExecuteNonQuery();

            // Aktualizuj cache
            lock (_cacheLock)
            {
                _cachedSettings = settings;
            }

            System.Diagnostics.Debug.WriteLine("[UserSettingsRepository] Ustawienia zapisane do SQLite.");
        }

        /// <inheritdoc/>
        public void UpdateSetting(string columnName, object value)
        {
            // Walidacja nazwy kolumny przed interpolacją (bezpieczeństwo)
            var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mode", "last_launch_id", "theme", "language", "telemetry_enabled",
                "mods_install_path", "license_accepted", "first_run_date", "update_channel",
                "vanilla_install_path", "av_warning_sig", "last_seen_version",
                "minimize_to_tray", "show_quick_launch_tray", "tray_first_minimize_shown",
                "settings_version", "active_sustats_guild_id",
                "mod_packs_enabled", "mod_packs_auto_install"
            };

            if (!allowedColumns.Contains(columnName))
                throw new ArgumentException($"Nieprawidłowa nazwa kolumny: {columnName}", nameof(columnName));

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE user_settings SET {columnName} = @value WHERE id = 1;";
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();

            // Unieważnij cache
            lock (_cacheLock)
            {
                _cachedSettings = null;
            }

            System.Diagnostics.Debug.WriteLine($"[UserSettingsRepository] Zaktualizowano {columnName} = {value}");
        }

        /// <inheritdoc/>
        public void UpdateSingleField(string column, object? value)
        {
            // Deleguje do istniejącej metody UpdateSetting (obsługuje whitelist kolumn)
            UpdateSetting(column, value ?? string.Empty);
        }

        /// <inheritdoc/>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedSettings = null;
            }
            System.Diagnostics.Debug.WriteLine("[UserSettingsRepository] Cache wyczyszczony.");
        }

        /// <summary>
        /// Mapuje wiersz z SqliteDataReader na obiekt UserSettings.
        /// </summary>
        private static UserSettings MapFromReader(SqliteDataReader reader)
        {
            return new UserSettings
            {
                Mode = reader.GetString(reader.GetOrdinal("mode")),
                LastLaunchId = reader.GetInt32(reader.GetOrdinal("last_launch_id")),
                Theme = reader.GetString(reader.GetOrdinal("theme")),
                Language = reader.GetString(reader.GetOrdinal("language")),
                TelemetryEnabled = reader.GetInt32(reader.GetOrdinal("telemetry_enabled")) != 0,
                ModsInstallPath = reader.GetString(reader.GetOrdinal("mods_install_path")),
                LicenseAccepted = reader.GetInt32(reader.GetOrdinal("license_accepted")) != 0,
                FirstRunDate = reader.GetString(reader.GetOrdinal("first_run_date")),
                UpdateChannel = reader.GetString(reader.GetOrdinal("update_channel")),
                VanillaInstallPath = reader.GetString(reader.GetOrdinal("vanilla_install_path")),
                AntivirusWarningAcknowledgedSignature = reader.GetString(reader.GetOrdinal("av_warning_sig")),
                LastSeenVersion = reader.GetString(reader.GetOrdinal("last_seen_version")),
                MinimizeToTray = reader.GetInt32(reader.GetOrdinal("minimize_to_tray")) != 0,
                ShowQuickLaunchInTray = reader.GetInt32(reader.GetOrdinal("show_quick_launch_tray")) != 0,
                TrayFirstMinimizeShown = reader.GetInt32(reader.GetOrdinal("tray_first_minimize_shown")) != 0,
                SettingsVersion = reader.GetInt32(reader.GetOrdinal("settings_version")),
                ActiveSustatsGuildId = reader.IsDBNull(reader.GetOrdinal("active_sustats_guild_id"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("active_sustats_guild_id")),
                ModPacksEnabled = TryGetBool(reader, "mod_packs_enabled", true),
                ModPacksAutoInstall = TryGetBool(reader, "mod_packs_auto_install", false),
                GlassReduceTransparency = TryGetBool(reader, "glass_reduce_transparency", false)
            };
        }

        private static bool TryGetBool(SqliteDataReader reader, string column, bool defaultValue)
        {
            try
            {
                var ordinal = reader.GetOrdinal(column);
                return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal) != 0;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
