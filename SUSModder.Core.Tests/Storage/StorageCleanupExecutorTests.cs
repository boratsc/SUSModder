using SUSModder.Core.Storage;
using Xunit;

namespace SUSModder.Core.Tests.Storage;

/// <summary>
/// 3.1-08 pozostaje pomiarowo-tylko: wykonawca nie usuwa niczego.
/// Testy sprawdzają, że nawet kwalifikowalne archiwum, link w przodkach i zmieniony plan
/// nie prowadzą do usunięcia pliku.
/// </summary>
public class StorageCleanupExecutorTests
{
    private const string StorageVersion = "2026.9.28";

    [Fact]
    public async Task ExecuteAsync_NeverRemovesQualifyingVanillaArchive()
    {
        using var tree = new StorageTestTree();
        StorageCleanupPlannerTests.CreateVanillaCache(tree, StorageVersion, withMarker: true);

        var archive = Path.Combine(tree.Root, "Among Us - Vanilla", StorageVersion + ".7z");

        var planner = new StorageCleanupPlanner();
        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Kwalifikacja informacyjna istnieje, ale uprawnienia do usunięcia nie ma.
        Assert.Contains(plan.Entries, e => e.ReasonCode == StorageCleanupRules.ReasonVanillaArchiveRedundant);
        Assert.True(plan.IsMeasurementOnly);

        var executor = new StorageCleanupExecutor();
        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(0, result.RemovedBytes);
        Assert.Equal(0, result.FailedCount);
        Assert.True(File.Exists(archive));

        Assert.All(result.Outcomes, outcome =>
        {
            Assert.Equal(StorageCleanupOutcome.Skipped, outcome.Outcome);
            Assert.Equal(StorageCleanupSkipReason.SafetyNotProven, outcome.Reason);
        });
    }

    [Fact]
    public async Task ExecuteAsync_NeverRemovesFileBehindAncestorLink()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("real/child.bin", 100);

        var junction = Path.Combine(tree.Root, "linked");
        if (!StorageSizeMeasurementServiceTests.TryCreateJunction(Path.Combine(tree.Root, "real"), junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            var child = Path.Combine(junction, "child.bin");

            var planner = new StorageCleanupPlanner();
            var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

            var executor = new StorageCleanupExecutor();
            var result = await executor.ExecuteAsync(plan);

            Assert.Equal(0, result.RemovedCount);
            Assert.True(File.Exists(child));
            Assert.True(File.Exists(Path.Combine(tree.Root, "real", "child.bin")));
        }
        finally
        {
            StorageSizeMeasurementServiceTests.TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NeverRemovesFile_WhenPlanChangedAfterPreview()
    {
        using var tree = new StorageTestTree();
        StorageCleanupPlannerTests.CreateVanillaCache(tree, StorageVersion, withMarker: true);

        var archive = Path.Combine(tree.Root, "Among Us - Vanilla", StorageVersion + ".7z");

        var planner = new StorageCleanupPlanner();
        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Podmiana pliku po podglądzie planu.
        File.WriteAllBytes(archive, new byte[4096]);

        var executor = new StorageCleanupExecutor();
        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(0, result.RemovedCount);
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task ExecuteAsync_NeverRemovesNonAllowlistedEntries()
    {
        using var tree = new StorageTestTree();
        var workspaceId = Guid.NewGuid().ToString("N");
        var workspace = tree.CreateDirectory(Path.Combine("temp", workspaceId));
        tree.CreateFile(Path.Combine("temp", workspaceId, "mod.zip"), 10);
        tree.CreateFile("My Mod/Among Us.exe", 128);

        var planner = new StorageCleanupPlanner();
        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var executor = new StorageCleanupExecutor();
        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(plan.Entries.Count, result.SkippedCount);
        Assert.True(Directory.Exists(workspace));
        Assert.True(File.Exists(Path.Combine(tree.Root, "My Mod", "Among Us.exe")));
        Assert.All(result.Outcomes, o => Assert.Equal(StorageCleanupOutcome.Skipped, o.Outcome));
    }

    [Fact]
    public async Task ExecuteAsync_IsIdempotent_AndNeverMutatesLibrary()
    {
        using var tree = new StorageTestTree();
        StorageCleanupPlannerTests.CreateVanillaCache(tree, StorageVersion, withMarker: true);
        tree.CreateFile(Path.Combine("temp", Guid.NewGuid().ToString("N"), "mod.zip"), 10);

        var planner = new StorageCleanupPlanner();
        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var executor = new StorageCleanupExecutor();
        var first = await executor.ExecuteAsync(plan);
        var second = await executor.ExecuteAsync(plan);

        Assert.Equal(0, first.RemovedCount);
        Assert.Equal(0, second.RemovedCount);
        Assert.Equal(first.SkippedCount, second.SkippedCount);
        Assert.True(File.Exists(Path.Combine(tree.Root, "Among Us - Vanilla", StorageVersion + ".7z")));
    }
}
