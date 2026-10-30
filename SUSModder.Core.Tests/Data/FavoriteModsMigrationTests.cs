using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Testy migracji schematu dla tabeli ulubionych modów (3.1-05, migracja v14).
/// Zakres: nowa baza, istniejąca baza w starszej wersji, powtórne zastosowanie
/// migracji oraz zachowanie danych użytkownika.
/// </summary>
public class FavoriteModsMigrationTests : IDisposable
{
    /// <summary>
    /// Wersja, w której powstała tabela ulubionych (3.1-05). Bieżąca wersja schematu
    /// jest wyższa (3.1-04 dodało migrację ikon) — testy sprawdzają wartość bieżącą.
    /// </summary>
    private const int FavoritesSchemaVersion = 14;

    private const int ExpectedSchemaVersion = 15;

    private readonly string _tempDir;

    public FavoriteModsMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_CreatesFavoritesTable_AndSetsLatestSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "new.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(GetUserVersion(db) >= FavoritesSchemaVersion);
        Assert.True(TableExists(db, "favorite_mods"));

        // Domyślnie zbiór pusty – brak ulubionych to prawidłowy stan.
        var repo = new FavoriteModsRepository(db);
        Assert.True(repo.GetSnapshot().IsLoaded);
        Assert.Empty(repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task ExistingDatabase_AppliesFavoritesMigration_WithoutLosingData()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v12.db");
        CreateLegacyDatabase(dbPath, userVersion: 12);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(TableExists(db, "favorite_mods"));

        // Dane istniejącej bazy pozostają nietknięte.
        Assert.Equal(1, CountMods(db, 10));

        var repo = new FavoriteModsRepository(db);
        Assert.Empty(repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task ExistingDatabase_AppliesFavoritesMigration_FromPreviousLatestVersion()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v13.db");
        CreateLegacyDatabase(dbPath, userVersion: 13);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(TableExists(db, "favorite_mods"));
    }

    [Fact]
    public async Task RepeatedInitialization_IsIdempotent_AndKeepsFavorites()
    {
        var dbPath = Path.Combine(_tempDir, "repeat.db");

        await using (var first = new DatabaseService(dbPath))
        {
            await first.InitializeAsync();
            new FavoriteModsRepository(first).AddFavorite(42);
        }

        await using var second = new DatabaseService(dbPath);
        await second.InitializeAsync();
        await second.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(second));
        Assert.True(TableExists(second, "favorite_mods"));

        var repo = new FavoriteModsRepository(second);
        Assert.True(repo.IsFavorite(42));
        Assert.Equal(new[] { 42 }, repo.GetFavoriteModIds());
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
    public async Task FavoritesTable_HasNoForeignKeyToCatalog_SoUnknownIdsArePreserved()
    {
        var dbPath = Path.Combine(_tempDir, "no-fk.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.False(HasForeignKeyToMods(db, "favorite_mods"));

        var repo = new FavoriteModsRepository(db);
        repo.AddFavorite(987654);

        Assert.True(repo.IsFavorite(987654));
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
            // best-effort cleanup; test assertions should not depend on temp deletion
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

            INSERT INTO user_settings (id, mode) VALUES (1, 'steam');
            INSERT INTO mods (Id, ModName, ModType) VALUES (10, 'Town of Us', 'full');";

        cmd.ExecuteNonQuery();

        // PRAGMA nie przyjmuje parametrów – wartość to kontrolowana liczba całkowita.
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

    private static bool HasForeignKeyToMods(DatabaseService db, string tableName)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_list(@table) WHERE \"table\" = 'mods';";
        cmd.Parameters.AddWithValue("@table", tableName);
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
