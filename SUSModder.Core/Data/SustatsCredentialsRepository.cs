using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla danych uwierzytelniających SUSTATS (tabela sustats_credentials).
    /// Cache w pamięci (Dictionary&lt;string, SustatsCredentials&gt;).
    /// </summary>
    public class SustatsCredentialsRepository : ISustatsCredentialsRepository
    {
        private readonly DatabaseService _db;
        private readonly Dictionary<string, SustatsCredentials> _cache = new();
        private readonly object _cacheLock = new();

        public SustatsCredentialsRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc/>
        public async Task<SustatsCredentials?> GetForGuildAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                throw new ArgumentException("GuildId nie może być puste.", nameof(guildId));

            // Sprawdź cache
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(guildId, out var cached))
                    return cached;
            }

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM sustats_credentials WHERE guild_id = @guild_id;";
            cmd.Parameters.AddWithValue("@guild_id", guildId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var creds = MapFromReader(reader);
                lock (_cacheLock)
                {
                    _cache[guildId] = creds;
                }
                return creds;
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task SaveAsync(SustatsCredentials creds)
        {
            if (creds == null)
                throw new ArgumentNullException(nameof(creds));

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sustats_credentials (
                    guild_id, server_name, token_enc, secret_enc, endpoint,
                    created_at, updated_at
                ) VALUES (
                    @guild_id, @server_name, @token_enc, @secret_enc, @endpoint,
                    COALESCE((SELECT created_at FROM sustats_credentials WHERE guild_id = @guild_id), datetime('now')),
                    datetime('now')
                )
                ON CONFLICT(guild_id) DO UPDATE SET
                    server_name = excluded.server_name,
                    token_enc = excluded.token_enc,
                    secret_enc = excluded.secret_enc,
                    endpoint = excluded.endpoint,
                    updated_at = excluded.updated_at;";

            cmd.Parameters.AddWithValue("@guild_id", creds.GuildId);
            cmd.Parameters.AddWithValue("@server_name", creds.ServerName ?? string.Empty);
            cmd.Parameters.AddWithValue("@token_enc", creds.TokenEncrypted ?? string.Empty);
            cmd.Parameters.AddWithValue("@secret_enc", creds.SecretEncrypted ?? string.Empty);
            cmd.Parameters.AddWithValue("@endpoint", creds.Endpoint ?? string.Empty);

            await cmd.ExecuteNonQueryAsync();

            // Aktualizuj cache
            lock (_cacheLock)
            {
                creds.CreatedAt = DateTime.UtcNow;
                creds.UpdatedAt = DateTime.UtcNow;
                _cache[creds.GuildId] = creds;
            }

            System.Diagnostics.Debug.WriteLine($"[SustatsCredentialsRepository] Credentials zapisane dla guild {creds.GuildId}.");
        }

        /// <inheritdoc/>
        public async Task DeleteAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                throw new ArgumentException("GuildId nie może być puste.", nameof(guildId));

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM sustats_credentials WHERE guild_id = @guild_id;";
            cmd.Parameters.AddWithValue("@guild_id", guildId);
            await cmd.ExecuteNonQueryAsync();

            // Wyczyść cache
            lock (_cacheLock)
            {
                _cache.Remove(guildId);
            }

            System.Diagnostics.Debug.WriteLine($"[SustatsCredentialsRepository] Credentials usunięte dla guild {guildId}.");
        }

        /// <inheritdoc/>
        public async Task DeleteAllAsync()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM sustats_credentials;";
            await cmd.ExecuteNonQueryAsync();

            lock (_cacheLock)
            {
                _cache.Clear();
            }

            System.Diagnostics.Debug.WriteLine("[SustatsCredentialsRepository] Wszystkie credentials usunięte.");
        }

        /// <inheritdoc/>
        public async Task<SustatsCredentials?> GetActiveAsync()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();

            // JOIN z user_settings.active_sustats_guild_id
            cmd.CommandText = @"
                SELECT sc.*
                FROM sustats_credentials sc
                INNER JOIN user_settings us ON us.active_sustats_guild_id = sc.guild_id
                WHERE us.id = 1;";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var creds = MapFromReader(reader);
                lock (_cacheLock)
                {
                    _cache[creds.GuildId] = creds;
                }
                return creds;
            }

            return null;
        }

        /// <summary>
        /// Mapuje wiersz z SqliteDataReader na obiekt SustatsCredentials.
        /// </summary>
        private static SustatsCredentials MapFromReader(SqliteDataReader reader)
        {
            return new SustatsCredentials
            {
                GuildId = reader.GetString(reader.GetOrdinal("guild_id")),
                ServerName = reader.GetString(reader.GetOrdinal("server_name")),
                TokenEncrypted = reader.GetString(reader.GetOrdinal("token_enc")),
                SecretEncrypted = reader.GetString(reader.GetOrdinal("secret_enc")),
                Endpoint = reader.GetString(reader.GetOrdinal("endpoint")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
            };
        }
    }
}
