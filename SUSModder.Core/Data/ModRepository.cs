using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla katalogu modów (tabela mods).
    /// Używa write-through cache w pamięci (List<ModConfiguration>).
    /// Dziedziczy logikę API z ConfigManager.
    /// </summary>
    public class ModRepository : IModRepository, IDisposable
    {
        private readonly DatabaseService _db;
        private List<ModConfiguration>? _cachedMods;
        private readonly object _cacheLock = new();
        private readonly HttpClient _httpClient;

        public ModRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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

                // Jeśli baza jest pusta, spróbuj załadować z API
                if (mods.Count == 0)
                {
                    var apiMods = Task.Run(async () => await FetchFromApiAsync()).GetAwaiter().GetResult();
                    if (apiMods.Count > 0)
                    {
                        SaveAllModsInternal(apiMods);
                        mods = apiMods;
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
            SaveAllModsInternal(mods);
            lock (_cacheLock)
            {
                _cachedMods = mods;
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
                MergeInstallDataFromPrevious(apiMods, localMods);
                EnsureVanillaConfigPresent(apiMods, localMods);
            }
            else
            {
                EnsureVanillaConfigPresent(apiMods);
            }

            SaveAllModsInternal(apiMods);
            lock (_cacheLock)
            {
                _cachedMods = apiMods;
            }
            PersistVanillaPathIfNeeded(apiMods);
            return apiMods;
        }

        /// <inheritdoc/>
        public async Task<bool> RefreshFromApiAsync()
        {
            try
            {
                var localMods = GetAllMods();
                var apiMods = await FetchFromApiAsync();

                if (apiMods.Count == 0)
                    return false;

                if (localMods.Count > 0)
                {
                    MergeInstallDataFromPrevious(apiMods, localMods);
                    EnsureVanillaConfigPresent(apiMods, localMods);
                }
                else
                {
                    EnsureVanillaConfigPresent(apiMods);
                }

                if (AreConfigsEquivalent(localMods, apiMods))
                    return false;

                SaveAllModsInternal(apiMods);
                lock (_cacheLock)
                {
                    _cachedMods = apiMods;
                }
                PersistVanillaPathIfNeeded(apiMods);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModRepository] RefreshFromApiAsync failed: {ex.Message}");
                return false;
            }
        }

        #region Private Helpers

        private void SaveAllModsInternal(List<ModConfiguration> mods)
        {
            var conn = _db.GetConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                // Wyczyść tabelę
                using var clearCmd = conn.CreateCommand();
                clearCmd.CommandText = "DELETE FROM mods;";
                clearCmd.ExecuteNonQuery();

                // Wstaw wszystkie mody
                foreach (var mod in mods)
                {
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT INTO mods (Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink,
                            EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, AmongVersion,
                            LastUpdated, Description, HasRoles, LobbyRegionBaseUrl, SupportsLobbySharing)
                        VALUES (@Id, @ModName, @PngFileName, @InstallPath, @GitHubRepoOrLink,
                            @EpicGitHubRepoOrLink, @ModType, @DllInstallPath, @ModVersion, @AmongVersion,
                            @LastUpdated, @Description, @HasRoles, @LobbyRegionBaseUrl, @SupportsLobbySharing);";
                    BindModParameters(insertCmd, mod);
                    insertCmd.Parameters.AddWithValue("@Id", mod.Id);
                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                System.Diagnostics.Debug.WriteLine($"[ModRepository] Zapisano {mods.Count} modów.");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task<List<ModConfiguration>> FetchFromApiAsync()
        {
            try
            {
                var appSettingsPath = ApplicationPaths.AppSettingsPath;

                if (!File.Exists(appSettingsPath))
                    return new List<ModConfiguration>();

                var json = await File.ReadAllTextAsync(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? apiUrl = null;
                if (root.TryGetProperty("Configuration", out var config) &&
                    config.TryGetProperty("UpdateServerUrl", out var url))
                {
                    apiUrl = url.GetString();
                }

                if (string.IsNullOrEmpty(apiUrl))
                    return new List<ModConfiguration>();

                var response = await _httpClient.GetStringAsync(apiUrl);
                var mods = JsonSerializer.Deserialize<List<ModConfiguration>>(response)
                    ?? new List<ModConfiguration>();

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

        private static void MergeInstallDataFromPrevious(List<ModConfiguration> apiMods, List<ModConfiguration> previousMods)
        {
            foreach (var apiMod in apiMods)
            {
                var prev = previousMods.FirstOrDefault(p => p.Id == apiMod.Id);
                if (prev != null)
                {
                    if (!string.IsNullOrEmpty(prev.InstallPath) && string.IsNullOrEmpty(apiMod.InstallPath))
                        apiMod.InstallPath = prev.InstallPath;
                    if (!string.IsNullOrEmpty(prev.ModVersion) && prev.ModVersion != apiMod.ModVersion)
                        apiMod.ModVersion = prev.ModVersion;
                    if (prev.LastUpdated.HasValue)
                        apiMod.LastUpdated = prev.LastUpdated;
                }
            }
        }

        private static bool AreConfigsEquivalent(List<ModConfiguration> a, List<ModConfiguration> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id ||
                    a[i].ModVersion != b[i].ModVersion ||
                    a[i].InstallPath != b[i].InstallPath)
                    return false;
            }
            return true;
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
