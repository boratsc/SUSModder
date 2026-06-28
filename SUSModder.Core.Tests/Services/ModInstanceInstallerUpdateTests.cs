using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Tests.Services;

public class ModInstanceInstallerUpdateTests : IDisposable
{
    private readonly string _tempDir;

    public ModInstanceInstallerUpdateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task UpdateInstance_ReinstallsFullModAndRestoresDllRows()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new ModInstanceRepository(db);
        var installPath = Path.Combine(_tempDir, "ToU-updated");
        Directory.CreateDirectory(installPath);
        File.WriteAllText(Path.Combine(installPath, "Among Us.exe"), "old");

        var instance = new ModInstance
        {
            InstanceId = "inst-update",
            DisplayName = "ToU - update me",
            BaseModId = 10,
            BaseModName = "Town of Us",
            FullModVersion = "5.4.0",
            AmongVersion = "2024.6",
            Platform = "steam",
            InstallPath = installPath,
            Origin = "manual",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        repo.AddInstance(instance);
        repo.AddDll(new ModInstanceDll
        {
            InstanceId = instance.InstanceId,
            DllModId = 20,
            DllName = "AleLuduMod",
            DllVersion = "2.0",
            InstalledPath = Path.Combine("BepInEx", "plugins", "AleLuduMod.dll"),
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        var fakeFull = new ModInstanceInstallerTestsHelpers.FakeFullModInstaller();
        var fakeDll = new ModInstanceInstallerTestsHelpers.FakeDllInstaller();
        var service = new ModInstanceInstaller(repo, fakeFull, fakeDll);
        var catalog = new List<ModConfiguration>
        {
            ModInstanceInstallerTestsHelpers.CreateFullMod(),
            ModInstanceInstallerTestsHelpers.CreateDllMod()
        };
        var updatedFull = ModInstanceInstallerTestsHelpers.CreateFullMod();
        updatedFull.ModVersion = "5.6.0";
        updatedFull.AmongVersion = "2025.1";

        await service.UpdateInstanceAsync(
            instance.InstanceId,
            updatedFull,
            catalog,
            "steam");

        var stored = repo.GetInstance(instance.InstanceId);
        Assert.NotNull(stored);
        Assert.Equal("5.6.0", stored.FullModVersion);
        Assert.Equal("2025.1", stored.AmongVersion);
        Assert.Equal(installPath, fakeFull.LastTargetPath);
        Assert.True(File.Exists(Path.Combine(installPath, "Among Us.exe")));
        Assert.Equal(1, fakeDll.InstallCallCount);
        Assert.Single(repo.GetDlls(instance.InstanceId));
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
