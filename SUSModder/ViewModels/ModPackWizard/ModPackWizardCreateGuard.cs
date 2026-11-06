using System;
using System.Collections.Generic;
using System.Linq;

namespace SUSModder.ViewModels.ModPackWizard;

/// <summary>
/// Reguły ponawiania tworzenia zestawu online bez duplikatu po niepewnej odpowiedzi serwera.
/// Nie zmienia backendu: korzystamy wyłącznie z istniejącego ListOwnPacksAsync.
/// </summary>
public static class ModPackWizardCreateGuard
{
    /// <summary>
    /// Błędy jednoznacznie oznaczające, że serwer NA PEWNO nie utworzył paczki.
    /// Wszystko inne (sieć, 5xx, nieoczekiwana odpowiedź, nieczytelny kod) jest niepewne —
    /// przed ponowieniem trzeba sprawdzić listę istniejących zestawów.
    /// </summary>
    public static bool IsDefiniteRejection(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            return false;

        return errorCode.Equals("PACK_LIMIT_REACHED", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("INVALID_CREATOR_HASH", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("BAD_REQUEST", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("BadRequest", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("FORBIDDEN", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("Forbidden", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("VALIDATION_FAILED", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("PAYLOAD_TOO_LARGE", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("RequestEntityTooLarge", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("UnsupportedMediaType", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Czy ponowienie musi najpierw zweryfikować, czy serwer już utworzył zestaw.</summary>
    public static bool RequiresProbeBeforeRetry(string? errorCode) => !IsDefiniteRejection(errorCode);

    /// <summary>
    /// Szuka świeżego zestawu odpowiadającego właśnie wysłanemu żądaniu (nazwa + pełny mod + wersja,
    /// aktywny, utworzony w oknie czasowym). Zwraca null, gdy dopasowanie jest niepewne —
    /// wtedy UI nie odpytuje ponownie POST-a automatycznie.
    /// </summary>
    public static SUSModder.Core.Models.ModPackListEntry? TryMatchExistingPack(
        IEnumerable<SUSModder.Core.Models.ModPackListEntry>? packs,
        string? modName,
        int fullModId,
        string? fullModVersion,
        DateTimeOffset nowUtc,
        TimeSpan? maxAge = null)
    {
        if (packs == null || string.IsNullOrWhiteSpace(modName))
            return null;

        var window = maxAge ?? TimeSpan.FromMinutes(10);

        return packs
            .Where(p => p.Active)
            .Where(p => p.CreatedAt.HasValue)
            .Where(p => nowUtc - p.CreatedAt!.Value >= TimeSpan.Zero && nowUtc - p.CreatedAt.Value <= window)
            .Where(p => string.Equals((p.ModName ?? string.Empty).Trim(), modName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(p => fullModId <= 0 || p.FullModId == fullModId)
            .Where(p =>
                string.IsNullOrWhiteSpace(fullModVersion) ||
                string.Equals(p.FullModVersion, fullModVersion, StringComparison.OrdinalIgnoreCase) ||
                fullModVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreatedAt!.Value)
            .FirstOrDefault();
    }

    /// <summary>Czy wynik listowania jest na tyle niepewny (błąd), że nie wolno automatycznie ponawiać POST.</summary>
    public static bool IsProbeInconclusive(SUSModder.Core.Models.ModPackListResult? probeResult) =>
        probeResult == null || !probeResult.Success;
}
