using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Services;

/// <summary>
/// Mapowanie statusu kompatybilności z API (F/W/NT/NW) na emoji i etykiety i18n — spójne z DllModSelectionView.
/// </summary>
public static class CompatibilityDisplayHelper
{
    public static bool IsVisible(CompatibilityInfo? compat) =>
        compat?.Status != CompatibilityStatus.NotWork;

    public static string GetEmoji(CompatibilityInfo? compat) =>
        compat?.Emoji ?? CompatibilityStatus.NotTested.GetEmoji();

    public static string GetStatusLabel(CompatibilityStatus status, ILocalizationService localization) =>
        localization.Get(GetStatusResourceKey(status));

    public static string GetStatusLabel(CompatibilityInfo? compat, ILocalizationService localization) =>
        compat == null
            ? localization.Get("DllModSelection.UnknownCompatibility")
            : GetStatusLabel(compat.Status, localization);

    public static string? GetWarning(CompatibilityInfo? compat, ILocalizationService localization)
    {
        if (compat is not { } info || !CompatibilityService.ShouldShowWarning(info))
            return null;

        return string.IsNullOrWhiteSpace(info.Warning)
            ? localization.Get("DllModSelection.CompatibilityWarning")
            : info.Warning;
    }

    public static string GetStatusResourceKey(CompatibilityStatus status) =>
        status switch
        {
            CompatibilityStatus.Favorite => "DllManager.CompatFavorite",
            CompatibilityStatus.Works => "DllManager.CompatWorks",
            _ => "DllManager.CompatNotTested"
        };

    public static int GetSortPriority(CompatibilityStatus status) =>
        status switch
        {
            CompatibilityStatus.Favorite => 1,
            CompatibilityStatus.Works => 2,
            CompatibilityStatus.NotTested => 3,
            _ => 4
        };
}
