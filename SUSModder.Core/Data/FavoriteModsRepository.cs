using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

/// <summary>
/// Repozytorium ulubionych modów (3.1-05) — tabela <c>favorite_mods</c>.
/// Cache w pamięci z zapisem „write-through”: najpierw trwały zapis do SQLite,
/// dopiero po jego powodzeniu aktualizowany jest cache. Dzięki temu błąd zapisu
/// nie rozjeżdża stanu pamięci z bazą (UI może bezpiecznie wycofać zmianę).
/// </summary>
public sealed class FavoriteModsRepository : IFavoriteModsRepository
{
    private readonly DatabaseService _db;
    private readonly object _lock = new();

    /// <summary>Cache w pamięci; null oznacza „nie wczytano jeszcze z bazy”.</summary>
    private HashSet<int>? _cachedIds;

    public FavoriteModsRepository(DatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public FavoriteModsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            if (_cachedIds == null)
            {
                if (!TryLoadFromDb(out var errorCode, out var technicalMessage))
                {
                    return new FavoriteModsSnapshot
                    {
                        ModIds = Array.Empty<int>(),
                        IsLoaded = false,
                        ErrorCode = errorCode,
                        TechnicalMessage = technicalMessage
                    };
                }
            }

            return new FavoriteModsSnapshot
            {
                ModIds = OrderIds(_cachedIds!),
                IsLoaded = true
            };
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> GetFavoriteModIds() => GetSnapshot().ModIds;

    /// <inheritdoc/>
    public bool IsFavorite(int modId)
    {
        if (modId <= 0)
            return false;

        lock (_lock)
        {
            if (_cachedIds == null && !TryLoadFromDb(out _, out _))
                return false;

            return _cachedIds!.Contains(modId);
        }
    }

    /// <inheritdoc/>
    public FavoriteModsOperationResult SetFavorite(int modId, bool isFavorite)
    {
        if (modId <= 0)
        {
            return FavoriteModsOperationResult.Fail(
                FavoriteModsErrorCodes.InvalidModId,
                $"Nieprawidłowe Id moda: {modId}.");
        }

        lock (_lock)
        {
            // Stan w pamięci musi być pewny, zanim podejmiemy decyzję o zapisie.
            // Gdy cache nie był wczytany, próbujemy go wczytać; przy błędzie odczytu
            // stan pozostaje nieznany – wtedy wykonujemy zapis mimo to (jest szansa
            // powodzenia), a po udanym zapisie cache zostanie unieważniony,
            // aby kolejny odczyt wczytał stan z bazy.
            if (_cachedIds == null)
            {
                TryLoadFromDb(out _, out _);
            }

            var stateKnown = _cachedIds != null;

            // Gdy stan jest znany, operację bez zmian kończymy bez zapisu.
            if (stateKnown)
            {
                var alreadyFavorite = _cachedIds!.Contains(modId);
                if (alreadyFavorite == isFavorite)
                    return FavoriteModsOperationResult.Ok();
            }

            if (!TryWriteToDb(modId, isFavorite, out var errorCode, out var technicalMessage))
            {
                // Cache pozostaje niezmienione — UI wycofuje optymistyczną zmianę.
                return FavoriteModsOperationResult.Fail(
                    errorCode ?? FavoriteModsErrorCodes.WriteFailed,
                    technicalMessage);
            }

            if (!stateKnown)
            {
                // Zapis się powiódł, ale stan w pamięci był nieznany —
                // wymuszamy ponowne wczytanie przy najbliższym odczycie.
                _cachedIds = null;
                return FavoriteModsOperationResult.Ok(stateChanged: true);
            }

            if (isFavorite)
                _cachedIds!.Add(modId);
            else
                _cachedIds!.Remove(modId);

            return FavoriteModsOperationResult.Ok(stateChanged: true);
        }
    }

    /// <inheritdoc/>
    public FavoriteModsOperationResult AddFavorite(int modId) => SetFavorite(modId, true);

    /// <inheritdoc/>
    public FavoriteModsOperationResult RemoveFavorite(int modId) => SetFavorite(modId, false);

    /// <inheritdoc/>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cachedIds = null;
        }
    }

    private static IReadOnlyList<int> OrderIds(HashSet<int> ids) => ids.OrderBy(id => id).ToArray();

    /// <summary>
    /// Wczytuje Id ulubionych z bazy do cache. Przy błędzie cache pozostaje
    /// niewczytany (null), aby kolejne wywołanie mogło powtórzyć próbę.
    /// </summary>
    private bool TryLoadFromDb(out string? errorCode, out string? technicalMessage)
    {
        errorCode = null;
        technicalMessage = null;

        try
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT mod_id FROM favorite_mods ORDER BY mod_id;";

            var ids = new HashSet<int>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetInt32(0));
                }
            }

            _cachedIds = ids;
            return true;
        }
        catch (Exception ex)
        {
            errorCode = FavoriteModsErrorCodes.StorageUnavailable;
            technicalMessage = ex.Message;
            Debug.WriteLine($"[FavoriteModsRepository] Błąd odczytu ulubionych: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Zapisuje zmianę do bazy (INSERT OR IGNORE / DELETE).
    /// Wszystkie wartości są parametryzowane; nazwy kolumn są stałe.
    /// </summary>
    private bool TryWriteToDb(int modId, bool add, out string? errorCode, out string? technicalMessage)
    {
        errorCode = null;
        technicalMessage = null;

        try
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();

            if (add)
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO favorite_mods (mod_id, created_at)
                    VALUES (@mod_id, @created_at);";
                cmd.Parameters.AddWithValue("@mod_id", modId);
                cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));
            }
            else
            {
                cmd.CommandText = "DELETE FROM favorite_mods WHERE mod_id = @mod_id;";
                cmd.Parameters.AddWithValue("@mod_id", modId);
            }

            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex)
        {
            // Brak tabeli (schemat niezmigrowany), baza tylko do odczytu, brak dostępu
            // lub blokada – magazyn ulubionych jest niedostępny, a nie „zapis nieudany”.
            errorCode = IsStorageUnavailable(ex)
                ? FavoriteModsErrorCodes.StorageUnavailable
                : FavoriteModsErrorCodes.WriteFailed;
            technicalMessage = ex.Message;
            Debug.WriteLine($"[FavoriteModsRepository] Błąd zapisu ulubionego (mod_id={modId}): {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            errorCode = FavoriteModsErrorCodes.WriteFailed;
            technicalMessage = ex.Message;
            Debug.WriteLine($"[FavoriteModsRepository] Błąd zapisu ulubionego (mod_id={modId}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Rozpoznaje błędy SQLite oznaczające brak dostępu do magazynu
    /// (SQLITE_ERROR, SQLITE_BUSY, SQLITE_LOCKED, SQLITE_READONLY, SQLITE_CANTOPEN).
    /// </summary>
    private static bool IsStorageUnavailable(SqliteException ex) =>
        ex.SqliteErrorCode is 1 or 5 or 6 or 8 or 14;
}
