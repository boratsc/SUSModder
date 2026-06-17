using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels.Helpers;

/// <summary>
/// Formatuje wpisy diff paczki modów do wyświetlenia w UI (i18n).
/// </summary>
public static class ModPackChangeFormatter
{
    public static string Format(ILocalizationService localization, ModPackChangeItem item)
    {
        var oldValue = string.IsNullOrWhiteSpace(item.OldValue)
            ? localization.Get("ModPacks.Change.Missing")
            : item.OldValue;
        var newValue = item.NewValue ?? "?";

        return item.ChangeType switch
        {
            "fullMod" => localization.GetFormatted("ModPacks.Change.FullMod", item.Name, oldValue, newValue),
            "dll" => localization.GetFormatted("ModPacks.Change.Dll", item.Name, oldValue, newValue),
            "externalDll" => localization.GetFormatted("ModPacks.Change.ExternalDll", item.Name),
            "config" => localization.GetFormatted("ModPacks.Change.Config", item.Name),
            _ => !string.IsNullOrWhiteSpace(item.Description)
                ? item.Description
                : localization.GetFormatted("ModPacks.Change.Generic", item.Name, oldValue, newValue)
        };
    }
}
