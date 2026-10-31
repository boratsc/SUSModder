using System.Collections.Generic;
using System.Linq;
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
            SupportsLobbySharing = item.SupportsLobbySharing,
            IsBeta = item.IsBeta ?? false,
            LatestStableVersion = item.LatestStableVersion,
            LatestBetaVersion = item.LatestBetaVersion,
            AvailableReleases = ToModReleases(item.AvailableReleases)
        };
    }

    /// <summary>
    /// Mapuje addytywną listę wydań katalogu (3.1-11). Brak pola w odpowiedzi
    /// (null) oznacza tryb legacy i daje pustą listę — bez zgadywania wydań.
    /// </summary>
    public static List<ModRelease> ToModReleases(List<CatalogReleaseDto>? releases)
    {
        if (releases is null || releases.Count == 0)
            return new List<ModRelease>();

        var result = new List<ModRelease>(releases.Count);
        foreach (var release in releases)
        {
            result.Add(new ModRelease
            {
                ReleaseId = release.ReleaseId,
                Version = release.Version,
                IsBeta = release.IsBeta,
                AmongVersion = release.AmongVersion ?? string.Empty,
                IsCurrent = release.IsCurrent,
                Variants = (release.Variants ?? new List<CatalogModVariantDto>())
                    .Select(v => new ModReleaseVariant
                    {
                        Platform = v.Platform,
                        Architecture = v.Architecture,
                        Version = v.Version,
                        DownloadUrl = v.DownloadUrl,
                        Sha256 = v.Sha256,
                        FileSizeBytes = v.FileSizeBytes
                    })
                    .ToList()
            });
        }

        return result;
    }

    public static ModVersionHistory ToModVersionHistory(CatalogVersionEntryDto entry, int modId)
    {
        return new ModVersionHistory
        {
            VersionId = entry.VersionId,
            ModId = modId,
            ModVersion = entry.Version,
            AmongVersion = entry.AmongVersion,
            IsBeta = entry.IsBeta,
            CreatedAt = entry.CreatedAt,
            CreatedBy = entry.CreatedBy,
            Notes = entry.Notes
        };
    }

}
