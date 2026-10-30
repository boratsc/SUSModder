using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Testy migracji schematu dla przypisań ikon (3.1-04, migracja v15 — po v14 ulubionych).
/// Zakres: nowa baza, istniejąca baza w starszej wersji, powtórne zastosowanie
/// migracji oraz zachowanie danych użytkownika.
/// </summary>
public class IconAssignmentMigrationTests : IDisposable
{
    private const int ExpectedSchemaVersion = 15;

    private readonly string _tempDir;

    public IconAssignmentMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_CreatesIconAssignmentsTable_AndSetsLatestSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "new.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(TableExists(db, "icon_assignments"));
        Assert.True(ColumnExists(db, "sync_state", "schema_version"));
        Assert.True(ColumnExists(db, "icon_assignments", "is_removed"));
    }

    [Fact]
    public async Task ExistingDatabase_AppliesIconMigration_WithoutLosingData()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v14.db");
        CreateLegacyDatabase(dbPath, userVersion: 14);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(TableExists(db, "icon_assignments"));
        Assert.True(ColumnExists(db, "sync_state", "schema_version"));

        // Dane istniejącej bazy pozostają nietknięte.
        Assert.Equal(1, CountMods(db, 10));
    }

    [Fact]
    public async Task RepeatedInitialization_IsIdempotent_AndKeepsAssignments()
    {
        var dbPath = Path.Combine(_tempDir, "repeat.db");

        await using (var first = new DatabaseService(dbPath))
        {
            await first.InitializeAsync();
            new IconAssignmentRepository(first).Upsert(new SUSModder.Core.Models.IconAssignment
            {
                AssignmentKey = "mod:42",
                AssetId = "icon_mod",
                Url = "https://susmodder.app/assets/icons/mod.png",
                Sha256 = new string('a', 64),
                ContentType = "image/png",
                SizeBytes = 1024,
                SchemaVersion = 1,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await using var second = new DatabaseService(dbPath);
        await second.InitializeAsync();
        await second.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(second));

        var repo = new IconAssignmentRepository(second);
        Assert.Equal("icon_mod", repo.GetForMod(42)!.AssetId);
    }

    [Fact]
    public async Task MigrationIsMonotonic_SchemaVersionNeverDecreases()
    {
        var dbPath = Path.Combine(_tempDir, "monotonic.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();
        var initial = GetUserVersion(db);

        await db.InitializeAsync();
        var afterSecondRun = GetUserVersion(db);

        Assert.Equal(ExpectedSchemaVersion, initial);
        Assert.True(afterSecondRun >= initial, $"Wersja schematu zmniejszyła się: {initial} -> {afterSecondRun}");
    }

    [Fact]
    public async Task FavoritesTableFromPreviousMigration_RemainsIntact()
    {
        var dbPath = Path.Combine(_tempDir, "favorites-kept.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var favorites = new FavoriteModsRepository(db);
        favorites.AddFavorite(42);

        Assert.True(TableExists(db, "favorite_mods"));
        Assert.True(favorites.IsFavorite(42));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static void CreateLegacyDatabase(string dbPath, int userVersion)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE mods (
                Id INTEGER PRIMARY KEY,
                ModName TEXT NOT NULL,
                InstallPath TEXT,
                ModType TEXT NOT NULL,
                ModVersion TEXT NOT NULL DEFAULT '',
                AmongVersion TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE user_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                mode TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE sync_state (
                key TEXT PRIMARY KEY,
                etag TEXT NULL,
                last_modified TEXT NULL,
                last_success_utc TEXT NULL,
                last_attempt_utc TEXT NULL,
                last_error_code TEXT NULL,
                failure_count INTEGER NOT NULL DEFAULT 0,
                next_allowed_attempt_utc TEXT NULL
            );

            INSERT INTO user_settings (id, mode) VALUES (1, 'steam');
            INSERT INTO mods (Id, ModName, ModType) VALUES (10, 'Town of Us', 'full');";
        cmd.ExecuteNonQuery();

        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = $"PRAGMA user_version = {userVersion};";
        versionCmd.ExecuteNonQuery();
    }

    private static int GetUserVersion(DatabaseService db)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool TableExists(DatabaseService db, string tableName)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        cmd.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static bool ColumnExists(DatabaseService db, string table, string column)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static int CountMods(DatabaseService db, int modId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE Id = @mod_id;";
        cmd.Parameters.AddWithValue("@mod_id", modId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
