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

    [Fact]
    public async Task RefreshCatalog_MergesPopularityById_AndKeepsStoredValueWhenDegraded()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db)
        {
            LocalMods = [new ModConfiguration { Id = 2, ModName = "Old", Popularity30d = 88 }]
        };
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200,
                Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "1", GitHubProjectUrl = "https://example.com" },
                        new CatalogItemDto { Id = 2, Name = "Two", Type = "full", CurrentVersion = "1", GitHubProjectUrl = "https://example.com" }]
            },
            PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>>
            {
                StatusCode = 200, IsDegraded = true, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 0 }]
            }
        };
        var service = CreateService(api, modRepo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db));
        await service.RefreshCatalogIfDueAsync(force: true);
        Assert.Equal(0, modRepo.LocalMods.Single(x => x.Id == 1).Popularity30d);
        Assert.Equal(88, modRepo.LocalMods.Single(x => x.Id == 2).Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalog_NotModified_StillAppliesPopularityFeed()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db)
        {
            LocalMods = [new ModConfiguration { Id = 1, ModName = "One", Popularity30d = 4 }]
        };
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 304 },
            PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>>
            {
                StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 12 }]
            }
        };
        var service = CreateService(api, modRepo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db));

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(12, modRepo.LocalMods.Single().Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_PreservesAndPersistsPopularity()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new ModRepository(db, new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 404 }
        });
        var seed = new ModConfiguration
        {
            Id = 1, ModName = "One", ModType = "full", ModVersion = "1",
            GitHubRepoOrLink = "https://example.com", Popularity30d = 88
        };
        modRepo.SaveAllMods([seed]);
        var sync = new CatalogSyncStateRepository(db);
        var cache = new CompatibilityCacheRepository(db);
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "2", GitHubProjectUrl = "https://example.com" }]
            },
            PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>>
                { StatusCode = 200, IsDegraded = true, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 0 }] }
        };
        await CreateService(api, modRepo, sync, cache).RefreshCatalogIfDueAsync(force: true);
        Assert.Equal(88, new ModRepository(db, api).GetAllMods().Single().Popularity30d);

        sync.SaveSuccess(CatalogSyncService.CatalogStateKey, "etag", null, DateTime.UtcNow.AddMinutes(-10));
        api.CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 304 };
        api.PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>>
            { StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 12 }] };
        await CreateService(api, modRepo, sync, cache).RefreshCatalogIfDueAsync(force: true);
        Assert.Equal(12, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_304PopularityUpdatesOnlyPopularityAndPreservesCachedObjects()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient { CatalogHandler = (_, _) => new() { StatusCode = 304 } };
        var repo = new ModRepository(db, api);
        repo.SaveAllMods([new ModConfiguration { Id = 1, ModName = "Before", ModType = "full", Popularity30d = 4 }]);
        var list = repo.GetAllMods();
        var instance = list.Single();
        instance.Description = "keep me";
        api.PopularityHandler = () => new() { StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 12 }] };
        var result = await CreateService(api, repo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db)).RefreshCatalogIfDueAsync(true);
        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Same(list, repo.GetAllMods());
        Assert.Same(instance, repo.GetAllMods().Single());
        Assert.Equal("keep me", instance.Description);
        Assert.Equal(12, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_304MalformedPopularityKeepsStoredValuesWithoutBackoff()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient { CatalogHandler = (_, _) => new() { StatusCode = 304 } };
        var repo = new ModRepository(db, api);
        repo.SaveAllMods([new ModConfiguration { Id = 1, ModName = "One", ModType = "full", Popularity30d = 4 }]);
        api.PopularityHandler = () => new() { StatusCode = 200, Data = [null!] };
        var sync = new CatalogSyncStateRepository(db);
        var result = await CreateService(api, repo, sync, new CompatibilityCacheRepository(db)).RefreshCatalogIfDueAsync(true);
        Assert.Equal(CatalogSyncStatus.NotModified, result.Status);
        Assert.Equal(4, repo.GetAllMods().Single().Popularity30d);
        Assert.Equal(0, sync.Get(CatalogSyncService.CatalogStateKey)?.FailureCount ?? 0);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_200MalformedPopularityKeepsStoredValuesWithoutBackoff()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new() { StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "2" }] },
            PopularityHandler = () => new() { StatusCode = 200, Data = [null!] }
        };
        var repo = new ModRepository(db, api);
        repo.SaveAllMods([new ModConfiguration { Id = 1, ModName = "One", ModType = "full", Popularity30d = 4 }]);
        var sync = new CatalogSyncStateRepository(db);

        var result = await CreateService(api, repo, sync, new CompatibilityCacheRepository(db)).RefreshCatalogIfDueAsync(true);

        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Equal(4, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
        Assert.Equal(0, sync.Get(CatalogSyncService.CatalogStateKey)?.FailureCount ?? 0);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_NegativePopularityIsPersistedAsZero()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new() { StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "1" }] },
            PopularityHandler = () => new() { StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = -5 }] }
        };
        var repo = new ModRepository(db, api);

        await CreateService(api, repo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db)).RefreshCatalogIfDueAsync(true);

        Assert.Equal(0, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_304UnchangedPopularityDoesNotWrite()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient { CatalogHandler = (_, _) => new() { StatusCode = 304 } };
        var repo = new ModRepository(db, api);
        repo.SaveAllMods([new ModConfiguration { Id = 1, ModName = "One", ModType = "full", Popularity30d = 4 }]);
        using (var trigger = db.GetConnection().CreateCommand())
        {
            trigger.CommandText = "CREATE TABLE popularity_writes (n INTEGER); INSERT INTO popularity_writes VALUES (0); CREATE TRIGGER popularity_write AFTER UPDATE OF popularity_30d ON mods BEGIN UPDATE popularity_writes SET n = n + 1; END;";
            trigger.ExecuteNonQuery();
        }
        api.PopularityHandler = () => new() { StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 4 }] };
        var service = CreateService(api, repo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db));
        var result = await service.RefreshCatalogIfDueAsync(true);
        Assert.Equal(CatalogSyncStatus.NotModified, result.Status);
        using var cmd = db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT n FROM popularity_writes";
        Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_PopularityNotFound_PreservesStoredValue()
    {
        await using var db = await CreateDatabaseAsync();
        var seed = new ModConfiguration
        {
            Id = 1, ModName = "One", ModType = "full", ModVersion = "1",
            GitHubRepoOrLink = "https://example.com", Popularity30d = 88
        };
        var repo = new ModRepository(db, new FakeApiClient { CatalogHandler = (_, _) => new() });
        repo.SaveAllMods([seed]);
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "2", GitHubProjectUrl = "https://example.com" }]
            },
            PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>> { StatusCode = 404 }
        };

        var result = await CreateService(api, repo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db))
            .RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Equal(88, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_PopularityRequestThrows_PreservesValueAndDoesNotFailCatalog()
    {
        await using var db = await CreateDatabaseAsync();
        var seed = new ModConfiguration
        {
            Id = 1, ModName = "One", ModType = "full", ModVersion = "1",
            GitHubRepoOrLink = "https://example.com", Popularity30d = 88
        };
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "2", GitHubProjectUrl = "https://example.com" }]
            },
            PopularityHandler = () => throw new HttpRequestException("network failure")
        };
        var repo = new ModRepository(db, api);
        repo.SaveAllMods([seed]);
        var sync = new CatalogSyncStateRepository(db);

        var result = await CreateService(api, repo, sync, new CompatibilityCacheRepository(db))
            .RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Equal(88, new ModRepository(db, api).GetAllMods().Single().Popularity30d);
        Assert.Equal(0, sync.Get(CatalogSyncService.CatalogStateKey)?.FailureCount ?? 0);
    }

    [Fact]
    public async Task RefreshCatalogWithSqlite_FreshInstallPersistsPopularityAndToleratesDuplicateFeedIds()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200, Data = [new CatalogItemDto { Id = 1, Name = "One", Type = "full", CurrentVersion = "1", GitHubProjectUrl = "https://example.com" }]
            },
            PopularityHandler = () => new SusModderApiResult<List<CatalogPopularityItemDto>>
                { StatusCode = 200, Data = [new CatalogPopularityItemDto { Id = 1, Popularity = 12 }, new CatalogPopularityItemDto { Id = 1, Popularity = 99 }] }
        };
        var repo = new ModRepository(db, api);
        var result = await CreateService(api, repo, new CatalogSyncStateRepository(db), new CompatibilityCacheRepository(db)).RefreshCatalogIfDueAsync(force: true);
        Assert.NotEqual(CatalogSyncStatus.OfflineUsingCache, result.Status);
        Assert.Equal(12, new ModRepository(db, api).GetAllMods().Single(x => x.Id == 1).Popularity30d);
    }

    [Fact]
    public async Task RefreshIconAssignmentsAsync_UpdateGlobalVanillaRole()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db);
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);
        var icons = new IconAssignmentRepository(db);

        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 200, Data = [] },
            MetaHandler = _ => new SusModderApiResult<CatalogMetaDto>
            {
                StatusCode = 200,
                ETag = "meta-27",
                Data = new CatalogMetaDto
                {
                    IconsSchemaVersion = 1,
                    IconsRevision = "27",
                    Icons = new CatalogGlobalIconsDto
                    {
                        Vanilla = ValidIcon("icon_vanilla", 'a'),
                        VanillaMemberPresent = true
                    }
                }
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache, icons);
        await service.RefreshIconAssignmentsAsync();

        var vanilla = icons.GetGlobal(IconGlobalRole.Vanilla);
        Assert.NotNull(vanilla);
        Assert.False(vanilla!.IsRemoved);
        Assert.Equal("icon_vanilla", vanilla.AssetId);
        Assert.Equal(1, vanilla.SchemaVersion);
        Assert.False(service.IsIconMetadataStale("27"));
        Assert.True(service.IsIconMetadataStale("28"));
    }

    [Fact]
    public async Task RefreshIconAssignmentsAsync_ExplicitNullRole_KeepsTombstone()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db);
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);
        var icons = new IconAssignmentRepository(db);

        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 200, Data = [] },
            MetaHandler = _ => new SusModderApiResult<CatalogMetaDto>
            {
                StatusCode = 200,
                ETag = "meta-26",
                Data = new CatalogMetaDto
                {
                    IconsSchemaVersion = 1,
                    IconsRevision = "26",
                    Icons = new CatalogGlobalIconsDto { Vanilla = null, VanillaMemberPresent = true }
                }
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache, icons);
        await service.RefreshIconAssignmentsAsync();

        var vanilla = icons.GetGlobal(IconGlobalRole.Vanilla);
        Assert.NotNull(vanilla);
        Assert.True(vanilla!.IsRemoved);
    }

    [Fact]
    public async Task RefreshIconAssignmentsFromCatalogAsync_AppliesModIconsWithoutCatalogWrite()
    {
        await using var db = await CreateDatabaseAsync();
        var modRepo = new RecordingModRepository(db);
        var syncState = new CatalogSyncStateRepository(db);
        var compatCache = new CompatibilityCacheRepository(db);
        var icons = new IconAssignmentRepository(db);

        var api = new FakeApiClient
        {
            CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>>
            {
                StatusCode = 200,
                ETag = "catalog-icons-39",
                Data =
                [
                    new CatalogItemDto
                    {
                        Id = 7,
                        Name = "Syzyfowy ToU",
                        Type = "full",
                        CurrentVersion = "7.2.7",
                        Icon = ValidIcon("icon_25409cc1018ca8fb", 'b'),
                        IconMemberPresent = true
                    }
                ]
            },
            MetaHandler = _ => new SusModderApiResult<CatalogMetaDto>
            {
                StatusCode = 200,
                ETag = "meta-icons-39",
                Data = new CatalogMetaDto { IconsSchemaVersion = 1, IconsRevision = "39" }
            }
        };

        var service = CreateService(api, modRepo, syncState, compatCache, icons);
        await service.RefreshIconAssignmentsFromCatalogAsync();

        var modIcon = icons.GetForMod(7);
        Assert.NotNull(modIcon);
        Assert.False(modIcon!.IsRemoved);
        Assert.Equal("icon_25409cc1018ca8fb", modIcon.AssetId);

        // Ikony nie mogą zużyć ETagu katalogu ani nadpisać listy modów.
        Assert.Equal(0, modRepo.ApplyCount);
        Assert.Null(syncState.Get(CatalogSyncService.CatalogStateKey));
        Assert.False(service.IsIconMetadataStale("39"));
    }

    private static CatalogIconDto ValidIcon(string assetId, char hexChar) => new()
    {
        AssetId = assetId,
        Url = $"https://susmodder-cdn.ovh/assets/icons/{new string(hexChar, 64)}.png",
        Sha256 = new string(hexChar, 64),
        ContentType = "image/png",
        SizeBytes = 1024
    };

    private static CatalogSyncService CreateService(
        ISUSModderApiClient api,
        IModRepository modRepo,
        ICatalogSyncStateRepository syncState,
        ICompatibilityCacheRepository compatCache,
        IIconAssignmentRepository? iconAssignments = null) =>
        new(api, modRepo, syncState, compatCache, new NullDiagnostics(), iconAssignments);

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
        public bool UpdatePopularity(IReadOnlyDictionary<int, int> popularityById)
        {
            var changed = false;
            foreach (var mod in LocalMods)
                if (popularityById.TryGetValue(mod.Id, out var value) && mod.Popularity30d != value)
                {
                    mod.Popularity30d = value;
                    changed = true;
                }
            return changed;
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
        public Func<string?, SusModderApiResult<CatalogMetaDto>>? MetaHandler;
        public Func<SusModderApiResult<List<CatalogPopularityItemDto>>>? PopularityHandler;

        public string BaseUrl => "https://example.test/v2";
        public string StaticAssetsBaseUrl => "https://example.test";

        public Task<SusModderApiResult<List<CatalogItemDto>>> GetCatalogAsync(
            CatalogQuery? query = null,
            string? ifNoneMatch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogHandler(query, ifNoneMatch));
        public Task<SusModderApiResult<List<CatalogPopularityItemDto>>> GetCatalogPopularityAsync(int days = 30, CancellationToken cancellationToken = default) =>
            Task.FromResult(PopularityHandler?.Invoke() ?? new SusModderApiResult<List<CatalogPopularityItemDto>> { StatusCode = 404 });

        public void Dispose() { }

        public string BuildModDownloadUrl(int modId, string version, string platform, string arch = "x86") =>
            $"{BaseUrl}/downloads/mod/{modId}/{version}?platform={platform}";
        public Task<List<ModConfiguration>> GetCatalogAsModConfigurationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<ModConfiguration>());
        public Task<SusModderApiResult<CatalogMetaDto>> GetCatalogMetaAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) =>
            MetaHandler is { } handler
                ? Task.FromResult(handler(ifNoneMatch))
                : throw new NotImplementedException();
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
