using Microsoft.Data.Sqlite;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Data;

public sealed class EnvironmentCheckPromptMigrationTests
{
    [Fact]
    public async Task RepositoryRoundTripsPromptVersionAndWhitelistUpdate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"environment-check-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new DatabaseService(path);
            await db.InitializeAsync();
            var repo = new UserSettingsRepository(db);
            Assert.Equal(0, repo.LoadSettings().EnvironmentCheckPromptVersion);
            var settings = repo.LoadSettings();
            settings.EnvironmentCheckPromptVersion = 7;
            repo.SaveSettings(settings);
            Assert.Equal(7, new UserSettingsRepository(db).LoadSettings().EnvironmentCheckPromptVersion);
            repo.UpdateSetting("environment_check_prompt_version", 3);
            Assert.Equal(3, new UserSettingsRepository(db).LoadSettings().EnvironmentCheckPromptVersion);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task MigratesV19WithoutChangingExistingSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"environment-check-v19-{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE user_settings (id INTEGER PRIMARY KEY CHECK(id=1), mode TEXT NOT NULL DEFAULT '');
                    INSERT INTO user_settings (id, mode) VALUES (1, 'steam');
                    PRAGMA user_version=19;";
                cmd.ExecuteNonQuery();
            }
            await using var db = new DatabaseService(path);
            await db.InitializeAsync();
            using var verify = db.GetConnection().CreateCommand();
            verify.CommandText = "SELECT mode, environment_check_prompt_version FROM user_settings WHERE id=1;";
            using (var reader = verify.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal("steam", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1));
            }
            using var version = db.GetConnection().CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(20L, (long)version.ExecuteScalar()!);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
