using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services;

/// <summary>
/// Źródło danych o kompatybilności dodatku DLL z wersją docelową moda FULL.
/// Mały interfejs — pozwala użyć prawdziwego <see cref="CompatibilityService"/>
/// w aplikacji i podmienionego źródła w testach, bez sieci w testach.
/// </summary>
public interface IUpdateCompatibilityLookup
{
    /// <summary>
    /// Zwraca informację o kompatybilności pary (FULL docelowa × DLL zainstalowana)
    /// albo null, gdy brak danych.
    /// </summary>
    Task<CompatibilityInfo?> GetAsync(
        int fullModId,
        string fullModVersion,
        int dllModId,
        string dllModVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// Buduje podsumowanie (prognozę) aktualizacji moda FULL.
///
/// Wyłącznie odczyt: nie zapisuje konfiguracji, nie tworzy katalogów,
/// nie uruchamia instalacji i nie modyfikuje plików moda.
/// Źródła danych są te same, z których korzysta odtwarzanie dodatków
/// (mapa instalacji + katalog modów + macierz kompatybilności),
/// dzięki czemu prognoza odpowiada temu, co faktycznie się wykona.
/// </summary>
public sealed class UpdatePreviewService
{
    private const string PluginsFolderName = "plugins";
    private const string BepInExFolderName = "BepInEx";

    /// <summary>Limit czasu na pojedynczy odczyt zgodności (dodatek × wersja docelowa).</summary>
    private static readonly TimeSpan CompatibilityLookupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Maksymalna liczba jednocześnie budowanych podsumowań (limit odczytów z API).</summary>
    private const int MaxConcurrentPreviews = 4;

    private readonly Func<List<ModConfiguration>> _loadCatalog;
    private readonly IUpdateCompatibilityLookup? _compatibilityLookup;
    private readonly IDiagnosticsOutput _log;

    public UpdatePreviewService(
        Func<List<ModConfiguration>> loadCatalog,
        IUpdateCompatibilityLookup? compatibilityLookup = null,
        IDiagnosticsOutput? log = null)
    {
        _loadCatalog = loadCatalog ?? throw new ArgumentNullException(nameof(loadCatalog));
        _compatibilityLookup = compatibilityLookup;
        _log = log ?? new NullDiagnosticsOutput();
    }

    /// <summary>
    /// Wariant aplikacyjny: katalog z repozytorium (SQLite/JSON) i prawdziwy serwis kompatybilności.
    /// Gdy serwis kompatybilności nie jest dostępny (brak klienta API), prognoza nadal
    /// powstaje — zgodność dodatków będzie wtedy jawnie „nieznana”.
    /// </summary>
    public static UpdatePreviewService CreateDefault()
    {
        var configService = new ConfigService();

        IUpdateCompatibilityLookup? lookup = null;
        try
        {
            lookup = new CompatibilityServiceUpdateLookup(
                new CompatibilityService(new NullDiagnosticsOutput()));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UpdatePreview] Serwis kompatybilności niedostępny: {ex.Message}");
        }

        return new UpdatePreviewService(configService.LoadConfig, lookup);
    }

    /// <summary>
    /// Buduje podsumowanie dla pojedynczej aktualizacji.
    /// </summary>
    public Task<UpdatePreview> BuildPreviewAsync(
        ModUpdateInfo updateInfo,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);

        var localMod = updateInfo.LocalMod ?? new ModConfiguration
        {
            Id = 0,
            ModName = updateInfo.ModName,
            ModVersion = updateInfo.CurrentVersion
        };

        var targetMod = updateInfo.RemoteMod ?? new ModConfiguration
        {
            Id = localMod.Id,
            ModName = updateInfo.ModName,
            ModVersion = updateInfo.NewVersion
        };

        return BuildPreviewAsync(localMod, targetMod, platform, cancellationToken);
    }

