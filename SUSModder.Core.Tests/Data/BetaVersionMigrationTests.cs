using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Testy schematu v16 i trwałości metadanych wersji beta (3.1-11).
/// Zakres: nowa baza, migracja z v15, zapis/odczyt przez repozytorium katalogu.
/// </summary>
public class BetaVersionMigrationTests : IDisposable
{
    private const int ExpectedSchemaVersion = 16;

    private readonly string _tempDir;

    public BetaVersionMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_HasBetaColumns_AndLatestSchemaVersion()
    {
        var dbPath = Path.Combine(_tempDir, "new.db");

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "mods", "IsBeta"));
        Assert.True(ColumnExists(db, "mods", "LatestStableVersion"));
        Assert.True(ColumnExists(db, "mods", "LatestBetaVersion"));
        Assert.True(ColumnExists(db, "mods", "AvailableReleases"));
    }

    [Fact]
    public async Task ExistingV15Database_GainsBetaColumns_WithoutLosingMods()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-v15.db");
        CreateLegacyV15Database(dbPath);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        Assert.Equal(ExpectedSchemaVersion, GetUserVersion(db));
        Assert.True(ColumnExists(db, "mods", "IsBeta"));
        Assert.Equal(1, CountMods(db, 10));
    }

    [Fact]
    public async Task Repository_RoundTripsBetaMetadataAndReleases()
    {
        var dbPath = Path.Combine(_tempDir, "roundtrip.db");
        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var repo = new ModRepository(db, CreateApiClient());
        repo.SaveAllMods(new List<ModConfiguration>
        {
            new()
            {
                Id = 13,
                ModName = "Town of Us Mira",
                ModType = "full",
                ModVersion = "1.7.3",
                AmongVersion = "2026-8-18",
                IsBeta = false,
                LatestStableVersion = "1.7.3",
                LatestBetaVersion = "1.6.3-beta2",
                AvailableReleases = new List<ModRelease>
                {
                    new() { ReleaseId = "v208", Version = "1.7.3", IsCurrent = true, AmongVersion = "2026-8-18" },
                    new()
                    {
                        ReleaseId = "v178",
                        Version = "1.6.3-beta2",
                        IsBeta = true,
                        AmongVersion = "2026-3-31",
                        Variants = new List<ModReleaseVariant>
                        {
                            new() { Platform = "steam", Architecture = "x86", Version = "1.6.3-beta2" }
                        }
                    }
                }
            }
        });

        // Nowa instancja repozytorium czyta wyłącznie z SQLite.
        var reopened = new ModRepository(db, CreateApiClient());
        var mod = Assert.Single(reopened.GetAllMods());

        Assert.Equal("1.6.3-beta2", mod.LatestBetaVersion);
        Assert.Equal("1.7.3", mod.LatestStableVersion);
        Assert.False(mod.IsBeta);
        Assert.Equal(2, mod.AvailableReleases.Count);

        var beta = mod.AvailableReleases.Single(r => r.IsBeta);
        Assert.Equal("v178", beta.ReleaseId);
        Assert.Equal("2026-3-31", beta.AmongVersion);
        Assert.Equal("steam", Assert.Single(beta.Variants).Platform);
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

    private static SUSModderApiClient CreateApiClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:ApiV2BaseUrl"] = "https://api.invalid/v2",
                ["Configuration:BaseUrl"] = "https://api.invalid"
            })
            .Build();

        return new SUSModderApiClient(configuration, new NullDiagnostics());
    }

    private static void CreateLegacyV15Database(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE mods (
                Id INTEGER PRIMARY KEY,
                ModName TEXT NOT NULL,
                PngFileName TEXT NOT NULL DEFAULT '',
                InstallPath TEXT,
                GitHubRepoOrLink TEXT NOT NULL DEFAULT '',
                EpicGitHubRepoOrLink TEXT,
                ModType TEXT NOT NULL,
                DllInstallPath TEXT,
                ModVersion TEXT NOT NULL DEFAULT '',
                AmongVersion TEXT NOT NULL DEFAULT '',
                LastUpdated TEXT,
                Description TEXT NOT NULL DEFAULT '',
                HasRoles INTEGER,
                LobbyRegionBaseUrl TEXT,
                SupportsLobbySharing INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE user_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                mode TEXT NOT NULL DEFAULT ''
            );

            INSERT INTO user_settings (id, mode) VALUES (1, 'steam');
            INSERT INTO mods (Id, ModName, ModType) VALUES (10, 'Town of Us', 'full');";
        cmd.ExecuteNonQuery();

        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version = 15;";
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

    private static int CountMods(DatabaseService db, int modId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", modId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private sealed class NullDiagnostics : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }
}
