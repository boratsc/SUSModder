using System.Text.Json;
using SUSModder.Core.Storage;
using Xunit;

namespace SUSModder.Core.Tests.Storage;

public class StorageCleanupPlannerTests
{
    private const string StorageVersion = "2026.9.28";

    [Fact]
    public async Task PlanAsync_ClassifiesVanillaArchiveAsMeasurementOnly_EvenWithValidCache()
    {
        using var tree = new StorageTestTree();
        CreateVanillaCache(tree, StorageVersion, withMarker: true);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var archive = Assert.Single(plan.Entries, e => e.Category == StorageCleanupCategory.VanillaArchive);

        // 3.1-08 jest pomiarowo-tylko: kwalifikacja nie daje uprawnienia do usunięcia.
        Assert.Equal(StorageCleanupEligibility.MeasurementOnly, archive.Eligibility);
        Assert.Equal(StorageCleanupSkipReason.SafetyNotProven, archive.SkipReason);
        Assert.Equal(StorageCleanupRules.ReasonVanillaArchiveRedundant, archive.ReasonCode);
        Assert.True(plan.IsMeasurementOnly);
        Assert.Equal(0, plan.EstimatedRecoverableBytes);
        Assert.Empty(plan.RemovableEntries);
    }

    [Fact]
    public async Task PlanAsync_ClassifiesVanillaArchiveWithoutMarkerAsMeasurementOnly()
    {
        using var tree = new StorageTestTree();
        CreateVanillaCache(tree, StorageVersion, withMarker: false);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var archive = Assert.Single(plan.Entries, e => e.Category == StorageCleanupCategory.VanillaArchive);

        Assert.Equal(StorageCleanupEligibility.MeasurementOnly, archive.Eligibility);
        Assert.Equal(StorageCleanupRules.ReasonVanillaArchiveCacheNotProven, archive.ReasonCode);
        Assert.True(plan.IsMeasurementOnly);
    }

    [Fact]
    public async Task PlanAsync_ClassifiesInstallWorkspacesAsMeasurementOnly()
    {
        using var tree = new StorageTestTree();
        var workspaceId = Guid.NewGuid().ToString("N");
        var workspace = tree.CreateDirectory(Path.Combine("temp", workspaceId));
        tree.CreateFile(Path.Combine("temp", workspaceId, "mod.zip"), 64);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var entry = Assert.Single(plan.Entries, e => e.Category == StorageCleanupCategory.InstallWorkspace);

        Assert.Equal(StorageCleanupEligibility.MeasurementOnly, entry.Eligibility);
        Assert.Equal(StorageCleanupSkipReason.ActiveUseNotProvable, entry.SkipReason);
        Assert.Equal(StorageCleanupRules.ReasonInstallWorkspaceActiveUseUnprovable, entry.ReasonCode);
        Assert.True(Directory.Exists(workspace));
    }

    [Fact]
    public async Task PlanAsync_ProtectsModInstallations()
    {
        using var tree = new StorageTestTree();
        var install = tree.CreateDirectory("My Mod");
        tree.CreateFile(Path.Combine("My Mod", "Among Us.exe"), 128);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest
        {
            LibraryRoot = tree.Root,
            KnownInstallPaths = new[] { install }
        });

        var entry = Assert.Single(plan.Entries, e => e.Category == StorageCleanupCategory.ModInstallations);

