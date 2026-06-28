namespace SUSModder.Core.Diagnostics.Launch;

/// <summary>
/// Stabilne kody diagnozy launch, używane przez LaunchSupervisor i wysyłane do API support.
/// UI mapuje kody na zlokalizowane komunikaty. Backend koreluje z KB articles.
/// </summary>
public static class DiagnosisCode
{
    // ── Proces ──────────────────────────────────────────────
    public const string ProcessStartFailed = "launch.process.start_failed";
    public const string ProcessExitedEarly = "launch.process.exited_early";

    // ── BepInEx ─────────────────────────────────────────────
    public const string BepInExLogMissing = "launch.bepinex.log_missing";
    public const string BepInExLogStale = "launch.bepinex.log_stale";
    public const string BepInExPluginLoadFailed = "launch.bepinex.plugin_load_failed";
    public const string BepInExAccessDenied = "launch.bepinex.access_denied";

    // ── Defender / AV ───────────────────────────────────────
    public const string DefenderThreatDetected = "launch.defender.threat_detected";
    public const string DefenderCfaBlocked = "launch.defender.cfa_blocked";
    public const string DefenderEventsUnavailable = "launch.defender.events_unavailable";

    // ── Firewall ────────────────────────────────────────────
    public const string FirewallRuleMissingOrBlocked = "launch.firewall.rule_missing_or_blocked";

    // ── Mod ─────────────────────────────────────────────────
    public const string ModVersionMismatch = "launch.mod.version_mismatch";

    // ── Fallback ────────────────────────────────────────────
    public const string Unknown = "launch.unknown";

    /// <summary>
    /// Wszystkie zdefiniowane kody – do celów walidacji.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ProcessStartFailed,
        ProcessExitedEarly,
        BepInExLogMissing,
        BepInExLogStale,
        BepInExPluginLoadFailed,
        BepInExAccessDenied,
        DefenderThreatDetected,
        DefenderCfaBlocked,
        DefenderEventsUnavailable,
        FirewallRuleMissingOrBlocked,
        ModVersionMismatch,
        Unknown
    };
}
