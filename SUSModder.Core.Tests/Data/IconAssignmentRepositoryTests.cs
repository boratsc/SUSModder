using SUSModder.Core.Data;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Testy repozytorium przypisań ikon (3.1-04): trwałość, rozróżnienie
/// „brak przypisania” od „jawnie usuniętego przypisania” (tombstone).
/// </summary>
public class IconAssignmentRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public IconAssignmentRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_HasNoAssignments()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new IconAssignmentRepository(db);

        Assert.Empty(repo.GetAll());
        Assert.Null(repo.Get("mod:1"));
        Assert.False(repo.Contains("mod:1"));
    }

    [Fact]
    public async Task Upsert_StoresReference_AndRetrievesByKeyModAndRole()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new IconAssignmentRepository(db);

        repo.Upsert(Assignment(IconAssignmentKeys.ForMod(42), "icon_mod"));
        repo.Upsert(Assignment(IconAssignmentKeys.GlobalVanilla, "icon_vanilla"));
        repo.Upsert(Assignment(IconAssignmentKeys.GlobalDll, "icon_dll"));

        var byKey = repo.Get(IconAssignmentKeys.ForMod(42));
        Assert.NotNull(byKey);
        Assert.Equal("icon_mod", byKey!.AssetId);
        Assert.False(byKey.IsRemoved);

        Assert.Equal("icon_mod", repo.GetForMod(42)!.AssetId);
        Assert.Equal("icon_vanilla", repo.GetGlobal(IconGlobalRole.Vanilla)!.AssetId);
        Assert.Equal("icon_dll", repo.GetGlobal(IconGlobalRole.Dll)!.AssetId);
        Assert.Equal(3, repo.GetAll().Count);
    }

    [Fact]
    public async Task ExplicitRemoval_KeepsDurableTombstone_InsteadOfDeletingRow()
    {
        var dbPath = Path.Combine(_tempDir, "tombstone.db");
        await using (var db = await CreateDatabaseAsync(dbPath))
        {
            var repo = new IconAssignmentRepository(db);
            repo.Upsert(Assignment(IconAssignmentKeys.ForMod(42), "icon_mod"));

            repo.MarkRemoved(IconAssignmentKeys.ForMod(42), 1);
        }

        await using var reopened = await CreateDatabaseAsync(dbPath);
        var reader = new IconAssignmentRepository(reopened);

        var assignment = reader.GetForMod(42);
        Assert.NotNull(assignment);
        Assert.True(assignment!.IsRemoved);
        Assert.Equal(1, assignment.SchemaVersion);
        Assert.Empty(assignment.AssetId);

        // Tombstone to nadal istniejące przypisanie — odróżnia „usunięte” od „brak przypisania”.
        Assert.True(reader.Contains(IconAssignmentKeys.ForMod(42)));
        Assert.Single(reader.GetAll());
    }

    [Fact]
    public async Task AbsentAssignment_IsDistinctFromTombstone()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new IconAssignmentRepository(db);

        Assert.Null(repo.GetForMod(7));

        repo.MarkRemoved(IconAssignmentKeys.ForMod(8), 1);

        Assert.Null(repo.GetForMod(7));
        Assert.NotNull(repo.GetForMod(8));
        Assert.True(repo.GetForMod(8)!.IsRemoved);
    }

    [Fact]
    public async Task Assignments_SurviveNewRepositoryInstance()
    {
        var dbPath = Path.Combine(_tempDir, "persistence.db");

        await using (var db = await CreateDatabaseAsync(dbPath))
        {
            new IconAssignmentRepository(db).Upsert(Assignment(IconAssignmentKeys.ForMod(42), "icon_mod"));
        }

        await using var reopened = await CreateDatabaseAsync(dbPath);
        var reader = new IconAssignmentRepository(reopened);

        Assert.Equal("icon_mod", reader.GetForMod(42)!.AssetId);
    }

    [Fact]
    public async Task Upsert_ReplacesExistingReference_WithoutAddingRows()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new IconAssignmentRepository(db);

        repo.Upsert(Assignment(IconAssignmentKeys.ForMod(42), "icon_first"));
        repo.Upsert(Assignment(IconAssignmentKeys.ForMod(42), "icon_second"));

        Assert.Single(repo.GetAll());
        Assert.Equal("icon_second", repo.GetForMod(42)!.AssetId);
    }

    [Fact]
    public async Task Purge_RemovesRowIncludingTombstone()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new IconAssignmentRepository(db);

        repo.MarkRemoved(IconAssignmentKeys.ForMod(42), 1);

        Assert.True(repo.Purge(IconAssignmentKeys.ForMod(42)));
        Assert.Null(repo.GetForMod(42));
        Assert.False(repo.Purge(IconAssignmentKeys.ForMod(42)));
    }

    [Fact]
    public void AssignmentKeys_AreStable()
    {
        Assert.Equal("mod:42", IconAssignmentKeys.ForMod(42));
        Assert.Equal("global:vanilla", IconAssignmentKeys.ForGlobal(IconGlobalRole.Vanilla));
        Assert.Equal("global:dll", IconAssignmentKeys.ForGlobal(IconGlobalRole.Dll));

        Assert.True(IconAssignmentKeys.TryGetModId("mod:42", out var modId));
        Assert.Equal(42, modId);
        Assert.False(IconAssignmentKeys.TryGetModId("global:vanilla", out _));
        Assert.False(IconAssignmentKeys.TryGetModId("mod:abc", out _));
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

    private static IconAssignment Assignment(string key, string assetId) => new()
    {
        AssignmentKey = key,
        AssetId = assetId,
        Url = $"https://susmodder.app/assets/icons/{assetId}.png",
        Sha256 = new string('a', 64),
        ContentType = "image/png",
        SizeBytes = 1024,
        SchemaVersion = 1,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task<DatabaseService> CreateDatabaseAsync(string? dbPath = null)
    {
        var db = new DatabaseService(dbPath ?? Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }
}
