using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;

namespace SUSModder.Core.Tests.Services;

public sealed class SqliteDependencyMigrationTests
{
    [Fact]
    public void BundledNativeSqlite_IsPatchedAndPreservesWalDataAcrossReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SUSModder.SqliteTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "migration.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var connection = db.GetConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT sqlite_version()";
                var nativeVersion = Version.Parse((string)command.ExecuteScalar()!);
                // CVE-2025-6965 affects native SQLite versions below 3.50.2.
                Assert.True(nativeVersion >= new Version(3, 50, 2), $"Bundled SQLite is vulnerable: {nativeVersion}");
                command.CommandText = "PRAGMA journal_mode";
                Assert.Equal("wal", (string)command.ExecuteScalar()!);
                command.CommandText = "CREATE TABLE migration_check (value TEXT NOT NULL)";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO migration_check (value) VALUES (@value)";
                command.Parameters.AddWithValue("@value", "PL/EN: Zażółć — settings survive reopen");
                command.ExecuteNonQuery();
            }

            using (var reopened = new DatabaseService(path))
            {
                using var command = reopened.GetConnection().CreateCommand();
                command.CommandText = "SELECT value FROM migration_check";
                Assert.Equal("PL/EN: Zażółć — settings survive reopen", (string)command.ExecuteScalar()!);
            }
        }
        finally
        {
            // Release only this test database's pooled connection before deleting it.
            using var poolKey = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            SqliteConnection.ClearPool(poolKey);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
