using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// E11: trwałe powiadomienia unread oparte o watermark przeczytania (IChatReadStateStore):
/// start z zapisanym watermarkiem (badge + plan toastów, własne wykluczone),
/// cichy baseline przy watermark == 0, przeliczanie unread przy każdym merge
/// (shrink → 0 → żądanie zdjęcia toastów, bez spamu planami co polling),
/// przeczytanie przy otwarciu (persist tylko przy zmianie), live-read gdy otwarty + focus.
/// </summary>
public sealed class CommunityChatReadStateTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.UtcNow;

    private static long Ms(int offsetSeconds) => BaseTime.AddSeconds(offsetSeconds).ToUnixTimeMilliseconds();

    private static CommunityChatMessage At(string id, int offsetSeconds, bool own = false) =>
        FakeCommunityChatService.Msg(id, own: own, publishedAt: BaseTime.AddSeconds(offsetSeconds));

    private static CommunityChatMessagesResult Snapshot(params CommunityChatMessage[] messages) =>
        new()
        {
            Status = messages.Length == 0 ? CommunityChatStatus.Empty : CommunityChatStatus.Success,
            Messages = messages
        };

    private static CommunityChatPanelViewModel NewVm(FakeCommunityChatService fake, FakeReadStateStore store) =>
        new(fake, new FakeLocalizationService(),
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(200),
            readState: store);

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

    [Fact]
    public async Task Startup_PersistedWatermark_UnreadBadgeAndGroupedPlan_OwnExcluded()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => Snapshot(At("old", -10), At("b", 1), At("own", 2, own: true), At("c", 3))
        };
        using var vm = NewVm(fake, store);
        ChatToastPlan? plan = null;
        vm.ChatToastsRequested += p => plan = p;

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 2, "unread = b + c (old przed watermarkiem, own wykluczona)");

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.GroupCount);
        Assert.Equal(new[] { "b", "c" }, plan.Previews.Select(m => m.Id));
        Assert.Equal(0, store.Writes); // brak przeczytania — brak zapisu
        vm.Dispose();
    }

    [Fact]
    public async Task Startup_FreshWatermarkZero_SilentBaseline_PersistsLatestTs()
    {
        var store = new FakeReadStateStore();
        var fake = new FakeCommunityChatService { GetFactory = _ => Snapshot(At("a", 1), At("b", 2)) };
        using var vm = NewVm(fake, store);
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2 && vm.HasLoadedSuccessfully, "baseline załadowany");

        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(0, toasts);
        Assert.Equal(Ms(2), store.Watermark);
        Assert.Equal(1, store.Writes);
        vm.Dispose();
    }

    [Fact]
    public async Task Polling_ShrinkingHistory_RecomputesUnread_DismissesAtZero_NoToastSpam()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = call => call switch
            {
                1 => Snapshot(At("a", 1), At("b", 2)),
                2 => Snapshot(At("a", 1)),
                _ => Snapshot()
            }
        };
        using var vm = NewVm(fake, store);
        var plans = 0;
        var dismissals = 0;
        vm.ChatToastsRequested += _ => plans++;
        vm.ChatToastsDismissRequested += () => dismissals++;

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 2, "unread = 2");
        Assert.Equal(1, plans);
        Assert.Equal(0, dismissals);

        await WaitUntilAsync(() => vm.NewMessageCount == 1, "b zniknęła z serwera → badge maleje");
        Assert.Equal(0, dismissals); // unread > 0 → toasty zostają

        await WaitUntilAsync(() => vm.NewMessageCount == 0 && dismissals == 1, "unread 0 → zdjęcie toastów");

        await WaitUntilAsync(() => fake.GetCalls >= 4, "kolejne pollingi");
        Assert.Equal(1, plans);      // bez reemisji planów co polling
        Assert.Equal(1, dismissals); // bez powtarzania dismiss przy każdym pollingu
        vm.Dispose();
    }

    [Fact]
    public async Task OpenChat_PersistsWatermark_ClearsBadge_DismissesToasts_WritesOnlyOnChange()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService { GetFactory = _ => Snapshot(At("a", 1), At("b", 2)) };
        using var vm = NewVm(fake, store);
        var dismissals = 0;
        vm.ChatToastsDismissRequested += () => dismissals++;

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 2, "badge przed otwarciem");
        Assert.Equal(0, store.Writes);

        vm.IsChatOpen = true;
        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(Ms(2), store.Watermark);
        Assert.Equal(1, store.Writes);
        Assert.Equal(1, dismissals);

        // Ponowne otwarcie bez nowych wiadomości — watermark bez zmian → bez zapisu.
        vm.IsChatOpen = false;
        vm.IsChatOpen = true;
        await WaitUntilAsync(() => fake.GetCalls >= 2, "polling po otwarciu");
        Assert.Equal(1, store.Writes);
        Assert.Equal(0, vm.NewMessageCount);
        vm.Dispose();
    }

    [Fact]
    public async Task LiveOpen_Focused_AtBottom_AdvancesWatermark_NoBadgeNoToasts()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = call => call == 1 ? Snapshot(At("a", 1)) : Snapshot(At("a", 1), At("b", 2))
        };
        using var vm = NewVm(fake, store);
        vm.IsChatOpen = true;
        vm.WindowFocused = true;
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => store.Watermark == Ms(2), "live-read przesuwa watermark do najnowszych");

        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(0, toasts);
        Assert.Equal(2, store.Writes); // a potem b — persist przy każdej zmianie
        vm.Dispose();
    }

    [Fact]
    public async Task OpenChat_ScrolledUp_DoesNotAdvanceWatermark_BadgeStable_NoToasts()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = call => call == 1 ? Snapshot(At("a", 1)) : Snapshot(At("a", 1), At("b", 2))
        };
        using var vm = NewVm(fake, store);
        vm.IsChatOpen = true;
        vm.WindowFocused = true;
        vm.NotifyUserAtBottom(false); // użytkownik czyta starsze treści
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 2, "unread rośnie przy czytaniu starszych");
        await WaitUntilAsync(() => fake.GetCalls >= 3, "kolejne pollingi");

        Assert.Equal(2, vm.NewMessageCount); // watermark nie przesuwa się — badge stabilny
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, toasts); // otwarty + focused → polityka tłumi toasty
        vm.Dispose();
    }

    [Fact]
    public async Task OwnMessages_NeverCountedUnread_NeverToast()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => Snapshot(At("o1", 1, own: true), At("o2", 2, own: true))
        };
        using var vm = NewVm(fake, store);
        var toasts = 0;
        vm.ChatToastsRequested += _ => toasts++;

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2 && vm.HasLoadedSuccessfully, "własne załadowane");

        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal(0, toasts);
        Assert.Equal(0, store.Writes); // czat zamknięty — watermark nietknięty
        Assert.Equal(Ms(0), store.Watermark);
        vm.Dispose();
    }

    // ══════════ E11b: divider nieprzeczytanych (snapshot z momentu otwarcia) ══════════

    [Fact]
    public async Task Open_WithUnread_AnchorFirstNewNotOwn_OwnNewerIgnored()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => Snapshot(At("old", -10), At("own", 1, own: true), At("b", 2), At("c", 3))
        };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 2, "unread przed otwarciem");

        vm.IsChatOpen = true;

        Assert.Equal(0, vm.NewMessageCount); // mark-read przy otwarciu zeruje badge
        Assert.True(vm.HasUnreadAnchor);
        Assert.Equal("b", vm.UnreadAnchorMessageId); // pierwsza nowa !own; własna nowsza nie kotwiczy
        Assert.True(vm.Messages.Single(m => m.Id == "b").IsUnreadAnchor);
        Assert.Equal(1, vm.Messages.Count(m => m.IsUnreadAnchor));
        vm.Dispose();
    }

    [Fact]
    public async Task Open_WithoutUnread_NoAnchor()
    {
        var store = new FakeReadStateStore { Watermark = Ms(5) };
        var fake = new FakeCommunityChatService { GetFactory = _ => Snapshot(At("a", 1), At("b", 2)) };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2 && vm.HasLoadedSuccessfully, "historia załadowana");
        Assert.Equal(0, vm.NewMessageCount);

        vm.IsChatOpen = true;

        Assert.False(vm.HasUnreadAnchor);
        Assert.Null(vm.UnreadAnchorMessageId);
        Assert.DoesNotContain(vm.Messages, m => m.IsUnreadAnchor);
        vm.Dispose();
    }

    [Fact]
    public async Task MarkRead_LiveReadDuringSession_AnchorStays()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService { GetFactory = _ => Snapshot(At("old", -10), At("b", 2)) };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "unread przed otwarciem");

        vm.IsChatOpen = true;
        Assert.Equal("b", vm.UnreadAnchorMessageId);
        Assert.Equal(0, vm.NewMessageCount);

        // Live-read przesuwa watermark przy każdym pollingu — snapshot sesji zostaje.
        await WaitUntilAsync(() => fake.GetCalls >= 4, "kilka merge'y po otwarciu");
        Assert.True(vm.HasUnreadAnchor);
        Assert.Equal("b", vm.UnreadAnchorMessageId);
        Assert.Equal(0, vm.NewMessageCount);
        vm.Dispose();
    }

    [Fact]
    public async Task Close_ResetsSnapshot_ReopenWithoutNew_NoAnchor()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService { GetFactory = _ => Snapshot(At("old", -10), At("b", 2)) };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "unread przed otwarciem");

        vm.IsChatOpen = true;
        Assert.Equal("b", vm.UnreadAnchorMessageId);

        vm.IsChatOpen = false;
        Assert.False(vm.HasUnreadAnchor); // zamknięcie resetuje snapshot sesji
        Assert.DoesNotContain(vm.Messages, m => m.IsUnreadAnchor);

        vm.IsChatOpen = true; // watermark = najnowszy ts → brak nowych !own
        Assert.False(vm.HasUnreadAnchor);
        Assert.Null(vm.UnreadAnchorMessageId);
        vm.Dispose();
    }

    [Fact]
    public async Task AnchorRemovedFromServer_DividerGone()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var shrink = false;
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => Volatile.Read(ref shrink)
                ? Snapshot(At("old", -10))
                : Snapshot(At("old", -10), At("b", 2))
        };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "unread przed otwarciem");

        vm.IsChatOpen = true;
        Assert.Equal("b", vm.UnreadAnchorMessageId);

        Volatile.Write(ref shrink, true);
        await WaitUntilAsync(() => !vm.HasUnreadAnchor, "b zniknęła z serwera → divider znika");
        Assert.Null(vm.UnreadAnchorMessageId);
        Assert.DoesNotContain(vm.Messages, m => m.IsUnreadAnchor);
        vm.Dispose();
    }

    [Fact]
    public async Task OwnOnlyNewMessages_NoAnchor()
    {
        var store = new FakeReadStateStore { Watermark = Ms(0) };
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => Snapshot(At("old", -10), At("o1", 1, own: true), At("o2", 2, own: true))
        };
        using var vm = NewVm(fake, store);

        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 3 && vm.HasLoadedSuccessfully, "własne załadowane");
        Assert.Equal(0, vm.NewMessageCount);

        vm.IsChatOpen = true;

        Assert.False(vm.HasUnreadAnchor);
        Assert.Null(vm.UnreadAnchorMessageId);
        Assert.DoesNotContain(vm.Messages, m => m.IsUnreadAnchor);
        vm.Dispose();
    }

    private sealed class FakeReadStateStore : IChatReadStateStore
    {
        public long Watermark { get; set; }
        public int Writes { get; private set; }

        public long GetReadWatermark() => Watermark;

        public void SetReadWatermark(long unixMs)
        {
            Watermark = unixMs;
            Writes++;
        }
    }
}
