using SUSModder.Core.Data;
using Xunit;

namespace SUSModder.Core.Tests.Data;

public class CompatibilityCacheRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public CompatibilityCacheRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveSnapshot_ReplacesPreviousRows()
    {
        await using var db = await CreateDatabaseAsync();
        var repo = new CompatibilityCacheRepository(db);

        repo.SaveSnapshot(
        [
            new CompatibilityCacheEntry
            {
                FullModId = 1,
                FullModVersion = "5.0",
                DllModId = 5,
                DllModVersion = "1.0",
                Status = "W",
                IsExactVersion = true,
                FetchedAtUtc = DateTime.UtcNow
            }
        ], "rev-1", DateTime.UtcNow);

        repo.SaveSnapshot(
        [
            new CompatibilityCacheEntry
            {
                FullModId = 1,
                FullModVersion = "5.1",
                DllModId = 5,
                DllModVersion = "1.1",
                Status = "NW",
                IsExactVersion = true,
                FetchedAtUtc = DateTime.UtcNow
            }
        ], "rev-2", DateTime.UtcNow);

        Assert.Equal(1, repo.Count());
        var entry = repo.GetPair(1, "5.1", 5, "1.1");
        Assert.NotNull(entry);
        Assert.Equal("NW", entry!.Status);
    }

    private static async Task<DatabaseService> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", $"{Guid.NewGuid():N}.db");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();
        return db;
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
            // ignore
        }
    }
}
