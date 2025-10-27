using SUSModder.ViewModels;

namespace SUSModder.Core.Configuration
{
    public static class ModItemAdapter
    {
        public static ModItem FromConfig(ModConfiguration config)
        {
            return new ModItem
            {
                Id = config.Id,
                Name = config.ModName, // API zwraca ModName
                Description = config.Description,
                PngFileName = config.PngFileName,
                ModVersion = config.ModVersion,
                AmongVersion = config.AmongVersion,
                InstallPath = config.InstallPath,
                GitHubRepoOrLink = config.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = config.EpicGitHubRepoOrLink,
                ModType = config.ModType,
                DllInstallPath = config.DllInstallPath ?? string.Empty,
                LastUpdated = config.LastUpdated,
                HasRoles = config.HasRoles
            };
        }

        public static ModConfiguration ToConfig(ModItem item)
        {
            return new ModConfiguration
            {
                Id = item.Id,
                ModName = item.Name, // Mapuj z powrotem na ModName
                Description = item.Description,
                PngFileName = item.PngFileName,
                ModVersion = item.ModVersion,
                AmongVersion = item.AmongVersion,
                InstallPath = item.InstallPath,
                GitHubRepoOrLink = item.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = item.EpicGitHubRepoOrLink,
                ModType = item.ModType,
                DllInstallPath = item.DllInstallPath,
                LastUpdated = item.LastUpdated,
                HasRoles = item.HasRoles
            };
        }
    }
}
