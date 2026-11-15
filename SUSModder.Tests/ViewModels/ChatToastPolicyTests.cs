using System.Linq;
using SUSModder.Core.Models;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E5: polityka toastów czatu — grupowanie (≥2 = licznik + max 2 najnowsze podglądy),
/// pojedynczy toast dla 1 wiadomości, tłumienie gdy czat otwarty I okno ma focus,
/// pomijanie własnych wiadomości.
/// </summary>
public sealed class ChatToastPolicyTests
{
    private static CommunityChatMessageViewModel Msg(string id, bool own = false) =>
        new(FakeCommunityChatService.Msg(id, own: own), new FakeLocalizationService());

    [Fact]
    public void NoMessages_NoPlan()
    {
        Assert.Null(ChatToastPolicy.Plan(new CommunityChatMessageViewModel[0], false, true));
    }

    [Fact]
    public void SingleMessage_SinglePreview_NoGroup()
    {
        var plan = ChatToastPolicy.Plan(new[] { Msg("a") }, false, true);

        Assert.NotNull(plan);
        Assert.Null(plan!.GroupCount);
        Assert.Equal(new[] { "a" }, plan.Previews.Select(m => m.Id));
    }

    [Fact]
    public void TwoMessages_GroupPlusTwoPreviews()
    {
        var plan = ChatToastPolicy.Plan(new[] { Msg("a"), Msg("b") }, false, true);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.GroupCount);
        Assert.Equal(new[] { "a", "b" }, plan.Previews.Select(m => m.Id));
    }

    [Fact]
    public void FiveMessages_GroupPlusOnlyTwoNewestPreviews()
    {
        var plan = ChatToastPolicy.Plan(
            new[] { Msg("a"), Msg("b"), Msg("c"), Msg("d"), Msg("e") }, false, true);

        Assert.NotNull(plan);
        Assert.Equal(5, plan!.GroupCount);
        Assert.Equal(new[] { "d", "e" }, plan.Previews.Select(m => m.Id));
    }

    [Fact]
    public void ChatOpenAndWindowFocused_NoToasts()
    {
        Assert.Null(ChatToastPolicy.Plan(new[] { Msg("a") }, true, true));
        Assert.Null(ChatToastPolicy.Plan(new[] { Msg("a"), Msg("b"), Msg("c") }, true, true));
    }

    [Fact]
    public void ChatOpenButWindowUnfocused_ToastsAllowed()
    {
        var plan = ChatToastPolicy.Plan(new[] { Msg("a"), Msg("b") }, true, false);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.GroupCount);
    }

    [Fact]
    public void ChatClosedAndWindowFocused_ToastsAllowed()
    {
        Assert.NotNull(ChatToastPolicy.Plan(new[] { Msg("a") }, false, true));
    }

    [Fact]
    public void OwnMessages_NeverToast()
    {
        Assert.Null(ChatToastPolicy.Plan(new[] { Msg("a", own: true) }, false, true));
    }

    [Fact]
    public void MixedOwn_GroupCountsOnlyForeignMessages()
    {
        var plan = ChatToastPolicy.Plan(
            new[] { Msg("a", own: true), Msg("b"), Msg("c"), Msg("d") }, false, true);

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.GroupCount);
        Assert.Equal(new[] { "c", "d" }, plan.Previews.Select(m => m.Id));
    }
}
