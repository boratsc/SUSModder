using System;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Utilities;

namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>
/// Gating akcji zapisu dla paczek w trybie ownerToken (kontrakt 02.8):
/// bez dostępnego tokenu UI ukrywa/wyłącza akcje zapisu i NIE wysyła żądań.
/// null OwnershipMode = nieznany → traktujemy jak legacy (bez blokady).
/// Tri-state <see cref="OwnerTokenState"/>: Available = normalnie, Missing = utrata tokenu,
/// Unreadable = neutralny „nie udało się sprawdzić” (błąd przejściowy, do ponowienia).
/// Komunikaty są neutralne i bez żargonu. ExpiresAt null = data nieznana (TTL paczki osobistej
/// zawsze istnieje) → bazowy tekst kontraktu, bez zdania o konkretnej dacie.
/// </summary>
public static class ModPackOwnerTokenGate
{
    public static bool IsOwnerTokenMode(string? ownershipMode) =>
        string.Equals(ownershipMode, "ownerToken", StringComparison.OrdinalIgnoreCase);

    /// <summary>ownerToken bez dostępnego tokenu → blokada zapisu (fail-closed). legacy/null → bez blokady.</summary>
    public static bool IsWriteBlocked(string? ownershipMode, OwnerTokenState state) =>
        IsOwnerTokenMode(ownershipMode) && state != OwnerTokenState.Available;

    /// <summary>Komunikat pod zablokowanym wierszem listy; null gdy brak blokady.</summary>
    public static string? WriteBlockedMessage(
        string? ownershipMode, OwnerTokenState state, DateTimeOffset? expiresAt, ILocalizationService loc)
    {
        if (!IsWriteBlocked(ownershipMode, state))
            return null;
        return state == OwnerTokenState.Unreadable
            ? loc.Get("ModPacks.Shared.OwnerTokenUnreadable")
            : TokenLostMessage(expiresAt, loc);
    }

    /// <summary>
    /// Czy blokada zapisu jest nieodwracalna (Missing → ukryj „Ponów”, komunikat kontraktu),
    /// czy przejściowa (Unreadable → neutralny „nie udało się sprawdzić” + „Ponów”).
    /// legacy/null/Available → false (brak nieodwracalnej utraty). Spójne z listą „Udostępnione”.
    /// </summary>
    public static bool IsIrreversibleWriteLoss(string? ownershipMode, OwnerTokenState state) =>
        IsWriteBlocked(ownershipMode, state) && state != OwnerTokenState.Unreadable;

    /// <summary>
    /// Komunikat utraty tokenu (lista „Udostępnione”, błąd PACK_NOT_MANAGEABLE w trybie ownerToken).
    /// ExpiresAt null → bazowy klucz kontraktu (data nieznana, TTL zawsze istnieje).
    /// </summary>
    public static string TokenLostMessage(DateTimeOffset? expiresAt, ILocalizationService loc) =>
        expiresAt.HasValue
            ? loc.GetFormatted("ModPacks.Shared.OwnerTokenLostWithDate", FormatDate(expiresAt.Value))
            : loc.Get("ModPacks.Shared.OwnerTokenLost");

    /// <summary>Komunikat OWNER_TOKEN_STORE_FAILED: paczka powstała, ale nie można nią zarządzać z tego urządzenia.</summary>
    public static string StoreFailedMessage(DateTimeOffset? expiresAt, ILocalizationService loc) =>
        expiresAt.HasValue
            ? loc.GetFormatted("ModPacks.OwnerTokenStoreFailedWithDate", FormatDate(expiresAt.Value))
            : loc.Get("ModPacks.OwnerTokenStoreFailed");

    /// <summary>
    /// 403 PACK_NOT_MANAGEABLE: ownerToken → komunikat utraty tokenu; legacy/null → neutralny brak uprawnień
    /// (bez straszenia utratą tokenu, której w legacy nie ma).
    /// </summary>
    public static string NotManageableMessage(string? ownershipMode, DateTimeOffset? expiresAt, ILocalizationService loc) =>
        IsOwnerTokenMode(ownershipMode)
            ? TokenLostMessage(expiresAt, loc)
            : loc.Get("ModPacks.NotManageable");

    /// <summary>
    /// Czy pokazać przycisk ponowienia wykonania. Przy nieodwracalnej utracie tokenu ukryty
    /// (zasada jeden przycisk na problem — zostaje samo zamknięcie + informacja).
    /// </summary>
    public static bool ShouldShowRetry(bool settled, bool allDone, int stageCount, bool irreversibleWriteLoss) =>
        settled && !allDone && stageCount > 0 && !irreversibleWriteLoss;

    /// <summary>Czy pokazać wskazówkę o ponowieniu finalizacji (nie przy nieodwracalnej utracie tokenu).</summary>
    public static bool ShouldShowFinalizeRetryHint(bool packAlreadyCreatedOnServer, bool irreversibleWriteLoss) =>
        packAlreadyCreatedOnServer && !irreversibleWriteLoss;

    /// <summary>Format spójny z resztą UI (SharedModPackItem.ExpiresAtText).</summary>
    private static string FormatDate(DateTimeOffset expiresAt) =>
        expiresAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
