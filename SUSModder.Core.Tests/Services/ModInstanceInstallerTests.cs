using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModInstanceInstallerTests : IDisposable
{
    private readonly string _tempDir;

    public ModInstanceInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task InstallFullModInstance_CreatesRepositoryRowAndInstallationMapV2()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var fakeFullInstaller = new FakeFullModInstaller();
        var service = new ModInstanceInstaller(repo, fakeFullInstaller);
        var requestedPath = Path.Combine(_tempDir, "ToU - friends");
        var fullMod = CreateFullMod();

        var instance = await service.InstallFullModInstanceAsync(
            fullMod,
            "ToU - friends",
            "steam",
            requestedInstallPath: requestedPath,
            notes: "Friday lobby");

        var stored = repo.GetInstance(instance.InstanceId);
        var map = await InstallationMapManager.LoadInstallationMapAsync(requestedPath);

        Assert.NotNull(stored);
        Assert.Equal("ToU - friends", stored.DisplayName);
        Assert.Equal(10, stored.BaseModId);
        Assert.Equal(requestedPath, stored.InstallPath);
        Assert.Null(fullMod.InstallPath);
        Assert.Equal(requestedPath, fakeFullInstaller.LastTargetPath);
        Assert.NotNull(map);
        Assert.Equal("2.0", map.Version);
        Assert.Equal(instance.InstanceId, map.InstanceId);
        Assert.Equal("ToU - friends", map.DisplayName);
        Assert.Equal("manual", map.Origin);
        Assert.Equal("steam", map.Platform);
        Assert.Equal("Friday lobby", map.Metadata.Notes);
        Assert.Equal(10, map.FullMod.ModId);
        Assert.Equal(requestedPath, map.FullMod.InstallPath);
    }

    [Fact]
    public async Task InstallFullModInstance_RejectsNonEmptyRequestedDirectory()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var service = new ModInstanceInstaller(repo, new FakeFullModInstaller());
        var requestedPath = Path.Combine(_tempDir, "existing");
        Directory.CreateDirectory(requestedPath);
        File.WriteAllText(Path.Combine(requestedPath, "keep.txt"), "do not overwrite");

        var ex = await Assert.ThrowsAsync<IOException>(() => service.InstallFullModInstanceAsync(
            CreateFullMod(),
            "Existing",
            "steam",
            requestedInstallPath: requestedPath));

        Assert.Equal("mod_instance_target_not_empty", ex.Message);
        Assert.Empty(repo.GetAllInstances());
    }

    [Fact]
    public async Task InstallDllToInstance_UsesSelectedInstancePathAndStoresDllRow()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var installPath = Path.Combine(_tempDir, "instance-a");
        Directory.CreateDirectory(installPath);
        var instance = CreateInstance("instance-a", installPath);
        repo.AddInstance(instance);
        await InstallationMapManager.SaveInstallationMapAsync(installPath, new InstallationMap
        {
            Version = "2.0",
            InstanceId = instance.InstanceId,
            DisplayName = instance.DisplayName,
            Platform = "steam",
            FullMod = new FullModInstallation
            {
                ModId = instance.BaseModId,
                ModName = instance.BaseModName,
                InstallPath = installPath
            }
        });

        var fakeDllInstaller = new FakeDllInstaller();
        var service = new ModInstanceInstaller(repo, new FakeFullModInstaller(), fakeDllInstaller);
        var dllMod = CreateDllMod();

        var dll = await service.InstallDllToInstanceAsync(dllMod, instance.InstanceId, "steam");

        Assert.Equal(instance.InstallPath, fakeDllInstaller.LastTargetMod?.InstallPath);
        Assert.Equal("steam", fakeDllInstaller.LastPlatform);
        Assert.Equal(20, dll.DllModId);
        Assert.Equal("AleLuduMod", dll.DllName);
        Assert.Equal(Path.Combine("BepInEx", "plugins", "AleLuduMod.dll"), dll.InstalledPath);
        Assert.Single(repo.GetDlls(instance.InstanceId));
    }

    [Fact]
    public async Task CloneInstance_CreatesSeparateFolderAndHonorsCopyOptions()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var sourcePath = Path.Combine(_tempDir, "source-pack");
        var pluginsDir = Path.Combine(sourcePath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsDir);
        var dllPath = Path.Combine(pluginsDir, "Extra.dll");
        await File.WriteAllTextAsync(dllPath, "dll");
        var integrationPath = Path.Combine(pluginsDir, "integration.dll");
        await File.WriteAllTextAsync(integrationPath, "integration");

        var source = CreateInstance("source-id", sourcePath);
        source.DisplayName = "ToU - source";
        repo.AddInstance(source);
        repo.AddDll(new ModInstanceDll
        {
            InstanceId = source.InstanceId,
            DllModId = 20,
            DllName = "Extra",
            InstalledPath = Path.Combine("BepInEx", "plugins", "Extra.dll")
        });
        await InstallationMapManager.SaveInstallationMapAsync(sourcePath, new InstallationMap
        {
            Version = "2.0",
            InstanceId = source.InstanceId,
            DisplayName = source.DisplayName,
            InstalledDlls =
            {
                new DllModInstallation { ModId = 20, ModName = "Extra", InstallPath = "BepInEx/plugins/Extra.dll" }
            }
        });

        var service = new ModInstanceInstaller(repo, new FakeFullModInstaller());
        var clone = await service.CloneInstanceAsync(
            source.InstanceId,
            new ModInstanceCloneOptions
            {
                NewDisplayName = "ToU - copy",
                CopyDlls = false,
                CopyIntegrationDll = false,
                CopyTouConfig = false,
                CopyPinnedVersion = false
            });

        Assert.NotEqual(source.InstanceId, clone.InstanceId);
        Assert.Equal("clone", clone.Origin);
        Assert.NotEqual(sourcePath, clone.InstallPath);
        Assert.True(Directory.Exists(clone.InstallPath));
        Assert.False(File.Exists(Path.Combine(clone.InstallPath, "BepInEx", "plugins", "Extra.dll")));
        Assert.False(File.Exists(Path.Combine(clone.InstallPath, "BepInEx", "plugins", "integration.dll")));
        Assert.Empty(repo.GetDlls(clone.InstanceId));
        Assert.Equal(2, repo.GetAllInstances().Count);

        var cloneMap = await InstallationMapManager.LoadInstallationMapAsync(clone.InstallPath);
        Assert.Equal(clone.InstanceId, cloneMap?.InstanceId);
        Assert.Empty(cloneMap?.InstalledDlls ?? []);
    }

    [Fact]
    public async Task RenameInstance_UpdatesRepositoryAndInstallationMapMetadata()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var installPath = Path.Combine(_tempDir, "instance-rename");
        Directory.CreateDirectory(installPath);
        var instance = CreateInstance("instance-rename", installPath);
        repo.AddInstance(instance);
        await InstallationMapManager.SaveInstallationMapAsync(installPath, new InstallationMap
        {
            Version = "2.0",
            InstanceId = instance.InstanceId,
            DisplayName = instance.DisplayName
        });
        var service = new ModInstanceInstaller(repo, new FakeFullModInstaller());

        await service.RenameInstanceAsync(instance.InstanceId, "ToU - renamed");

        var stored = repo.GetInstance(instance.InstanceId);
        var map = await InstallationMapManager.LoadInstallationMapAsync(installPath);
        Assert.Equal("ToU - renamed", stored?.DisplayName);
        Assert.Equal("ToU - renamed", map?.DisplayName);
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

    private async Task<DatabaseService> CreateInitializedDatabaseAsync()
    {
        var db = new DatabaseService(Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }

    private static ModConfiguration CreateFullMod()
    {
        return new ModConfiguration
        {
            Id = 10,
            ModName = "Town of Us",
            ModType = "full",
            ModVersion = "5.4.0",
            AmongVersion = "2024.6",
            GitHubRepoOrLink = "https://example.test/tou.zip"
        };
    }

    private static ModConfiguration CreateDllMod()
    {
        return new ModConfiguration
        {
            Id = 20,
            ModName = "AleLuduMod",
            ModType = "dll",
            ModVersion = "2.0",
            DllInstallPath = Path.Combine("BepInEx", "plugins"),
            GitHubRepoOrLink = "https://example.test/aleludu.dll"
        };
    }

    private static ModInstance CreateInstance(string id, string installPath)
    {
        return new ModInstance
        {
            InstanceId = id,
            DisplayName = "ToU - instance",
            BaseModId = 10,
            BaseModName = "Town of Us",
            FullModVersion = "5.4.0",
            AmongVersion = "2024.6",
            Platform = "steam",
            InstallPath = installPath,
            Origin = "manual"
        };
    }

    private sealed class FakeFullModInstaller : IFullModInstanceInstaller
    {
        public string? LastTargetPath { get; private set; }

        public Task InstallAsync(
            ModConfiguration modConfig,
            string targetInstallPath,
            string platform,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            ModManagerUserCallbacks userCallbacks,
            Action<string>? onSpeedUpdate = null)
        {
            LastTargetPath = targetInstallPath;
            Directory.CreateDirectory(targetInstallPath);
            File.WriteAllText(Path.Combine(targetInstallPath, "Among Us.exe"), string.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDllInstaller : IDllModInstanceInstaller
    {
        public ModConfiguration? LastTargetMod { get; private set; }
        public string? LastPlatform { get; private set; }

        public Task<string?> InstallAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            LastTargetMod = targetMod;
            LastPlatform = platform;
            var targetDir = Path.Combine(targetMod.InstallPath!, dllMod.DllInstallPath ?? string.Empty);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, $"{dllMod.ModName}.dll");
            File.WriteAllText(targetPath, string.Empty);
            return Task.FromResult<string?>(targetPath);
        }
    }
}
