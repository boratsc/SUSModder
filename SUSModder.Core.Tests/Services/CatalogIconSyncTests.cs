using System.Text;
using System.Text.Json;
using SUSModder.Core.Api;
using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.Tests.Services;

/// <summary>
/// Testy integracji synchronizacji katalogu z przypisaniami ikon (3.1-04).
/// Odpowiedzi API są atrapami proponowanego kontraktu — nie dowodem istnienia
/// wdrożonego backendu.
/// </summary>
public class CatalogIconSyncTests : IDisposable
{
    private readonly string _tempDir;

    public CatalogIconSyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Schema1_CommitsModAndGlobalAssignments()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        var changes = new List<IconAssignmentChange>();
        service.IconAssignmentsChanged += (_, args) => changes.AddRange(args.Changes);

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal("icon_mod_one", icons.GetForMod(1)!.AssetId);
        Assert.Equal("icon_vanilla", icons.GetGlobal(IconGlobalRole.Vanilla)!.AssetId);
        Assert.Equal("icon_dll", icons.GetGlobal(IconGlobalRole.Dll)!.AssetId);
        Assert.True(service.IsVersionedIconSchemaActive);
        Assert.Contains(changes, c => c.AssignmentKey == "mod:1" && c.Reference is not null);
        Assert.Contains(changes, c => c.AssignmentKey == "global:vanilla" && c.Reference is not null);
        Assert.Contains(changes, c => c.AssignmentKey == "global:dll" && c.Reference is not null);
    }

    [Fact]
    public async Task ExplicitNull_CreatesDurableTombstone_NotDeletion()
    {
        var dbPath = Path.Combine(_tempDir, "tombstone.db");
        await using var db = await CreateDatabaseAsync(dbPath);
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);
        Assert.False(icons.GetForMod(1)!.IsRemoved);

        // Drugi przebieg: backend potwierdza jawne usunięcie przypisania.
        api.CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", null, iconMemberPresent: true)]);
        await service.RefreshCatalogIfDueAsync(force: true);

        var tombstone = icons.GetForMod(1);
        Assert.NotNull(tombstone);
        Assert.True(tombstone!.IsRemoved);
        Assert.Equal(1, tombstone.SchemaVersion);

        // Tombstone przeżywa restart — wiersz nie jest kasowany.
        await using var reopened = await CreateDatabaseAsync(dbPath);
        var persisted = new IconAssignmentRepository(reopened).GetForMod(1);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsRemoved);
    }

    [Fact]
    public async Task AbsentIconMember_KeepsPreviousAssignment()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        // Kolejna odpowiedź bez członu "icon" — przypisanie zostaje bez zmian.
        api.CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", null, iconMemberPresent: false)]);
        await service.RefreshCatalogIfDueAsync(force: true);

        var assignment = icons.GetForMod(1);
        Assert.NotNull(assignment);
        Assert.False(assignment!.IsRemoved);
        Assert.Equal("icon_mod_one", assignment.AssetId);
    }

    [Fact]
    public async Task UnsupportedSchema_KeepsAssignments_AndRequestsVersionedClear()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        IconAssignmentsChangedEventArgs? observed = null;
        service.IconAssignmentsChanged += (_, args) => observed = args;

        // Backend odpowiada bez obsługiwanego schematu.
        api.MetaHandler = _ => MetaResponse(new CatalogMetaDto { IconsSchemaVersion = 2 });
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.NotNull(observed);
        Assert.True(observed!.ClearVersionedIcons);
        Assert.False(observed.IsSchemaActive);
        Assert.False(service.IsVersionedIconSchemaActive);

        // Ostatni dobry stan przypisań pozostaje w bazie.
        Assert.Equal("icon_mod_one", icons.GetForMod(1)!.AssetId);
    }

    [Fact]
    public async Task AbsentSchema_IsLegacyFallback_WithoutClearSignal()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(new CatalogMetaDto())
        };

        var service = CreateService(api, db, icons);
        IconAssignmentsChangedEventArgs? observed = null;
        service.IconAssignmentsChanged += (_, args) => observed = args;

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.NotNull(observed);
        Assert.True(observed!.ClearVersionedIcons);
        Assert.False(observed.IsSchemaActive);
        Assert.False(service.IsVersionedIconSchemaActive);
        Assert.Empty(icons.GetAll());
    }

    [Fact]
    public async Task NetworkFailure_RetainsLastGoodAssignments_AndDoesNotClear()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        IconAssignmentsChangedEventArgs? observed = null;
        service.IconAssignmentsChanged += (_, args) => observed = args;

        // Awaria transportu metadanych — przypisania zostają, brak sygnału czyszczenia.
        api.MetaHandler = _ => throw new HttpRequestException("offline");
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Null(observed);
        Assert.Equal("icon_mod_one", icons.GetForMod(1)!.AssetId);
        Assert.True(service.IsVersionedIconSchemaActive);
    }

    [Fact]
    public async Task CatalogNotModified_StillRefreshesGlobalIcons()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        // Katalog bez zmian (304), ale globalna ikona się zmieniła.
        api.CatalogHandler = (_, _) => new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 304, ETag = "etag-catalog" };
        api.MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla_new"), IconRef("dll")));

        var changes = new List<IconAssignmentChange>();
        service.IconAssignmentsChanged += (_, args) => changes.AddRange(args.Changes);

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal("icon_vanilla_new", icons.GetGlobal(IconGlobalRole.Vanilla)!.AssetId);
        Assert.Contains(changes, c => c.AssignmentKey == "global:vanilla");
    }

    [Fact]
    public async Task MetadataNotModified_UsesPersistedSchema_AndCommitsModRefs()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        // Metadane bez zmian — schemat pochodzi z ostatniego udanego zapisu stanu.
        api.MetaHandler = _ => new SusModderApiResult<CatalogMetaDto> { StatusCode = 304, ETag = "etag-meta" };
        api.CatalogHandler = (_, _) => CatalogResponse([CatalogItem(2, "In the Woods", IconRef("mod_two"))]);

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal("icon_mod_two", icons.GetForMod(2)!.AssetId);
        Assert.True(service.IsVersionedIconSchemaActive);
    }

    [Fact]
    public async Task IncompletePagination_DoesNotCommitModAssignments()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            // Pierwsza strona zgłasza większy rozmiar katalogu, druga zawodzi —
            // migawka jest ucięta i nie może być traktowana jako kompletna.
            CatalogHandler = (query, _) => query?.Offset == 0
                ? new SusModderApiResult<List<CatalogItemDto>>
                {
                    StatusCode = 200,
                    ETag = "etag-catalog",
                    Data = Enumerable.Range(1, 200).Select(i => CatalogItem(i, $"Mod {i}", IconRef($"mod_{i}"))).ToList(),
                    Meta = new SusModderApiMeta { Total = 400 }
                }
                : new SusModderApiResult<List<CatalogItemDto>> { StatusCode = 500 },
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        // Migawka katalogu była ucięta — przypisania per mod nie są commitowane.
        Assert.DoesNotContain(icons.GetAll(), a => a.AssignmentKey.StartsWith("mod:", StringComparison.Ordinal));
        // Przypisania globalne nie zależą od kompletności katalogu.
        Assert.Equal("icon_vanilla", icons.GetGlobal(IconGlobalRole.Vanilla)!.AssetId);
    }

    [Fact]
    public async Task InvalidCatalog_DoesNotCommitModAssignments()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            // Brak wersji i linku — katalog jest odrzucany przez walidację.
            CatalogHandler = (_, _) => CatalogResponse([new CatalogItemDto { Id = 1, Name = "Broken", Type = "full" }]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        var result = await service.RefreshCatalogIfDueAsync(force: true);

        Assert.NotEqual(CatalogSyncStatus.Updated, result.Status);
        Assert.DoesNotContain(icons.GetAll(), a => a.AssignmentKey.StartsWith("mod:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnchangedReferences_DoNotRaiseChanges()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        var changes = new List<IconAssignmentChange>();
        service.IconAssignmentsChanged += (_, args) => changes.AddRange(args.Changes);

        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task InvalidReference_KeepsLastGoodAssignment()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        // Nieprawidłowy hash referencji — nie zapisujemy jej i nie kasujemy poprzedniej.
        api.CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", new CatalogIconDto
        {
            AssetId = "icon_broken",
            Url = "https://susmodder.app/assets/icons/broken.png",
            Sha256 = "not-a-hash",
            ContentType = "image/png",
            SizeBytes = 10
        })]);

        await service.RefreshCatalogIfDueAsync(force: true);

        var assignment = icons.GetForMod(1);
        Assert.NotNull(assignment);
        Assert.Equal("icon_mod_one", assignment!.AssetId);
    }

    [Fact]
    public async Task WithoutIconRepository_SyncRemainsCompatible()
    {
        await using var db = await CreateDatabaseAsync();
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        // Brak repozytorium przypisań — synchronizacja nadal działa, bez synchronizacji ikon.
        var service = new CatalogSyncService(
            api,
            new RecordingModRepository(),
            new CatalogSyncStateRepository(db),
            new CompatibilityCacheRepository(db),
            new NullDiagnostics());

        var result = await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Equal(CatalogSyncStatus.Updated, result.Status);
        Assert.Empty(service.GetIconAssignments());
        Assert.False(service.IsVersionedIconSchemaActive);
    }

    [Fact]
    public async Task SchemaReactivation_EmitsEvent_EvenWhenReferencesAreUnchanged()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        var events = new List<IconAssignmentsChangedEventArgs>();
        service.IconAssignmentsChanged += (_, args) => events.Add(args);

        // 1) Kontrakt wersjonowany aktywny.
        await service.RefreshCatalogIfDueAsync(force: true);
        Assert.Single(events);
        Assert.True(events[0].IsSchemaActive);
        Assert.True(service.IsVersionedIconSchemaActive);

        // 2) Backend przechodzi na nieobsługiwany schemat — UI czyści klucze wersjonowane,
        //    ale wiersze przypisań pozostają w bazie.
        events.Clear();
        api.MetaHandler = _ => MetaResponse(new CatalogMetaDto { IconsSchemaVersion = 2 });
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Single(events);
        Assert.True(events[0].ClearVersionedIcons);
        Assert.False(events[0].IsSchemaActive);
        Assert.True(events[0].SchemaTransition);
        Assert.False(service.IsVersionedIconSchemaActive);
        Assert.Equal("icon_mod_one", icons.GetForMod(1)!.AssetId);

        // 3) Backend wraca do kontraktu wersjonowanego z identycznymi referencjami.
        //    Zmian przypisań nie ma, ale UI musi dostać powiadomienie o aktywacji schematu,
        //    inaczej pozostałoby w trybie legacy do restartu aplikacji.
        events.Clear();
        api.MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")));
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Single(events);
        Assert.True(events[0].IsSchemaActive);
        Assert.False(events[0].ClearVersionedIcons);
        Assert.True(events[0].SchemaTransition);
        Assert.Empty(events[0].Changes);

        // Pełna migawka pozostaje dostępna dla UI.
        Assert.True(service.IsVersionedIconSchemaActive);
        Assert.Equal("icon_mod_one", service.GetIconAssignmentForMod(1)!.AssetId);
        Assert.Equal("icon_vanilla", service.GetGlobalIconAssignment(IconGlobalRole.Vanilla)!.AssetId);
    }

    [Fact]
    public async Task UnchangedSchemaBaseline_DoesNotEmitEvent()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        var events = new List<IconAssignmentsChangedEventArgs>();
        service.IconAssignmentsChanged += (_, args) => events.Add(args);

        // Ten sam schemat i te same referencje — bez przejścia i bez zmian: brak zdarzenia.
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.Empty(events);
    }

    [Fact]
    public async Task MetadataNotModified_DoesNotEmitSchemaTransition()
    {
        await using var db = await CreateDatabaseAsync();
        var icons = new IconAssignmentRepository(db);
        var api = new FakeIconApiClient
        {
            CatalogHandler = (_, _) => CatalogResponse([CatalogItem(1, "Town of Us", IconRef("mod_one"))]),
            MetaHandler = _ => MetaResponse(Schema1Meta(IconRef("vanilla"), IconRef("dll")))
        };

        var service = CreateService(api, db, icons);
        await service.RefreshCatalogIfDueAsync(force: true);

        var events = new List<IconAssignmentsChangedEventArgs>();
        service.IconAssignmentsChanged += (_, args) => events.Add(args);

        // 304 metadanych potwierdza niezmieniony schemat — nie jest to przejście.
        api.MetaHandler = _ => new SusModderApiResult<CatalogMetaDto> { StatusCode = 304, ETag = "etag-meta" };
        await service.RefreshCatalogIfDueAsync(force: true);

        Assert.DoesNotContain(events, e => e.SchemaTransition);
        Assert.True(service.IsVersionedIconSchemaActive);
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

    private static CatalogSyncService CreateService(
        ISUSModderApiClient api,
        DatabaseService db,
        IIconAssignmentRepository icons) =>
        new(api,
            new RecordingModRepository(),
            new CatalogSyncStateRepository(db),
            new CompatibilityCacheRepository(db),
            new NullDiagnostics(),
            icons);

    private async Task<DatabaseService> CreateDatabaseAsync(string? dbPath = null)
    {
        var db = new DatabaseService(dbPath ?? Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }

    private static CatalogIconDto IconRef(string id) => new()
    {
        AssetId = $"icon_{id}",
        Url = $"https://susmodder.app/assets/icons/{id}.png",
        Sha256 = DeterministicHash(id),
        ContentType = "image/png",
        SizeBytes = 1024
    };

    /// <summary>Deterministyczny hash 64-znakowy (hex) — różne identyfikatory mają różne hashe.</summary>
    private static string DeterministicHash(string seed) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static CatalogItemDto CatalogItem(int id, string name, CatalogIconDto? icon, bool iconMemberPresent = true) => new()
    {
        Id = id,
        Name = name,
        Type = "full",
        CurrentVersion = "1.0.0",
        GitHubProjectUrl = $"https://example.test/mods/{id}.zip",
        Icon = icon,
        IconMemberPresent = iconMemberPresent
    };

    private static CatalogMetaDto Schema1Meta(CatalogIconDto? vanilla, CatalogIconDto? dll) => new()
    {
        IconsSchemaVersion = 1,
        IconsRevision = "rev-1",
        Icons = new CatalogGlobalIconsDto
        {
            Vanilla = vanilla,
            VanillaMemberPresent = true,
            Dll = dll,
            DllMemberPresent = true
        }
    };

    private static SusModderApiResult<List<CatalogItemDto>> CatalogResponse(List<CatalogItemDto> items) => new()
    {
        StatusCode = 200,
        ETag = "etag-catalog",
        Data = items,
        Meta = new SusModderApiMeta { Total = items.Count }
    };

    private static SusModderApiResult<CatalogMetaDto> MetaResponse(CatalogMetaDto meta) => new()
    {
        StatusCode = 200,
        ETag = "etag-meta",
        Data = meta
    };

    private sealed class NullDiagnostics : IDiagnosticsOutput
    {
        public void Write(string message) { }
    }

    private sealed class RecordingModRepository : IModRepository
    {
        public List<ModConfiguration> LocalMods { get; set; } = [];

        public List<ModConfiguration> GetAllMods() => LocalMods;

        public Task<bool> ApplyRemoteCatalogAsync(List<ModConfiguration> apiMods)
        {
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

    private sealed class FakeIconApiClient : ISUSModderApiClient
    {
        public required Func<CatalogQuery?, string?, SusModderApiResult<List<CatalogItemDto>>> CatalogHandler { get; set; }
        public required Func<string?, SusModderApiResult<CatalogMetaDto>> MetaHandler { get; set; }

        public string BaseUrl => "https://example.test/v2";
        public string StaticAssetsBaseUrl => "https://example.test";

        public Task<SusModderApiResult<List<CatalogItemDto>>> GetCatalogAsync(
            CatalogQuery? query = null,
            string? ifNoneMatch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogHandler(query, ifNoneMatch));

        public Task<SusModderApiResult<CatalogMetaDto>> GetCatalogMetaAsync(
            string? ifNoneMatch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MetaHandler(ifNoneMatch));

        public void Dispose() { }

        public string BuildModDownloadUrl(int modId, string version, string platform, string arch = "x86") =>
            $"{BaseUrl}/downloads/mod/{modId}/{version}?platform={platform}";

        public Task<List<ModConfiguration>> GetCatalogAsModConfigurationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ModConfiguration>());

        public Task<SusModderApiResult<CatalogModDetailDto>> GetCatalogModDetailAsync(int modId, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CatalogVersionsDto>> GetCatalogVersionsAsync(int modId, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<List<CatalogChangelogEntryDto>>> GetCatalogChangelogAsync(int modId, string lang = "pl", int limit = 5, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CompatibilityDataDto>> GetCompatibilityAsync(CompatibilityQueryParams query, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<CompatibilitySnapshotDto>> GetCompatibilitySnapshotAsync(bool onlyCurrentVersions = true, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<List<AmongUsVersionDto>>> GetAmongUsVersionsAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<AmongUsVersionDto>> GetAmongUsVersionAsync(string dbValue, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<JsonElement>> GetRolesAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<JsonElement>> GetDiscordFavoritesPublicAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<JsonElement>> GetDiscordServerCountsAsync(string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<OnlineUsersDto>> GetOnlineAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<JsonElement>> GetReleasesAsync(string? channel = null, string? ifNoneMatch = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SendHeartbeatAsync(object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<HttpResponseMessage> SendAsync(SusModderApiRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SusModderApiResult<ModVariantVirusTotalReportDto>> GetModVariantVirusTotalReportAsync(int modId, string version, string platform, string arch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
