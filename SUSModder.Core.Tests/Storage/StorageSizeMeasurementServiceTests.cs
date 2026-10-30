using System.Diagnostics;
using SUSModder.Core.Storage;
using Xunit;

namespace SUSModder.Core.Tests.Storage;

public class StorageSizeMeasurementServiceTests
{
    [Fact]
    public async Task MeasureAsync_ReturnsCompleteResult_ForKnownTree()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateFile("sub/b.bin", 200);

        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync(tree.Root);

        Assert.Equal(StorageSizeState.Complete, result.State);
        Assert.Equal(300, result.LogicalBytes);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(StorageSizeErrorCode.None, result.ErrorCode);
        Assert.True(result.HasValue);
        Assert.True(result.HasMeasuredValue);
    }

    [Fact]
    public async Task MeasureAsync_ReturnsZeroBytes_ForEmptyDirectory()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync(tree.Root);

        Assert.Equal(StorageSizeState.Complete, result.State);
        Assert.Equal(0, result.LogicalBytes);
        Assert.True(result.HasValue);
    }

    [Fact]
    public async Task MeasureAsync_ReturnsUnavailable_ForMissingDirectory()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync(Path.Combine(tree.Root, "nie-ma-mnie"));

        Assert.Equal(StorageSizeState.Unavailable, result.State);
        Assert.False(result.HasValue);
        Assert.False(result.HasMeasuredValue);
        Assert.Equal(StorageSizeErrorCode.PathNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task MeasureAsync_ReturnsUnavailable_ForEmptyPath()
    {
        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync("   ");

        Assert.Equal(StorageSizeState.Unavailable, result.State);
        Assert.Equal(StorageSizeErrorCode.PathEmpty, result.ErrorCode);
    }

    [Fact]
    public async Task MeasureAsync_MarksPartial_WhenSubdirectoryIsNotAccessible()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("readable.bin", 100);
        var denied = tree.CreateDirectory("secret");
        tree.CreateFile("secret/hidden.bin", 500);

        var probe = new FakeStorageFileSystemProbe();
        probe.AccessDeniedDirectories.Add(denied);

        using var service = new StorageSizeMeasurementService(4, null, probe);

        var result = await service.MeasureAsync(tree.Root);

        Assert.Equal(StorageSizeState.Partial, result.State);
        Assert.Equal(100, result.LogicalBytes);
        Assert.Equal(1, result.SkippedEntryCount);
        Assert.Equal(StorageSizeErrorCode.AccessDenied, result.ErrorCode);
        Assert.True(result.HasValue);
    }

    [Fact]
    public async Task MeasureAsync_MarksPartial_WhenFileDisappearsDuringScan()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        var vanishing = tree.CreateFile("b.bin", 200);

        var probe = new FakeStorageFileSystemProbe();
        probe.MissingFiles.Add(vanishing);

        using var service = new StorageSizeMeasurementService(4, null, probe);

        var result = await service.MeasureAsync(tree.Root);

        Assert.Equal(StorageSizeState.Partial, result.State);
        Assert.Equal(100, result.LogicalBytes);
        Assert.True(result.SkippedEntryCount >= 1);
    }

    [Fact]
    public async Task MeasureAsync_DoesNotFollowReparsePoints()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var external = new StorageTestTree();
        external.CreateFile("outside.bin", 4096);

        var junction = Path.Combine(tree.Root, "link-to-external");
        if (!TryCreateJunction(external.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var result = await service.MeasureAsync(tree.Root);

            Assert.Equal(StorageSizeState.Partial, result.State);
            Assert.Equal(100, result.LogicalBytes);
            Assert.Equal(1, result.SkippedReparsePointCount);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_DoesNotLoop_OnJunctionToAncestor()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateDirectory("nested");

        var junction = Path.Combine(tree.Root, "nested", "loop");
        if (!TryCreateJunction(tree.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var measurement = service.MeasureAsync(tree.Root);
            var finished = await Task.WhenAny(measurement, Task.Delay(TimeSpan.FromSeconds(20)));

            Assert.Same(measurement, finished);

            var result = await measurement;
            Assert.Equal(StorageSizeState.Partial, result.State);
            Assert.Equal(100, result.LogicalBytes);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_RejectsReparsePointRoot_WithoutScanningExternalTree()
    {
        using var external = new StorageTestTree();
        external.CreateFile("outside.bin", 4096);

        var junction = Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"));
        if (!TryCreateJunction(external.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var result = await service.MeasureAsync(junction);

            Assert.Equal(StorageSizeState.Unavailable, result.State);
            Assert.Equal(StorageSizeErrorCode.PathIsLink, result.ErrorCode);
            Assert.False(result.HasValue);
            Assert.Equal(0, result.LogicalBytes);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_RejectsPathThroughReparsePointAncestor()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("real/child.bin", 100);

        var junction = Path.Combine(tree.Root, "linked");
        if (!TryCreateJunction(Path.Combine(tree.Root, "real"), junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var result = await service.MeasureAsync(Path.Combine(junction, "child.bin"));

            Assert.Equal(StorageSizeState.Unavailable, result.State);
            Assert.Equal(StorageSizeErrorCode.PathIsLink, result.ErrorCode);
            Assert.False(result.HasValue);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_TrustedRoot_LimitsAncestorCheckToManagedScope()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("sub/child.bin", 100);

        // Junction w obrębie biblioteki — musi blokować pomiar w jej zakresie.
        var junction = Path.Combine(tree.Root, "sub", "linked");
        if (!TryCreateJunction(Path.Combine(tree.Root, "sub"), junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var blocked = await service.MeasureAsync(
                junction,
                new StorageMeasurementOptions { TrustedRoot = tree.Root });

            Assert.Equal(StorageSizeState.Unavailable, blocked.State);
            Assert.Equal(StorageSizeErrorCode.PathIsLink, blocked.ErrorCode);

            // Bez granicy zaufania blokada nadal obowiązuje (przodkowie sprawdzani do korzenia woluminu).
            var alsoBlocked = await service.MeasureAsync(junction);
            Assert.Equal(StorageSizeState.Unavailable, alsoBlocked.State);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_ConcurrentRequests_WithDifferentTrustedRoot_DoNotShareFlight()
    {
        using var real = new StorageTestTree();
        real.CreateFile("lib/sub/child.bin", 100);

        // Link jest POWYŻEJ granicy zaufania: ograniczony pomiar przechodzi, nieograniczony nie.
        var junction = Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"));
        if (!TryCreateJunction(real.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            var probe = new FakeStorageFileSystemProbe { DelayPerDirectory = TimeSpan.FromMilliseconds(120) };
            using var service = new StorageSizeMeasurementService(4, null, probe);

            var measuredPath = Path.Combine(junction, "lib", "sub");
            var trustedRoot = Path.Combine(junction, "lib");

            var bounded = service.MeasureAsync(measuredPath, new StorageMeasurementOptions { TrustedRoot = trustedRoot });
            var unbounded = service.MeasureAsync(measuredPath);

            await Task.WhenAll(bounded, unbounded);

            // Różne zasięgi zaufania nie mogą współdzielić lotu pomiaru.
            Assert.Equal(StorageSizeState.Complete, bounded.Result.State);
            Assert.Equal(100, bounded.Result.LogicalBytes);
            Assert.True(bounded.Result.HasValue);

            Assert.Equal(StorageSizeState.Unavailable, unbounded.Result.State);
            Assert.Equal(StorageSizeErrorCode.PathIsLink, unbounded.Result.ErrorCode);
            Assert.False(unbounded.Result.HasValue);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_TrustedRootEqualToMeasuredPath_LimitsCheckToPathItself()
    {
        using var real = new StorageTestTree();
        real.CreateFile("lib/child.bin", 100);

        var junction = Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"));
        if (!TryCreateJunction(real.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            using var service = new StorageSizeMeasurementService();

            var measuredPath = Path.Combine(junction, "lib");

            // Granica zaufania to sama mierzona ścieżka — przodkowie powyżej nie są sprawdzani.
            var result = await service.MeasureAsync(
                measuredPath,
                new StorageMeasurementOptions { TrustedRoot = measuredPath });

            Assert.Equal(StorageSizeState.Complete, result.State);
            Assert.Equal(100, result.LogicalBytes);
        }
        finally
        {
            TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task MeasureAsync_Throws_WhenCancelledBeforeStart()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.MeasureAsync(tree.Root, null, cts.Token));
    }

    [Fact]
    public async Task MeasureAsync_Throws_WhenCancelledDuringScan()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateDirectory("sub");

        var probe = new FakeStorageFileSystemProbe { DelayPerDirectory = TimeSpan.FromMilliseconds(200) };
        using var service = new StorageSizeMeasurementService(4, null, probe);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.MeasureAsync(tree.Root, null, cts.Token));
    }

    [Fact]
    public async Task MeasureAsync_UsesCache_ForSameCanonicalPath()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var first = await service.MeasureAsync(tree.Root);

        tree.CreateFile("b.bin", 1000);

        var second = await service.MeasureAsync(tree.Root);

        Assert.Same(first, second);
        Assert.Equal(100, second.LogicalBytes);
    }

    [Fact]
    public async Task MeasureAsync_ForceRefresh_RecomputesValue()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var first = await service.MeasureAsync(tree.Root);
        tree.CreateFile("b.bin", 1000);

        var second = await service.MeasureAsync(tree.Root, new StorageMeasurementOptions { ForceRefresh = true });

        Assert.NotSame(first, second);
        Assert.Equal(1100, second.LogicalBytes);
    }

    [Fact]
    public async Task Invalidate_MarksSnapshotAsStale_AndAllowsRecompute()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var first = await service.MeasureAsync(tree.Root);
        service.Invalidate(tree.Root);

        var display = service.GetDisplaySnapshot(tree.Root);
        Assert.Equal(StorageSizeState.Stale, display.State);
        Assert.Equal(first.LogicalBytes, display.LogicalBytes);

        tree.CreateFile("b.bin", 1000);
        var second = await service.MeasureAsync(tree.Root);

        Assert.NotSame(first, second);
        Assert.Equal(1100, second.LogicalBytes);
        Assert.Equal(StorageSizeState.Complete, service.GetDisplaySnapshot(tree.Root).State);
    }

    [Fact]
    public async Task GetDisplaySnapshot_KeepsUnavailableResultUnavailable_AfterInvalidation()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService();

        var missing = await service.MeasureAsync(Path.Combine(tree.Root, "nie-ma-mnie"));
        Assert.Equal(StorageSizeState.Unavailable, missing.State);

        service.Invalidate(tree.Root);

        var display = service.GetDisplaySnapshot(Path.Combine(tree.Root, "nie-ma-mnie"));

        // Wynik niedostępny nigdy nie staje się „stale z zerem”.
        Assert.Equal(StorageSizeState.Unavailable, display.State);
        Assert.False(display.HasValue);
        Assert.False(display.HasMeasuredValue);
    }

    [Fact]
    public async Task GetDisplaySnapshot_KeepsUnavailableResultUnavailable_AfterExpiry()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService(StorageSizeMeasurementService.DefaultMaxConcurrency, TimeSpan.Zero);

        var missing = await service.MeasureAsync(Path.Combine(tree.Root, "nie-ma-mnie"));
        Assert.Equal(StorageSizeState.Unavailable, missing.State);

        var display = service.GetDisplaySnapshot(Path.Combine(tree.Root, "nie-ma-mnie"));

        Assert.Equal(StorageSizeState.Unavailable, display.State);
        Assert.False(display.HasValue);
    }

    [Fact]
    public async Task InvalidateUnder_InvalidatesNestedEntries()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("sub/a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var nested = await service.MeasureAsync(Path.Combine(tree.Root, "sub"));
        Assert.Equal(StorageSizeState.Complete, nested.State);

        service.InvalidateUnder(tree.Root);

        Assert.Equal(StorageSizeState.Stale, service.GetDisplaySnapshot(Path.Combine(tree.Root, "sub")).State);
    }

    [Fact]
    public async Task InvalidateDuringScan_DoesNotPublishStaleResult()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateDirectory("sub");

        var probe = new FakeStorageFileSystemProbe { DelayPerDirectory = TimeSpan.FromMilliseconds(150) };
        using var service = new StorageSizeMeasurementService(4, null, probe);

        var measurement = service.MeasureAsync(tree.Root);
        await Task.Delay(50);

        service.Invalidate(tree.Root);

        await measurement;

        // Wynik zadania rozpoczętego przed unieważnieniem nie może być opublikowany jako świeży.
        Assert.Equal(StorageSizeState.Loading, service.GetDisplaySnapshot(tree.Root).State);
    }

    [Fact]
    public async Task MeasureAsync_AfterInvalidation_StartsNewScanInsteadOfJoiningOldFlight()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        var probe = new FakeStorageFileSystemProbe { DelayPerDirectory = TimeSpan.FromMilliseconds(150) };
        using var service = new StorageSizeMeasurementService(4, null, probe);

        var first = service.MeasureAsync(tree.Root);
        await Task.Delay(40);

        service.Invalidate(tree.Root);

        var second = await service.MeasureAsync(tree.Root);
        await first;

        // Po unieważnieniu nie dołączamy do lotu o starej rewizji — powstaje nowy skan.
        Assert.True(probe.EnumerateCallCount >= 2);
        Assert.True(second.Revision > first.Result.Revision);
    }

    [Fact]
    public async Task MeasureManyAsync_DeduplicatesIdenticalRoots()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var measurement = await service.MeasureManyAsync(new[] { tree.Root, tree.Root, tree.Root });

        Assert.Single(measurement.IncludedRootPaths);
        Assert.Equal(2, measurement.DeduplicatedRootPaths.Count);
        Assert.Equal(100, measurement.TotalLogicalBytes);
    }

    [Fact]
    public async Task MeasureManyAsync_DeduplicatesNestedRoots()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateFile("sub/b.bin", 200);

        using var service = new StorageSizeMeasurementService();

        var measurement = await service.MeasureManyAsync(new[] { tree.Root, Path.Combine(tree.Root, "sub") });

        Assert.Single(measurement.IncludedRootPaths);
        Assert.Equal(300, measurement.TotalLogicalBytes);
        Assert.Contains(measurement.DeduplicatedRootPaths, p => p.EndsWith("sub", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MeasureManyAsync_CompletePlusUnavailable_IsPartial()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var measurement = await service.MeasureManyAsync(new[]
        {
            tree.Root,
            // Ścieżka spoza mierzonego korzenia — inaczej zostałaby odrzucona jako zagnieżdżona.
            Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"))
        });

        Assert.Equal(StorageSizeState.Partial, measurement.State);
        Assert.Equal(1, measurement.UnavailableRootCount);
        Assert.Equal(100, measurement.TotalLogicalBytes);
        Assert.True(measurement.HasValue);
    }

    [Fact]
    public async Task MeasureManyAsync_AllUnavailable_IsUnavailable()
    {
        using var tree = new StorageTestTree();
        using var service = new StorageSizeMeasurementService();

        var measurement = await service.MeasureManyAsync(new[]
        {
            Path.Combine(tree.Root, "nie-ma-1"),
            Path.Combine(tree.Root, "nie-ma-2")
        });

        Assert.Equal(StorageSizeState.Unavailable, measurement.State);
        Assert.False(measurement.HasValue);
    }

    [Fact]
    public async Task MeasureAsync_SharesSingleScan_ForConcurrentRequests()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        var probe = new FakeStorageFileSystemProbe { DelayPerDirectory = TimeSpan.FromMilliseconds(120) };
        using var service = new StorageSizeMeasurementService(4, null, probe);

        var first = service.MeasureAsync(tree.Root);
        var second = service.MeasureAsync(tree.Root);

        await Task.WhenAll(first, second);

        Assert.Equal(1, probe.EnumerateCallCount);
        Assert.Equal(100, first.Result.LogicalBytes);
        Assert.Equal(100, second.Result.LogicalBytes);
    }

    [Fact]
    public async Task MeasureAsync_HandlesSingleFile()
    {
        using var tree = new StorageTestTree();
        var file = tree.CreateFile("a.bin", 250);

        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync(file);

        Assert.Equal(StorageSizeState.Complete, result.State);
        Assert.Equal(250, result.LogicalBytes);
    }

    [Fact]
    public async Task GetRevision_GrowsOnInvalidate_AndCoversMeasuredResult()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var service = new StorageSizeMeasurementService();

        var result = await service.MeasureAsync(tree.Root);
        var revision = service.GetRevision(tree.Root);

        Assert.True(result.Revision > 0);
        Assert.True(revision >= result.Revision);

        service.Invalidate(tree.Root);

        Assert.True(service.GetRevision(tree.Root) > revision);
    }

    internal static bool TryCreateJunction(string targetPath, string junctionPath)
    {
        try
        {
            if (Directory.Exists(junctionPath))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(junctionPath);
            startInfo.ArgumentList.Add(targetPath);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            if (!process.WaitForExit(15_000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignoruj
                }

                return false;
            }

            return process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }

    internal static void TryDeleteJunction(string junctionPath)
    {
        try
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
        catch
        {
            // ignoruj
        }
    }
}
