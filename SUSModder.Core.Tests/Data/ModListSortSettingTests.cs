using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Data;

/// <summary>K2: trwałe ustawienie sortowania listy modów (mod_list_sort, migracja v22).</summary>
public class ModListSortSettingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));

    public ModListSortSettingTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task NewDatabase_HasSortColumnAndDefault()
    {
        await using var db = NewDb("new.db");
        await db.InitializeAsync();
        Assert.True(ColumnExists(db));
        Assert.Equal("PopularityDesc", new UserSettingsRepository(db).LoadSettings().ModListSort);
    }

    [Fact]
    public async Task ExistingV21Database_GainsColumnAndPreservesSettings()
    {
        var path = Path.Combine(_tempDir, "old.db");
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE mods (Id INTEGER PRIMARY KEY, ModName TEXT NOT NULL, ModType TEXT NOT NULL); CREATE TABLE user_settings (id INTEGER PRIMARY KEY CHECK(id=1), mode TEXT NOT NULL DEFAULT ''); INSERT INTO user_settings VALUES (1, 'steam'); PRAGMA user_version=21;";
            cmd.ExecuteNonQuery();
        }
        await using var db = new DatabaseService(path);
        await db.InitializeAsync();
        Assert.True(ColumnExists(db));
        Assert.Equal("steam", ReadMode(db));
        Assert.Equal("PopularityDesc", ReadSort(db));
    }

    [Fact]
    public async Task Repository_RoundTripsSortOption()
    {
        await using var db = NewDb("roundtrip.db");
        await db.InitializeAsync();
        var repo = new UserSettingsRepository(db);
        var settings = repo.LoadSettings();
        settings.ModListSort = "NameDesc";
        repo.SaveSettings(settings);
        Assert.Equal("NameDesc", new UserSettingsRepository(db).LoadSettings().ModListSort);
        repo.UpdateSetting("mod_list_sort", "NameAsc");
        Assert.Equal("NameAsc", new UserSettingsRepository(db).LoadSettings().ModListSort);
    }

    [Fact]
    public async Task Service_UpdateUserSetting_PersistsSortOption()
    {
        await using var db = NewDb("service.db");
        await db.InitializeAsync();
        var service = new UserSettingsService(new UserSettingsRepository(db));
        Assert.Equal("PopularityDesc", service.LoadUserSettings().ModListSort);
        service.UpdateUserSetting(s => s.ModListSort = "PopularityAsc");
        Assert.Equal("PopularityAsc", new UserSettingsService(new UserSettingsRepository(db)).LoadUserSettings().ModListSort);
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
        cmd.Parameters.AddWithValue("@name", "mod_list_sort");
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string? ReadMode(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT mode FROM user_settings WHERE id=1";
        return cmd.ExecuteScalar()?.ToString();
    }

    private static string? ReadSort(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT mod_list_sort FROM user_settings WHERE id=1";
        return cmd.ExecuteScalar()?.ToString();
    }
}