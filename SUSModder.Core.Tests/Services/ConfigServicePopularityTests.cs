using Moq;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

public class ConfigServicePopularityTests
{
    [Fact]
    public void MergeInstallDataFromPrevious_PreservesStoredPopularity()
    {
        var method = typeof(ConfigManager).GetMethod("MergeInstallDataFromPrevious",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var target = new List<ModConfiguration> { new() { Id = 1, ModName = "One", Popularity30d = 0 } };
        var previous = new List<ModConfiguration> { new() { Id = 1, ModName = "One", Popularity30d = 57 } };

        method.Invoke(null, [target, previous]);

        Assert.Equal(57, target.Single().Popularity30d);
    }

    [Fact]
    public async Task UpdateSingleModConfigAsync_PreservesStoredPopularityInSqlite()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await using (var db = new DatabaseService(Path.Combine(dir, "test.db")))
            {
                await db.InitializeAsync();
                var api = new Mock<ISUSModderApiClient>().Object;
                var repo = new ModRepository(db, api);
                repo.SaveAllMods([new ModConfiguration { Id = 1, ModName = "One", ModType = "full", Popularity30d = 57 }]);
                ConfigManager.SetRepository(repo);

                Assert.True(await new ConfigService().UpdateSingleModConfigAsync(
                    new ModConfiguration { Id = 1, ModName = "One", ModType = "full", ModVersion = "2" }));

                Assert.Equal(57, repo.GetAllMods().Single().Popularity30d);
            }
        }
        finally
        {
            ConfigManager.SetRepository(new EmptyModRepository());
        }
    }

    private sealed class EmptyModRepository : IModRepository
    {
        public List<ModConfiguration> GetAllMods() => [];
        public Task<bool> ApplyRemoteCatalogAsync(List<ModConfiguration> apiMods) => Task.FromResult(false);
        public bool UpdatePopularity(IReadOnlyDictionary<int, int> popularityById) => false;
        public void SaveAllMods(List<ModConfiguration> mods) { }
        public void UpdateMod(ModConfiguration mod) { }
        public void AddMod(ModConfiguration mod) { }
        public void DeleteMod(int id) { }
        public void UpsertMod(ModConfiguration mod) { }
        public void ClearCache() { }
        public Task<List<ModConfiguration>> FetchAndMergeFromApiAsync() => Task.FromResult(new List<ModConfiguration>());
        public Task<bool> RefreshFromApiAsync() => Task.FromResult(false);
        public void SaveModVirusTotalData(int modId, string? scanStatus, string? permalink, string? lastCheckedAt, string? stats, string? aiReviewStatus, string? aiReviewSummary) { }
    }
}
