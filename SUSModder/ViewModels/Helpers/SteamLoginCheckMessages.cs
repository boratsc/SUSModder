using SUSModder.Core.GameIntegration.Steam;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels.Helpers;

/// <summary>
/// Mapowanie wyniku headless-testu logowania Steam na zlokalizowany komunikat (toast/dialog).
/// Wydzielone z MainWindowViewModel, aby mapowanie statusów było testowalne bez Avalonii.
/// Bez fałszywych sukcesów: „Confirmed" tylko po markerze w logu bieżącego uruchomienia.
/// </summary>
internal static class SteamLoginCheckMessages
{
    internal static (ToastType Type, string Title, string Message) Build(
        ILocalizationService loc, SteamHeadlessLoginResult result)
    {
        string? extra = result.OwnInstanceUnconfirmed
            ? loc.Get("UI.Repair.SteamLoginCheck.ProcessLeftOpen")
            : null;

        return result.Status switch
        {
            SteamHeadlessLoginStatus.Confirmed => (
                ToastType.Success,
                loc.Get("Dialogs.Success.Title"),
                Append(loc.Get("UI.Repair.SteamLoginCheck.Success"), extra)),

            SteamHeadlessLoginStatus.NotConfirmed => (
                ToastType.Warning,
                loc.Get("UI.Repair.SteamLoginCheck.NotConfirmedTitle"),
                Append(loc.GetFormatted("UI.Repair.SteamLoginCheck.NotConfirmed", result.LogPath ?? "-"), extra)),

            SteamHeadlessLoginStatus.Cancelled => (
                ToastType.Info,
                loc.Get("Dialogs.Info.Title"),
                Append(loc.Get("UI.Repair.Cancelled"), extra)),

            SteamHeadlessLoginStatus.GameAlreadyRunning => (
                ToastType.Info,
                loc.Get("Dialogs.Info.Title"),
                loc.Get("UI.Repair.SteamLoginCheck.GameRunning")),

            SteamHeadlessLoginStatus.SteamUnavailable => (
                ToastType.Error,
                loc.Get("Dialogs.Error.Title"),
                loc.Get("UI.Repair.SteamLoginCheck.SteamUnavailable")),

            SteamHeadlessLoginStatus.GameNotInstalled => (
                ToastType.Warning,
                loc.Get("UI.Repair.SteamLoginCheck.NotConfirmedTitle"),
                loc.Get("UI.Repair.SteamLoginCheck.GameNotInstalled")),

            // Test pominięty przed uruchomieniem gry — wersja testowa (beta) nie nadaje się do modów.
            SteamHeadlessLoginStatus.BetaBranchMismatch => (
                ToastType.Warning,
                loc.Get("UI.Repair.SteamLoginCheck.BetaBranchMismatchTitle"),
                loc.Get("UI.Repair.SteamLoginCheck.BetaBranchMismatch")),

            // Test pominięty — wersji gry nie dało się ustalić; ścieżka ręczna pyta o override.
            SteamHeadlessLoginStatus.BranchUnknown => (
                ToastType.Info,
                loc.Get("UI.Repair.SteamLoginCheck.BranchUnknownTitle"),
                loc.Get("UI.Repair.SteamLoginCheck.BranchUnknown")),

            _ => (
                ToastType.Error,
                loc.Get("Dialogs.Error.Title"),
                Append(loc.GetFormatted("UI.Repair.SteamLoginCheck.Failed", result.Diagnostic ?? "-"), extra)),
        };
    }

    private static string Append(string message, string? extra) =>
        extra == null ? message : message + "\n\n" + extra;
}
