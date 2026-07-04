using System;
using System.Collections.Generic;

namespace SUSModder.Core.Models;

public sealed class FullModAddonSnapshot
{
    public int FullModId { get; init; }
    public string FullModName { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public bool FullModAutoUpdateEnabled { get; init; }
    public bool FullModDisableAutoUpdatePrompt { get; init; }
    public string? FullModPinnedInstallVersion { get; init; }
    public bool FullModDontShowPostInstallDialog { get; init; }
    public IReadOnlyList<PreservedDllAddon> DllAddons { get; init; } = Array.Empty<PreservedDllAddon>();

    public bool IsEmpty => DllAddons.Count == 0;

    public static FullModAddonSnapshot Empty(int fullModId, string fullModName, string? installPath = null) => new()
    {
        FullModId = fullModId,
        FullModName = fullModName,
        InstallPath = installPath ?? string.Empty
    };
}

public sealed class PreservedDllAddon
{
    public int ModId { get; init; }
    public string ModName { get; init; } = string.Empty;
    public string ModVersion { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string InstalledFrom { get; init; } = string.Empty;
    public bool AutoUpdateEnabled { get; init; }
}

public sealed class FullModAddonRestoreResult
{
    public IReadOnlyList<DllAddonRestoreItemResult> Items { get; init; } = Array.Empty<DllAddonRestoreItemResult>();

    public int RestoredCount => Count(DllRestoreStatus.Restored);
    public int SkippedCount => Count(DllRestoreStatus.SkippedMissingCatalog) + Count(DllRestoreStatus.SkippedUnsafePath);
    public int FailedCount => Count(DllRestoreStatus.Failed);
    public bool HasProblems => SkippedCount > 0 || FailedCount > 0;

    private int Count(DllRestoreStatus status)
    {
        var count = 0;
        foreach (var item in Items)
        {
            if (item.Status == status)
                count++;
        }

        return count;
    }
}

public sealed class DllAddonRestoreItemResult
{
    public int ModId { get; init; }
    public string ModName { get; init; } = string.Empty;
    public DllRestoreStatus Status { get; init; }
    public string? Message { get; init; }
}

public enum DllRestoreStatus
{
    Restored,
    SkippedMissingCatalog,
    Failed,
    SkippedUnsafePath
}
