using System;

namespace SUSModder.Core.Models;

/// <summary>
/// Wynik rewalidacji zatwierdzonego celu aktualizacji.
///
/// Podsumowanie (preview) jest wiązane z konkretną wersją. Jeśli katalog zmieni się
/// między podglądem a wykonaniem, instalacja innej wersji po cichu jest niedopuszczalna —
/// wymagane jest ponowne potwierdzenie.
/// </summary>
public sealed class UpdateTargetValidationResult
{
    /// <summary>Status rewalidacji.</summary>
    public UpdateTargetValidationStatus Status { get; init; } = UpdateTargetValidationStatus.Valid;

    /// <summary>Wersja zatwierdzona w podsumowaniu.</summary>
    public string ApprovedVersion { get; init; } = string.Empty;

    /// <summary>Wersja Among Us zatwierdzona w podsumowaniu.</summary>
    public string ApprovedAmongVersion { get; init; } = string.Empty;

    /// <summary>Wersja aktualnie dostępna w katalogu (null, gdy moda nie ma w katalogu).</summary>
    public string? AvailableVersion { get; init; }

    /// <summary>Wersja Among Us aktualnie dostępna w katalogu.</summary>
    public string? AvailableAmongVersion { get; init; }

    /// <summary>Czy wymagane jest ponowne potwierdzenie przez użytkownika.</summary>
    public bool RequiresConfirmation => Status != UpdateTargetValidationStatus.Valid;

    /// <summary>Stabilny kod błędu — UI lokalizuje, nie buduje tekstu z Core.</summary>
    public string ErrorCode => Status switch
    {
        UpdateTargetValidationStatus.Valid => "update_preview.target_valid",
        UpdateTargetValidationStatus.TargetChanged => "update_preview.target_changed",
        UpdateTargetValidationStatus.TargetMissing => "update_preview.target_missing",
        _ => "update_preview.catalog_unavailable"
    };
}

/// <summary>
/// Status rewalidacji zatwierdzonego celu.
/// </summary>
public enum UpdateTargetValidationStatus
{
    /// <summary>Cel nadal odpowiada zatwierdzonemu podsumowaniu.</summary>
    Valid = 0,

    /// <summary>Katalog oferuje inną wersję (lub inną wersję gry) niż zatwierdzona.</summary>
    TargetChanged = 1,

    /// <summary>Mod zniknął z katalogu.</summary>
    TargetMissing = 2,

    /// <summary>Nie udało się pobrać katalogu — nie można potwierdzić zgodności celu.</summary>
    CatalogUnavailable = 3
}

/// <summary>
/// Decyzja po rewalidacji zatwierdzonego celu — wspólna dla ścieżek ręcznej,
/// automatycznej i zbiorczej.
///
/// Zasada: instalować wolno wyłącznie wersję zatwierdzoną przez użytkownika
/// (albo wersję ogłoszoną w auto-update, jeśli nadal jest aktualna).
/// „Wymaga potwierdzenia” i „przerwane” oznaczają, że nie wolno doinstalować
/// innej wersji po cichu.
/// </summary>
public static class UpdateTargetApproval
{
    /// <summary>Decyzja po rewalidacji.</summary>
    public enum Decision
    {
        /// <summary>Cel nie zmienił się — można instalować zatwierdzoną wersję.</summary>
        Approved = 0,

        /// <summary>Cel się zmienił — trzeba poprosić użytkownika o ponowne potwierdzenie.</summary>
        RequiresConfirmation = 1,

        /// <summary>Nie da się potwierdzić celu (brak katalogu, brak moda) — operacja przerwana.</summary>
        Aborted = 2
    }

    /// <summary>
    /// Mapuje wynik rewalidacji na decyzję operacyjną.
    /// </summary>
    public static Decision Decide(UpdateTargetValidationResult? validation)
    {
        if (validation == null)
            return Decision.Aborted;

        return validation.Status switch
        {
            UpdateTargetValidationStatus.Valid => Decision.Approved,
            UpdateTargetValidationStatus.TargetChanged => Decision.RequiresConfirmation,
            _ => Decision.Aborted
        };
    }
}
