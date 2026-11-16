using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Data;

public class ChatReadWatermarkMigrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));

    public ChatReadWatermarkMigrationTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task NewDatabase_HasWatermarkColumnAndDefault()
    {
        await using var db = NewDb("new.db");
        await db.InitializeAsync();
        Assert.True(ColumnExists(db));
        Assert.Equal(0, new UserSettingsRepository(db).LoadSettings().ChatReadWatermarkUnixMs);
    }

    [Fact]
    public async Task ExistingV18Database_GainsColumnAndPreservesSettings()
    {
        var path = Path.Combine(_tempDir, "old.db");
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE mods (Id INTEGER PRIMARY KEY, ModName TEXT NOT NULL, ModType TEXT NOT NULL); CREATE TABLE user_settings (id INTEGER PRIMARY KEY CHECK(id=1), mode TEXT NOT NULL DEFAULT ''); INSERT INTO user_settings VALUES (1, 'steam'); PRAGMA user_version=18;";
            cmd.ExecuteNonQuery();
        }
        await using var db = new DatabaseService(path);
        await db.InitializeAsync();
        Assert.True(ColumnExists(db));
        Assert.Equal("steam", ReadMode(db));
    }

    [Fact]
    public async Task Repository_RoundTripsWatermark()
    {
        await using var db = NewDb("roundtrip.db");
        await db.InitializeAsync();
        var repo = new UserSettingsRepository(db);
        var settings = repo.LoadSettings();
        settings.ChatReadWatermarkUnixMs = 1735689600123;
        repo.SaveSettings(settings);
        Assert.Equal(1735689600123, new UserSettingsRepository(db).LoadSettings().ChatReadWatermarkUnixMs);
    }

    [Fact]
    public void Store_ReturnsZeroWhenSettingsReadFails()
    {
        var store = new UserSettingsChatReadStateStore(new UserSettingsService());
        Assert.Equal(0, store.GetReadWatermark());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private DatabaseService NewDb(string file) => new(Path.Combine(_tempDir, file));

    private static bool ColumnExists(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('user_settings') WHERE name=@name";
        cmd.Parameters.AddWithValue("@name", "chat_read_watermark_unix_ms");
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string? ReadMode(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT mode FROM user_settings WHERE id=1";
        return cmd.ExecuteScalar()?.ToString();
    }
}