    /// <summary>
    /// Buduje podsumowania dla listy aktualizacji (np. jedno podsumowanie dla operacji zbiorczej).
    ///
    /// Podsumowania powstają równolegle, ale z ograniczoną liczbą jednoczesnych odczytów
    /// zgodności — paczka kilkunastu modów nie może zasypywać API ani blokować okna.
    /// </summary>
    public async Task<IReadOnlyList<UpdatePreview>> BuildPreviewsAsync(
        IEnumerable<ModUpdateInfo> updates,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var items = updates.ToList();
        var previews = new UpdatePreview[items.Count];

        using var gate = new SemaphoreSlim(Math.Max(1, Math.Min(MaxConcurrentPreviews, items.Count)));

        var tasks = new List<Task>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            var item = items[index];

            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    previews[index] = await BuildPreviewAsync(item, platform, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Write($"[UpdatePreview] Nie udało się przygotować podsumowania: {ex.Message}");
                    previews[index] = BuildUnavailablePreview(item, platform);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return previews;
    }

    /// <summary>
    /// Podsumowanie zastępcze, gdy prognoza nie mogła zostać przygotowana.
    /// Świadomie bez obietnic: brak danych, a nie „zgodność”.
    /// </summary>
    private static UpdatePreview BuildUnavailablePreview(ModUpdateInfo updateInfo, string? platform)
    {
        return new UpdatePreview
        {
            ModId = updateInfo.LocalMod?.Id ?? updateInfo.RemoteMod?.Id ?? 0,
            ModName = updateInfo.ModName,
            Platform = NormalizePlatform(platform),
            CurrentVersion = updateInfo.CurrentVersion,
            TargetVersion = updateInfo.NewVersion,
            TargetAmongVersion = updateInfo.RemoteMod?.AmongVersion ?? string.Empty,
            IsInstalled = false,
            HasInstallationMap = false,
            ConfigStatus = UpdateConfigStatus.Unknown,
            Addons = Array.Empty<UpdateAddonPreview>(),
            TargetKey = BuildTargetKey(
                updateInfo.LocalMod?.Id ?? updateInfo.RemoteMod?.Id ?? 0,
                NormalizePlatform(platform),
                updateInfo.NewVersion,
                updateInfo.RemoteMod?.AmongVersion ?? string.Empty)
        };
    }

    /// <summary>
    /// Buduje podsumowanie na podstawie moda zainstalowanego i wersji docelowej.
    /// </summary>
    public async Task<UpdatePreview> BuildPreviewAsync(
        ModConfiguration localMod,
        ModConfiguration targetMod,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localMod);
        ArgumentNullException.ThrowIfNull(targetMod);

        string effectivePlatform = NormalizePlatform(platform);
        string targetVersion = targetMod.ModVersion ?? string.Empty;
        string targetAmongVersion = targetMod.AmongVersion ?? string.Empty;
        string installPath = localMod.InstallPath ?? string.Empty;
        string targetKey = BuildTargetKey(localMod.Id, effectivePlatform, targetVersion, targetAmongVersion);

        bool isInstalled = !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath);

        if (!isInstalled)
        {
            return new UpdatePreview
            {
                ModId = localMod.Id,
                ModName = localMod.ModName,
                Platform = effectivePlatform,
                CurrentVersion = localMod.ModVersion ?? string.Empty,
                TargetVersion = targetVersion,
                TargetAmongVersion = targetAmongVersion,
                IsInstalled = false,
                HasInstallationMap = false,
                ConfigStatus = UpdateConfigStatus.Unknown,
                Addons = Array.Empty<UpdateAddonPreview>(),
                TargetKey = targetKey
            };
        }

