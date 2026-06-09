using SUSModder.Core.Api.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Models;

namespace SUSModder.Core.Api;

public static class CatalogMapper
{
    public static ModConfiguration ToModConfiguration(CatalogItemDto item, ISUSModderApiClient apiClient)
    {
        var version = item.CurrentVersion ?? string.Empty;
        return new ModConfiguration
        {
            Id = item.Id,
            ModName = item.Name,
            ModType = item.Type,
            ModVersion = version,
            Description = item.Description ?? string.Empty,
            InstallPath = item.InstallPath,
            DllInstallPath = item.DllInstallPath,
            PngFileName = CdnAssetUrlResolver.Resolve(item.IconUrl, apiClient.BaseUrl, apiClient.StaticAssetsBaseUrl),
            GitHubRepoOrLink = apiClient.BuildModDownloadUrl(item.Id, version, "steam"),
            EpicGitHubRepoOrLink = apiClient.BuildModDownloadUrl(item.Id, version, "epic"),
            AmongVersion = item.AmongVersion?.DbValue ?? string.Empty,
            LastUpdated = item.LastUpdated,
            HasRoles = item.HasRoles,
            LobbyRegionBaseUrl = item.LobbyRegionBaseUrl,
            SupportsLobbySharing = item.SupportsLobbySharing
        };
    }

    public static ModVersionHistory ToModVersionHistory(CatalogVersionEntryDto entry, int modId)
    {
        return new ModVersionHistory
        {
            VersionId = entry.VersionId,
            ModId = modId,
            ModVersion = entry.Version,
            AmongVersion = entry.AmongVersion,
            CreatedAt = entry.CreatedAt,
            CreatedBy = entry.CreatedBy,
            Notes = entry.Notes
        };
    }

}
