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
                HasRoles = config.HasRoles,
                LobbyRegionBaseUrl = config.LobbyRegionBaseUrl,
                SupportsLobbySharing = config.SupportsLobbySharing,
                VtScanStatus = config.VtScanStatus,
                VtPermalink = config.VtPermalink,
                VtLastCheckedAt = config.VtLastCheckedAt,
                VtStats = config.VtStats,
                VtAiReviewStatus = config.VtAiReviewStatus,
                VtAiReviewSummary = config.VtAiReviewSummary
            };
        }

        /// <summary>
        /// Aktualizuje istniejący ModItem danymi z konfiguracji (bez resetu stanu instalacji / bulk).
        /// </summary>
        public static void ApplyConfigToModItem(ModItem item, ModConfiguration config)
        {
            item.Id = config.Id;
            item.Name = config.ModName;
            item.Description = config.Description;
            item.PngFileName = config.PngFileName;
            item.ModVersion = config.ModVersion;
            item.AmongVersion = config.AmongVersion;
            item.InstallPath = config.InstallPath;
            item.GitHubRepoOrLink = config.GitHubRepoOrLink;
            item.EpicGitHubRepoOrLink = config.EpicGitHubRepoOrLink;
            item.ModType = config.ModType;
            item.DllInstallPath = config.DllInstallPath ?? string.Empty;
            item.LastUpdated = config.LastUpdated;
            item.HasRoles = config.HasRoles;
            item.LobbyRegionBaseUrl = config.LobbyRegionBaseUrl;
            item.SupportsLobbySharing = config.SupportsLobbySharing;
            item.VtScanStatus = config.VtScanStatus;
            item.VtPermalink = config.VtPermalink;
            item.VtLastCheckedAt = config.VtLastCheckedAt;
            item.VtStats = config.VtStats;
            item.VtAiReviewStatus = config.VtAiReviewStatus;
            item.VtAiReviewSummary = config.VtAiReviewSummary;
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
                HasRoles = item.HasRoles,
                LobbyRegionBaseUrl = item.LobbyRegionBaseUrl,
                SupportsLobbySharing = item.SupportsLobbySharing
            };
        }
    }
}
