namespace SUSModder.Core.Diagnostics.Environment;

public enum SelfCheckStatus { Ok, Info, Warning, Error, Unknown, NotApplicable }
public sealed record SelfCheckResult(string CheckId, SelfCheckStatus Status, string Code, IReadOnlyDictionary<string, string> Data, string? ArticleId = null);
public sealed record SelfCheckReport(DateTimeOffset CheckedAtUtc, IReadOnlyList<SelfCheckResult> Results);
public sealed class SelfCheckContext
{
    public required string Mode { get; init; }
    public string? VanillaInstallPath { get; init; }
    public required string ModsInstallPath { get; init; }
}
public sealed record FirewallProfilesSnapshot(int CurrentProfileTypes, bool DomainEnabled, bool PrivateEnabled, bool PublicEnabled);
public sealed record DefenderSnapshot(bool? AntivirusEnabled, bool? RealTimeProtectionEnabled, bool? IsTamperProtected, int? EnableControlledFolderAccess);
