using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla tokenów Discord OAuth2 (tabela discord_auth).
    /// Singleton (CHECK id = 1), cache w pamięci.
    /// Wzorzec implementacji: UserSettingsRepository.
    /// </summary>
    public class DiscordAuthRepository : IDiscordAuthRepository
    {
        private readonly DatabaseService _db;
        private DiscordTokenInfo? _cachedToken;
        private readonly object _cacheLock = new();

        public DiscordAuthRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc/>
        public async Task<DiscordTokenInfo?> GetTokenInfoAsync()
        {
            // Zwróć z cache jeśli dostępne
            if (_cachedToken != null)
                return _cachedToken;

            lock (_cacheLock)
            {
                if (_cachedToken != null)
                    return _cachedToken;

                var conn = _db.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM discord_auth WHERE id = 1;";

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    _cachedToken = MapFromReader(reader);
                }

                return _cachedToken;
            }
        }

        /// <inheritdoc/>
        public async Task SaveTokenInfoAsync(DiscordTokenInfo info)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO discord_auth (
                    id, access_token_enc, refresh_token_enc, token_type,
                    expires_at, discord_user_id, discord_username,
                    created_at, updated_at
                ) VALUES (
                    1, @access_token_enc, @refresh_token_enc, @token_type,
                    @expires_at, @discord_user_id, @discord_username,
                    COALESCE((SELECT created_at FROM discord_auth WHERE id = 1), datetime('now')),
                    datetime('now')
                )
                ON CONFLICT(id) DO UPDATE SET
                    access_token_enc = excluded.access_token_enc,
                    refresh_token_enc = excluded.refresh_token_enc,
                    token_type = excluded.token_type,
                    expires_at = excluded.expires_at,
                    discord_user_id = excluded.discord_user_id,
                    discord_username = excluded.discord_username,
                    updated_at = excluded.updated_at;";

            cmd.Parameters.AddWithValue("@access_token_enc", info.AccessTokenEncrypted ?? string.Empty);
            cmd.Parameters.AddWithValue("@refresh_token_enc", info.RefreshTokenEncrypted ?? string.Empty);
            cmd.Parameters.AddWithValue("@token_type", info.TokenType ?? "Bearer");
            cmd.Parameters.AddWithValue("@expires_at", info.ExpiresAt.ToString("O"));
            cmd.Parameters.AddWithValue("@discord_user_id", info.DiscordUserId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@discord_username", info.DiscordUsername ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();

            // Aktualizuj cache
            lock (_cacheLock)
            {
                info.CreatedAt = DateTime.UtcNow;
                info.UpdatedAt = DateTime.UtcNow;
                _cachedToken = info;
            }

            System.Diagnostics.Debug.WriteLine("[DiscordAuthRepository] Token Discord OAuth2 zapisany.");
        }

        /// <inheritdoc/>
        public async Task ClearTokenAsync()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM discord_auth WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();

            // Wyczyść cache
            lock (_cacheLock)
            {
                _cachedToken = null;
            }

            System.Diagnostics.Debug.WriteLine("[DiscordAuthRepository] Token Discord OAuth2 usunięty.");
        }

        /// <summary>
        /// Mapuje wiersz z SqliteDataReader na obiekt DiscordTokenInfo.
        /// </summary>
        private static DiscordTokenInfo MapFromReader(SqliteDataReader reader)
        {
            return new DiscordTokenInfo
            {
                AccessTokenEncrypted = reader.GetString(reader.GetOrdinal("access_token_enc")),
                RefreshTokenEncrypted = reader.GetString(reader.GetOrdinal("refresh_token_enc")),
                TokenType = reader.GetString(reader.GetOrdinal("token_type")),
                ExpiresAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("expires_at"))),
                DiscordUserId = reader.IsDBNull(reader.GetOrdinal("discord_user_id"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("discord_user_id")),
                DiscordUsername = reader.IsDBNull(reader.GetOrdinal("discord_username"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("discord_username")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
            };
        }
    }
}
