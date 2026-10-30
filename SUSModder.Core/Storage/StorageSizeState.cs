namespace SUSModder.Core.Storage;

/// <summary>
/// Stan pomiaru rozmiaru katalogu.
/// Wartości są stabilnymi kodami — UI lokalizuje komunikaty po nazwie stanu.
/// </summary>
public enum StorageSizeState
{
    /// <summary>Trwa pomiar — brak jeszcze wartości (albo wartość jest nieaktualna i odświeżana).</summary>
    Loading = 0,

    /// <summary>Pomiar zakończony w całości — pominięto zero wpisów.</summary>
    Complete = 1,

    /// <summary>
    /// Pomiar zakończony, ale część wpisów została pominięta
    /// (brak dostępu, reparse point, plik zniknął w trakcie skanu).
    /// Suma jest wtedy zaniżona i nie wolno jej przedstawiać jako pełnej.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// Pomiar niemożliwy — ścieżka nie istnieje, jest niepoprawna, jest linkiem albo jest niedostępna.
    /// Niedostępność NIE jest zerem: <see cref="StorageSizeResult.HasValue"/> pozostaje false.
    /// </summary>
    Unavailable = 3,

    /// <summary>
    /// Znana wartość, ale nieaktualna (unieważniona albo poza czasem życia cache).
    /// Powstaje wyłącznie z wyniku, który miał zmierzoną wartość — nigdy z wyniku niedostępnego.
    /// </summary>
    Stale = 4
}

/// <summary>
/// Stabilne kody błędów pomiaru. UI lokalizuje po kodzie, nie po treści wyjątku.
/// </summary>
public enum StorageSizeErrorCode
{
    /// <summary>Brak błędu.</summary>
    None = 0,

    /// <summary>Ścieżka jest pusta.</summary>
    PathEmpty = 1,

    /// <summary>Ścieżka jest niepoprawna (np. zawiera niedozwolone znaki).</summary>
    PathInvalid = 2,

    /// <summary>Ścieżka nie istnieje.</summary>
    PathNotFound = 3,

    /// <summary>Brak uprawnień do odczytu.</summary>
    AccessDenied = 4,

    /// <summary>Błąd wejścia/wyjścia (np. katalog zniknął w trakcie skanu).</summary>
    IoError = 5,

    /// <summary>Operacja anulowana.</summary>
    Cancelled = 6,

    /// <summary>
    /// Ścieżka przechodzi przez reparse point (junction/symlink) — skanowanie zostałoby odłożone
    /// na obce drzewo. Pomiar jest odrzucany, żeby nigdy nie mierzyć poza zakresem.
    /// </summary>
    PathIsLink = 7
}
