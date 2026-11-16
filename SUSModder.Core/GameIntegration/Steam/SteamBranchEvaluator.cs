namespace SUSModder.Core.GameIntegration.Steam;

public enum SteamBranchStatus { Default, NonDefault, Inconsistent, Unknown }
public sealed record SteamBranchResult(SteamBranchStatus Status, string? BranchName);

public static class SteamBranchEvaluator
{
    public static SteamBranchResult Evaluate(SteamAppManifest? manifest)
    {
        if (manifest is null) return new(SteamBranchStatus.Unknown, null);
        var user = Normalize(manifest.UserConfigBetaKey);
        var mounted = Normalize(manifest.MountedConfigBetaKey);
        if (manifest.MountedConfigBetaKey is not null &&
            !string.Equals(user, mounted, StringComparison.OrdinalIgnoreCase))
            return new(SteamBranchStatus.Inconsistent, user);
        return user is null ? new(SteamBranchStatus.Default, null) : new(SteamBranchStatus.NonDefault, user);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("public", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
}
