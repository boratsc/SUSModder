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
            catalogMod = StableUpdateTarget.Resolve(catalogMod);
            if (catalogMod == null || catalogMod.IsBeta || string.IsNullOrWhiteSpace(catalogMod.ModVersion))
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
            if (string.IsNullOrWhiteSpace(catalogVersion))
                return false;

            if (string.IsNullOrWhiteSpace(installedVersion) ||
                string.Equals(installedVersion, "unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(installedVersion, "nieznana", StringComparison.OrdinalIgnoreCase))
                return true;

            return ConfigService.IsNewerVersion(catalogVersion, installedVersion);
        }
    }
}
