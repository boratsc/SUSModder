using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy prognozy aktualizacji (3.1-06). Scenariusze: brak instalacji, brak dodatków,
/// dodatek zgodny / niezgodny / bez danych / brak w katalogu, plik niestandardowy,
/// przypięta wersja i brak zapisów podczas budowy podsumowania.
/// </summary>
public sealed class UpdatePreviewServiceTests : IDisposable
{
    private readonly string _tempDir;

    public UpdatePreviewServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReturnsUnknownState_WhenModIsNotInstalled()
    {
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(installPath: string.Empty, installedVersion: "1.0.0"),
            CreateTarget("2.0.0"));

        Assert.False(preview.IsInstalled);
        Assert.False(preview.HasInstallationMap);
        Assert.Equal(UpdateConfigStatus.Unknown, preview.ConfigStatus);
        Assert.Empty(preview.Addons);
        Assert.Equal("1.0.0", preview.CurrentVersion);
        Assert.Equal("2.0.0", preview.TargetVersion);
    }

    [Fact]
    public async Task BuildPreviewAsync_UsesInstallationMapVersion_AsCurrentVersion()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.2.3"));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, installedVersion: "0.0.1"),
            CreateTarget("2.0.0"));

        Assert.Equal("1.2.3", preview.CurrentVersion);
        Assert.True(preview.HasInstallationMap);
        Assert.True(preview.IsInstalled);
    }

    [Fact]
    public async Task BuildPreviewAsync_MarksConfigAsNotGuaranteed_ForInstalledMod()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        Assert.Equal(UpdateConfigStatus.NotGuaranteed, preview.ConfigStatus);
    }

    [Fact]
    public async Task BuildPreviewAsync_TreatsFailingCompatibilityLookupAsUnknown()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            new ThrowingCompatibilityLookup());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.RetainedCompatibilityUnknown, addon.Status);
        Assert.False(addon.RequiresAttention);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsNoAddons_WhenModHasNoDllAddons()
    {
        string modPath = CreateModDirectory();
        var map = CreateMap(modPath, "1.0.0");
        map.InstalledDlls.Clear();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, map);
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        Assert.Empty(preview.Addons);
        Assert.False(preview.RequiresAttention);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsRetainedAddon_WhenCompatibilityIsWorks()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            new FakeCompatibilityLookup
            {
                Result = new CompatibilityInfo { StatusCode = "W", IsCurrentVersion = true }
            });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.Retained, addon.Status);
        Assert.Equal("retained", addon.StatusCode);
        Assert.True(addon.IsCompatibilityConfirmed);
        Assert.False(addon.RequiresAttention);
        Assert.Equal("W", addon.CompatibilityStatusCode);
    }

    [Fact]
    public async Task PreviewChecksTheCatalogVersionThatReplayWillActuallyDownload()
    {
        var path = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(path, CreateMap(path, "1.0.0"));
        var lookup = new FakeCompatibilityLookup
        {
            Result = new CompatibilityInfo { StatusCode = "W", IsCurrentVersion = true }
        };
        var preview = await CreateService(new[] { CreateDllMod(30, "CrowdedMod") }, lookup)
            .BuildPreviewAsync(CreateFullMod(path, "1.0.0"), CreateTarget("2.0.0"));
        Assert.Equal("2.0.0", lookup.LastDllVersion);
        Assert.Equal("1.2.3", Assert.Single(preview.Addons).InstalledVersion);
        Assert.Equal("2.0.0", Assert.Single(preview.Addons).RestoredVersion);
    }

    [Fact]
    public async Task PreviewDoesNotPromiseTrackedRestorationFromForeignMap()
    {
        var path = CreateModDirectory();
        var map = CreateMap(path, "1.0.0");
        map.FullMod.ModId = 999;
        await InstallationMapManager.SaveInstallationMapAsync(path, map);
        var preview = await CreateService(new[] { CreateDllMod(30, "CrowdedMod") })
            .BuildPreviewAsync(CreateFullMod(path, "1.0.0"), CreateTarget("2.0.0"));
        Assert.False(preview.HasInstallationMap);
        Assert.Empty(preview.Addons);
    }

    [Fact]
    public async Task NonExactNegativeCompatibilityIsUnknownNotConfirmedIncompatibility()
    {
        var path = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(path, CreateMap(path, "1.0.0"));
        var lookup = new FakeCompatibilityLookup
        {
            Result = new CompatibilityInfo { StatusCode = "NW", IsCurrentVersion = false }
        };
        var preview = await CreateService(new[] { CreateDllMod(30, "CrowdedMod") }, lookup)
            .BuildPreviewAsync(CreateFullMod(path, "1.0.0"), CreateTarget("2.0.0"));
        Assert.Equal(UpdateAddonStatus.RetainedCompatibilityUnknown, Assert.Single(preview.Addons).Status);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsIncompatibleAddon_WhenCompatibilityIsNotWork()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            new FakeCompatibilityLookup
            {
                Result = new CompatibilityInfo { StatusCode = "NW", IsCurrentVersion = true }
            });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.RetainedIncompatible, addon.Status);
        Assert.True(addon.RequiresAttention);
        Assert.True(preview.RequiresAttention);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsUnknownCompatibility_WhenNoDataAvailable()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            new FakeCompatibilityLookup { Result = null });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.RetainedCompatibilityUnknown, addon.Status);
        Assert.False(addon.IsCompatibilityConfirmed);
        Assert.False(addon.RequiresAttention);
    }

    [Fact]
    public async Task BuildPreviewAsync_DoesNotConfirmCompatibility_WhenDataIsForOtherVersion()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(
            new[] { CreateDllMod(30, "CrowdedMod") },
            new FakeCompatibilityLookup
            {
                Result = new CompatibilityInfo { StatusCode = "F", IsCurrentVersion = false }
            });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.RetainedCompatibilityUnknown, addon.Status);
        Assert.False(addon.IsExactCompatibilityMatch);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsMissingCatalogAddon()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.NotRestoredMissingCatalog, addon.Status);
        Assert.Equal("not_restored_missing_catalog", addon.StatusCode);
        Assert.True(addon.RequiresAttention);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsCustomDllFile_WhenFileIsNotTrackedInInstallationMap()
    {
        string modPath = CreateModDirectory();
        var map = CreateMap(modPath, "1.0.0");
        map.InstalledDlls.Clear();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, map);

        string pluginsPath = Path.Combine(modPath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsPath);
        File.WriteAllText(Path.Combine(pluginsPath, "MyCustomMod.dll"), "binary");

        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(UpdateAddonStatus.NotRestoredCustomFile, addon.Status);
        Assert.True(addon.IsCustomFile);
        Assert.Equal("MyCustomMod.dll", addon.Name);
    }

    [Fact]
    public async Task BuildPreviewAsync_DoesNotReportTrackedDllAsCustomFile()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));

        string pluginsPath = Path.Combine(modPath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsPath);
        File.WriteAllText(Path.Combine(pluginsPath, "CrowdedMod.dll"), "binary");

        var service = CreateService(new[] { CreateDllMod(30, "CrowdedMod") });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        var addon = Assert.Single(preview.Addons);
        Assert.Equal(30, addon.ModId);
        Assert.False(addon.IsCustomFile);
    }

    [Fact]
    public async Task BuildPreviewAsync_DoesNotWriteAnything_ToInstallationDirectory()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));

        string mapPath = Path.Combine(modPath, ".susmodder-install.json");
        string mapContentBefore = await File.ReadAllTextAsync(mapPath);
        var filesBefore = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(modPath, path))
            .OrderBy(path => path)
            .ToList();

        var service = CreateService(new[] { CreateDllMod(30, "CrowdedMod") });
        await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("2.0.0"));

        string mapContentAfter = await File.ReadAllTextAsync(mapPath);
        var filesAfter = Directory.GetFiles(modPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(modPath, path))
            .OrderBy(path => path)
            .ToList();

        Assert.Equal(mapContentBefore, mapContentAfter);
        Assert.Equal(filesBefore, filesAfter);
    }

    [Fact]
    public async Task BuildPreviewAsync_ReadsInstallationMap_FromEpicGameSubfolder()
    {
        // Epic: katalog gry leży w podkatalogu „AmongUs”, a mapa instalacji bywa w katalogu nadrzędnym.
        string outerPath = CreateModDirectory();
        string gamePath = Path.Combine(outerPath, "AmongUs");
        Directory.CreateDirectory(gamePath);
        await InstallationMapManager.SaveInstallationMapAsync(gamePath, CreateMap(gamePath, "1.0.0"));

        string pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsPath);
        File.WriteAllText(Path.Combine(pluginsPath, "CrowdedMod.dll"), "binary");

        var service = CreateService(new[] { CreateDllMod(30, "CrowdedMod") });

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(outerPath, "1.0.0"),
            CreateTarget("2.0.0"));

        Assert.True(preview.HasInstallationMap);
        var addon = Assert.Single(preview.Addons);
        Assert.Equal(30, addon.ModId);
        Assert.False(addon.IsCustomFile);
    }

    [Fact]
    public async Task BuildPreviewsAsync_ReturnsOnePreviewPerUpdate()
    {
        string firstPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(firstPath, CreateMap(firstPath, "1.0.0"));
        string secondPath = CreateModDirectory();

        var service = CreateService(Array.Empty<ModConfiguration>());

        var updates = new[]
        {
            new ModUpdateInfo
            {
                ModName = "Town of Us",
                CurrentVersion = "1.0.0",
                NewVersion = "2.0.0",
                LocalMod = CreateFullMod(firstPath, "1.0.0"),
                RemoteMod = CreateTarget("2.0.0")
            },
            new ModUpdateInfo
            {
                ModName = "Other Mod",
                CurrentVersion = "0.9.0",
                NewVersion = "1.1.0",
                LocalMod = new ModConfiguration
                {
                    Id = 11,
                    ModName = "Other Mod",
                    ModType = "full",
                    ModVersion = "0.9.0",
                    AmongVersion = "2025.4.20",
                    InstallPath = secondPath
                },
                RemoteMod = new ModConfiguration
                {
                    Id = 11,
                    ModName = "Other Mod",
                    ModType = "full",
                    ModVersion = "1.1.0",
                    AmongVersion = "2025.6.1"
                }
            }
        };

        var previews = await service.BuildPreviewsAsync(updates);

        Assert.Equal(2, previews.Count);
        Assert.Equal("Town of Us", previews[0].ModName);
        Assert.Equal("2.0.0", previews[0].TargetVersion);
        Assert.Equal("Other Mod", previews[1].ModName);
        Assert.NotEqual(previews[0].TargetKey, previews[1].TargetKey);
    }

    [Fact]
    public async Task BuildPreviewAsync_UsesPinnedVersion_AsTargetVersion()
    {
        string modPath = CreateModDirectory();
        await InstallationMapManager.SaveInstallationMapAsync(modPath, CreateMap(modPath, "1.0.0"));
        var service = CreateService(Array.Empty<ModConfiguration>());

        var preview = await service.BuildPreviewAsync(
            CreateFullMod(modPath, "1.0.0"),
            CreateTarget("1.0.0"));

        Assert.Equal("1.0.0", preview.TargetVersion);
        Assert.Contains("version:1.0.0", preview.TargetKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private string CreateModDirectory()
    {
        string path = Path.Combine(_tempDir, "Mods", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static UpdatePreviewService CreateService(
        IEnumerable<ModConfiguration> catalog,
        IUpdateCompatibilityLookup? lookup = null) =>
        new(() => catalog.ToList(), lookup);

    private static ModConfiguration CreateFullMod(string installPath, string installedVersion) => new()
    {
        Id = 10,
        ModName = "Town of Us",
        ModType = "full",
        ModVersion = installedVersion,
        AmongVersion = "2025.4.20",
        InstallPath = installPath
    };

    private static ModConfiguration CreateTarget(string version) => new()
    {
        Id = 10,
        ModName = "Town of Us",
        ModType = "full",
        ModVersion = version,
        AmongVersion = "2025.6.1"
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

    private static InstallationMap CreateMap(string installPath, string version) => new()
    {
        InstalledAt = DateTime.Now,
        InstalledBy = "test",
        Platform = "steam",
        FullMod = new FullModInstallation
        {
            ModId = 10,
            ModName = "Town of Us",
            ModVersion = version,
            AmongVersion = "2025.4.20",
            InstallPath = installPath
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
                LastUpdated = DateTime.Now
            }
        }
    };

    private sealed class FakeCompatibilityLookup : IUpdateCompatibilityLookup
    {
        public CompatibilityInfo? Result { get; set; }
        public string? LastDllVersion { get; private set; }

        public Task<CompatibilityInfo?> GetAsync(
            int fullModId,
            string fullModVersion,
            int dllModId,
            string dllModVersion,
            CancellationToken cancellationToken)
        {
            LastDllVersion = dllModVersion;
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingCompatibilityLookup : IUpdateCompatibilityLookup
    {
        public Task<CompatibilityInfo?> GetAsync(
            int fullModId,
            string fullModVersion,
            int dllModId,
            string dllModVersion,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
