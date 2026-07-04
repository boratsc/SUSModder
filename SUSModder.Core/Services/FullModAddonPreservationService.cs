using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services;

public sealed class FullModAddonPreservationService
{
    private readonly Func<List<ModConfiguration>> _loadCatalog;
    private readonly Func<ModConfiguration, ModConfiguration, string, Task<string?>> _installDllToModAsync;
    private readonly IDiagnosticsOutput _log;

    public FullModAddonPreservationService(
        ConfigService configService,
        DllModificationService dllModificationService,
        IDiagnosticsOutput log)
        : this(configService.LoadConfig, dllModificationService.InstallDllToModAsync, log)
    {
    }

    internal FullModAddonPreservationService(
        Func<List<ModConfiguration>> loadCatalog,
        Func<ModConfiguration, ModConfiguration, string, Task<string?>> installDllToModAsync,
        IDiagnosticsOutput log)
    {
        _loadCatalog = loadCatalog ?? throw new ArgumentNullException(nameof(loadCatalog));
        _installDllToModAsync = installDllToModAsync ?? throw new ArgumentNullException(nameof(installDllToModAsync));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<FullModAddonSnapshot> CaptureFromInstallationMapAsync(ModConfiguration fullMod)
    {
        ArgumentNullException.ThrowIfNull(fullMod);

        var map = await InstallationMapManager.LoadInstallationMapAsync(fullMod.InstallPath);
        if (map == null)
        {
            _log.Write($"[DllRestore] Brak mapy instalacji dla {fullMod.ModName}; snapshot pusty");
            return FullModAddonSnapshot.Empty(fullMod.Id, fullMod.ModName, fullMod.InstallPath);
        }

        if (!MatchesFullMod(fullMod, map.FullMod))
        {
            _log.Write($"[DllRestore] Mapa instalacji nie pasuje do aktualizowanego moda {fullMod.ModName}; snapshot pusty");
            return FullModAddonSnapshot.Empty(fullMod.Id, fullMod.ModName, fullMod.InstallPath);
        }

        return new FullModAddonSnapshot
        {
            FullModId = map.FullMod.ModId,
            FullModName = map.FullMod.ModName,
            InstallPath = map.FullMod.InstallPath,
            FullModAutoUpdateEnabled = map.FullMod.AutoUpdateEnabled,
            FullModDisableAutoUpdatePrompt = map.FullMod.DisableAutoUpdatePrompt,
            FullModPinnedInstallVersion = map.FullMod.PinnedInstallVersion,
            FullModDontShowPostInstallDialog = map.FullMod.DontShowPostInstallDialog,
            DllAddons = map.InstalledDlls
                .Select(dll => new PreservedDllAddon
                {
                    ModId = dll.ModId,
                    ModName = dll.ModName,
                    ModVersion = dll.ModVersion,
                    InstallPath = dll.InstallPath,
                    InstalledFrom = dll.InstalledFrom,
                    AutoUpdateEnabled = dll.AutoUpdateEnabled
                })
                .ToList()
        };
    }

    public async Task<FullModAddonRestoreResult> RestoreToFullModAsync(
        ModConfiguration updatedFullMod,
        FullModAddonSnapshot snapshot,
        string platform)
    {
        ArgumentNullException.ThrowIfNull(updatedFullMod);
        ArgumentNullException.ThrowIfNull(snapshot);

        await ApplyFullModFlagsAsync(updatedFullMod.InstallPath, snapshot);

        if (snapshot.IsEmpty)
            return new FullModAddonRestoreResult();

        var catalogDlls = _loadCatalog()
            .Where(m => string.Equals(m.ModType, "dll", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(m => m.Id, m => m);
        var results = new List<DllAddonRestoreItemResult>();

        foreach (var preservedDll in snapshot.DllAddons)
        {
            if (!catalogDlls.TryGetValue(preservedDll.ModId, out var dllConfig))
            {
                results.Add(CreateResult(preservedDll, DllRestoreStatus.SkippedMissingCatalog, "missingCatalog"));
                _log.Write($"[DllRestore] Pominięto {preservedDll.ModName}: brak w katalogu");
                continue;
            }

            try
            {
                var installedPath = await _installDllToModAsync(dllConfig, updatedFullMod, platform);
                if (string.IsNullOrEmpty(installedPath))
                {
                    results.Add(CreateResult(preservedDll, DllRestoreStatus.Failed, "restoreFailed"));
                    _log.Write($"[DllRestore] Nie udało się przywrócić {preservedDll.ModName}");
                    continue;
                }

                await ApplyDllFlagsAsync(updatedFullMod.InstallPath, preservedDll);
                results.Add(CreateResult(preservedDll, DllRestoreStatus.Restored));
                _log.Write($"[DllRestore] Przywrócono {preservedDll.ModName}");
            }
            catch (Exception ex)
            {
                results.Add(CreateResult(preservedDll, DllRestoreStatus.Failed, "restoreFailed"));
                _log.Write($"[DllRestore] Błąd przywracania {preservedDll.ModName}: {ex.Message}");
            }
        }

        return new FullModAddonRestoreResult { Items = results };
    }

    public async Task ApplyFullModFlagsAsync(string? updatedInstallPath, FullModAddonSnapshot snapshot)
    {
        var map = await InstallationMapManager.LoadInstallationMapAsync(updatedInstallPath);
        if (map == null)
            return;

        map.FullMod.AutoUpdateEnabled = snapshot.FullModAutoUpdateEnabled;
        map.FullMod.DontShowPostInstallDialog = snapshot.FullModDontShowPostInstallDialog;

        // DisableAutoUpdatePrompt/PinnedInstallVersion are intentionally not restored here.
        // They describe a user's decision to stay on an older version; after a successful
        // update to a newer full mod version, keeping that pin could suppress future update
        // prompts incorrectly. Auto-update and post-install dialog preferences are stable
        // user preferences, so they survive the reinstall.
        await InstallationMapManager.SaveInstallationMapAsync(updatedInstallPath!, map);
    }

    private async Task ApplyDllFlagsAsync(string? updatedInstallPath, PreservedDllAddon preservedDll)
    {
        var map = await InstallationMapManager.LoadInstallationMapAsync(updatedInstallPath);
        if (map == null)
            return;

        var restoredDll = map.InstalledDlls.FirstOrDefault(d => d.ModId == preservedDll.ModId);
        if (restoredDll == null)
            return;

        restoredDll.AutoUpdateEnabled = preservedDll.AutoUpdateEnabled;
        await InstallationMapManager.SaveInstallationMapAsync(updatedInstallPath!, map);
    }

    private static bool MatchesFullMod(ModConfiguration fullMod, FullModInstallation installedFullMod)
    {
        if (fullMod.Id > 0 && installedFullMod.ModId > 0)
            return fullMod.Id == installedFullMod.ModId;

        return string.Equals(fullMod.ModName, installedFullMod.ModName, StringComparison.OrdinalIgnoreCase);
    }

    private static DllAddonRestoreItemResult CreateResult(
        PreservedDllAddon dll,
        DllRestoreStatus status,
        string? message = null) => new()
        {
            ModId = dll.ModId,
            ModName = dll.ModName,
            Status = status,
            Message = message
        };
}
