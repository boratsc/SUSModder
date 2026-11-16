using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E5: stan nakładki czatu i badge/toasty na poziomie VM —
/// baseline pierwszego odczytu bez badge, badge przy zamkniętym czacie,
/// tłumienie toastów gdy otwarty + focus, czyszczenie badge i fokus przy otwarciu,
/// X panelu przez RequestChatOpen, opis dymka z liczbą.
/// </summary>
public sealed class CommunityChatOverlayTests
{
    private static CommunityChatPanelViewModel NewVm(FakeCommunityChatService fake) =>
        new(fake, new FakeLocalizationService(),
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(200));

    private static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Warunek niespełniony: {because} (po {timeoutMs} ms)");
            await Task.Delay(10);
        }
    }

    private static FakeCommunityChatService TwoStageFake() => new()
    {
        GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a") }
            }
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b") }
            }
    };

    [Fact]
    public async Task FirstLoad_IsReadBaseline_NoBadgeNoToasts()
    {
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b") }
            }
        };
        using var vm = NewVm(fake);
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2 && vm.HasLoadedSuccessfully, "baseline załadowany");

        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(0, toasts);
        vm.Dispose();
    }

    [Fact]
    public async Task ClosedChat_NewMessage_IncrementsBadge_AndRequestsSingleToast()
    {
        var fake = TwoStageFake();
        using var vm = NewVm(fake);
        ChatToastPlan? plan = null;
        vm.ChatToastsRequested += p => plan = p;

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "badge nieprzeczytanych przy zamkniętym czacie");

        Assert.True(vm.HasNewMessages);
        Assert.NotNull(plan);
        Assert.Null(plan!.GroupCount);
        Assert.Equal(new[] { "b" }, plan.Previews.Select(m => m.Id));
        Assert.Equal("UI.TopBar.ChatToggleUnreadOne|1", vm.ChatToggleLabel);
        vm.Dispose();
    }

    [Fact]
    public async Task OpenChat_Focused_AtBottom_Scrolls_NoBadgeNoToasts()
    {
        var fake = TwoStageFake();
        using var vm = NewVm(fake);
        vm.IsChatOpen = true;
        vm.WindowFocused = true;
        var toasts = 0;
        var scrolls = 0;
        vm.ChatToastsRequested += _ => toasts++;
        vm.ScrollToBottomRequested += () => scrolls++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2, "druga wiadomość dopłynęła");

        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(0, toasts);
        Assert.True(scrolls >= 1);
        vm.Dispose();
    }

    [Fact]
    public async Task OpenChat_Unfocused_ToastsStillRequested()
    {
        var fake = TwoStageFake();
        using var vm = NewVm(fake);
        vm.IsChatOpen = true;
        vm.WindowFocused = false;
        ChatToastPlan? plan = null;
        vm.ChatToastsRequested += p => plan = p;

        vm.Start();
        await WaitUntilAsync(() => plan != null, "toast przy otwartym czacie bez focusu okna");

        Assert.Null(plan!.GroupCount);
        vm.Dispose();
    }

    [Fact]
    public async Task OpenChat_ScrolledUp_BadgeButNoToasts()
    {
        var fake = TwoStageFake();
        using var vm = NewVm(fake);
        vm.IsChatOpen = true;
        vm.WindowFocused = true;
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 1, "baseline");
        vm.NotifyUserAtBottom(false);
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "licznik przy czytaniu starszych");

        Assert.Equal(0, toasts);
        vm.Dispose();
    }

    [Fact]
    public async Task OpeningChat_ClearsBadge_RequestsScrollAndFocus()
    {
        var fake = TwoStageFake();
        using var vm = NewVm(fake);
        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "badge przed otwarciem");

        // Pierwsze otwarcie pokazuje panel zasad — fokus do pola dopiero po potwierdzeniu.
        await vm.AcknowledgeRulesCommand.Execute().ToTask();

        var scrolls = 0;
        var focus = 0;
        vm.ScrollToBottomRequested += () => scrolls++;
        vm.FocusInputRequested += () => focus++;

        vm.IsChatOpen = true;

        Assert.Equal(0, vm.NewMessageCount);
        Assert.False(vm.HasNewMessages);
        Assert.Equal("UI.TopBar.ChatToggle", vm.ChatToggleLabel);
        Assert.True(scrolls >= 1);
        Assert.Equal(1, focus);
        vm.Dispose();
    }

    [Fact]
    public async Task CloseChatCommand_RequestsCloseThroughOwner()
    {
        using var vm = NewVm(new FakeCommunityChatService());
        bool? requested = null;
        vm.RequestChatOpen = open => requested = open;

        await vm.CloseChatCommand.Execute().ToTask();

        Assert.False(requested);
        vm.Dispose();
    }

    [Fact]
    public async Task OwnMessage_ArrivingAfterPause_DoesNotToast()
    {
        var fake = new FakeCommunityChatService
        {
            GetFactory = call => call == 1
                ? new CommunityChatMessagesResult { Status = CommunityChatStatus.Empty }
                : new CommunityChatMessagesResult
                {
                    Status = CommunityChatStatus.Success,
                    Messages = new[] { FakeCommunityChatService.Msg("own", own: true) }
                }
        };
        using var vm = NewVm(fake);
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 1, "własna wiadomość z merge");

        Assert.Equal(0, toasts);
        vm.Dispose();
    }
}
