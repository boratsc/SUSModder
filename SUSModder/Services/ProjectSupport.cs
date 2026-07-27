using System;
using System.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.Services;

/// <summary>
/// Dobrowolne wsparcie projektu (suppi.pl) — jedna stała URL i helper otwierania.
/// </summary>
public static class ProjectSupport
{
    public const string SuppiUrl = "https://suppi.pl/susmodder";

    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SuppiUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProjectSupport] Nie udało się otworzyć linku: {ex.Message}");
        }
    }

    public static bool ShouldShowBanner(string? dismissedAtUtcIso, DateTimeOffset? nowUtc = null)
        => SupportBannerPolicy.ShouldShow(dismissedAtUtcIso, nowUtc);
}
