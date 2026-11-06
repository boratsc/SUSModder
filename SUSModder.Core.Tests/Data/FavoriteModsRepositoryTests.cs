using SUSModder.Core.Data;
using SUSModder.Core.Models;

namespace SUSModder.Core.Tests.Data;

/// <summary>
/// Testy repozytorium ulubionych modów (3.1-05).
/// Zasięg: trwałość zapisu, idempotencja, niezależność od katalogu i zachowanie cache
/// przy błędzie zapisu.
/// </summary>
public class FavoriteModsRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public FavoriteModsRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SUSModder.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task NewDatabase_HasNoFavoritesByDefault()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        var snapshot = repo.GetSnapshot();

        Assert.True(snapshot.IsLoaded);
        Assert.Null(snapshot.ErrorCode);
        Assert.Empty(snapshot.ModIds);
        Assert.False(repo.IsFavorite(10));
    }

    [Fact]
    public async Task AddFavorite_StoresModIdAndIsIdempotent()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        var first = repo.AddFavorite(42);
        var second = repo.AddFavorite(42);

        Assert.True(first.Success);
        Assert.True(first.StateChanged);
        Assert.True(second.Success);
        Assert.False(second.StateChanged); // idempotentne – bez ponownego zapisu
        Assert.True(repo.IsFavorite(42));
        Assert.Equal(new[] { 42 }, repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task RemoveFavorite_DeletesOnlySelectedId()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        repo.AddFavorite(42);
        repo.AddFavorite(43);

        var result = repo.RemoveFavorite(42);

        Assert.True(result.Success);
        Assert.True(result.StateChanged);
        Assert.False(repo.IsFavorite(42));
        Assert.True(repo.IsFavorite(43));
        Assert.Equal(new[] { 43 }, repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task RemoveFavorite_UnknownId_IsNoOp()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        var result = repo.RemoveFavorite(999);

        Assert.True(result.Success);
        Assert.False(result.StateChanged);
        Assert.Empty(repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task SetFavorite_InvalidModId_ReturnsStableErrorCode()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        var result = repo.SetFavorite(0, true);

        Assert.False(result.Success);
        Assert.Equal(FavoriteModsErrorCodes.InvalidModId, result.ErrorCode);
        Assert.Empty(repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task Favorites_SurviveNewRepositoryInstance_SoRestartKeepsStars()
    {
        var dbPath = Path.Combine(_tempDir, "restart.db");

        await using (var first = await CreateInitializedDatabaseAsync(dbPath))
        {
            var repo = new FavoriteModsRepository(first);
            repo.AddFavorite(42);
        }

        await using var second = await CreateInitializedDatabaseAsync(dbPath);
        var reopened = new FavoriteModsRepository(second);

        Assert.True(reopened.IsFavorite(42));
        Assert.Equal(new[] { 42 }, reopened.GetFavoriteModIds());
    }

    [Fact]
    public async Task Favorites_AreIndependentOfCatalogRefreshAndModRemoval()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        // Mod obecny w katalogu i oznaczony jako ulubiony.
        Execute(db, @"
            INSERT INTO mods (Id, ModName, PngFileName, ModType)
            VALUES (42, 'Town of Us', 'tou.png', 'full');");
        repo.AddFavorite(42);

        // Odświeżenie katalogu – upsert istniejącego wpisu.
        Execute(db, @"
            INSERT OR REPLACE INTO mods (Id, ModName, PngFileName, ModType, ModVersion)
            VALUES (42, 'Town of Us 2', 'tou.png', 'full', '6.0.0');");

        // Usunięcie moda z katalogu.
        Execute(db, "DELETE FROM mods WHERE Id = 42;");

        Assert.True(repo.IsFavorite(42));
        Assert.Equal(new[] { 42 }, repo.GetFavoriteModIds());
    }

    [Fact]
    public async Task Favorites_KeepUnknownCatalogIds()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        // Id nieznane katalogowi – nie wolno kasować zapisu ani tworzyć z niego karty.
        repo.AddFavorite(987654);

        Assert.True(repo.IsFavorite(987654));
        Assert.Equal(new[] { 987654 }, repo.GetFavoriteModIds());
        Assert.Equal(0, CountMods(db, 987654));
    }

    [Fact]
    public async Task WriteFailure_ReturnsErrorCode_AndKeepsCacheConsistent()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);
        repo.AddFavorite(42);
        Assert.True(repo.IsFavorite(42));

        // Symulacja awarii magazynu – tabela ulubionych znikająca z bazy.
        Execute(db, "DROP TABLE favorite_mods;");

        var removeResult = repo.RemoveFavorite(42);

        Assert.False(removeResult.Success);
        Assert.Equal(FavoriteModsErrorCodes.StorageUnavailable, removeResult.ErrorCode);
        Assert.NotNull(removeResult.TechnicalMessage);

        // Stan w pamięci pozostaje niezmieniony – UI może bezpiecznie wycofać zmianę.
        Assert.True(repo.IsFavorite(42));
    }

    [Fact]
    public async Task ReadFailure_ReturnsSnapshotWithErrorCode_AndEmptyList()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);

        Execute(db, "DROP TABLE favorite_mods;");

        var snapshot = repo.GetSnapshot();

        Assert.False(snapshot.IsLoaded);
        Assert.Equal(FavoriteModsErrorCodes.StorageUnavailable, snapshot.ErrorCode);
        Assert.Empty(snapshot.ModIds);
        Assert.False(repo.IsFavorite(42));
    }

    [Fact]
    public async Task ClearCache_ReloadsStateFromDatabase()
    {
        await using var db = await CreateInitializedDatabaseAsync();
        var repo = new FavoriteModsRepository(db);
        repo.AddFavorite(42);

        // Zmiana w bazie z pominięciem repozytorium (np. migracja/restauracja).
        Execute(db, "DELETE FROM favorite_mods;");

        Assert.True(repo.IsFavorite(42)); // cache w pamięci

        repo.ClearCache();

        Assert.False(repo.IsFavorite(42)); // po czyszczeniu stan pochodzi z bazy
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
            // best-effort cleanup; test assertions should not depend on temp deletion
        }
    }

    private async Task<DatabaseService> CreateInitializedDatabaseAsync(string? dbPath = null)
    {
        var db = new DatabaseService(dbPath ?? Path.Combine(_tempDir, Guid.NewGuid().ToString("N"), "susmodder.db"));
        await db.InitializeAsync();
        return db;
    }

    private static void Execute(DatabaseService db, string sql)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int CountMods(DatabaseService db, int modId)
    {
        var conn = db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM mods WHERE Id = @mod_id;";
        cmd.Parameters.AddWithValue("@mod_id", modId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
