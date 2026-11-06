using System;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels.Helpers;

public static class UpdateAddonPresentation
{
    public static string NameWithVersions(UpdateAddonPreview addon) =>
        !string.IsNullOrWhiteSpace(addon.RestoredVersion) &&
        !string.Equals(addon.InstalledVersion, addon.RestoredVersion, StringComparison.OrdinalIgnoreCase)
            ? $"{addon.Name} ({addon.InstalledVersion} → {addon.RestoredVersion})"
            : addon.Name;
}
