using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Api;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla katalogu modów (tabela mods).
    /// Używa write-through cache w pamięci (List<ModConfiguration>).
    /// Dziedziczy logikę API z ConfigManager.
    /// </summary>
    public class ModRepository : IModRepository
    {
        private readonly DatabaseService _db;
        private readonly ISUSModderApiClient _apiClient;
        private List<ModConfiguration>? _cachedMods;
        private readonly object _cacheLock = new();
        private readonly object _catalogLock = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private static readonly HttpClient _legacyConfigHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
        private const string LegacyConfigUrl = "https://susmodder.app/api/susmodder-config";

        public ModRepository(DatabaseService db, ISUSModderApiClient apiClient)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <inheritdoc/>
        public List<ModConfiguration> GetAllMods()
        {
            if (_cachedMods != null)
                return _cachedMods;

            lock (_cacheLock)
            {
                if (_cachedMods != null)
                    return _cachedMods;

                var conn = _db.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM mods ORDER BY Id;";

                var mods = new List<ModConfiguration>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    mods.Add(MapModFromReader(reader));
                }

                NormalizeIconReferences(mods);

                // Jeśli baza jest pusta, spróbuj załadować z API
                if (mods.Count == 0)
                {
                    var apiMods = Task.Run(async () => await FetchFromApiAsync()).GetAwaiter().GetResult();
                    if (apiMods.Count > 0)
                    {
                        lock (_catalogLock)
                        {
                            SaveAllModsInternal(apiMods);
                            mods = ReadAllModsFromDb();
                        }
                    }
                }
                else
                {
                    // Upewnij się, że Vanilla config istnieje
                    EnsureVanillaConfigPresent(mods);
                }

                _cachedMods = mods;
                PersistVanillaPathIfNeeded(mods);
                return _cachedMods;
            }
        }

        /// <inheritdoc/>
        public void SaveAllMods(List<ModConfiguration> mods)
        {
            lock (_catalogLock)
            {
                SaveAllModsInternal(mods);
                lock (_cacheLock)
                {
                    _cachedMods = ReadAllModsFromDb();
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateMod(ModConfiguration mod)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE mods SET
                    ModName = @ModName,
                    PngFileName = @PngFileName,
                    InstallPath = @InstallPath,
                    GitHubRepoOrLink = @GitHubRepoOrLink,
                    EpicGitHubRepoOrLink = @EpicGitHubRepoOrLink,
                    ModType = @ModType,
                    DllInstallPath = @DllInstallPath,
                    ModVersion = @ModVersion,
                    AmongVersion = @AmongVersion,
                    LastUpdated = @LastUpdated,
                    Description = @Description,
                    HasRoles = @HasRoles,
                    UpdatedAt = datetime('now')
                WHERE Id = @Id;";

            BindModParameters(cmd, mod);
            cmd.Parameters.AddWithValue("@Id", mod.Id);
            cmd.ExecuteNonQuery();

            // Aktualizuj cache
            lock (_cacheLock)
            {
                if (_cachedMods != null)
                {
                    var idx = _cachedMods.FindIndex(m => m.Id == mod.Id);
                    if (idx >= 0)
                        _cachedMods[idx] = mod;
                    else
                        _cachedMods.Add(mod);
                }
            }
        }

        /// <inheritdoc/>
        public void AddMod(ModConfiguration mod)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mods (Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink,
                    EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, AmongVersion,
                    LastUpdated, Description, HasRoles, LobbyRegionBaseUrl, SupportsLobbySharing)
                VALUES (@Id, @ModName, @PngFileName, @InstallPath, @GitHubRepoOrLink,
                    @EpicGitHubRepoOrLink, @ModType, @DllInstallPath, @ModVersion, @AmongVersion,
                    @LastUpdated, @Description, @HasRoles, @LobbyRegionBaseUrl, @SupportsLobbySharing);";

            BindModParameters(cmd, mod);
            cmd.ExecuteNonQuery();

            lock (_cacheLock)
            {
                _cachedMods?.Add(mod);
            }
        }

        /// <inheritdoc/>
        public void DeleteMod(int id)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mods WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            lock (_cacheLock)
            {
                _cachedMods?.RemoveAll(m => m.Id == id);
            }
        }

        /// <inheritdoc/>
        public void UpsertMod(ModConfiguration mod)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mods (Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink,
                    EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, AmongVersion,
                    LastUpdated, Description, HasRoles, LobbyRegionBaseUrl, SupportsLobbySharing)
                VALUES (@Id, @ModName, @PngFileName, @InstallPath, @GitHubRepoOrLink,
                    @EpicGitHubRepoOrLink, @ModType, @DllInstallPath, @ModVersion, @AmongVersion,
                    @LastUpdated, @Description, @HasRoles, @LobbyRegionBaseUrl, @SupportsLobbySharing)
                ON CONFLICT(Id) DO UPDATE SET
                    ModName = excluded.ModName,
                    PngFileName = excluded.PngFileName,
                    InstallPath = excluded.InstallPath,
                    GitHubRepoOrLink = excluded.GitHubRepoOrLink,
                    EpicGitHubRepoOrLink = excluded.EpicGitHubRepoOrLink,
                    ModType = excluded.ModType,
                    DllInstallPath = excluded.DllInstallPath,
                    ModVersion = excluded.ModVersion,
                    AmongVersion = excluded.AmongVersion,
                    LastUpdated = excluded.LastUpdated,
                    Description = excluded.Description,
                    HasRoles = excluded.HasRoles,
                    LobbyRegionBaseUrl = excluded.LobbyRegionBaseUrl,
                    SupportsLobbySharing = excluded.SupportsLobbySharing,
                    UpdatedAt = datetime('now');";

            BindModParameters(cmd, mod);
            cmd.ExecuteNonQuery();

            lock (_cacheLock)
            {
                if (_cachedMods != null)
                {
                    var idx = _cachedMods.FindIndex(m => m.Id == mod.Id);
                    if (idx >= 0)
                        _cachedMods[idx] = mod;
                    else
                        _cachedMods.Add(mod);
                }
            }
        }

        /// <inheritdoc/>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedMods = null;
            }
        }

        /// <inheritdoc/>
        public async Task<List<ModConfiguration>> FetchAndMergeFromApiAsync()
        {
            var localMods = GetAllMods();
            var apiMods = await FetchFromApiAsync();

            if (apiMods.Count == 0)
                return localMods;

            if (localMods.Count > 0)
            {
                MergeCatalogMetadataFromPrevious(apiMods, localMods);
                MergeInstallDataFromPrevious(apiMods, localMods);
                PreserveLocalOnlyMods(apiMods, localMods);
                EnsureVanillaConfigPresent(apiMods, localMods);
            }
            else
            {
                EnsureVanillaConfigPresent(apiMods);
            }

            await MergeLegacyIconsFromV1Async(apiMods);

            List<ModConfiguration> mergedMods;
            lock (_catalogLock)
            {
                SaveAllModsInternal(apiMods);
                lock (_cacheLock)
                {
                    _cachedMods = ReadAllModsFromDb();
                    mergedMods = _cachedMods;
                }
            }

            PersistVanillaPathIfNeeded(mergedMods);
            return mergedMods;
        }

        /// <inheritdoc/>
        public async Task<bool> RefreshFromApiAsync()
        {
            var sync = CatalogSyncServiceProvider.TryGetDefault();
            if (sync is not null)
            {
                var result = await sync.RefreshCatalogIfDueAsync(force: true);
                return result.ConfigChanged;
            }

            if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return false;

            try
            {
                var apiMods = await FetchFromApiAsync();
                if (apiMods.Count == 0)
                    return false;

                return await ApplyRemoteCatalogAsync(apiMods);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModRepository] RefreshFromApiAsync failed: {ex.Message}");
                return false;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<bool> ApplyRemoteCatalogAsync(List<ModConfiguration> apiMods)
        {
            if (apiMods.Count == 0)
                return false;

            if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return false;

            try
            {
                var localMods = GetAllMods();

                if (localMods.Count > 0)
                {
                    MergeCatalogMetadataFromPrevious(apiMods, localMods);
                    MergeInstallDataFromPrevious(apiMods, localMods);
                    PreserveLocalOnlyMods(apiMods, localMods);
                    EnsureVanillaConfigPresent(apiMods, localMods);
                }
                else
                {
                    EnsureVanillaConfigPresent(apiMods);
                }

                await MergeLegacyIconsFromV1Async(apiMods);

                if (AreConfigsEquivalent(localMods, apiMods))
                    return false;

                lock (_catalogLock)
                {
                    SaveAllModsInternal(apiMods);
                    lock (_cacheLock)
                    {
                        _cachedMods = ReadAllModsFromDb();
                    }
                }

                PersistVanillaPathIfNeeded(apiMods);
                return true;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        #region Private Helpers

        private void SaveAllModsInternal(List<ModConfiguration> mods)
        {
            mods = DeduplicateModsById(mods);

            var conn = _db.GetConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var mod in mods)
                {
                    using var upsertCmd = conn.CreateCommand();
                    upsertCmd.CommandText = @"
                        INSERT INTO mods (Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink,
                            EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, AmongVersion,
                            LastUpdated, Description, HasRoles, LobbyRegionBaseUrl, SupportsLobbySharing)
                        VALUES (@Id, @ModName, @PngFileName, @InstallPath, @GitHubRepoOrLink,
                            @EpicGitHubRepoOrLink, @ModType, @DllInstallPath, @ModVersion, @AmongVersion,
                            @LastUpdated, @Description, @HasRoles, @LobbyRegionBaseUrl, @SupportsLobbySharing)
                        ON CONFLICT(Id) DO UPDATE SET
                            ModName = excluded.ModName,
                            PngFileName = excluded.PngFileName,
                            InstallPath = excluded.InstallPath,
                            GitHubRepoOrLink = excluded.GitHubRepoOrLink,
                            EpicGitHubRepoOrLink = excluded.EpicGitHubRepoOrLink,
                            ModType = excluded.ModType,
                            DllInstallPath = excluded.DllInstallPath,
                            ModVersion = excluded.ModVersion,
                            AmongVersion = excluded.AmongVersion,
                            LastUpdated = excluded.LastUpdated,
                            Description = excluded.Description,
                            HasRoles = excluded.HasRoles,
                            LobbyRegionBaseUrl = excluded.LobbyRegionBaseUrl,
                            SupportsLobbySharing = excluded.SupportsLobbySharing,
                            UpdatedAt = datetime('now');";
                    BindModParameters(upsertCmd, mod);
                    upsertCmd.Parameters.AddWithValue("@Id", mod.Id);
                    upsertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                System.Diagnostics.Debug.WriteLine($"[ModRepository] Zaktualizowano {mods.Count} modów (upsert).");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private List<ModConfiguration> ReadAllModsFromDb()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM mods ORDER BY Id;";

            var mods = new List<ModConfiguration>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                mods.Add(MapModFromReader(reader));
            }

            return mods;
        }

        private async Task<List<ModConfiguration>> FetchFromApiAsync()
        {
            try
            {
                var mods = await _apiClient.GetCatalogAsModConfigurationsAsync();
                System.Diagnostics.Debug.WriteLine($"[ModRepository] API returned {mods.Count} mods.");
                return mods;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModRepository] API fetch failed: {ex.Message}");
                return new List<ModConfiguration>();
            }
        }

        private static void BindModParameters(SqliteCommand cmd, ModConfiguration mod)
        {
            cmd.Parameters.AddWithValue("@ModName", mod.ModName ?? string.Empty);
            cmd.Parameters.AddWithValue("@PngFileName", mod.PngFileName ?? string.Empty);
            cmd.Parameters.AddWithValue("@InstallPath", (object?)mod.InstallPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@GitHubRepoOrLink", mod.GitHubRepoOrLink ?? string.Empty);
            cmd.Parameters.AddWithValue("@EpicGitHubRepoOrLink", (object?)mod.EpicGitHubRepoOrLink ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ModType", mod.ModType ?? string.Empty);
            cmd.Parameters.AddWithValue("@DllInstallPath", (object?)mod.DllInstallPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ModVersion", mod.ModVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@AmongVersion", mod.AmongVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@LastUpdated", mod.LastUpdated?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", mod.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("@HasRoles", mod.HasRoles.HasValue ? (object)(mod.HasRoles.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@LobbyRegionBaseUrl", (object?)mod.LobbyRegionBaseUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SupportsLobbySharing", mod.SupportsLobbySharing ? 1 : 0);
        }

        private static ModConfiguration MapModFromReader(SqliteDataReader reader)
        {
            return new ModConfiguration
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                ModName = reader.GetString(reader.GetOrdinal("ModName")),
                PngFileName = reader.GetString(reader.GetOrdinal("PngFileName")),
                InstallPath = reader.IsDBNull(reader.GetOrdinal("InstallPath")) ? null : reader.GetString(reader.GetOrdinal("InstallPath")),
                GitHubRepoOrLink = reader.GetString(reader.GetOrdinal("GitHubRepoOrLink")),
                EpicGitHubRepoOrLink = reader.IsDBNull(reader.GetOrdinal("EpicGitHubRepoOrLink")) ? null : reader.GetString(reader.GetOrdinal("EpicGitHubRepoOrLink")),
                ModType = reader.GetString(reader.GetOrdinal("ModType")),
                DllInstallPath = reader.IsDBNull(reader.GetOrdinal("DllInstallPath")) ? null : reader.GetString(reader.GetOrdinal("DllInstallPath")),
                ModVersion = reader.GetString(reader.GetOrdinal("ModVersion")),
                AmongVersion = reader.GetString(reader.GetOrdinal("AmongVersion")),
                LastUpdated = reader.IsDBNull(reader.GetOrdinal("LastUpdated")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastUpdated"))),
                Description = reader.GetString(reader.GetOrdinal("Description")),
                HasRoles = reader.IsDBNull(reader.GetOrdinal("HasRoles")) ? null : reader.GetInt32(reader.GetOrdinal("HasRoles")) != 0,
                LobbyRegionBaseUrl = reader.IsDBNull(reader.GetOrdinal("LobbyRegionBaseUrl")) ? null : reader.GetString(reader.GetOrdinal("LobbyRegionBaseUrl")),
                SupportsLobbySharing = !reader.IsDBNull(reader.GetOrdinal("SupportsLobbySharing")) && reader.GetInt32(reader.GetOrdinal("SupportsLobbySharing")) != 0
            };
        }

        // --- Metody przeniesione z ConfigManager ---

        private static bool IsVanillaConfig(ModConfiguration config)
        {
            if (config == null) return false;
            return config.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
                   config.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetVanillaInstallPath(IEnumerable<ModConfiguration> configs, out string? installPath, out ModConfiguration? vanillaConfig)
        {
            vanillaConfig = configs.FirstOrDefault(IsVanillaConfig);
            if (vanillaConfig != null && !string.IsNullOrWhiteSpace(vanillaConfig.InstallPath))
            {
                installPath = vanillaConfig.InstallPath;
                return true;
            }
            installPath = null;
            return false;
        }

        private bool EnsureVanillaConfigPresent(List<ModConfiguration> configs, List<ModConfiguration>? previousConfigs = null)
        {
            if (TryGetVanillaInstallPath(configs, out _, out _))
                return false;

            if (previousConfigs != null && TryGetVanillaInstallPath(previousConfigs, out var prevPath, out var prevVanilla))
            {
                configs.Add(prevVanilla!);
                return true;
            }

            return false;
        }

        private void PersistVanillaPathIfNeeded(List<ModConfiguration> configs)
        {
            if (TryGetVanillaInstallPath(configs, out var installPath, out _))
            {
                try
                {
                    var settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SUSModder", "user-settings.json");

                    if (File.Exists(settingsPath))
                    {
                        var json = File.ReadAllText(settingsPath);
                        using var doc = JsonDocument.Parse(json);
                        // Path is persisted through UserSettingsService, handled elsewhere
                    }
                }
                catch { /* non-critical */ }
            }
        }

        private void MergeCatalogMetadataFromPrevious(
            List<ModConfiguration> apiMods,
            List<ModConfiguration> previousMods)
        {
            foreach (var apiMod in apiMods)
            {
                var prev = previousMods.FirstOrDefault(p => p.Id == apiMod.Id);
                if (prev == null)
                    continue;

                if (IsVanillaConfig(apiMod) || IsVanillaConfig(prev))
                {
                    apiMod.PngFileName = BundledModIconHelper.VanillaIconFileName;
                    continue;
                }

                if (string.IsNullOrEmpty(apiMod.PngFileName) && !string.IsNullOrEmpty(prev.PngFileName))
                    apiMod.PngFileName = ResolveIconReference(prev.PngFileName, apiMod);
                else if (!string.IsNullOrEmpty(apiMod.PngFileName))
                    apiMod.PngFileName = ResolveIconReference(apiMod.PngFileName, apiMod);
                if (string.IsNullOrEmpty(apiMod.DllInstallPath) && !string.IsNullOrEmpty(prev.DllInstallPath))
                    apiMod.DllInstallPath = prev.DllInstallPath;
                if (!apiMod.HasRoles.HasValue && prev.HasRoles.HasValue)
                    apiMod.HasRoles = prev.HasRoles;
                if (string.IsNullOrEmpty(apiMod.LobbyRegionBaseUrl) && !string.IsNullOrEmpty(prev.LobbyRegionBaseUrl))
                    apiMod.LobbyRegionBaseUrl = prev.LobbyRegionBaseUrl;
                if (!apiMod.SupportsLobbySharing && prev.SupportsLobbySharing)
                    apiMod.SupportsLobbySharing = prev.SupportsLobbySharing;
            }
        }

        private static void PreserveLocalOnlyMods(List<ModConfiguration> apiMods, List<ModConfiguration> previousMods)
        {
            foreach (var prev in previousMods)
            {
                if (prev.Id == 0)
                    continue;

                if (apiMods.All(a => a.Id != prev.Id))
                    apiMods.Add(prev);
            }
        }

        private static List<ModConfiguration> DeduplicateModsById(List<ModConfiguration> mods)
        {
            var result = new List<ModConfiguration>(mods.Count);
            var seen = new HashSet<int>();

            foreach (var mod in mods)
            {
                if (!seen.Add(mod.Id))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ModRepository] Pomijam duplikat moda Id={mod.Id} ({mod.ModName}) podczas zapisu.");
                    continue;
                }

                result.Add(mod);
            }

            return result;
        }

        private static void MergeInstallDataFromPrevious(List<ModConfiguration> apiMods, List<ModConfiguration> previousMods)
        {
            foreach (var apiMod in apiMods)
            {
                var prev = previousMods.FirstOrDefault(p => p.Id == apiMod.Id);
                if (prev != null && CanPreserveInstallData(apiMod, prev))
                {
                    if (!string.IsNullOrEmpty(prev.InstallPath) && string.IsNullOrEmpty(apiMod.InstallPath))
                        apiMod.InstallPath = prev.InstallPath;
                    if (!string.IsNullOrEmpty(prev.InstallPath) &&
                        !string.IsNullOrEmpty(prev.ModVersion) &&
                        prev.ModVersion != apiMod.ModVersion)
                    {
                        apiMod.ModVersion = prev.ModVersion;
                    }
                    if (prev.LastUpdated.HasValue)
                        apiMod.LastUpdated = prev.LastUpdated;
                }
            }
        }

        private static bool CanPreserveInstallData(ModConfiguration apiMod, ModConfiguration previousMod)
        {
            // DLL installations are tracked per full-mod instance in InstallationMap/instance tables.
            // A single mods.InstallPath on a DLL catalog row is ambiguous and can be stale after API ID reuse.
            if (!string.Equals(apiMod.ModType, "full", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousMod.ModType, "full", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(apiMod.ModName) &&
                !string.IsNullOrWhiteSpace(previousMod.ModName) &&
                !string.Equals(apiMod.ModName.Trim(), previousMod.ModName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private async Task MergeLegacyIconsFromV1Async(List<ModConfiguration> apiMods)
        {
            try
            {
                using var response = await _legacyConfigHttp.GetAsync(LegacyConfigUrl);
                if (!response.IsSuccessStatusCode)
                    return;

                var json = await response.Content.ReadAsStringAsync();
                var legacyMods = JsonSerializer.Deserialize<List<ModConfiguration>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (legacyMods == null || legacyMods.Count == 0)
                    return;

                foreach (var apiMod in apiMods)
                {
                    if (!string.IsNullOrWhiteSpace(apiMod.PngFileName))
                        continue;

                    var legacy = legacyMods.FirstOrDefault(m => m.Id == apiMod.Id);
                    if (legacy == null || string.IsNullOrWhiteSpace(legacy.PngFileName))
                        continue;

                    apiMod.PngFileName = ResolveIconReference(legacy.PngFileName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModRepository] Legacy icon merge failed: {ex.Message}");
            }
        }

        private string ResolveIconReference(string pngFileName, ModConfiguration? mod = null)
        {
            if (mod is not null && IsVanillaConfig(mod))
                return BundledModIconHelper.VanillaIconFileName;

            if (BundledModIconHelper.IsBundledAssetFileName(pngFileName))
                return BundledModIconHelper.VanillaIconFileName;

            if (pngFileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pngFileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return CdnAssetUrlResolver.Resolve(pngFileName, _apiClient.BaseUrl, _apiClient.StaticAssetsBaseUrl);
            }

            var path = pngFileName.StartsWith('/') ? pngFileName : $"/icons/{pngFileName}";
            return CdnAssetUrlResolver.Resolve(path, _apiClient.BaseUrl, _apiClient.StaticAssetsBaseUrl);
        }

        private void NormalizeIconReferences(IEnumerable<ModConfiguration> mods)
        {
            foreach (var mod in mods)
            {
                if (IsVanillaConfig(mod))
                {
                    mod.PngFileName = BundledModIconHelper.NormalizeVanillaIconReference(mod.PngFileName);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mod.PngFileName))
                    continue;

                mod.PngFileName = ResolveIconReference(mod.PngFileName, mod);
            }
        }

        private static bool AreConfigsEquivalent(List<ModConfiguration> a, List<ModConfiguration> b)
        {
            if (a.Count != b.Count)
                return false;

            var bById = b.ToDictionary(m => m.Id);
            foreach (var mod in a)
            {
                if (!bById.TryGetValue(mod.Id, out var other))
                    return false;

                if (mod.ModVersion != other.ModVersion ||
                    mod.InstallPath != other.InstallPath ||
                    mod.PngFileName != other.PngFileName)
                    return false;
            }

            return true;
        }

        #endregion
    }
}
