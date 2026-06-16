using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using Xunit;

namespace SUSModder.Core.Tests.Services;

public class CatalogSyncServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CatalogSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task RefreshCatalogIfDueAsync_304_DoesNotModifyRepository()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db);
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);
        syncState.SaveSuccess(CatalogSyncService.CatalogStateKey, "etag-1", null, DateTime.UtcNow.AddMinutes(-10));

        var api = new FakeApiClient
        {
            CatalogHandler = (_, etag) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 304,
                ETag = etag ?? "etag-1"
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache);
        var result = await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.NotModified, result.Status);
        Assert.Equal(0, modRepo.ApplyCount);
    }

    [Fact]
    public async Task RefreshCatalogIfDueAsync_InvalidEmptyResponse_UsesLocalCache()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db)
        {
            LocalMods =
            [
                new ModConfiguration { Id = 1, ModName = "Town of Us", ModType = "full", ModVersion = "1.0", GitHubRepoOrLink = "https://example.com/a.zip" }
            ]
        };
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);

        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200,
                Data = []
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache);
        var result = await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.OfflineUsingCache, result.Status);
        Assert.Equal(0, modRepo.ApplyCount);
    }

    [Fact]
    public async Task RefreshCatalogIfDueAsync_ValidResponse_AppliesRepository()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db);
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);

        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200,
                ETag = "etag-new",
                Data =
                [
                    new CatalogItemDto
                    {
                        Id = 1,
                        Name = "Town of Us",
                        Type = "full",
                        CurrentVersion = "5.4.0",
                        GitHubProjectUrl = "https://example.com/a.zip"
                    }
                ]
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache);
        var result = await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Equal(1, modRepo.ApplyCount);
    }

    private static CatalogSyncService CreateService(
        ISUSModderApiClient api,
        IModRepository modRepo,
        ICatalogSyncStateRepository syncState,
        ICompatibilityCacheRepository compatCache) =>
        new(api, modRepo, syncState, compatCache, new NullDiagnostics());

    private async Task<DatabaseService> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.db");
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
            // ignore cleanup errors in tests
        }
    }

    private sealed class NullDiagnostics : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }

    private sealed class RecordingModRepository : IModRepository
    {
        private readonly DatabaseService _db;

        public RecordingModRepository(DatabaseService db) => _db = db;

        public List<ModConfiguration> LocalMods { get; set; } = [];
        public int ApplyCount { get; private set; }

        public List<ModConfiguration> GetAllMods() => LocalMods;

        public Task<bool> ApplyRemoteCatalogAsync(List<ModConfiguration> apiMods)
        {
            ApplyCount++;
            LocalMods = apiMods;
            return Task.FromResult(true);
        }

        public void SaveAllMods(List<ModConfiguration> mods) => LocalMods = mods;
        public void UpdateMod(ModConfiguration mod) { }
        public void AddMod(ModConfiguration mod) { }
        public void DeleteMod(int id) { }
        public void UpsertMod(ModConfiguration mod) { }
        public void ClearCache() { }
        public Task<List<ModConfiguration>> FetchAndMergeFromApiAsync() => Task.FromResult(LocalMods);
        public Task<bool> RefreshFromApiAsync() => Task.FromResult(false);
        public void SaveModVirusTotalData(int modId, string? scanStatus, string? permalink, string? lastCheckedAt, string? stats, string? aiReviewStatus, string? aiReviewSummary) { }
    }

    private sealed class FakeApiClient : ISUSModderApiClient
    {
        public required Func<CatalogQuery?, string?, SusModderApiResult<List<CatalogItemDto>>> CatalogHandler;

        public string BaseUrl => "https://example.test/v2";
        public string StaticAssetsBaseUrl => "https://example.test";

        public Task<SusModderApiResult<List<CatalogItemDto>>> GetCatalogAsync(
            CatalogQuery? query = null,
            string? ifNoneMatch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogHandler(query, ifNoneMatch));

        public void Dispose() { }

        public string BuildModDownloadUrl(int modId, string version, string platform, string arch = "x86") =>
            $"{BaseUrl}/downloads/mod/{modId}/{version}?platform={platform}";
        public Task<List<ModConfiguration>> GetCatalogAsModConfigurationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<ModConfiguration>());
        public Task<SusModderApiResult<CatalogMetaDto>> GetCatalogMetaAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CatalogModDetailDto>> GetCatalogModDetailAsync(int modId, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CatalogVersionsDto>> GetCatalogVersionsAsync(int modId, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<List<CatalogChangelogEntryDto>>> GetCatalogChangelogAsync(int modId, string lang = "pl", int limit = 5, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CompatibilityDataDto>> GetCompatibilityAsync(CompatibilityQueryParams query, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CompatibilitySnapshotDto>> GetCompatibilitySnapshotAsync(bool onlyCurrentVersions = true, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<List<AmongUsVersionDto>>> GetAmongUsVersionsAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<AmongUsVersionDto>> GetAmongUsVersionAsync(string dbValue, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<System.Text.Json.JsonElement>> GetRolesAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<System.Text.Json.JsonElement>> GetDiscordFavoritesPublicAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<System.Text.Json.JsonElement>> GetDiscordServerCountsAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<OnlineUsersDto>> GetOnlineAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<System.Text.Json.JsonElement>> GetReleasesAsync(string? channel = null, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SendHeartbeatAsync(object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HttpResponseMessage> SendAsync(SusModderApiRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<ModVariantVirusTotalReportDto>> GetModVariantVirusTotalReportAsync(int modId, string version, string platform, string arch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
