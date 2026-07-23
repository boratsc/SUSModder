using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public sealed class FullModAddonPreservationServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FullModAddonPreservationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task CaptureFromInstallationMapAsync_ReturnsEmptySnapshot_WhenMapDoesNotExist()
    {
        var modPath = Path.Combine(_tempDir, "Town of Us");
        Directory.CreateDirectory(modPath);
        var service = CreateService(Array.Empty<ModConfiguration>());

        var snapshot = await service.CaptureFromInstallationMapAsync(CreateFullMod(modPath));

        Assert.True(snapshot.IsEmpty);
        Assert.Equal(10, snapshot.FullModId);
        Assert.Equal("Town of Us", snapshot.FullModName);
    }

    [Fact]
    public async Task CaptureFromInstallationMapAsync_ReturnsEmptySnapshot_WhenMapBelongsToDifferentFullMod()
    {
        var modPath = Path.Combine(_tempDir, "Town of Us");
        Directory.CreateDirectory(modPath);
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, fullModId: 999));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var snapshot = await service.CaptureFromInstallationMapAsync(CreateFullMod(modPath));

        Assert.True(snapshot.IsEmpty);
        Assert.Equal(10, snapshot.FullModId);
    }

    [Fact]
    public async Task CaptureFromInstallationMapAsync_CapturesDllsAndFullModAutoUpdateFlag()
    {
        var modPath = Path.Combine(_tempDir, "Town of Us");
        Directory.CreateDirectory(modPath);
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var snapshot = await service.CaptureFromInstallationMapAsync(CreateFullMod(modPath));

        Assert.False(snapshot.IsEmpty);
        Assert.True(snapshot.FullModAutoUpdateEnabled);
        Assert.True(snapshot.FullModDontShowPostInstallDialog);
        var dll = Assert.Single(snapshot.DllAddons);
        Assert.Equal(30, dll.ModId);
        Assert.True(dll.AutoUpdateEnabled);
    }

    [Fact]
    public async Task RestoreToFullModAsync_ReinstallsDllAndPreservesAutoUpdateFlags()
    {
        var modPath = Path.Combine(_tempDir, "Town of Us");
        Directory.CreateDirectory(modPath);
        var updatedMap = CreateMap(modPath);
        updatedMap.FullMod.AutoUpdateEnabled = false;
        updatedMap.FullMod.DontShowPostInstallDialog = false;
        updatedMap.FullMod.DisableAutoUpdatePrompt = false;
        updatedMap.FullMod.PinnedInstallVersion = null;
        updatedMap.InstalledDlls.Clear();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, updatedMap);
        var snapshot = new FullModAddonSnapshot
        {
            FullModId = 10,
            FullModName = "Town of Us",
            InstallPath = modPath,
            FullModAutoUpdateEnabled = true,
            FullModDontShowPostInstallDialog = true,
            FullModDisableAutoUpdatePrompt = true,
            FullModPinnedInstallVersion = "5.0.0",
            DllAddons = new[]
            {
                new PreservedDllAddon
                {
                    ModId = 30,
                    ModName = "CrowdedMod",
                    ModVersion = "1.2.3",
                    InstallPath = @"BepInEx\plugins\CrowdedMod.dll",
                    InstalledFrom = "https://example.invalid/old.dll",
                    AutoUpdateEnabled = true
                }
            }
        };
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            async (_, targetMod, _) =>
            {
                var map = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);
                Assert.NotNull(map);
                map!.InstalledDlls.Add(new DllModInstallation
                {
                    ModId = 30,
                    ModName = "CrowdedMod",
                    ModVersion = "2.0.0",
                    InstallPath = @"BepInEx\plugins\CrowdedMod.dll",
                    InstalledFrom = "https://example.invalid/new.dll",
                    InstalledAt = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    AutoUpdateEnabled = false
                });
                await InstallationMapManager.SaveInstallationMapAsync(targetMod.InstallPath!, map);
                return Path.Combine(targetMod.InstallPath!, "BepInEx", "plugins", "CrowdedMod.dll");
            });

        var result = await service.RestoreToFullModAsync(CreateFullMod(modPath), snapshot, "steam");

        Assert.Equal(1, result.RestoredCount);
        Assert.False(result.HasProblems);
        var finalMap = await InstallationMapManager.LoadInstallationMapAsync(modPath);
        Assert.NotNull(finalMap);
        Assert.True(finalMap!.FullMod.AutoUpdateEnabled);
        Assert.True(finalMap.FullMod.DontShowPostInstallDialog);
        Assert.False(finalMap.FullMod.DisableAutoUpdatePrompt);
        Assert.Null(finalMap.FullMod.PinnedInstallVersion);
        Assert.True(Assert.Single(finalMap.InstalledDlls).AutoUpdateEnabled);
    }

    [Fact]
    public async Task RestoreToFullModAsync_ReturnsSkippedMissingCatalog_WhenDllIsUnavailable()
    {
        var modPath = Path.Combine(_tempDir, "Town of Us");
        Directory.CreateDirectory(modPath);
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath));
        var snapshot = await CreateService(Array.Empty<ModConfiguration>()).CaptureFromInstallationMapAsync(CreateFullMod(modPath));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var result = await service.RestoreToFullModAsync(CreateFullMod(modPath), snapshot, "steam");

        var item = Assert.Single(result.Items);
        Assert.Equal(DllRestoreStatus.SkippedMissingCatalog, item.Status);
        Assert.Equal(1, result.SkippedCount);
        Assert.True(result.HasProblems);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static FullModAddonPreservationService CreateService(
        IEnumerable<ModConfiguration> catalog,
        Func<ModConfiguration, ModConfiguration, string, Task<string?>>? installer = null) => new(
            () => catalog.ToList(),
            installer ?? ((_, _, _) => Task.FromResult<string?>("installed.dll")),
            new TestDiagnosticsOutput());

    private static ModConfiguration CreateFullMod(string installPath) => new()
    {
        Id = 10,
        ModName = "Town of Us",
        ModType = "full",
        ModVersion = "5.0.0",
        InstallPath = installPath
    };

    private static ModConfiguration CreateDllMod(int id, string name) => new()
    {
        Id = id,
        ModName = name,
        ModType = "dll",
        ModVersion = "2.0.0",
        GitHubRepoOrLink = "https://example.invalid/new.dll",
        DllInstallPath = @"BepInEx\plugins"
    };

    private static InstallationMap CreateMap(string installPath, int fullModId = 10) => new()
    {
        InstalledAt = DateTime.Now,
        InstalledBy = "test",
        Platform = "steam",
        FullMod = new FullModInstallation
        {
            ModId = fullModId,
            ModName = fullModId == 10 ? "Town of Us" : "Other Mod",
            ModVersion = "5.0.0",
            InstallPath = installPath,
            AutoUpdateEnabled = true,
            DontShowPostInstallDialog = true,
            DisableAutoUpdatePrompt = true,
            PinnedInstallVersion = "5.0.0"
        },
        InstalledDlls = new List<DllModInstallation>
        {
            new()
            {
                ModId = 30,
                ModName = "CrowdedMod",
                ModVersion = "1.2.3",
                InstallPath = @"BepInEx\plugins\CrowdedMod.dll",
                InstalledFrom = "https://example.invalid/old.dll",
                InstalledAt = DateTime.Now,
                LastUpdated = DateTime.Now,
                AutoUpdateEnabled = true
            }
        }
    };

    private sealed class TestDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string line)
        {
        }
    }
}
