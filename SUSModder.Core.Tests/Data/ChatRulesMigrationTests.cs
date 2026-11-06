using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Migracja v17: znacznik zapoznania się z zasadami czatu w user_settings.
/// </summary>
public class ChatRulesMigrationTests : IDisposable
{
    private const int ExpectedSchemaVersion = 18;

    private readonly string _tempDir;

    public ChatRulesMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_HasChatRulesColumn_AndLatestSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "new.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "user_settings", "chat_rules_acknowledged_version"));
    }

    [Fact]
    public async Task ExistingV16Database_GainsChatRulesColumn_WithoutLosingSettings()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v16.db");
        CreateLegacyV16Database(dbPath);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "user_settings", "chat_rules_acknowledged_version"));
        Assert.Equal("steam", GetUserSetting(db, "mode"));
    }

    [Fact]
    public async Task UserSettingsRepository_RoundTripsChatRulesVersion()
    {
        var dbPath = Path.Combine(_tempDir, "roundtrip.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var repo = new UserSettingsRepository(db);
        var settings = repo.LoadSettings();
        settings.ChatRulesAcknowledgedVersion = 3;
        repo.SaveSettings(settings);

        var reloaded = new UserSettingsRepository(db).LoadSettings();
        Assert.Equal(3, reloaded.ChatRulesAcknowledgedVersion);
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

    private static void CreateLegacyV16Database(string dbPath)
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
        versionCmd.CommandText = "PRAGMA user_version = 16;";
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
