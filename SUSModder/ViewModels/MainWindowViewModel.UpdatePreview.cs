using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// 3.1-06 — podsumowanie aktualizacji (preview) i wiązanie zatwierdzenia z celem.
    ///
    /// Izolowany partial: nie zmienia istniejących plików
    /// MainWindowViewModel.Updates.cs ani MainWindowViewModel.BulkOperations.cs
    /// (oba są obsługiwane w ramach tego samego pakietu).
    ///
    /// Zasada: operacja aktualizacji jest wiązana z parą (wersja moda, wersja gry).
    /// Zmiana katalogu po zatwierdzeniu oznacza ponowne potwierdzenie (ścieżka ręczna)
    /// albo pominięcie pozycji (auto/bulk) — nigdy instalację innej wersji po cichu.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>
        /// Zatwierdzony cel aktualizacji: para (wersja moda, wersja gry) + dane źródłowe.
        /// </summary>
        internal sealed record ApprovedUpdateTarget(
            ModUpdateInfo UpdateInfo,
            string Version,
            string? AmongVersion);

        /// <summary>
        /// Wynik zatwierdzenia zbiorczego: mapa moda → zatwierdzony cel + lista pominiętych pozycji.
        /// </summary>
        internal sealed class BulkUpdateApproval
        {
            public IReadOnlyDictionary<string, ApprovedUpdateTarget> Targets { get; init; } =
                new Dictionary<string, ApprovedUpdateTarget>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();
        }

        /// <summary>
        /// Buduje podsumowanie aktualizacji (wyłącznie odczyt).
        /// Zwraca null, gdy nie udało się przygotować podsumowania.
        /// </summary>
        internal static async Task<UpdatePreview?> BuildUpdatePreviewAsync(ModUpdateInfo updateInfo)
        {
            if (updateInfo == null)
                return null;

            try
            {
                var service = UpdatePreviewService.CreateDefault();
                return await service.BuildPreviewAsync(updateInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdatePreview] Nie udało się przygotować podsumowania: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Buduje podsumowania dla listy aktualizacji (np. jedno podsumowanie dla bulk).
        /// </summary>
        internal static async Task<IReadOnlyList<UpdatePreview>> BuildUpdatePreviewsAsync(
            IEnumerable<ModUpdateInfo> updates)
        {
            if (updates == null)
                return Array.Empty<UpdatePreview>();

            try
            {
                var service = UpdatePreviewService.CreateDefault();
                return await service.BuildPreviewsAsync(updates);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdatePreview] Nie udało się przygotować podsumowań: {ex.Message}");
                return Array.Empty<UpdatePreview>();
            }
        }

        /// <summary>
        /// Sprawdza, czy wersja docelowa pobrana do instalacji nadal odpowiada
        /// zatwierdzonej wersji z podsumowania. Różnica = wymagane ponowne potwierdzenie.
        /// </summary>
        internal static bool IsApprovedTargetStillValid(UpdatePreview preview, ModConfiguration? availableTarget)
        {
            if (preview == null)
                return false;

            return UpdateTargetRevalidator.Validate(preview, availableTarget).Status
                == UpdateTargetValidationStatus.Valid;
        }

        /// <summary>
        /// Rewalidacja przed wykonaniem operacji: pobiera świeży katalog z API
        /// i porównuje go z zatwierdzonym podsumowaniem.
        /// Gdy katalog jest niedostępny (offline), wynik wymaga ponownego potwierdzenia —
        /// nie wolno instalować innej wersji po cichu.
        /// </summary>
        internal static Task<UpdateTargetValidationResult> RevalidateApprovedUpdateTargetAsync(UpdatePreview preview)
        {
            if (preview == null)
            {
                return Task.FromResult(new UpdateTargetValidationResult
                {
                    Status = UpdateTargetValidationStatus.TargetMissing
                });
            }

            return RevalidateApprovedUpdateTargetAsync(preview.TargetVersion, preview.TargetAmongVersion);
        }

        /// <summary>
        /// Rewalidacja przed wykonaniem operacji na podstawie wersji zatwierdzonej
        /// w dialogu (działa też, gdy podsumowanie nie powstało).
        /// </summary>
        internal static Task<UpdateTargetValidationResult> RevalidateApprovedUpdateTargetAsync(
            string approvedVersion,
            string? approvedAmongVersion)
        {
            var configService = new ConfigService();
            return UpdateTargetRevalidator.RevalidateAsync(
                approvedVersion,
                approvedAmongVersion,
                () => configService.LoadConfigFromApiAsync());
        }

        /// <summary>
        /// Rewalidacja z uwzględnieniem tożsamości moda — konieczna, gdy kilka modów
        /// ma tę samą wersję (rozpoznanie po wersji byłoby niejednoznaczne).
        /// </summary>
        internal static Task<UpdateTargetValidationResult> RevalidateApprovedUpdateTargetAsync(
            ModUpdateInfo updateInfo,
            string approvedVersion,
            string? approvedAmongVersion)
        {
            int modId = updateInfo?.LocalMod?.Id ?? updateInfo?.RemoteMod?.Id ?? 0;
            string? modName = updateInfo?.ModName;

            var configService = new ConfigService();
            return UpdateTargetRevalidator.RevalidateAsync(
                () => configService.LoadConfigFromApiAsync(),
                modId,
                modName,
                approvedVersion,
                approvedAmongVersion);
        }

        /// <summary>
        /// Odświeża dane aktualizacji ze świeżego katalogu (wersja moda + wersja gry).
        /// Zwraca null, gdy katalog jest niedostępny albo moda nie ma już w katalogu.
        /// </summary>
        internal static async Task<ModUpdateInfo?> RefreshUpdateInfoFromCatalogAsync(ModUpdateInfo updateInfo)
        {
            if (updateInfo == null)
                return null;

            var catalog = await LoadRemoteCatalogAsync();
            if (catalog == null)
                return null;

            return RefreshUpdateInfoFromCatalog(updateInfo, catalog);
        }

        /// <summary>
        /// Odświeża dane aktualizacji z już pobranego katalogu (bez dodatkowego round-tripu).
        /// </summary>
        internal static ModUpdateInfo? RefreshUpdateInfoFromCatalog(
            ModUpdateInfo updateInfo,
            IEnumerable<ModConfiguration> catalog)
        {
            if (updateInfo == null || catalog == null)
                return null;

            var entry = FindCatalogEntry(catalog, updateInfo);
            if (entry == null)
                return null;

            return new ModUpdateInfo
            {
                ModName = updateInfo.ModName,
                Description = entry.Description ?? string.Empty,
                CurrentVersion = updateInfo.CurrentVersion,
                NewVersion = entry.ModVersion ?? string.Empty,
                IsSelected = true,
                LocalMod = updateInfo.LocalMod,
                RemoteMod = entry
            };
        }

        /// <summary>
        /// Rewalidacja zatwierdzonej pary względem już pobranego katalogu.
        /// </summary>
        internal static UpdateTargetValidationResult ValidateApprovedTargetAgainstCatalog(
            IEnumerable<ModConfiguration>? catalog,
            ModUpdateInfo updateInfo,
            string approvedVersion,
            string? approvedAmongVersion)
        {
            int modId = updateInfo?.LocalMod?.Id ?? updateInfo?.RemoteMod?.Id ?? 0;
            string? modName = updateInfo?.ModName;

            return UpdateTargetRevalidator.ValidateAgainstCatalog(
                catalog,
                modId,
                modName,
                approvedVersion,
                approvedAmongVersion);
        }

        /// <summary>
        /// Znajduje wpis moda w katalogu — najpierw po Id, potem po nazwie.
        /// </summary>
        internal static ModConfiguration? FindCatalogEntry(
            IEnumerable<ModConfiguration> catalog,
            ModUpdateInfo updateInfo)
        {
            if (catalog == null || updateInfo == null)
                return null;

            var mods = catalog as IList<ModConfiguration> ?? catalog.ToList();

            int modId = updateInfo.LocalMod?.Id ?? updateInfo.RemoteMod?.Id ?? 0;
            if (modId > 0)
            {
                var byId = mods.FirstOrDefault(m => m.Id == modId);
                if (byId != null)
                    return byId;
            }

            if (!string.IsNullOrWhiteSpace(updateInfo.ModName))
            {
                return mods.FirstOrDefault(m => string.Equals(
                    m.ModName?.Trim(),
                    updateInfo.ModName.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        /// <summary>
        /// Pobiera świeży katalog z API (null przy braku połączenia / błędzie).
        /// </summary>
        internal static async Task<List<ModConfiguration>?> LoadRemoteCatalogAsync()
        {
            try
            {
                var configService = new ConfigService();
                return await configService.LoadConfigFromApiAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdatePreview] Nie udało się pobrać katalogu: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Czy wersję można przypiąć. Wersje zastępcze („Nieznana”, „Unknown”)
        /// nie nadają się do przypięcia — bez prawidłowej wersji nie ma czego pinować.
        /// </summary>
        internal static bool IsPinnableVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return false;

            var trimmed = version.Trim();
            return !trimmed.Equals("Nieznana", StringComparison.OrdinalIgnoreCase)
                && !trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sprawdza, czy pobrana do instalacji para (wersja moda, wersja gry) odpowiada
        /// zatwierdzonej parze. Pusta wersja gry w katalogu nie pozwala zaprzeczyć
        /// zgodności, więc jej nie blokujemy.
        /// </summary>
        internal static bool IsApprovedPairStillValid(ApprovedUpdateTarget approved, ModConfiguration? target)
        {
            if (approved == null || target == null)
                return false;

            var approvedId = approved.UpdateInfo.LocalMod?.Id ?? approved.UpdateInfo.RemoteMod?.Id ?? 0;
            if (approvedId > 0 && target.Id != approvedId)
                return false;

            if (!string.Equals(target.ModVersion, approved.Version, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(approved.AmongVersion) || string.IsNullOrWhiteSpace(target.AmongVersion))
                return true;

            return string.Equals(target.AmongVersion, approved.AmongVersion, StringComparison.OrdinalIgnoreCase);
        }

    }
}
