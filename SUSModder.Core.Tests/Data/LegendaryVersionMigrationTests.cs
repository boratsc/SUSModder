using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Migracja v18: wybór wersji legendary (pinned / ręczny) w user_settings.
/// </summary>
public class LegendaryVersionMigrationTests : IDisposable
{
    private const int ExpectedSchemaVersion = 18;

    private readonly string _tempDir;

    public LegendaryVersionMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_HasLegendaryColumns_AndLatestSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "new.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "user_settings", "legendary_use_pinned"));
        Assert.True(ColumnExists(db, "user_settings", "legendary_release_source"));
        Assert.True(ColumnExists(db, "user_settings", "legendary_release_tag"));
        Assert.True(ColumnExists(db, "user_settings", "legendary_release_asset"));
        Assert.True(ColumnExists(db, "user_settings", "legendary_release_sha256"));
    }

    [Fact]
    public async Task ExistingV17Database_GainsLegendaryColumns_WithoutLosingSettings()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v17.db");
        CreateLegacyV17Database(dbPath);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "user_settings", "legendary_use_pinned"));
        Assert.Equal("steam", GetUserSetting(db, "mode"));
    }

    [Fact]
    public async Task UserSettingsRepository_RoundTripsLegendarySelection()
    {
        var dbPath = Path.Combine(_tempDir, "roundtrip.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var repo = new UserSettingsRepository(db);
        var settings = repo.LoadSettings();
        settings.LegendaryUsePinned = false;
        settings.LegendaryReleaseSource = "Heroic";
        settings.LegendaryReleaseTag = "0.20.41";
        settings.LegendaryReleaseAsset = "legendary_windows_x86_64.exe";
        settings.LegendaryReleaseSha256 = "b8866cca30d1d66a9d1331d6f38ec8249073bd749b871821ef9c20e70ce7d263";
        repo.SaveSettings(settings);

        var reloaded = new UserSettingsRepository(db).LoadSettings();
        Assert.False(reloaded.LegendaryUsePinned);
        Assert.Equal("Heroic", reloaded.LegendaryReleaseSource);
        Assert.Equal("0.20.41", reloaded.LegendaryReleaseTag);
        Assert.Equal("legendary_windows_x86_64.exe", reloaded.LegendaryReleaseAsset);
        Assert.Equal(
            "b8866cca30d1d66a9d1331d6f38ec8249073bd749b871821ef9c20e70ce7d263",
            reloaded.LegendaryReleaseSha256);
    }

    [Fact]
    public async Task UserSettingsRepository_DefaultsToPinnedMode()
    {
        var dbPath = Path.Combine(_tempDir, "defaults.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var settings = new UserSettingsRepository(db).LoadSettings();
        Assert.True(settings.LegendaryUsePinned);
        Assert.Equal(string.Empty, settings.LegendaryReleaseSource);
        Assert.Equal(string.Empty, settings.LegendaryReleaseTag);
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

    private static void CreateLegacyV17Database(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE mods (
                Id INTEGER PRIMARY KEY,
                ModName TEXT NOT NULL,
                ModType TEXT NOT NULL
            );

            CREATE TABLE user_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                mode TEXT NOT NULL DEFAULT ''
            );

            INSERT INTO user_settings (id, mode) VALUES (1, 'steam');";
        cmd.ExecuteNonQuery();

        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 17;";
        versionCmd.ExecuteNonQuery();
    }

    private static int GetUserVersion(DatabaseService db)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool ColumnExists(DatabaseService db, string table, string column)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @column;";
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string? GetUserSetting(DatabaseService db, string column)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM user_settings WHERE id = 1;";
        return cmd.ExecuteScalar()?.ToString();
    }
}
