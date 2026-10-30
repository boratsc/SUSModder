using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

/// <summary>
/// Repozytorium oczekiwanych referencji ikon (3.1-04) — tabela <c>icon_assignments</c>.
/// Cache w pamięci z zapisem „write-through”: najpierw trwały zapis do SQLite,
/// dopiero po jego powodzeniu aktualizowany jest cache. Odczyty nie wykonują
/// operacji sieciowych.
/// </summary>
public sealed class IconAssignmentRepository : IIconAssignmentRepository
{
    private readonly DatabaseService _db;
    private readonly object _lock = new();

    /// <summary>Cache w pamięci; null oznacza „nie wczytano jeszcze z bazy”.</summary>
    private Dictionary<string, IconAssignment>? _cache;

    public IconAssignmentRepository(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public IReadOnlyList<IconAssignment> GetAll()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _cache!.Values
                .OrderBy(a => a.AssignmentKey, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <inheritdoc/>
    public IconAssignment? Get(string assignmentKey)
    {
        if (string.IsNullOrWhiteSpace(assignmentKey))
            return null;

        lock (_lock)
        {
            EnsureLoaded();
            return _cache!.GetValueOrDefault(assignmentKey);
        }
    }

    /// <inheritdoc/>
    public IconAssignment? GetForMod(int modId)
    {
        if (modId <= 0)
            return null;

        return Get(IconAssignmentKeys.ForMod(modId));
    }

    /// <inheritdoc/>
    public IconAssignment? GetGlobal(IconGlobalRole role) => Get(IconAssignmentKeys.ForGlobal(role));

    /// <inheritdoc/>
    public bool Contains(string assignmentKey) => Get(assignmentKey) is not null;

    /// <inheritdoc/>
    public void Upsert(IconAssignment assignment)
    {
        if (assignment is null)
            throw new ArgumentNullException(nameof(assignment));

        if (string.IsNullOrWhiteSpace(assignment.AssignmentKey))
            throw new ArgumentException("Assignment key cannot be empty.", nameof(assignment));

        lock (_lock)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO icon_assignments (
                    assignment_key, is_removed, asset_id, url, sha256, content_type,
                    size_bytes, schema_version, updated_at
                ) VALUES (
                    @assignment_key, @is_removed, @asset_id, @url, @sha256, @content_type,
                    @size_bytes, @schema_version, @updated_at
                )
                ON CONFLICT(assignment_key) DO UPDATE SET
                    is_removed = excluded.is_removed,
                    asset_id = excluded.asset_id,
                    url = excluded.url,
                    sha256 = excluded.sha256,
                    content_type = excluded.content_type,
                    size_bytes = excluded.size_bytes,
                    schema_version = excluded.schema_version,
                    updated_at = excluded.updated_at;";
            cmd.Parameters.AddWithValue("@assignment_key", assignment.AssignmentKey);
            cmd.Parameters.AddWithValue("@is_removed", assignment.IsRemoved ? 1 : 0);
            cmd.Parameters.AddWithValue("@asset_id", assignment.AssetId ?? string.Empty);
            cmd.Parameters.AddWithValue("@url", assignment.Url ?? string.Empty);
            cmd.Parameters.AddWithValue("@sha256", assignment.Sha256 ?? string.Empty);
            cmd.Parameters.AddWithValue("@content_type", assignment.ContentType ?? string.Empty);
            cmd.Parameters.AddWithValue("@size_bytes", assignment.SizeBytes);
            cmd.Parameters.AddWithValue("@schema_version", assignment.SchemaVersion);
            cmd.Parameters.AddWithValue("@updated_at", assignment.UpdatedAtUtc.ToString("O"));

            cmd.ExecuteNonQuery();

            EnsureLoaded();
            _cache![assignment.AssignmentKey] = assignment;
        }
    }

    /// <inheritdoc/>
    public void MarkRemoved(string assignmentKey, int schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(assignmentKey))
            throw new ArgumentException("Assignment key cannot be empty.", nameof(assignmentKey));

        Upsert(new IconAssignment
        {
            AssignmentKey = assignmentKey,
            IsRemoved = true,
            SchemaVersion = schemaVersion,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    /// <inheritdoc/>
    public bool Purge(string assignmentKey)
    {
        if (string.IsNullOrWhiteSpace(assignmentKey))
            return false;

        lock (_lock)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM icon_assignments WHERE assignment_key = @assignment_key;";
            cmd.Parameters.AddWithValue("@assignment_key", assignmentKey);
            var removed = cmd.ExecuteNonQuery() > 0;

            EnsureLoaded();
            _cache!.Remove(assignmentKey);
            return removed;
        }
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }

    private void EnsureLoaded()
    {
        if (_cache is not null)
            return;

        var loaded = new Dictionary<string, IconAssignment>(StringComparer.Ordinal);
        try
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT assignment_key, is_removed, asset_id, url, sha256, content_type,
                       size_bytes, schema_version, updated_at
                FROM icon_assignments
                ORDER BY assignment_key;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                loaded[key] = new IconAssignment
                {
                    AssignmentKey = key,
                    IsRemoved = reader.IsDBNull(1) ? false : reader.GetInt64(1) != 0,
                    AssetId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Url = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Sha256 = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    ContentType = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    SizeBytes = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    SchemaVersion = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    UpdatedAtUtc = reader.IsDBNull(8) ? DateTime.MinValue : ParseUtc(reader.GetString(8))
                };
            }

            _cache = loaded;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IconAssignmentRepository] Błąd odczytu przypisań ikon: {ex.Message}");
            throw;
        }
    }

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : DateTime.MinValue;
}
