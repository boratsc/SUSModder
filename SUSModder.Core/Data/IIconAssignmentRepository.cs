using SUSModder.Core.Models;

namespace SUSModder.Core.Data;

/// <summary>
/// Repozytorium oczekiwanych referencji ikon (3.1-04) — tabela <c>icon_assignments</c>.
/// Przechowuje wyłącznie metadane oczekiwanych zasobów; bajty obrazu pozostają
/// w technicznym cache plikowym. Repozytorium nie wykonuje żadnych operacji sieciowych.
/// </summary>
public interface IIconAssignmentRepository
{
    /// <summary>Zwraca wszystkie zapisane przypisania (klucz rosnąco), wraz z tombstonami.</summary>
    IReadOnlyList<IconAssignment> GetAll();

    /// <summary>Zwraca przypisanie o podanym stabilnym kluczu albo null.</summary>
    IconAssignment? Get(string assignmentKey);

    /// <summary>Zwraca przypisanie ikony moda (klucz <c>mod:&lt;id&gt;</c>) albo null.</summary>
    IconAssignment? GetForMod(int modId);

    /// <summary>Zwraca przypisanie ikony globalnej (vanilla / dll) albo null.</summary>
    IconAssignment? GetGlobal(IconGlobalRole role);

    /// <summary>Sprawdza, czy istnieje przypisanie o podanym kluczu.</summary>
    bool Contains(string assignmentKey);

    /// <summary>Wstawia lub aktualizuje przypisanie (UPSERT).</summary>
    void Upsert(IconAssignment assignment);

    /// <summary>
    /// Zapisuje trwały tombstone: backend potwierdził w schemacie 1, że przypisanie
    /// zostało jawnie usunięte. Wiersz pozostaje w bazie, aby odróżnić „usunięte”
    /// od „nigdy nie było przypisania” (restart offline pokazuje placeholder).
    /// </summary>
    void MarkRemoved(string assignmentKey, int schemaVersion);

    /// <summary>
    /// Usuwa wiersz przypisania razem z tombstonem. Używane wyłącznie przy jawnym
    /// resecie przypisań — synchronizacja nigdy nie usuwa wierszy.
    /// </summary>
    bool Purge(string assignmentKey);

    /// <summary>Czyści cache w pamięci — kolejny odczyt wczyta stan z bazy.</summary>
    void ClearCache();
}
