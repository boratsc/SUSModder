using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services;

public static class StableUpdateTarget
{
    public static ModConfiguration? Resolve(ModConfiguration? mod)
    {
        if (mod == null || !mod.IsBeta)
            return mod;

        var version = mod.LatestStableVersion;
        if (mod.Id <= 0 || string.IsNullOrWhiteSpace(version) || version.Length > 50 ||
            version != version.Trim() || version.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
            version.IndexOfAny(['/', '\\', '?', '#']) >= 0 || version.Any(char.IsControl))
            return null;

        var matches = mod.AvailableReleases.Where(r =>
            string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1 || matches[0].IsBeta)
            return null;

        var target = new ModConfiguration
        {
            Id = mod.Id, ModName = mod.ModName, ModType = mod.ModType,
            ModVersion = version, AmongVersion = matches[0].AmongVersion,
            Description = mod.Description, InstallPath = mod.InstallPath,
            PngFileName = mod.PngFileName, DllInstallPath = mod.DllInstallPath,
            LastUpdated = mod.LastUpdated, HasRoles = mod.HasRoles,
            LobbyRegionBaseUrl = mod.LobbyRegionBaseUrl, SupportsLobbySharing = mod.SupportsLobbySharing,
            LatestStableVersion = mod.LatestStableVersion, LatestBetaVersion = mod.LatestBetaVersion,
            AvailableReleases = mod.AvailableReleases
        };
        target.GitHubRepoOrLink = ModDownloadUrlBuilder.Build(target, "steam");
        target.EpicGitHubRepoOrLink = ModDownloadUrlBuilder.Build(target, "epic");
        return ValidUrl(target.GitHubRepoOrLink, target.Id, version) &&
               ValidUrl(target.EpicGitHubRepoOrLink, target.Id, version) ? target : null;
    }

    private static bool ValidUrl(string? url, int id, string version)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return false;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 4 && segments[^3] == "mod" && segments[^2] == id.ToString() &&
               string.Equals(Uri.UnescapeDataString(segments[^1]), version, StringComparison.Ordinal);
    }
}
