using System;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

public sealed class CatalogSyncStateRepository : ICatalogSyncStateRepository
{
    private readonly DatabaseService _db;

    public CatalogSyncStateRepository(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public CatalogSnapshotMetadata? Get(string key)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT key, etag, last_success_utc, failure_count, next_allowed_attempt_utc
            FROM sync_state
            WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new CatalogSnapshotMetadata
        {
            Key = reader.GetString(0),
            ETag = reader.IsDBNull(1) ? null : reader.GetString(1),
            LastSuccessUtc = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
            FailureCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            NextAllowedAttemptUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4))
        };
    }

    public void SaveSuccess(string key, string? etag, string? lastModified, DateTime successUtc)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state (
                key, etag, last_modified, last_success_utc, last_attempt_utc,
                last_error_code, failure_count, next_allowed_attempt_utc
            ) VALUES (
                @key, @etag, @last_modified, @last_success_utc, @last_attempt_utc,
                NULL, 0, NULL
            )
            ON CONFLICT(key) DO UPDATE SET
                etag = excluded.etag,
                last_modified = excluded.last_modified,
                last_success_utc = excluded.last_success_utc,
                last_attempt_utc = excluded.last_attempt_utc,
                last_error_code = NULL,
                failure_count = 0,
                next_allowed_attempt_utc = NULL;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@last_modified", (object?)lastModified ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@last_success_utc", successUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@last_attempt_utc", successUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SaveFailure(string key, string? errorCode, DateTime attemptUtc, DateTime? nextAllowedAttemptUtc)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_state (
                key, last_attempt_utc, last_error_code, failure_count, next_allowed_attempt_utc
            ) VALUES (
                @key, @last_attempt_utc, @last_error_code, 1, @next_allowed_attempt_utc
            )
            ON CONFLICT(key) DO UPDATE SET
                last_attempt_utc = excluded.last_attempt_utc,
                last_error_code = excluded.last_error_code,
                failure_count = sync_state.failure_count + 1,
                next_allowed_attempt_utc = excluded.next_allowed_attempt_utc;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@last_attempt_utc", attemptUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@last_error_code", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@next_allowed_attempt_utc",
            nextAllowedAttemptUtc.HasValue ? nextAllowedAttemptUtc.Value.ToString("O") : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void ClearFailure(string key)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state
            SET last_error_code = NULL, failure_count = 0, next_allowed_attempt_utc = NULL
            WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.ExecuteNonQuery();
    }
}