        InstallationMap? map = null;
        try
        {
            map = await InstallationMapManager.LoadInstallationMapAsync(installPath).ConfigureAwait(false);

            // Epic: katalog gry leży w podkatalogu „AmongUs” — mapa instalacji bywa w katalogu nadrzędnym.
            if (map == null)
            {
                string contentRoot = PathSettings.GetActualModPath(installPath);
                if (!string.Equals(contentRoot, installPath, StringComparison.OrdinalIgnoreCase))
                {
                    map = await InstallationMapManager.LoadInstallationMapAsync(contentRoot).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Write($"[UpdatePreview] Nie udało się odczytać mapy instalacji: {ex.Message}");
        }

        // Match the replay service: a copied/foreign map cannot promise addon restoration.
        if (map?.FullMod is { } owner && (localMod.Id > 0 && owner.ModId > 0
                ? localMod.Id != owner.ModId
                : !string.Equals(localMod.ModName, owner.ModName, StringComparison.OrdinalIgnoreCase)))
            map = null;

        string? currentVersion = map?.FullMod?.ModVersion;
        if (string.IsNullOrWhiteSpace(currentVersion))
            currentVersion = localMod.ModVersion ?? string.Empty;

        var addons = new List<UpdateAddonPreview>();

        var trackedDlls = map?.InstalledDlls is { Count: > 0 } ? map.InstalledDlls : new List<DllModInstallation>();

        if (trackedDlls.Count > 0)
        {
            Dictionary<int, ModConfiguration> dllCatalog = LoadDllCatalog();

            // Odczyt zgodności idzie równolegle — podsumowanie nie może blokować okna na wiele rund API.
            var compatibilityResults = await Task.WhenAll(trackedDlls.Select(installedDll =>
                    LookupCompatibilitySafeAsync(
                        targetMod.Id,
                        targetVersion,
                        installedDll.ModId,
                        dllCatalog.TryGetValue(installedDll.ModId, out var catalogDll)
                            ? catalogDll.ModVersion : installedDll.ModVersion,
                        cancellationToken)))
                .ConfigureAwait(false);

            for (int i = 0; i < trackedDlls.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                addons.Add(BuildTrackedAddon(trackedDlls[i], dllCatalog, compatibilityResults[i]));
            }
        }

        foreach (var customFile in FindUntrackedDllFiles(installPath, map))
        {
            addons.Add(new UpdateAddonPreview
            {
                ModId = 0,
                Name = customFile,
                Status = UpdateAddonStatus.NotRestoredCustomFile,
                IsCustomFile = true
            });
        }

        return new UpdatePreview
        {
            ModId = localMod.Id,
            ModName = localMod.ModName,
            Platform = effectivePlatform,
            CurrentVersion = currentVersion,
            TargetVersion = targetVersion,
            TargetAmongVersion = targetAmongVersion,
            IsInstalled = true,
            HasInstallationMap = map != null,
            // Aktualizacja zastępuje katalog moda (Steam: katalog moda, Epic: katalog gry w podkatalogu).
            // Aplikacja odtwarza wyłącznie zarejestrowane dodatki DLL — plików konfiguracji nie przenosi.
            ConfigStatus = UpdateConfigStatus.NotGuaranteed,
            Addons = addons,
            TargetKey = targetKey
        };
    }

    private Dictionary<int, ModConfiguration> LoadDllCatalog()
    {
        var catalog = new Dictionary<int, ModConfiguration>();

        List<ModConfiguration> mods;
        try
        {
            mods = _loadCatalog() ?? new List<ModConfiguration>();
        }
        catch (Exception ex)
        {
            _log.Write($"[UpdatePreview] Nie udało się wczytać katalogu modów: {ex.Message}");
            return catalog;
        }

        foreach (var mod in mods.Where(m => string.Equals(m.ModType, "dll", StringComparison.OrdinalIgnoreCase)))
        {
            catalog[mod.Id] = mod;
        }

        return catalog;
    }

    private static UpdateAddonPreview BuildTrackedAddon(
        DllModInstallation installedDll,
        Dictionary<int, ModConfiguration> dllCatalog,
        CompatibilityInfo? compatibility)
    {
        if (!dllCatalog.TryGetValue(installedDll.ModId, out var catalogDll))
        {
            // Brak wpisu katalogowego — odtwarzanie pominie ten dodatek (SkippedMissingCatalog).
            return new UpdateAddonPreview
            {
                ModId = installedDll.ModId,
                Name = installedDll.ModName,
                InstalledVersion = installedDll.ModVersion,
                Status = UpdateAddonStatus.NotRestoredMissingCatalog
            };
        }

        UpdateAddonStatus status;
        if (compatibility == null)
        {
            status = UpdateAddonStatus.RetainedCompatibilityUnknown;
        }
        else
        {
            status = compatibility.Status switch
            {
                CompatibilityStatus.Favorite => UpdateAddonStatus.Retained,
                CompatibilityStatus.Works => UpdateAddonStatus.Retained,
                CompatibilityStatus.NotWork => UpdateAddonStatus.RetainedIncompatible,
                _ => UpdateAddonStatus.RetainedCompatibilityUnknown
            };

            if (!compatibility.IsCurrentVersion)
            {
                // Dane dotyczą innej pary wersji — nie są potwierdzeniem zgodności z celem.
                status = UpdateAddonStatus.RetainedCompatibilityUnknown;
            }
        }

        return new UpdateAddonPreview
        {
            ModId = installedDll.ModId,
            Name = installedDll.ModName,
            InstalledVersion = installedDll.ModVersion,
            Status = status,
            CompatibilityStatusCode = compatibility?.StatusCode,
            IsExactCompatibilityMatch = compatibility?.IsCurrentVersion ?? false,
            RestoredVersion = catalogDll.ModVersion
        };
    }

    /// <summary>
    /// Odczyt zgodności z limitem czasu. Brak danych (timeout, offline, błąd) to „nieznane”,
    /// a nie zgodność i nie błąd krytyczny podsumowania.
    /// </summary>
    private async Task<CompatibilityInfo?> LookupCompatibilitySafeAsync(
        int fullModId,
        string fullModVersion,
        int dllModId,
        string dllModVersion,
        CancellationToken cancellationToken)
    {
        if (_compatibilityLookup == null)
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CompatibilityLookupTimeout);

        try
        {
            return await _compatibilityLookup
                .GetAsync(fullModId, fullModVersion, dllModId, dllModVersion, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log.Write("[UpdatePreview] Przekroczono limit odczytu zgodności — status pozostaje nieznany.");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Write($"[UpdatePreview] Błąd odczytu zgodności: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Znajduje pliki DLL w katalogu dodatków moda, których nie ma w mapie instalacji.
    /// Takich plików aktualizacja nie odtwarza — katalog moda jest zastępowany.
    /// </summary>
    private static IReadOnlyList<string> FindUntrackedDllFiles(string installPath, InstallationMap? map)
    {
        var results = new List<string>();

        try
        {
            string contentRoot = PathSettings.GetActualModPath(installPath);
            string pluginsPath = Path.Combine(contentRoot, BepInExFolderName, PluginsFolderName);

            if (!Directory.Exists(pluginsPath))
                return results;

            var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trackedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var installedDll in map?.InstalledDlls ?? new List<DllModInstallation>())
            {
                if (string.IsNullOrWhiteSpace(installedDll.InstallPath))
                    continue;

                string relative = installedDll.InstallPath.Replace('/', Path.DirectorySeparatorChar);
                trackedPaths.Add(Path.GetFullPath(Path.Combine(installPath, relative)));
                trackedPaths.Add(Path.GetFullPath(Path.Combine(contentRoot, relative)));

                string fileName = Path.GetFileName(relative);
                if (!string.IsNullOrWhiteSpace(fileName))
                    trackedNames.Add(fileName);
            }

            foreach (var file in Directory.EnumerateFiles(pluginsPath, "*.dll", SearchOption.AllDirectories))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(file);
                }
                catch (Exception)
                {
                    continue;
                }

                if (trackedPaths.Contains(fullPath))
                    continue;

                string fileName = Path.GetFileName(fullPath);
                if (trackedNames.Contains(fileName))
                    continue;

                results.Add(fileName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UpdatePreview] Skanowanie plików DLL nie powiodło się: {ex.Message}");
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static string NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return "steam";

        return platform.Trim().ToLowerInvariant() switch
        {
            "epic" => "epic",
            "steam" => "steam",
            _ => platform.Trim().ToLowerInvariant()
        };
    }

    internal static string BuildTargetKey(int modId, string platform, string targetVersion, string targetAmongVersion)
        => $"mod:{modId}|platform:{platform}|version:{targetVersion}|among:{targetAmongVersion}";

    private sealed class NullDiagnosticsOutput : IDiagnosticsOutput
    {
        public void Write(string message)
        {
        }
    }
}

/// <summary>
/// Adapter <see cref="CompatibilityService"/> do interfejsu
/// <see cref="IUpdateCompatibilityLookup"/>.
/// </summary>
internal sealed class CompatibilityServiceUpdateLookup : IUpdateCompatibilityLookup
{
    private readonly CompatibilityService _service;

    public CompatibilityServiceUpdateLookup(CompatibilityService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    public Task<CompatibilityInfo?> GetAsync(
        int fullModId,
        string fullModVersion,
        int dllModId,
        string dllModVersion,
        CancellationToken cancellationToken) =>
        _service.CheckCompatibilityAsync(
            dllModId,
            dllModVersion,
            fullModId,
            fullModVersion,
            cancellationToken);
}
