using SUSModder.Core.GameIntegration.Steam;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Faza 3a: mapowanie nowych statusów headless-testu Steam (beta/branch) na komunikaty.
/// Fake lokalizacji zwraca klucze — test dowodzi, że UI używa stabilnych kluczy i właściwych typów.
/// </summary>
public class SteamLoginCheckMessagesTests
{
    private static readonly FakeLocalizationService Loc = new();

    [Fact]
    public void BetaBranchMismatch_IsWarning_WithMismatchTitleAndInstruction()
    {
        var (type, title, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.BetaBranchMismatch));

        Assert.Equal(ToastType.Warning, type);
        Assert.Equal("UI.Repair.SteamLoginCheck.BetaBranchMismatchTitle", title);
        Assert.Equal("UI.Repair.SteamLoginCheck.BetaBranchMismatch", message);
    }

    [Fact]
    public void BranchUnknown_IsInfo_WithUnknownTitleAndManualHint()
    {
        var (type, title, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.BranchUnknown));

        Assert.Equal(ToastType.Info, type);
        Assert.Equal("UI.Repair.SteamLoginCheck.BranchUnknownTitle", title);
        Assert.Equal("UI.Repair.SteamLoginCheck.BranchUnknown", message);
    }

    [Fact]
    public void Confirmed_StillSuccess()
    {
        var (type, title, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Confirmed));

        Assert.Equal(ToastType.Success, type);
        Assert.Equal("Dialogs.Success.Title", title);
        Assert.Equal("UI.Repair.SteamLoginCheck.Success", message);
    }

    [Fact]
    public void NotConfirmed_StillWarning_WithLogPath()
    {
        var (type, _, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.NotConfirmed, LogPath: "C:\\log.txt"));

        Assert.Equal(ToastType.Warning, type);
        Assert.Equal("UI.Repair.SteamLoginCheck.NotConfirmed|C:\\log.txt", message);
    }

    [Fact]
    public void Failed_StillError_WithDiagnostic()
    {
        var (type, _, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Failed, Diagnostic: "boom"));

        Assert.Equal(ToastType.Error, type);
        Assert.Equal("UI.Repair.SteamLoginCheck.Failed|boom", message);
    }

    [Fact]
    public void UnconfirmedOwnInstance_AppendsProcessLeftOpenNote()
    {
        var (_, _, message) = SteamLoginCheckMessages.Build(
            Loc, new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Confirmed, OwnInstanceUnconfirmed: true));

        Assert.Equal(
            "UI.Repair.SteamLoginCheck.Success\n\nUI.Repair.SteamLoginCheck.ProcessLeftOpen",
            message);
    }
}
