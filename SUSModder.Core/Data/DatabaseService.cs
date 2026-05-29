using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Zarządza połączeniem z bazą SQLite, migracjami schematu i inicjalizacją.
    /// Baza: %APPDATA%/SUSModder/susmodder.db
    /// </summary>
    public class DatabaseService : IDisposable, IAsyncDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private readonly object _lock = new();

        // Aktualna wersja schematu bazy danych.
        // Zwiększaj przy każdej zmianie schematu (CREATE TABLE, ALTER TABLE, etc.).
        private const int LatestSchemaVersion = 3;

        public DatabaseService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SUSModder"
            );
            Directory.CreateDirectory(appData);
            _dbPath = Path.Combine(appData, "susmodder.db");
        }

        public string DatabasePath => _dbPath;

        /// <summary>
        /// Zwraca współdzieloną koneksję do bazy.
        /// Tworzy ją przy pierwszym wywołaniu.
        /// </summary>
        public SqliteConnection GetConnection()
        {
            if (_connection != null)
                return _connection;

            lock (_lock)
            {
                if (_connection != null)
                    return _connection;

                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString();

                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                ConfigurePragmas(_connection);

                return _connection;
            }
        }

        /// <summary>
        /// Inicjalizuje bazę danych: tworzy tabele, wykonuje migracje, importuje dane z JSON.
        /// </summary>
        public async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                var conn = GetConnection();
                var isNewDatabase = !File.Exists(_dbPath) || new FileInfo(_dbPath).Length == 0;

                if (isNewDatabase)
                {
                    CreateAllTables(conn);
                    SetUserVersion(conn, LatestSchemaVersion);
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] Nowa baza utworzona (v{0}).", LatestSchemaVersion);

                    // Importuj dane ze starych plików JSON
                    MigrateFromJson(conn);

                    // Po udanej migracji posprzątaj stare pliki JSON
                    CleanupJsonFiles();
                }
                else
                {
                    // Sprawdź i aplikuj pending migracje
                    ApplyMigrations(conn);

                    // Fallback: jeśli baza istnieje ale tabele są puste (np. z buggy first-run),
                    // spróbuj zaimportować dane z JSON
                    if (EnsureDataMigratedIfEmpty(conn))
                    {
                        CleanupJsonFiles();
                    }
                }

                // Walidacja integralności
                ValidateIntegrity(conn);

                // WAL checkpoint – flush danych z WAL do głównego pliku DB
                CheckpointWal(conn);
            });
        }

        /// <summary>
        /// Konfiguruje PRAGMA dla optymalnej wydajności.
        /// </summary>
        private void ConfigurePragmas(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();

            // WAL mode - pozwala na współbieżne odczyty podczas zapisu
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();

            // NORMAL synchronous - dobry balans wydajności i bezpieczeństwa
            cmd.CommandText = "PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();

            // FOREIGN KEYS - włączamy dla integralności referencyjnej
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();

            // busy_timeout - czekaj do 5s gdy baza jest zablokowana
            cmd.CommandText = "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();

            System.Diagnostics.Debug.WriteLine("[DatabaseService] PRAGMA skonfigurowane (WAL, NORMAL, FK, busy_timeout=5000)");
        }

        /// <summary>
        /// Tworzy wszystkie tabele w nowej bazie.
        /// </summary>
        private void CreateAllTables(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();

            // Tabela mods
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS mods (
                    Id              INTEGER PRIMARY KEY,
                    ModName         TEXT    NOT NULL,
                    PngFileName     TEXT    NOT NULL,
                    InstallPath     TEXT,
                    GitHubRepoOrLink TEXT   NOT NULL DEFAULT '',
                    EpicGitHubRepoOrLink TEXT,
                    ModType         TEXT    NOT NULL CHECK (ModType IN ('full', 'dll', 'Vanilla')),
                    DllInstallPath  TEXT,
                    ModVersion      TEXT    NOT NULL DEFAULT '',
                    AmongVersion    TEXT    NOT NULL DEFAULT '',
                    LastUpdated     TEXT,
                    Description     TEXT    NOT NULL DEFAULT '',
                    HasRoles        INTEGER,
                    LobbyRegionBaseUrl TEXT,
                    SupportsLobbySharing INTEGER NOT NULL DEFAULT 0,
                    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();

            // Indeksy dla mods
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_mods_type ON mods(ModType);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_mods_name ON mods(ModName);";
            cmd.ExecuteNonQuery();

            // Tabela user_settings (singleton, 16 kolumn + active_sustats_guild_id)
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS user_settings (
                    id                  INTEGER PRIMARY KEY CHECK (id = 1),
                    mode                TEXT    NOT NULL DEFAULT '',
                    last_launch_id      INTEGER NOT NULL DEFAULT 0,
                    theme               TEXT    NOT NULL DEFAULT 'dark',
                    language            TEXT    NOT NULL DEFAULT '',
                    telemetry_enabled   INTEGER NOT NULL DEFAULT 1,
                    mods_install_path   TEXT    NOT NULL DEFAULT '',
                    license_accepted    INTEGER NOT NULL DEFAULT 0,
                    first_run_date      TEXT    NOT NULL DEFAULT '',
                    update_channel      TEXT    NOT NULL DEFAULT 'release',
                    vanilla_install_path TEXT   NOT NULL DEFAULT '',
                    av_warning_sig      TEXT    NOT NULL DEFAULT '',
                    last_seen_version   TEXT    NOT NULL DEFAULT '',
                    minimize_to_tray    INTEGER NOT NULL DEFAULT 1,
                    show_quick_launch_tray INTEGER NOT NULL DEFAULT 1,
                    tray_first_minimize_shown INTEGER NOT NULL DEFAULT 0,
                    settings_version    INTEGER NOT NULL DEFAULT 0,
                    active_sustats_guild_id TEXT DEFAULT NULL
                );";
            cmd.ExecuteNonQuery();

            // Wstaw domyślny wiersz singleton
            cmd.CommandText = @"
                INSERT OR IGNORE INTO user_settings (id) VALUES (1);";
            cmd.ExecuteNonQuery();

            // Tabela tou_configs
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS tou_configs (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    hash            TEXT    NOT NULL,
                    created_at      TEXT    NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_tou_configs_hash ON tou_configs(hash);";
            cmd.ExecuteNonQuery();

            // Tabela discord_auth (singleton, przechowuje zaszyfrowane tokeny Discord OAuth2)
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS discord_auth (
                    id                  INTEGER PRIMARY KEY CHECK (id = 1),
                    access_token_enc    TEXT NOT NULL,
                    refresh_token_enc   TEXT NOT NULL,
                    token_type          TEXT NOT NULL DEFAULT 'Bearer',
                    expires_at          TEXT NOT NULL,
                    discord_user_id     TEXT,
                    discord_username    TEXT,
                    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();

            // Tabela sustats_credentials (przechowuje token+secret SUSTATS dla serwerów Discord)
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS sustats_credentials (
                    guild_id            TEXT PRIMARY KEY,
                    server_name         TEXT NOT NULL,
                    token_enc           TEXT NOT NULL,
                    secret_enc          TEXT NOT NULL,
                    endpoint            TEXT NOT NULL,
                    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();

            System.Diagnostics.Debug.WriteLine("[DatabaseService] Wszystkie tabele utworzone (v2 – discord_auth, sustats_credentials).");
        }

        /// <summary>
        /// Aplikuje migracje schematu na podstawie PRAGMA user_version.
        /// </summary>
        private void ApplyMigrations(SqliteConnection conn)
        {
            var currentVersion = GetUserVersion(conn);
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Aktualna wersja schematu: {currentVersion}");

            if (currentVersion < 1)
            {
                // Pierwsza migracja: utwórz tabele jeśli nie istnieją
                // CreateAllTables tworzy wszystkie tabele do LatestSchemaVersion
                BackupDatabase();
                CreateAllTables(conn);
                SetUserVersion(conn, LatestSchemaVersion);
            }

            if (currentVersion < 2)
            {
                // Migracja v2: dodanie kolumny active_sustats_guild_id + tabele Discord OAuth2
                // Używamy jawnej transakcji — jeśli którykolwiek krok się nie powiedzie,
                // cała migracja jest wycofywana, a baza pozostaje w stanie v1.
                BackupDatabase();
                System.Diagnostics.Debug.WriteLine("[DatabaseService] Migracja do v2 – Discord OAuth2...");

                using var tx = conn.BeginTransaction();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;

                    // Dodaj kolumnę active_sustats_guild_id do user_settings (jeśli nie istnieje)
                    // SQLite nie ma ALTER TABLE ADD COLUMN IF NOT EXISTS – sprawdzamy przez pragma_table_info
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('user_settings')
                        WHERE name = 'active_sustats_guild_id';";
                    var colExists = (long)(cmd.ExecuteScalar() ?? 0) > 0;

                    if (!colExists)
                    {
                        cmd.CommandText = @"
                            ALTER TABLE user_settings ADD COLUMN active_sustats_guild_id TEXT DEFAULT NULL;";
                        cmd.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[DatabaseService] Dodano kolumnę active_sustats_guild_id.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[DatabaseService] Kolumna active_sustats_guild_id już istnieje – pomijam.");
                    }

                    // Utwórz tabelę discord_auth (jeśli nie istnieje)
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS discord_auth (
                            id                  INTEGER PRIMARY KEY CHECK (id = 1),
                            access_token_enc    TEXT NOT NULL,
                            refresh_token_enc   TEXT NOT NULL,
                            token_type          TEXT NOT NULL DEFAULT 'Bearer',
                            expires_at          TEXT NOT NULL,
                            discord_user_id     TEXT,
                            discord_username    TEXT,
                            created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                            updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
                        );";
                    cmd.ExecuteNonQuery();

                    // Utwórz tabelę sustats_credentials (jeśli nie istnieje)
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS sustats_credentials (
                            guild_id            TEXT PRIMARY KEY,
                            server_name         TEXT NOT NULL,
                            token_enc           TEXT NOT NULL,
                            secret_enc          TEXT NOT NULL,
                            endpoint            TEXT NOT NULL,
                            created_at          TEXT NOT NULL DEFAULT (datetime('now')),
                            updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
                        );";
                    cmd.ExecuteNonQuery();

                    tx.Commit();
                    SetUserVersion(conn, 2);
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] Migracja do v2 zakończona pomyślnie.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DatabaseService] BŁĄD migracji do v2: {ex.Message}. Wycofywanie...");
                    try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                    throw;
                }
            }

            if (currentVersion < 3)
            {
                // Migracja v3: dodanie kolumn LobbyRegionBaseUrl i SupportsLobbySharing do tabeli mods.
                BackupDatabase();
                System.Diagnostics.Debug.WriteLine("[DatabaseService] Migracja do v3 – lobby board columns...");

                using var tx = conn.BeginTransaction();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;

                    // Dodaj LobbyRegionBaseUrl (jeśli nie istnieje)
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('mods')
                        WHERE name = 'LobbyRegionBaseUrl';";
                    var lobbyUrlExists = (long)(cmd.ExecuteScalar() ?? 0) > 0;
                    if (!lobbyUrlExists)
                    {
                        cmd.CommandText = "ALTER TABLE mods ADD COLUMN LobbyRegionBaseUrl TEXT;";
                        cmd.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[DatabaseService] Dodano kolumnę LobbyRegionBaseUrl.");
                    }

                    // Dodaj SupportsLobbySharing (jeśli nie istnieje)
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM pragma_table_info('mods')
                        WHERE name = 'SupportsLobbySharing';";
                    var supportsLobbyExists = (long)(cmd.ExecuteScalar() ?? 0) > 0;
                    if (!supportsLobbyExists)
                    {
                        cmd.CommandText = "ALTER TABLE mods ADD COLUMN SupportsLobbySharing INTEGER NOT NULL DEFAULT 0;";
                        cmd.ExecuteNonQuery();
                        System.Diagnostics.Debug.WriteLine("[DatabaseService] Dodano kolumnę SupportsLobbySharing.");
                    }

                    tx.Commit();
                    SetUserVersion(conn, 3);
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] Migracja do v3 zakończona pomyślnie.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DatabaseService] BŁĄD migracji do v3: {ex.Message}. Wycofywanie...");
                    try { tx.Rollback(); } catch { /* ignore rollback errors */ }
                    throw;
                }
            }
        }

        /// <summary>
        /// Pobiera wersję schematu z PRAGMA user_version.
        /// </summary>
        private int GetUserVersion(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Ustawia wersję schematu przez PRAGMA user_version.
        /// </summary>
        private void SetUserVersion(SqliteConnection conn, int version)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";
            cmd.ExecuteNonQuery();
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Ustawiono user_version = {version}");
        }

        /// <summary>
        /// Wykonuje WAL checkpoint – flush danych z WAL do głównego pliku .db
        /// </summary>
        private void CheckpointWal(SqliteConnection conn)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                var result = cmd.ExecuteScalar();
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] WAL checkpoint result: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] WAL checkpoint error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sprawdza integralność bazy danych.
        /// </summary>
        private void ValidateIntegrity(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();

            if (result != "ok")
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] OSTRZEŻENIE - integrity_check: {result}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DatabaseService] integrity_check: OK");
            }
        }

        /// <summary>
        /// Tworzy kopię zapasową bazy przed migracją.
        /// </summary>
        public void BackupDatabase()
        {
            try
            {
                if (File.Exists(_dbPath))
                {
                    var backupPath = _dbPath + ".bak";
                    File.Copy(_dbPath, backupPath, overwrite: true);
                    System.Diagnostics.Debug.WriteLine($"[DatabaseService] Backup utworzony: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd tworzenia backupu: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        #region JSON → SQLite Migration

        /// <summary>
        /// Sprawdza czy tabele są puste i jeśli tak, próbuje zaimportować dane z JSON.
        /// Obsługuje przypadek buggy first-run gdzie tabele utworzono bez migracji danych.
        /// </summary>
        private bool EnsureDataMigratedIfEmpty(SqliteConnection conn)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM mods;";
                var modCount = Convert.ToInt32(cmd.ExecuteScalar());

                cmd.CommandText = "SELECT mode FROM user_settings WHERE id = 1;";
                var mode = cmd.ExecuteScalar()?.ToString() ?? string.Empty;

                bool needsUserSettingsMigration = string.IsNullOrEmpty(mode);
                bool needsModsMigration = modCount == 0;

                if (needsUserSettingsMigration || needsModsMigration)
                {
                    System.Diagnostics.Debug.WriteLine($"[DatabaseService] Wykryto puste tabele (mods={modCount}, mode='{mode}') – próbuję migracji z JSON...");
                    MigrateFromJson(conn);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] EnsureDataMigratedIfEmpty error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Po udanej migracji: przemianowuje stare pliki JSON na .bak (backup) i tworzy flagę .sqlite-migrated.
        /// </summary>
        private void CleanupJsonFiles()
        {
            try
            {
                var appData = _dbPath.Replace("susmodder.db", "");
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;

                // Lista plików do sprzątnięcia: [ścieżka, nazwa]
                var filesToClean = new (string path, string name)[]
                {
                    (Path.Combine(appData, "user-settings.json"), "user-settings.json"),
                    (Path.Combine(appData, "touConfigsBase.json"), "touConfigsBase.json"),
                    (Path.Combine(exeDir, "config.json"), "config.json"),
                };

                foreach (var (path, name) in filesToClean)
                {
                    if (File.Exists(path))
                    {
                        var bakPath = path + ".bak";
                        try
                        {
                            // Usuń stary .bak jeśli istnieje
                            if (File.Exists(bakPath))
                                File.Delete(bakPath);

                            File.Move(path, bakPath);
                            System.Diagnostics.Debug.WriteLine($"[DatabaseService] {name} → {name}.bak");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Nie można przenieść {name}: {ex.Message}");
                        }
                    }
                }

                // Utwórz flagę .sqlite-migrated
                var flagPath = Path.Combine(appData, ".sqlite-migrated");
                File.WriteAllText(flagPath, DateTime.Now.ToString("O"));
                System.Diagnostics.Debug.WriteLine("[DatabaseService] Flaga .sqlite-migrated utworzona.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] CleanupJsonFiles error: {ex.Message}");
            }
        }

        /// <summary>
        /// Importuje dane ze starych plików JSON do nowo utworzonej bazy SQLite.
        /// </summary>
        private void MigrateFromJson(SqliteConnection conn)
        {
            System.Diagnostics.Debug.WriteLine("[DatabaseService] Rozpoczynam migrację JSON → SQLite...");

            ImportUserSettingsFromJson(conn);
            ImportModsFromJson(conn);
            ImportTouConfigsFromJson(conn);

            System.Diagnostics.Debug.WriteLine("[DatabaseService] Migracja JSON → SQLite zakończona.");
        }

        /// <summary>
        /// Importuje user-settings.json → tabela user_settings.
        /// </summary>
        private void ImportUserSettingsFromJson(SqliteConnection conn)
        {
            try
            {
                var jsonPath = Path.Combine(_dbPath.Replace("susmodder.db", ""), "user-settings.json");
                if (!File.Exists(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] user-settings.json nie istnieje – pomijam.");
                    return;
                }

                var json = File.ReadAllText(jsonPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<SUSModder.Core.Configuration.UserSettings>(json);
                if (settings == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] user-settings.json pusty – pomijam.");
                    return;
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO user_settings (
                        id, mode, last_launch_id, theme, language, telemetry_enabled,
                        mods_install_path, license_accepted, first_run_date, update_channel,
                        vanilla_install_path, av_warning_sig, last_seen_version,
                        minimize_to_tray, show_quick_launch_tray, tray_first_minimize_shown,
                        settings_version
                    ) VALUES (
                        1, @mode, @last_launch_id, @theme, @language, @telemetry_enabled,
                        @mods_install_path, @license_accepted, @first_run_date, @update_channel,
                        @vanilla_install_path, @av_warning_sig, @last_seen_version,
                        @minimize_to_tray, @show_quick_launch_tray, @tray_first_minimize_shown,
                        @settings_version
                    );";

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

                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Zaimportowano user-settings.json → user_settings (mode={settings.Mode}, theme={settings.Theme}, lang={settings.Language})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd importu user-settings.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Importuje config.json → tabela mods.
        /// </summary>
        private void ImportModsFromJson(SqliteConnection conn)
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configPath = Path.Combine(exeDir, "config.json");
                if (!File.Exists(configPath))
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] config.json nie istnieje – pomijam.");
                    return;
                }

                var json = File.ReadAllText(configPath);
                var mods = System.Text.Json.JsonSerializer.Deserialize<List<SUSModder.Core.Configuration.ModConfiguration>>(json);
                if (mods == null || mods.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] config.json pusty – pomijam.");
                    return;
                }

                int imported = 0;
                foreach (var mod in mods)
                {
                    try
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO mods (
                                Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink,
                                EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, AmongVersion,
                                LastUpdated, Description, HasRoles
                            ) VALUES (
                                @Id, @ModName, @PngFileName, @InstallPath, @GitHubRepoOrLink,
                                @EpicGitHubRepoOrLink, @ModType, @DllInstallPath, @ModVersion, @AmongVersion,
                                @LastUpdated, @Description, @HasRoles
                            );";

                        cmd.Parameters.AddWithValue("@Id", mod.Id);
                        cmd.Parameters.AddWithValue("@ModName", mod.ModName ?? string.Empty);
                        cmd.Parameters.AddWithValue("@PngFileName", mod.PngFileName ?? string.Empty);
                        cmd.Parameters.AddWithValue("@InstallPath", (object?)mod.InstallPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@GitHubRepoOrLink", mod.GitHubRepoOrLink ?? string.Empty);
                        cmd.Parameters.AddWithValue("@EpicGitHubRepoOrLink", (object?)mod.EpicGitHubRepoOrLink ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ModType", mod.ModType ?? string.Empty);
                        cmd.Parameters.AddWithValue("@DllInstallPath", (object?)mod.DllInstallPath ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ModVersion", mod.ModVersion ?? string.Empty);
                        cmd.Parameters.AddWithValue("@AmongVersion", mod.AmongVersion ?? string.Empty);
                        cmd.Parameters.AddWithValue("@LastUpdated", mod.LastUpdated?.ToString("O") ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Description", mod.Description ?? string.Empty);
                        cmd.Parameters.AddWithValue("@HasRoles", mod.HasRoles.HasValue ? (object)(mod.HasRoles.Value ? 1 : 0) : DBNull.Value);

                        cmd.ExecuteNonQuery();
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd importu moda Id={mod.Id}: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Zaimportowano config.json → mods: {imported}/{mods.Count} modów.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd importu config.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Importuje touConfigsBase.json → tabela tou_configs.
        /// </summary>
        private void ImportTouConfigsFromJson(SqliteConnection conn)
        {
            try
            {
                var jsonPath = Path.Combine(_dbPath.Replace("susmodder.db", ""), "touConfigsBase.json");
                if (!File.Exists(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] touConfigsBase.json nie istnieje – pomijam.");
                    return;
                }

                var json = File.ReadAllText(jsonPath);
                var configs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<dynamic>>(json);
                if (configs == null || configs.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[DatabaseService] touConfigsBase.json pusty – pomijam.");
                    return;
                }

                int imported = 0;
                foreach (var config in configs)
                {
                    try
                    {
                        var hash = config.hash?.ToString();
                        var date = config.date?.ToString();
                        if (string.IsNullOrEmpty(hash)) continue;

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "INSERT INTO tou_configs (hash, created_at) VALUES (@hash, @date);";
                        cmd.Parameters.AddWithValue("@hash", hash);
                        cmd.Parameters.AddWithValue("@date", date ?? DateTime.Now.ToString("yyyy-MM-dd, HH:mm"));
                        cmd.ExecuteNonQuery();
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd importu tou_config: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Zaimportowano touConfigsBase.json → tou_configs: {imported}/{configs.Count} wpisów.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Błąd importu touConfigsBase.json: {ex.Message}");
            }
        }

        #endregion

    }
}
