using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

/// <summary>
/// Repozytorium ulubionych modów (3.1-05) — tabela <c>favorite_mods</c> w SQLite.
/// Oznaczenie (gwiazdka) jest trwałą lokalną preferencją powiązaną ze stabilnym
/// Id katalogowym moda. Preferencja jest niezależna od wersji, nazwy, ścieżki
/// instalacji oraz od cyklu życia katalogu: odświeżenie katalogu, instalacja,
/// aktualizacja i usunięcie moda nie usuwają zapisu, a chwilowy brak moda
/// w katalogu nie tworzy z wpisu dostępnej do instalacji karty.
/// </summary>
/// <remarks>
/// Osobne repozytorium (zamiast kolumny w tabeli <c>mods</c>) eliminuje ryzyko
/// nadpisania preferencji przez synchronizację katalogu.
/// </remarks>
public interface IFavoriteModsRepository
{
    /// <summary>
    /// Zwraca migawkę ulubionych Id wraz z informacją o ewentualnym błędzie odczytu.
    /// Nie rzuca wyjątków — błąd odczytu jest raportowany przez
    /// <see cref="FavoriteModsSnapshot.ErrorCode"/>.
    /// </summary>
    FavoriteModsSnapshot GetSnapshot();

    /// <summary>
    /// Zwraca listę ulubionych katalogowych Id (posortowaną rosnąco).
    /// Przy błędzie odczytu zwraca pustą listę — stan jest wtedy nieznany, nie pusty.
    /// </summary>
    IReadOnlyList<int> GetFavoriteModIds();

    /// <summary>
    /// Sprawdza, czy mod o podanym Id jest oznaczony jako ulubiony.
    /// Przy błędzie odczytu zwraca false (stan nieznany traktowany jako brak ulubienia).
    /// </summary>
    bool IsFavorite(int modId);

    /// <summary>
    /// Ustawia stan ulubionego (dodaje lub usuwa). Operacja idempotentna.
    /// Przy błędzie zapisu stan w pamięci pozostaje niezmieniony, dzięki czemu
    /// UI może spójnie wycofać optymistyczną zmianę.
    /// </summary>
    FavoriteModsOperationResult SetFavorite(int modId, bool isFavorite);

    /// <summary>Dodaje moda do ulubionych (idempotentne).</summary>
    FavoriteModsOperationResult AddFavorite(int modId);

    /// <summary>Usuwa moda z ulubionych (idempotentne).</summary>
    FavoriteModsOperationResult RemoveFavorite(int modId);

    /// <summary>Czyści cache w pamięci — kolejny odczyt wczyta stan ponownie z bazy.</summary>
    void ClearCache();
}