        Assert.Equal(StorageCleanupEligibility.Protected, entry.Eligibility);
        Assert.Equal(StorageCleanupSkipReason.ProtectedData, entry.SkipReason);
        Assert.Equal(128, entry.MeasuredBytes);
    }

    [Fact]
    public async Task PlanAsync_ProtectsExtractedVanillaCache()
    {
        using var tree = new StorageTestTree();
        CreateVanillaCache(tree, StorageVersion, withMarker: true);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        var extracted = Assert.Single(plan.Entries, e => e.Category == StorageCleanupCategory.VanillaExtractedCache);

        Assert.Equal(StorageCleanupEligibility.Protected, extracted.Eligibility);
    }

    [Fact]
    public async Task PlanAsync_DoesNotDeleteAnything()
    {
        using var tree = new StorageTestTree();
        CreateVanillaCache(tree, StorageVersion, withMarker: true);
        tree.CreateFile(Path.Combine("temp", "abc", "mod.zip"), 10);

        var planner = new StorageCleanupPlanner();

        await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        Assert.True(File.Exists(Path.Combine(tree.Root, "Among Us - Vanilla", StorageVersion + ".7z")));
        Assert.True(File.Exists(Path.Combine(tree.Root, "temp", "abc", "mod.zip")));
    }

    [Fact]
    public async Task PlanAsync_ReturnsUnavailablePlan_ForMissingLibrary()
    {
        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest
        {
            LibraryRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });

        Assert.Empty(plan.Entries);
        Assert.Equal(0, plan.MeasuredBytes);
        Assert.Equal(StorageSizeState.Unavailable, plan.State);
        Assert.Equal(StorageSizeErrorCode.PathNotFound, plan.ErrorCode);
        Assert.True(plan.IsMeasurementOnly);
    }

    [Fact]
    public async Task PlanAsync_ReportsUnavailable_WhenLibraryRootCannotBeEnumerated()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        var probe = new FakeStorageFileSystemProbe();
        probe.AccessDeniedDirectories.Add(tree.Root);

        using var measurement = new StorageSizeMeasurementService(4, null, probe);
        using var planner = new StorageCleanupPlanner(measurement, probe);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Błąd inwentaryzacji nie jest cicho zamieniany na „0 B i komplet”.
        Assert.Equal(StorageSizeState.Unavailable, plan.State);
        Assert.Equal(StorageSizeErrorCode.AccessDenied, plan.ErrorCode);
        Assert.Equal(0, plan.MeasuredBytes);
    }

    [Fact]
    public async Task PlanAsync_ReportsPartial_WhenChildDirectoryCannotBeEnumerated()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        var broken = tree.CreateDirectory("broken");
        tree.CreateFile(Path.Combine("broken", "b.bin"), 50);

        var probe = new FakeStorageFileSystemProbe();
        probe.AccessDeniedDirectories.Add(broken);

        using var measurement = new StorageSizeMeasurementService(4, null, probe);
        using var planner = new StorageCleanupPlanner(measurement, probe);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        Assert.Equal(StorageSizeState.Partial, plan.State);
    }

    [Fact]
    public async Task PlanAsync_DoesNotPromoteLinkedChildToRoot()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        using var external = new StorageTestTree();
        external.CreateFile("outside.bin", 4096);

        var junction = Path.Combine(tree.Root, "link-to-external");
        if (!StorageSizeMeasurementServiceTests.TryCreateJunction(external.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            var planner = new StorageCleanupPlanner();

            var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

            var linked = Assert.Single(plan.Entries, e => e.IsLink);
            Assert.Equal(StorageCleanupSkipReason.PathIsLink, linked.SkipReason);
            Assert.Equal(StorageSizeState.Unavailable, linked.SizeState);
            Assert.Equal(0, linked.MeasuredBytes);

            // Obce drzewo nie zostało zmierzone.
            Assert.Equal(100, plan.MeasuredBytes);
            Assert.Equal(StorageSizeState.Partial, plan.State);
        }
        finally
        {
            StorageSizeMeasurementServiceTests.TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task PlanAsync_RejectsLinkedLibraryRoot_BeforeEnumeration()
    {
        using var external = new StorageTestTree();
        // Zwykłe pliki — bez podkatalogów.
        external.CreateFile("ordinary.bin", 4096);

        var junction = Path.Combine(Path.GetTempPath(), "susmodder-storage-tests", Guid.NewGuid().ToString("N"));
        if (!StorageSizeMeasurementServiceTests.TryCreateJunction(external.Root, junction))
        {
            Assert.Skip("System nie pozwala utworzyć junctiona w środowisku testowym.");
            return;
        }

        try
        {
            var planner = new StorageCleanupPlanner();

            var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = junction });

            // Preflight: biblioteka będąca linkiem jest odrzucona, zanim policzymy cokolwiek.
            Assert.Equal(StorageSizeState.Unavailable, plan.State);
            Assert.Equal(StorageSizeErrorCode.PathIsLink, plan.ErrorCode);
            Assert.Equal(0, plan.MeasuredBytes);
            Assert.Empty(plan.Entries);
        }
        finally
        {
            StorageSizeMeasurementServiceTests.TryDeleteJunction(junction);
        }
    }

    [Fact]
    public async Task PlanAsync_ReportsPartial_WhenDirectFileAttributesCannotBeRead()
    {
        using var tree = new StorageTestTree();
        var direct = tree.CreateFile("direct.bin", 100);

        var probe = new FakeStorageFileSystemProbe();
        probe.FailingAttributePaths.Add(direct);

        using var measurement = new StorageSizeMeasurementService(4, null, probe);
        using var planner = new StorageCleanupPlanner(measurement, probe);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Nieczytelne atrybuty = nie wiemy, czy to link — suma nie może być podawana jako kompletna.
        Assert.Equal(StorageSizeState.Partial, plan.State);
    }

    [Fact]
    public async Task PlanAsync_ReportsPartial_WhenDirectFileLengthCannotBeRead()
    {
        using var tree = new StorageTestTree();
        var direct = tree.CreateFile("direct.bin", 100);

        var probe = new FakeStorageFileSystemProbe();
        probe.FailingLengthPaths.Add(direct);

        using var measurement = new StorageSizeMeasurementService(4, null, probe);
        using var planner = new StorageCleanupPlanner(measurement, probe);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Rozmiar pliku bez odczytu = suma zaniżona bez ostrzeżenia — plan musi być niepełny.
        Assert.Equal(StorageSizeState.Partial, plan.State);
    }

    [Fact]
    public async Task PlanAsync_PropagatesUnavailableState_FromWholeLibraryMeasurement()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);

        var denyingProbe = new FakeStorageFileSystemProbe();
        denyingProbe.AccessDeniedDirectories.Add(tree.Root);

        using var measurement = new StorageSizeMeasurementService(4, null, denyingProbe);
        using var planner = new StorageCleanupPlanner(measurement);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest
        {
            LibraryRoot = tree.Root,
            MeasureLibraryTotal = true
        });

        // Pomiar całej biblioteki się nie udał — plan nie może raportować „Complete”.
        Assert.Equal(StorageSizeState.Unavailable, plan.State);
        Assert.Equal(StorageSizeErrorCode.AccessDenied, plan.ErrorCode);
    }

    [Fact]
    public async Task PlanAsync_PropagatesPartialState_FromWholeLibraryMeasurement()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        var broken = tree.CreateDirectory("broken");
        tree.CreateFile(Path.Combine("broken", "b.bin"), 50);

        var denyingProbe = new FakeStorageFileSystemProbe();
        denyingProbe.AccessDeniedDirectories.Add(broken);

        using var measurement = new StorageSizeMeasurementService(4, null, denyingProbe);
        using var planner = new StorageCleanupPlanner(measurement);

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest
        {
            LibraryRoot = tree.Root,
            MeasureLibraryTotal = true
        });

        Assert.Equal(StorageSizeState.Partial, plan.State);
    }

    [Fact]
    public async Task PlanAsync_ReportsPartial_WhenVanillaExtractedRootContainsFiles()
    {
        using var tree = new StorageTestTree();
        CreateVanillaCache(tree, StorageVersion, withMarker: true);

        var stray = Path.Combine(tree.Root, "Among Us - Vanilla", "extracted", "stray.bin");
        File.WriteAllBytes(stray, new byte[128]);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        // Plik w katalogu rozpakowanego cache nie jest katalogiem wersji — plan musi to ujawnić.
        Assert.Equal(StorageSizeState.Partial, plan.State);
        Assert.Contains(plan.Entries, e =>
            e.Category == StorageCleanupCategory.Unclassified
            && e.Path.EndsWith("stray.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanAsync_ReportsMeasuredBytes_ForKnownTree()
    {
        using var tree = new StorageTestTree();
        tree.CreateFile("a.bin", 100);
        tree.CreateFile("sub/b.bin", 200);

        var planner = new StorageCleanupPlanner();

        var plan = await planner.PlanAsync(new StorageCleanupPlanRequest { LibraryRoot = tree.Root });

        Assert.Equal(300, plan.MeasuredBytes);
        Assert.Equal(StorageSizeState.Complete, plan.State);
    }

    internal static void CreateVanillaCache(StorageTestTree tree, string storageVersion, bool withMarker)
    {
        var vanillaRoot = Path.Combine(tree.Root, "Among Us - Vanilla");
        var extracted = Path.Combine(vanillaRoot, "extracted", storageVersion);

        Directory.CreateDirectory(extracted);
        File.WriteAllText(Path.Combine(extracted, "Among Us.exe"), "stub");
        Directory.CreateDirectory(Path.Combine(extracted, "Among Us_Data"));

        if (withMarker)
        {
            var marker = JsonSerializer.Serialize(new
            {
                AmongVersion = "2026.9.28",
                StorageVersion = storageVersion,
                ManifestId = "manifest-1",
                BuildId = "build-1",
                Source = "Fallback7z",
                FetchedAt = DateTimeOffset.UtcNow
            });

            File.WriteAllText(Path.Combine(extracted, ".vanilla-cache.json"), marker);
        }

        var archive = Path.Combine(vanillaRoot, storageVersion + ".7z");
        File.WriteAllBytes(archive, new byte[2048]);
    }
}
