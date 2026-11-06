namespace SUSModder.Core.Models;

/// <summary>
/// Stabilne kody błędów dla operacji na ulubionych modach (3.1-05).
/// UI mapuje kody na zlokalizowane komunikaty (PL/EN).
/// <see cref="FavoriteModsOperationResult.TechnicalMessage"/> i
/// <see cref="FavoriteModsSnapshot.TechnicalMessage"/> to komunikaty techniczne
/// do logów — nie są tekstem dla użytkownika.
/// </summary>
public static class FavoriteModsErrorCodes
{
    /// <summary>Id moda jest nieprawidłowe (puste lub &lt;= 0).</summary>
    public const string InvalidModId = "FAVORITES_INVALID_MOD_ID";

    /// <summary>Baza danych z ulubionymi jest niedostępna (odczyt nieudany).</summary>
    public const string StorageUnavailable = "FAVORITES_STORAGE_UNAVAILABLE";

    /// <summary>Zapis ulubionego nie powiódł się — stan w pamięci pozostał niezmieniony.</summary>
    public const string WriteFailed = "FAVORITES_WRITE_FAILED";
}

/// <summary>
/// Wynik operacji zapisu ulubionego (gwiazdki).
/// Zwraca stabilny kod błędu zamiast tekstu dla użytkownika —
/// lokalizacja leży po stronie UI.
/// </summary>
public sealed class FavoriteModsOperationResult
{
    /// <summary>true, gdy operacja zakończyła się powodzeniem (także gdy stan już był taki sam).</summary>
    public bool Success { get; init; }

    /// <summary>Stabilny kod błędu (patrz <see cref="FavoriteModsErrorCodes"/>); null przy powodzeniu.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Komunikat techniczny do logów; nigdy nie jest prezentowany użytkownikowi.</summary>
    public string? TechnicalMessage { get; init; }

    /// <summary>Zmiana stanu w pamięci — true tylko gdy zapis faktycznie zmienił stan.</summary>
    public bool StateChanged { get; init; }

    public static FavoriteModsOperationResult Ok(bool stateChanged = false) => new()
    {
        Success = true,
        StateChanged = stateChanged
    };

    public static FavoriteModsOperationResult Fail(string errorCode, string? technicalMessage = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        TechnicalMessage = technicalMessage
    };
}

/// <summary>
/// Migawka ulubionych modów (katalogowe Id) wraz z informacją o ewentualnym błędzie odczytu.
/// Zbiór jest domyślnie pusty — brak ulubionych to prawidłowy stan, nie błąd.
/// </summary>
public sealed class FavoriteModsSnapshot
{
    /// <summary>Id ulubionych modów posortowane rosnąco (deterministyczna kolejność).</summary>
    public IReadOnlyList<int> ModIds { get; init; } = Array.Empty<int>();

    /// <summary>true, gdy udało się wczytać stan z bazy; false przy błędzie odczytu.</summary>
    public bool IsLoaded { get; init; }

    /// <summary>Stabilny kod błędu odczytu (patrz <see cref="FavoriteModsErrorCodes"/>); null przy powodzeniu.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Komunikat techniczny do logów; nigdy nie jest prezentowany użytkownikowi.</summary>
    public string? TechnicalMessage { get; init; }
}
