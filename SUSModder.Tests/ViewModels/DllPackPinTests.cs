using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;
using SUSModder.ViewModels;
using Xunit;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// F3g: reguła origin-paczki w monitorze DLL — paczka pinuje wersje, więc lokalizacje DLL
/// w instancjach z SourcePackCode nie mogą być flagowane do aktualizacji z katalogu.
/// Czysta funkcja bez Avalonii/DI.
/// </summary>
public class DllPackPinTests
{
    private static ModInstance Instance(string id, string installPath, string? sourcePackCode) => new()
    {
        InstanceId = id,
        InstallPath = installPath,
        SourcePackCode = sourcePackCode,
        Origin = sourcePackCode != null ? "shared_pack" : "manual"
    };

    private static DllUpdateInfo Update(string dllName, params string[] installPaths) => new()
    {
        DllMod = new ModConfiguration { ModName = dllName, ModType = "dll" },
        NewVersion = "2.0.0",
        LocationUpdates = installPaths.Select(p => new DllLocationUpdate
        {
            FullMod = new ModConfiguration { ModName = "Full " + p, InstallPath = p },
            CurrentVersion = "1.0.0",
            NewVersion = "2.0.0"
        }).ToList()
    };

    [Fact]
    public void PinnedLocationIsRemovedAndUnpinnedKept()
    {
        var update = Update("Syzyf", @"C:\Mods\Packed", @"C:\Mods\Manual");
        var instances = new[] { Instance("i1", @"C:\Mods\Packed", "ABCD-1234") };

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, instances);

        Assert.Equal(1, skipped);
        var kept = Assert.Single(updates);
        var location = Assert.Single(kept.LocationUpdates);
        Assert.Equal(@"C:\Mods\Manual", location.FullMod.InstallPath);
        // Także listy używane przez dialog/auto-update nie zawierają przypiętej lokalizacji.
        Assert.Single(kept.InstallLocations);
        Assert.Single(kept.SelectedLocations);
        Assert.Equal(@"C:\Mods\Manual", kept.SelectedLocations[0].InstallPath);
    }

    [Fact]
    public void UpdateWithAllLocationsPinnedIsDroppedEntirely()
    {
        var update = Update("Syzyf", @"C:\Mods\Packed");
        var instances = new[] { Instance("i1", @"C:\Mods\Packed", "ABCD-1234") };

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, instances);

        Assert.Empty(updates);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void InstanceWithoutSourcePackCodeDoesNotPin()
    {
        var update = Update("Syzyf", @"C:\Mods\Manual");
        var instances = new[] { Instance("i1", @"C:\Mods\Manual", null) };

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, instances);

        Assert.Equal(0, skipped);
        Assert.Single(updates);
        Assert.Single(updates[0].LocationUpdates);
    }

    [Fact]
    public void PathMatchingIgnoresTrailingSeparatorsAndCase()
    {
        var update = Update("Syzyf", @"c:\mods\packed\");
        var instances = new[] { Instance("i1", @"C:\Mods\Packed", "ABCD-1234") };

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, instances);

        Assert.Equal(1, skipped);
        Assert.Empty(updates);
    }

    [Fact]
    public void CloneWithSourcePackCodeAlsoPins()
    {
        var update = Update("Syzyf", @"C:\Mods\Clone");
        var clone = Instance("i1", @"C:\Mods\Clone", "ABCD-1234");
        clone.Origin = "clone";

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, new[] { clone });

        Assert.Equal(1, skipped);
        Assert.Empty(updates);
    }

    [Fact]
    public void NoInstancesLeavesUpdatesUntouched()
    {
        var update = Update("Syzyf", @"C:\Mods\Manual");

        var (updates, skipped) = MainWindowViewModel.ExcludePinnedPackDllLocations(
            new List<DllUpdateInfo> { update }, System.Array.Empty<ModInstance>());

        Assert.Equal(0, skipped);
        Assert.Same(update, Assert.Single(updates));
    }
}