using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModPackInstallerInstallAsNewInstanceTests : IDisposable
{
    private readonly string _tempDir;

    public ModPackInstallerInstallAsNewInstanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task InstallPack_AsNewInstance_CreatesInstanceDllRowsAndTouSnapshot()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var modRepo = new ModRepository(db);
        ConfigManager.SetRepository(modRepo);
        modRepo.SaveAllMods(new List<ModConfiguration>
        {
            ModInstanceInstallerTestsHelpers.CreateFullMod(),
            ModInstanceInstallerTestsHelpers.CreateDllMod()
        });

        var instanceRepo = new ModInstanceRepository(db);
        var fakeFull = new ModInstanceInstallerTestsHelpers.FakeFullModInstaller();
        var fakeDll = new ModInstanceInstallerTestsHelpers.FakeDllInstaller();
        var instanceInstaller = new ModInstanceInstaller(instanceRepo, fakeFull, fakeDll);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Configuration:BaseUrl"] = "https://susmodder.app/",
                ["Configuration:ModPacksEndpoint"] = "/api/mod-packs"
            })
            .Build();

        var configService = new ConfigService();
        var log = new TestDiagnosticsOutput();
        var dllService = new DllModificationService(configService, log);
        var installer = new ModPackInstaller(
            configuration,
            configService,
            dllService,
            log,
            instanceInstaller,
            instanceRepo);

        var pack = new ModPack
        {
            PackCode = "TEST-CODE-1234",
            ModName = "ToU - pack test",
            FullMod = new ModPackFullMod { Id = 10, Version = "5.5.0" },
            DllMods = new[]
            {
                new ModPackDllMod { DllModId = 20, DllModVersion = "2.0" }
            },
            TouConfig = JsonDocument.Parse("{\"roles\":1}").RootElement
        };

        var result = await installer.InstallPackAsync(pack, "steam", displayName: "ToU - shared");

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.InstanceId));

        var stored = instanceRepo.GetInstance(result.InstanceId!);
        Assert.NotNull(stored);
        Assert.Equal("ToU - shared", stored.DisplayName);
        Assert.Equal("shared_pack", stored.Origin);
        Assert.Equal("TEST-CODE-1234", stored.SourcePackCode);
        Assert.Contains("Town of Us", result.InstalledMods);
        Assert.Contains("AleLuduMod", result.InstalledMods);
        Assert.Contains("ToU config", result.InstalledMods);
        Assert.Single(instanceRepo.GetDlls(result.InstanceId!));
        Assert.Contains(
            instanceRepo.GetConfigs(result.InstanceId!),
            c => string.Equals(c.ConfigType, "tou", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(fakeFull.LastTargetPath);
        Assert.Equal(stored.InstallPath, fakeFull.LastTargetPath);
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
}

/// <summary>
/// Współdzielone buildery i fake installery z ModInstanceInstallerTests.
/// </summary>
internal static class ModInstanceInstallerTestsHelpers
{
    public static ModConfiguration CreateFullMod() =>
        new()
        {
            Id = 10,
            ModName = "Town of Us",
            ModType = "full",
            ModVersion = "5.5.0",
            AmongVersion = "2024.6",
            GitHubRepoOrLink = "https://example.test/tou.zip"
        };

    public static ModConfiguration CreateDllMod() =>
        new()
        {
            Id = 20,
            ModName = "AleLuduMod",
            ModType = "dll",
            ModVersion = "2.0",
            DllInstallPath = Path.Combine("BepInEx", "plugins"),
            GitHubRepoOrLink = "https://example.test/aleludu.dll"
        };

    public sealed class FakeFullModInstaller : IFullModInstanceInstaller
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

    public sealed class FakeDllInstaller : IDllModInstanceInstaller
    {
        public ModConfiguration? LastTargetMod { get; private set; }
        public int InstallCallCount { get; private set; }

        public Task<string?> InstallAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
        {
            InstallCallCount++;
            LastTargetMod = targetMod;
            var targetDir = Path.Combine(targetMod.InstallPath!, dllMod.DllInstallPath ?? string.Empty);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, $"{dllMod.ModName}.dll");
            File.WriteAllText(targetPath, string.Empty);
            return Task.FromResult<string?>(targetPath);
        }
    }
}
