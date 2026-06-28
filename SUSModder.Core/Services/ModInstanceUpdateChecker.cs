using System;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Porównanie wersji lokalnej instancji z katalogiem API (badge UPDATE na „Moje zestawy”).
    /// </summary>
    public static class ModInstanceUpdateChecker
    {
        public static bool HasCatalogUpdate(ModInstance instance, ModConfiguration? catalogMod)
        {
            if (catalogMod == null || string.IsNullOrWhiteSpace(catalogMod.ModVersion))
                return false;

            if (!instance.AutoUpdateEnabled &&
                !string.IsNullOrWhiteSpace(instance.PinnedVersion) &&
                string.Equals(instance.FullModVersion, instance.PinnedVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return HasNewerCatalogVersion(instance.FullModVersion, catalogMod.ModVersion);
        }

        internal static bool HasNewerCatalogVersion(string? installedVersion, string? catalogVersion)
        {
            if (!string.IsNullOrEmpty(installedVersion) && !string.IsNullOrEmpty(catalogVersion))
            {
                return !string.Equals(installedVersion, catalogVersion, StringComparison.OrdinalIgnoreCase);
            }

            return string.IsNullOrEmpty(installedVersion) && !string.IsNullOrEmpty(catalogVersion);
        }
    }
}
