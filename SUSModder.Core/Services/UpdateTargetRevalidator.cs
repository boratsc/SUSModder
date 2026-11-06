using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

/// <summary>
/// Rewalidacja zatwierdzonego celu aktualizacji.
///
/// Podsumowanie jest wiązane z konkretną wersją (patrz <see cref="UpdatePreview.TargetKey"/>).
/// Jeśli katalog zmieni się między podglądem a wykonaniem, nie wolno zainstalować innej
/// wersji po cichu — wymagane jest ponowne potwierdzenie przez użytkownika.
/// </summary>
public static class UpdateTargetRevalidator
{
    /// <summary>
    /// Porównuje zatwierdzony cel z aktualnym wpisem w katalogu.
    /// </summary>
    public static UpdateTargetValidationResult Validate(UpdatePreview preview, ModConfiguration? availableTarget)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return Validate(preview.TargetVersion, preview.TargetAmongVersion, availableTarget);
    }

    /// <summary>
    /// Porównuje zatwierdzoną wersję (i wersję gry) z aktualnym wpisem w katalogu.
    /// Używane, gdy podsumowanie nie powstało, a zatwierdzona wersja pochodzi z dialogu.
    /// </summary>
    public static UpdateTargetValidationResult Validate(
        string approvedVersion,
        string? approvedAmongVersion,
        ModConfiguration? availableTarget)
    {
        string safeApprovedVersion = approvedVersion ?? string.Empty;

        if (availableTarget == null)
        {
            return new UpdateTargetValidationResult
            {
                Status = UpdateTargetValidationStatus.TargetMissing,
                ApprovedVersion = safeApprovedVersion,
                ApprovedAmongVersion = approvedAmongVersion ?? string.Empty
            };
        }

        string availableVersion = availableTarget.ModVersion ?? string.Empty;
        string availableAmongVersion = availableTarget.AmongVersion ?? string.Empty;

        bool versionMatches = string.Equals(
            availableVersion,
            safeApprovedVersion,
            StringComparison.OrdinalIgnoreCase);

        // Pusta wartość w katalogu nie może unieważnić zatwierdzonej wersji gry.
        bool amongChanged =
            !string.IsNullOrWhiteSpace(approvedAmongVersion) &&
            !string.IsNullOrWhiteSpace(availableAmongVersion) &&
            !string.Equals(availableAmongVersion, approvedAmongVersion, StringComparison.OrdinalIgnoreCase);

        return new UpdateTargetValidationResult
        {
            Status = versionMatches && !amongChanged
                ? UpdateTargetValidationStatus.Valid
                : UpdateTargetValidationStatus.TargetChanged,
            ApprovedVersion = safeApprovedVersion,
            ApprovedAmongVersion = approvedAmongVersion ?? string.Empty,
            AvailableVersion = availableVersion,
            AvailableAmongVersion = availableAmongVersion
        };
    }

    /// <summary>
    /// Pobiera świeży katalog i sprawdza, czy zatwierdzony cel wciąż jest aktualny.
    /// Brak katalogu (offline / błąd API) oznacza brak możliwości potwierdzenia celu.
    /// </summary>
    public static Task<UpdateTargetValidationResult> RevalidateAsync(
        UpdatePreview preview,
        Func<Task<List<ModConfiguration>?>> loadCatalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return RevalidateAsync(
            loadCatalog,
            preview.ModId,
            preview.ModName,
            preview.TargetVersion,
            preview.TargetAmongVersion,
            cancellationToken);
    }

    /// <summary>
    /// Pobiera świeży katalog i sprawdza, czy zatwierdzona wersja wciąż jest aktualna.
    /// </summary>
    public static Task<UpdateTargetValidationResult> RevalidateAsync(
        string approvedVersion,
        string? approvedAmongVersion,
        Func<Task<List<ModConfiguration>?>> loadCatalog,
        CancellationToken cancellationToken = default) =>
        RevalidateAsync(
            loadCatalog,
            0,
            null,
            approvedVersion,
            approvedAmongVersion,
            cancellationToken);

    /// <summary>
    /// Pobiera świeży katalog i sprawdza, czy zatwierdzona para (wersja moda, wersja gry)
    /// wciąż odpowiada wpisowi tego moda w katalogu.
    ///
    /// Identyfikacja moda: najpierw po Id, potem po nazwie, a w ostateczności
    /// po jednoznacznej wersji. Dzięki temu dwa mody o tej samej wersji
    /// nie powodują fałszywego „brak moda”.
    /// </summary>
    public static async Task<UpdateTargetValidationResult> RevalidateAsync(
        Func<Task<List<ModConfiguration>?>> loadCatalog,
        int modId,
        string? modName,
        string approvedVersion,
        string? approvedAmongVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loadCatalog);

        var catalog = await LoadCatalogAsync(loadCatalog).ConfigureAwait(false);
        if (catalog == null)
        {
            return new UpdateTargetValidationResult
            {
                Status = UpdateTargetValidationStatus.CatalogUnavailable,
                ApprovedVersion = approvedVersion ?? string.Empty,
                ApprovedAmongVersion = approvedAmongVersion ?? string.Empty
            };
        }

        var target = FindTarget(catalog, modId, modName, approvedVersion);
        return Validate(approvedVersion ?? string.Empty, approvedAmongVersion, target);
    }

    private static async Task<List<ModConfiguration>?> LoadCatalogAsync(
        Func<Task<List<ModConfiguration>?>> loadCatalog)
    {
        try
        {
            return await loadCatalog().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Sprawdza zatwierdzoną parę (wersja moda, wersja gry) względem już pobranego katalogu.
    ///
    /// Używane, gdy katalog jest pobierany raz dla całej paczki operacji (auto-update,
    /// zbiorcza aktualizacja) — bez dodatkowych round-tripów do API dla każdego moda.
    /// </summary>
    public static UpdateTargetValidationResult ValidateAgainstCatalog(
        IEnumerable<ModConfiguration>? catalog,
        int modId,
        string? modName,
        string approvedVersion,
        string? approvedAmongVersion)
    {
        if (catalog == null)
        {
            return new UpdateTargetValidationResult
            {
                Status = UpdateTargetValidationStatus.CatalogUnavailable,
                ApprovedVersion = approvedVersion ?? string.Empty,
                ApprovedAmongVersion = approvedAmongVersion ?? string.Empty
            };
        }

        var target = FindTarget(catalog, modId, modName, approvedVersion ?? string.Empty);
        return Validate(approvedVersion ?? string.Empty, approvedAmongVersion, target);
    }

    private static ModConfiguration? FindTarget(
        IEnumerable<ModConfiguration> catalog,
        int modId,
        string? modName,
        string approvedVersion)
    {
        var mods = catalog as IList<ModConfiguration> ?? catalog.ToList();
        bool hasIdentity = modId > 0 || !string.IsNullOrWhiteSpace(modName);

        if (modId > 0)
        {
            var byId = mods.FirstOrDefault(m => m.Id == modId);
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(modName))
        {
            var byName = mods.FirstOrDefault(m => string.Equals(
                m.ModName?.Trim(),
                modName.Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;
        }

        // Gdy znamy tożsamość moda, brak wpisu oznacza „mod zniknął” —
        // nie podmieniamy go innym modem o tej samej wersji.
        if (hasIdentity)
            return null;

        // Bez tożsamości: jednoznaczna wersja.
        return FindTargetByVersion(mods, approvedVersion);
    }

    private static ModConfiguration? FindTargetByVersion(
        IEnumerable<ModConfiguration> catalog,
        string approvedVersion)
    {
        if (string.IsNullOrWhiteSpace(approvedVersion))
            return null;

        var byVersion = catalog
            .Where(m => string.Equals(m.ModVersion, approvedVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byVersion.Count == 1 ? byVersion[0] : null;
    }
}
