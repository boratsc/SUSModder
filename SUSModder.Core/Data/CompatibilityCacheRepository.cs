using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SUSModder.Core.Data;

public sealed class CompatibilityCacheRepository : ICompatibilityCacheRepository
{
    private readonly DatabaseService _db;

    public CompatibilityCacheRepository(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public CompatibilityCacheEntry? GetPair(int fullModId, string fullModVersion, int dllModId, string dllModVersion)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT full_mod_id, full_mod_version, dll_mod_id, dll_mod_version,
                   status, is_exact_version, warning, source_updated_at, fetched_at_utc
            FROM compatibility_cache
            WHERE full_mod_id = @full_mod_id
              AND full_mod_version = @full_mod_version
              AND dll_mod_id = @dll_mod_id
              AND dll_mod_version = @dll_mod_version;";
        cmd.Parameters.AddWithValue("@full_mod_id", fullModId);
        cmd.Parameters.AddWithValue("@full_mod_version", fullModVersion ?? string.Empty);
        cmd.Parameters.AddWithValue("@dll_mod_id", dllModId);
        cmd.Parameters.AddWithValue("@dll_mod_version", dllModVersion ?? string.Empty);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<CompatibilityCacheEntry> GetForFullMod(int fullModId, string fullModVersion)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT full_mod_id, full_mod_version, dll_mod_id, dll_mod_version,
                   status, is_exact_version, warning, source_updated_at, fetched_at_utc
            FROM compatibility_cache
            WHERE full_mod_id = @full_mod_id
              AND full_mod_version = @full_mod_version;";
        cmd.Parameters.AddWithValue("@full_mod_id", fullModId);
        cmd.Parameters.AddWithValue("@full_mod_version", fullModVersion ?? string.Empty);

        return ReadAll(cmd);
    }

    public IReadOnlyList<CompatibilityCacheEntry> GetForDllMod(int dllModId, string dllModVersion)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT full_mod_id, full_mod_version, dll_mod_id, dll_mod_version,
                   status, is_exact_version, warning, source_updated_at, fetched_at_utc
            FROM compatibility_cache
            WHERE dll_mod_id = @dll_mod_id
              AND dll_mod_version = @dll_mod_version;";
        cmd.Parameters.AddWithValue("@dll_mod_id", dllModId);
        cmd.Parameters.AddWithValue("@dll_mod_version", dllModVersion ?? string.Empty);

        return ReadAll(cmd);
    }

    public void SaveSnapshot(IEnumerable<CompatibilityCacheEntry> entries, string? revision, DateTime fetchedAtUtc)
    {
        var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var clearCmd = conn.CreateCommand())
            {
                clearCmd.Transaction = tx;
                clearCmd.CommandText = "DELETE FROM compatibility_cache;";
                clearCmd.ExecuteNonQuery();
            }

            foreach (var entry in entries)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO compatibility_cache (
                        full_mod_id, full_mod_version, dll_mod_id, dll_mod_version,
                        status, is_exact_version, warning, source_updated_at, fetched_at_utc
                    ) VALUES (
                        @full_mod_id, @full_mod_version, @dll_mod_id, @dll_mod_version,
                        @status, @is_exact_version, @warning, @source_updated_at, @fetched_at_utc
                    );";
                insertCmd.Parameters.AddWithValue("@full_mod_id", entry.FullModId);
                insertCmd.Parameters.AddWithValue("@full_mod_version", entry.FullModVersion);
                insertCmd.Parameters.AddWithValue("@dll_mod_id", entry.DllModId);
                insertCmd.Parameters.AddWithValue("@dll_mod_version", entry.DllModVersion);
                insertCmd.Parameters.AddWithValue("@status", entry.Status);
                insertCmd.Parameters.AddWithValue("@is_exact_version", entry.IsExactVersion ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@warning", (object?)entry.Warning ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@source_updated_at", (object?)entry.SourceUpdatedAt ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@fetched_at_utc", fetchedAtUtc.ToString("O"));
                insertCmd.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(revision))
            {
                using var metaCmd = conn.CreateCommand();
                metaCmd.Transaction = tx;
                metaCmd.CommandText = @"
                    INSERT INTO sync_state (key, etag, last_success_utc, last_attempt_utc, failure_count)
                    VALUES ('compatibility.snapshot', @etag, @utc, @utc, 0)
                    ON CONFLICT(key) DO UPDATE SET
                        etag = excluded.etag,
                        last_success_utc = excluded.last_success_utc,
                        last_attempt_utc = excluded.last_attempt_utc,
                        last_error_code = NULL,
                        failure_count = 0,
                        next_allowed_attempt_utc = NULL;";
                metaCmd.Parameters.AddWithValue("@etag", revision);
                metaCmd.Parameters.AddWithValue("@utc", fetchedAtUtc.ToString("O"));
                metaCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public int Count()
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM compatibility_cache;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<CompatibilityCacheEntry> ReadAll(SqliteCommand cmd)
    {
        var result = new List<CompatibilityCacheEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(Map(reader));
        return result;
    }

    private static CompatibilityCacheEntry Map(SqliteDataReader reader) => new()
    {
        FullModId = reader.GetInt32(0),
        FullModVersion = reader.GetString(1),
        DllModId = reader.GetInt32(2),
        DllModVersion = reader.GetString(3),
        Status = reader.GetString(4),
        IsExactVersion = reader.GetInt32(5) != 0,
        Warning = reader.IsDBNull(6) ? null : reader.GetString(6),
        SourceUpdatedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
        FetchedAtUtc = DateTime.Parse(reader.GetString(8))
    };
}
