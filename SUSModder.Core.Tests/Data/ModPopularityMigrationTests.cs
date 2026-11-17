using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

public class ModPopularityMigrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));

    public ModPopularityMigrationTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task MigrationAndRepository_RoundTripPopularity()
    {
        var path = Path.Combine(_tempDir, "legacy.db");
        CreateLegacyDatabase(path);
        await using var db = new DatabaseService(path);
        await db.InitializeAsync();
        Assert.Equal(23, GetUserVersion(db));
        Assert.Equal(0, GetPopularity(db));

        using var conn = db.GetConnection();
        using (var update = conn.CreateCommand())
        {
            update.CommandText = "UPDATE mods SET popularity_30d = @pop WHERE Id = @id;";
            update.Parameters.AddWithValue("@pop", 123);
            update.Parameters.AddWithValue("@id", 1);
            update.ExecuteNonQuery();
        }
        Assert.Equal(123, GetPopularity(db));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static void CreateLegacyDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE mods (Id INTEGER PRIMARY KEY, ModName TEXT NOT NULL, ModType TEXT NOT NULL);
            INSERT INTO mods VALUES (1, 'Test', 'full');
            CREATE TABLE user_settings (id INTEGER PRIMARY KEY CHECK(id=1), mode TEXT NOT NULL DEFAULT '');
            INSERT INTO user_settings VALUES (1, 'steam');
            PRAGMA user_version = 20;
            """;
        cmd.ExecuteNonQuery();
    }

    private static int GetUserVersion(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetPopularity(DatabaseService db)
    {
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT popularity_30d FROM mods WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", 1);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
