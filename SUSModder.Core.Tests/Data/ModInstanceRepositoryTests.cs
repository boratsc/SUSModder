using System.Text.Json;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Data;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Data;

public class ModInstanceRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public ModInstanceRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Repository_AllowsTwoInstancesForSameBaseMod()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);

        repo.AddInstance(CreateInstance("pack-a", "ToU - piątek", Path.Combine(_tempDir, "ToU-a")));
        repo.AddInstance(CreateInstance("pack-b", "ToU - test", Path.Combine(_tempDir, "ToU-b")));

        var instances = repo.GetInstancesForBaseMod(10);

        Assert.Equal(2, instances.Count);
        Assert.Contains(instances, i => i.InstanceId == "pack-a" && i.DisplayName == "ToU - piątek");
        Assert.Contains(instances, i => i.InstanceId == "pack-b" && i.DisplayName == "ToU - test");
    }

    [Fact]
    public async Task DeleteInstance_RemovesOnlySelectedInstanceAndDependentRows()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);

        repo.AddInstance(CreateInstance("pack-a", "ToU - piątek", Path.Combine(_tempDir, "ToU-a")));
        repo.AddInstance(CreateInstance("pack-b", "ToU - test", Path.Combine(_tempDir, "ToU-b")));
        repo.AddDll(new ModInstanceDll
        {
            InstanceId = "pack-a",
            DllModId = 20,
            DllName = "AleLuduMod",
            DllVersion = "2.0",
            InstalledPath = "BepInEx/plugins/AleLuduMod.dll"
        });
        repo.AddConfig(new ModInstanceConfig
        {
            InstanceId = "pack-a",
            ConfigType = "tou",
            ConfigName = "Lobby",
            ConfigJson = "{}"
        });

        repo.DeleteInstance("pack-a");

        Assert.Null(repo.GetInstance("pack-a"));
        Assert.NotNull(repo.GetInstance("pack-b"));
        Assert.Empty(repo.GetDlls("pack-a"));
        Assert.Empty(repo.GetConfigs("pack-a"));
    }

    [Fact]
    public async Task GetPackInstances_ExcludesLegacyOrigin()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);

        repo.AddInstance(CreateInstance("pack-real", "ToU - lobby", Path.Combine(_tempDir, "pack-real")));
        repo.AddInstance(new ModInstance
        {
            InstanceId = "legacy-shadow",
            DisplayName = "Town of Us",
            BaseModId = 10,
            BaseModName = "Town of Us",
            FullModVersion = "5.4.0",
            AmongVersion = "2024.6",
            Platform = "steam",
            InstallPath = Path.Combine(_tempDir, "catalog-only"),
            Origin = "legacy"
        });

        Assert.Single(repo.GetPackInstances());
        Assert.Equal(2, repo.GetAllInstances().Count);
    }

    [Fact]
    public async Task InitializeAsync_RemovesLegacyInstancesOnMigrationV6()
    {
        var dbPath = Path.Combine(_tempDir, "legacy-cleanup.db");
        CreateLegacyVersion5DatabaseWithLegacyInstance(dbPath);

        await using var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var repo = new ModInstanceRepository(db);
        Assert.Empty(repo.GetPackInstances());
        Assert.Empty(repo.GetAllInstances());
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

    private async Task<DatabaseService> CreateInitializedDatabaseAsync()
    {
        var db = new DatabaseService(Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }

    private static ModInstance CreateInstance(string id, string name, string installPath)
    {
        return new ModInstance
        {
            InstanceId = id,
            DisplayName = name,
            BaseModId = 10,
            BaseModName = "Town of Us",
            FullModVersion = "5.4.0",
            AmongVersion = "2024.6",
            Platform = "steam",
            InstallPath = installPath,
            Origin = "manual"
        };
    }

    private static void CreateLegacyVersion5DatabaseWithLegacyInstance(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE mod_instances (
                instance_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                base_mod_id INTEGER NOT NULL,
                base_mod_name TEXT NOT NULL,
                full_mod_version TEXT NOT NULL DEFAULT '',
                among_version TEXT NOT NULL DEFAULT '',
                platform TEXT NOT NULL DEFAULT '',
                install_path TEXT NOT NULL,
                origin TEXT NOT NULL DEFAULT 'manual',
                source_pack_code TEXT,
                pinned_version TEXT,
                auto_update_enabled INTEGER NOT NULL DEFAULT 0,
                notes TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_launched_at TEXT
            );

            INSERT INTO mod_instances (
                instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                among_version, platform, install_path, origin, notes, created_at, updated_at
            ) VALUES (
                'legacy-1', 'Town of Us', 10, 'Town of Us', '5.4.0',
                '2024.6', 'steam', '/tmp/catalog', 'legacy', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'
            );

            PRAGMA user_version = 5;";
        cmd.ExecuteNonQuery();
    }

    private static void CreateLegacyVersion4Database(string dbPath, string installPath)
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
            INSERT INTO mods (Id, ModName, InstallPath, ModType, ModVersion, AmongVersion)
            VALUES (10, 'Town of Us', @install_path, 'full', '5.3.0', '2024.5');

            PRAGMA user_version = 4;";
        cmd.Parameters.AddWithValue("@install_path", installPath);
        cmd.ExecuteNonQuery();
    }

    private static void WriteInstallationMap(string installPath)
    {
        var map = new InstallationMap
        {
            Version = "1.0",
            InstalledAt = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc),
            InstalledBy = "SUSModder test",
            Platform = "epic",
            FullMod = new FullModInstallation
            {
                ModId = 10,
                ModName = "Town of Us",
                ModVersion = "5.4.0",
                AmongVersion = "2024.6",
                InstallPath = installPath,
                AutoUpdateEnabled = true,
                PinnedInstallVersion = "5.4.0"
            },
            InstalledDlls = new List<DllModInstallation>
            {
                new()
                {
                    ModId = 20,
                    ModName = "AleLuduMod",
                    ModVersion = "2.0",
                    InstallPath = "BepInEx/plugins/AleLuduMod.dll",
                    InstalledAt = new DateTime(2026, 5, 25, 12, 5, 0, DateTimeKind.Utc)
                }
            },
            Metadata = new InstallationMetadata
            {
                Notes = "Legacy notes"
            }
        };

        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(installPath, ".susmodder-install.json"), json);
    }
}
