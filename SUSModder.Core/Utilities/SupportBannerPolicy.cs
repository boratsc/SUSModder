using System;
using System.Globalization;

namespace SUSModder.Core.Utilities;

/// <summary>
/// Polityka widoczności belki dobrowolnego wsparcia (cooldown po dismiss).
/// </summary>
public static class SupportBannerPolicy
{
    public static readonly TimeSpan DismissCooldown = TimeSpan.FromDays(7);

    public static bool ShouldShow(string? dismissedAtUtcIso, DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(dismissedAtUtcIso))
            return true;

        if (!DateTimeOffset.TryParse(dismissedAtUtcIso, null, DateTimeStyles.RoundtripKind, out var dismissedAt))
            return true;

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return now - dismissedAt.ToUniversalTime() >= DismissCooldown;
    }
}
