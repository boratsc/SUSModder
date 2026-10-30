using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Tests.Support;
using SUSModder.ViewModels;

namespace SUSModder.Tests.ViewModels;

/// <summary>
/// Testy CommunityChatPanelViewModel na fake ICommunityChatService:
/// lifecycle (start/pause/resume/dispose), polling z backoffem, deduplikacja po ID,
/// zachowanie historii i szkicu przy awarii, strażnik podwójnego wysłania,
/// potwierdzane usuwanie własnej wiadomości, zgłaszanie i lokalizacja błędów.
/// </summary>
public sealed class CommunityChatPanelViewModelTests
{
    private static CommunityChatPanelViewModel NewVm(
        FakeCommunityChatService fake,
        FakeLocalizationService loc,
        TimeSpan? pollInterval = null) =>
        new(fake, loc, pollInterval ?? TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(200));

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

    // ══════════════════════════════════════════════════════
    // Lifecycle i polling
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task MessagesWithoutAuthorAreNotShownOrCounted()
    {
        var fake = new FakeCommunityChatService
        {
            GetFactory = _ => new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[]
                {
                    FakeCommunityChatService.Msg("hidden") with { Author = null },
                    FakeCommunityChatService.Msg("visible")
                }
            }
        };
        using var vm = NewVm(fake, new FakeLocalizationService());
        vm.NotifyUserAtBottom(false);
        await vm.RefreshCommand.Execute().ToTask();
        Assert.Empty(vm.Messages);
        Assert.Equal(0, vm.NewMessageCount);
        Assert.Equal("UI.CommunityChat.Error.Protocol", vm.BannerText);
    }

    [Fact]
    public async Task SentMessageWithoutAuthorIsNotInsertedOptimistically()
    {
        var fake = new FakeCommunityChatService
        {
            SendFactory = _ => new CommunityChatSendResult
            {
                Status = CommunityChatStatus.Success,
                Message = FakeCommunityChatService.Msg("hidden", own: true) with { Author = null }
            }
        };
        using var vm = NewVm(fake, new FakeLocalizationService());
        vm.Draft = "test message long enough";
        await vm.SendCommand.Execute().ToTask();
        Assert.Empty(vm.Messages);
        Assert.Equal("test message long enough", vm.Draft);
        Assert.Equal("UI.CommunityChat.Error.SendUncertain", vm.BannerText);
    }

    [Fact]
    public async Task IncompleteRefreshPreservesExistingHistoryAndDraft()
    {
        var fake = new FakeCommunityChatService
        {
            GetFactory = call => new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = call == 1 ? new[] { FakeCommunityChatService.Msg("existing") }
                    : new[] { FakeCommunityChatService.Msg("invalid") with { Author = null } }
            }
        };
        using var vm = NewVm(fake, new FakeLocalizationService());
        await vm.RefreshCommand.Execute().ToTask();
        vm.Draft = "draft is preserved";
        await vm.RefreshCommand.Execute().ToTask();
        Assert.Equal("existing", Assert.Single(vm.Messages).Id);
        Assert.Equal("draft is preserved", vm.Draft);
        Assert.True(vm.IsStale);
        Assert.False(vm.ShowEmptyState);
        Assert.Equal("UI.CommunityChat.Error.Protocol", vm.BannerText);
    }

    [Fact]
    public async Task Banner_FadesBeforeBeingRemoved_WithoutClearingDraft()
    {
        using var vm = new CommunityChatPanelViewModel(new FakeCommunityChatService(),
            new FakeLocalizationService(), bannerDisplayDuration: TimeSpan.FromMilliseconds(50));
        vm.Draft = "x";
        vm.SendCommand.Execute().Subscribe();
        Assert.True(vm.HasBanner);
        Assert.Equal(1, vm.BannerOpacity);
        await WaitUntilAsync(() => vm.BannerOpacity == 0, "banner zaczyna zanikać");
        Assert.True(vm.HasBanner);
        await WaitUntilAsync(() => !vm.HasBanner, "banner znika po animacji");
        Assert.Equal("x", vm.Draft);
    }

    [Fact]
    public async Task NewBanner_CancelsPreviousDismissal()
    {
        using var vm = new CommunityChatPanelViewModel(new FakeCommunityChatService(),
            new FakeLocalizationService(), bannerDisplayDuration: TimeSpan.FromMilliseconds(50));
        vm.Draft = "x";
        vm.SendCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.BannerOpacity == 0, "pierwszy banner zanika");
        vm.Draft = "y";
        vm.SendCommand.Execute().Subscribe();
        Assert.Equal(1, vm.BannerOpacity);
        Assert.True(vm.HasBanner);
        await WaitUntilAsync(() => vm.BannerOpacity == 0, "nowy banner zanika");
        Assert.True(vm.HasBanner);
        await WaitUntilAsync(() => !vm.HasBanner, "nowy banner znika po własnym czasie");
    }

    [Fact]
    public async Task Pause_ClearsBannerAndCancelsDelayedChanges()
    {
        using var vm = new CommunityChatPanelViewModel(new FakeCommunityChatService(),
            new FakeLocalizationService(), bannerDisplayDuration: TimeSpan.FromMilliseconds(30));
        vm.Draft = "x";
        vm.SendCommand.Execute().Subscribe();
        Assert.True(vm.HasBanner);
        vm.Pause();
        Assert.False(vm.HasBanner);
        await Task.Delay(80);
        Assert.Equal(1, vm.BannerOpacity);
        Assert.Equal("x", vm.Draft);
    }

    [Fact]
    public async Task Start_LoadsInitialHistory_AndSwitchesStates()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b") }
            }
            : new CommunityChatMessagesResult { Status = CommunityChatStatus.Empty };

        var loc = new FakeLocalizationService();
        var vm = NewVm(fake, loc);

        Assert.True(vm.IsInitialLoading);
        vm.Start();

        await WaitUntilAsync(() => vm.Messages.Count == 2, "załadowano 2 wiadomości");
        Assert.False(vm.IsInitialLoading);
        Assert.True(vm.HasLoadedSuccessfully);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.ShowDisconnectedState);
        vm.Dispose();
    }

    [Fact]
    public async Task Polling_KeepsRefreshingOnInterval()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));

        vm.Start();
        await WaitUntilAsync(() => fake.GetCalls >= 3, "co najmniej 3 odczyty");
        vm.Dispose();

        Assert.True(fake.GetCalls >= 3);
    }

    [Fact]
    public async Task ConsecutiveFailures_BackoffSlowsPolling_VersusLinearRate()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.NetworkError,
            ErrorCode = CommunityChatErrorCodes.NetworkError
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(20));
        vm.Start();

        await Task.Delay(320);
        var callsInWindow = fake.GetCalls;
        vm.Dispose();

        // Liniowo (20 ms) by byłoby ~16 odczytów; backoff wykładniczy: 20/40/80/160 → ok. 4-6.
        Assert.True(callsInWindow >= 2, $"oczekiwano ≥2 odczytów, było {callsInWindow}");
        Assert.True(callsInWindow <= 8, $"backoff nie zadziałał: {callsInWindow} odczytów w oknie");
    }

    [Fact]
    public async Task Pause_StopsPolling_Resume_RestartsIt()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));

        vm.Start();
        await WaitUntilAsync(() => fake.GetCalls >= 2, "polling ruszył");

        vm.Pause();
        var afterPause = fake.GetCalls;
        await Task.Delay(150);
        Assert.Equal(afterPause, fake.GetCalls);

        vm.Resume();
        await WaitUntilAsync(() => fake.GetCalls > afterPause, "wznowiony polling");
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_CancelsPolling()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));

        vm.Start();
        await WaitUntilAsync(() => fake.GetCalls >= 2, "polling ruszył");

        vm.Dispose();
        var atDispose = fake.GetCalls;
        await Task.Delay(150);
        Assert.Equal(atDispose, fake.GetCalls);

        Assert.Throws<ObjectDisposedException>(() => vm.Start());
    }

    // ══════════════════════════════════════════════════════
    // Merge / deduplikacja / kolejność
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Refresh_DeduplicatesById_KeepsInstanceIdentity_AndChronologicalOrder()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b") }
            }
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b"), FakeCommunityChatService.Msg("c") }
            };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));
        vm.Start();

        await WaitUntilAsync(() => vm.Messages.Count == 3, "dołączono 'c'");

        Assert.Equal(new[] { "a", "b", "c" }, vm.Messages.Select(m => m.Id).ToArray());
        // ten sam obiekt dla wcześniejszych wiadomości (zaznaczony tekst nie znika)
        var first = vm.Messages[0];
        await WaitUntilAsync(() => fake.GetCalls >= 3, "kolejne tury");
        Assert.Same(first, vm.Messages[0]);
        Assert.Equal(3, vm.Messages.Select(m => m.Id).Distinct().Count());

        vm.Dispose();
    }

    [Fact]
    public async Task FailedRefresh_PreservesHistory_MarksStale_AndShowsLocalizedErrorCode()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a") }
            }
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.NetworkError,
                ErrorCode = CommunityChatErrorCodes.NetworkError,
                // surowy tekst serwera — nigdy nie może trafić do UI
                ErrorMessage = "raw server failure text"
            };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));
        vm.Start();

        await WaitUntilAsync(() => vm.IsStale, "stan nieaktualny po awarii");

        Assert.Single(vm.Messages);                       // historia zachowana
        Assert.True(vm.HasLoadedSuccessfully);
        Assert.False(vm.ShowEmptyState);                  // awaria != "brak wiadomości"
        Assert.Equal("UI.CommunityChat.Error.Network", vm.BannerText);
        Assert.DoesNotContain("raw server", vm.BannerText, StringComparison.Ordinal);
        Assert.True(vm.BannerIsError);

        vm.Dispose();
    }

    [Fact]
    public async Task NeverLoadedWithNetworkFailure_ShowsDisconnectedNotEmpty()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.NetworkError,
            ErrorCode = CommunityChatErrorCodes.NetworkError
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));
        vm.Start();

        await WaitUntilAsync(() => vm.ShowDisconnectedState, "stan offline bez danych");
        Assert.False(vm.ShowEmptyState);
        Assert.Equal("UI.CommunityChat.Error.Network", vm.BannerText);
        vm.Dispose();
    }

    [Fact]
    public async Task EmptyChannel_ShowsEmptyState_NotError()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult { Status = CommunityChatStatus.Empty };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));
        vm.Start();

        await WaitUntilAsync(() => vm.ShowEmptyState, "pusty kanał to sukces");
        Assert.False(vm.ShowDisconnectedState);
        Assert.False(vm.HasBanner);
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Przewijanie: at-bottom vs. "nowe wiadomości"
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task NewMessage_WhileScrolledUp_IncrementsCounter_AndButtonResetsWithScroll()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a") }
            }
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a"), FakeCommunityChatService.Msg("b") }
            };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(50));
        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 1, "start z 1 wiadomością");

        vm.NotifyUserAtBottom(false);
        await WaitUntilAsync(() => vm.NewMessageCount == 1, "licznik nowych wiadomości");
        Assert.True(vm.HasNewMessages);
        Assert.Equal("UI.CommunityChat.NewMessages|1", vm.NewMessagesText);

        var scrollFired = false;
        vm.ScrollToBottomRequested += () => scrollFired = true;
        vm.OnNewMessagesButton();

        Assert.Equal(0, vm.NewMessageCount);
        Assert.True(scrollFired);
        vm.Dispose();
    }

    [Fact]
    public async Task NewMessage_WhileAtBottom_TriggersScrollEvent_NotCounter()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult { Status = CommunityChatStatus.Empty }
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("a") }
            };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));
        var scrollEvents = 0;
        vm.ScrollToBottomRequested += () => Interlocked.Increment(ref scrollEvents);

        vm.Start();
        // Collection notification precedes the scroll event in the same merge; await the operation's outcome.
        await WaitUntilAsync(() => vm.Messages.Count == 1 && Volatile.Read(ref scrollEvents) > 0,
            "dopłynęła wiadomość i zgłoszono przewinięcie");

        Assert.True(scrollEvents >= 1);
        Assert.Equal(0, vm.NewMessageCount);
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Wysyłanie
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Send_Success_ClearsDraft_AddsOnce_ThenRefreshes()
    {
        var fake = new FakeCommunityChatService();
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.Success,
            Message = FakeCommunityChatService.Msg("own1", "Cześć świecie!!!", own: true)
        };
        // start: pusto; odświeżenie po wysłaniu zwraca tę samą ID → bez duplikatu
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.Success,
            Messages = new[] { FakeCommunityChatService.Msg("own1", "Cześć świecie!!!", own: true) }
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.Draft = "  Cześć świecie!!!  ";
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => vm.Messages.Count == 1, "wiadomość dodana raz");
        await WaitUntilAsync(() => vm.Draft.Length == 0, "szkic wyczyszczony po sukcesie");

        Assert.Equal(1, fake.SendCalls);
        // VM przekazuje szkic jak jest; normalizacja (trim) leży w kontrakcie Core.
        Assert.Equal("Cześć świecie!!!", fake.LastSentContent!.Trim());
        Assert.Equal(1, vm.Messages.Count(m => m.Id == "own1"));
        Assert.True(fake.GetCalls >= 1); // wymuszone odświeżenie po wysłaniu (bez Start to pierwszy GET)

        vm.Dispose();
    }

    [Fact]
    public async Task Send_ValidationError_CallsNoService_KeepsDraft_ShowsLocalizedKey()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.Draft = "abc"; // za krótka (< 10)
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => vm.HasBanner, "banner walidacji");
        Assert.Equal("UI.CommunityChat.Error.TooShort|10", vm.BannerText);
        Assert.Equal(0, fake.SendCalls);
        Assert.Equal("abc", vm.Draft); // szkic zachowany
        vm.Dispose();
    }

    [Fact]
    public async Task Send_NewlineInDraft_IsExplicitValidationError()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.Draft = "linia jedna\nlinia druga"; // wklejony znak nowej linii → jawny błąd
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => vm.HasBanner, "banner nowej linii");
        Assert.Equal("UI.CommunityChat.Error.Newline", vm.BannerText);
        Assert.Equal(0, fake.SendCalls);
        vm.Dispose();
    }

    [Fact]
    public async Task Send_Failure_KeepsDraft_NoAutoRetry()
    {
        var fake = new FakeCommunityChatService();
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.NetworkError,
            ErrorCode = CommunityChatErrorCodes.NetworkError
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Treść próbna 123";
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => vm.HasBanner, "banner błędu wysłania");
        await Task.Delay(120); // odczekać na ewentualne ciche ponowienia

        Assert.Equal("Treść próbna 123", vm.Draft); // błąd wysłania pozostawia szkic
        Assert.Equal(1, fake.SendCalls);            // brak automatycznego ponawiania POST
        Assert.Equal("UI.CommunityChat.Error.SendUncertain", vm.BannerText);
        vm.Dispose();
    }

    [Fact]
    public async Task Send_RateLimited_BannerCarriesRetryAfterSeconds()
    {
        var fake = new FakeCommunityChatService();
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.RateLimited,
            ErrorCode = CommunityChatErrorCodes.RateLimited,
            RetryAfterSeconds = 42
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Za szybko 12345";
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => vm.HasBanner, "banner limitu");
        Assert.Equal("UI.CommunityChat.Error.RateLimitedSeconds|42", vm.BannerText);
        Assert.Equal("Za szybko 12345", vm.Draft);
        vm.Dispose();
    }

    [Fact]
    public async Task DoubleSend_ExecutesServiceOnlyOnce()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeCommunityChatService();
        fake.SendDelay = () => gate.Task; // wstrzymaj pierwsze wysłanie w locie

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Podwójny enter 1";

        vm.SendCommand.Execute().Subscribe();   // Enter #1
        await WaitUntilAsync(() => vm.IsSending, "wysyłanie w toku");
        vm.SendCommand.Execute().Subscribe();   // Enter #2 podczas trwającej operacji

        await Task.Delay(80);
        Assert.Equal(1, fake.SendCalls);        // druga intencja zablokowana (CanExecute + flaga)

        gate.SetResult();
        await WaitUntilAsync(() => !vm.IsSending, "zakończenie wysyłania");
        await Task.Delay(80);
        Assert.Equal(1, fake.SendCalls);        // i brak ponowienia po zakończeniu
        vm.Dispose();
    }

    [Fact]
    public async Task Send_DraftEditedDuringPost_KeepsNewerDraft()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeCommunityChatService();
        fake.SendDelay = () => gate.Task;
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.Success,
            Message = FakeCommunityChatService.Msg("own1", "Pierwsza wersja!!!", own: true)
        };
        // merge po odświeżeniu zwraca tę samą wiadomość — bez duplikatu
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.Success,
            Messages = new[] { FakeCommunityChatService.Msg("own1", "Pierwsza wersja!!!", own: true) }
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.Draft = "Pierwsza wersja!!!";
        vm.SendCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.IsSending, "POST w toku");

        // Użytkownik edytuje pole WHILE trwa żądanie — nowsza rewizja musi przeżyć.
        vm.Draft = "Nowszy szkic podczas POST";
        gate.SetResult();

        await WaitUntilAsync(() => !vm.IsSending, "wysyłanie zakończone");

        Assert.Equal("Nowszy szkic podczas POST", vm.Draft); // skasowany zostałby tylko szkic niezmieniony
        Assert.Equal(1, fake.SendCalls);
        Assert.Equal(1, vm.Messages.Count(m => m.Id == "own1"));
        vm.Dispose();
    }

    [Fact]
    public async Task Send_UnchangedDraft_IsClearedAfterSuccess()
    {
        var fake = new FakeCommunityChatService();
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.Success,
            Message = FakeCommunityChatService.Msg("own2", "Bez zmian durante", own: true)
        };
        fake.GetFactory = _ => new CommunityChatMessagesResult { Status = CommunityChatStatus.Empty };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Bez zmian durante";
        vm.SendCommand.Execute().Subscribe();

        await WaitUntilAsync(() => !vm.IsSending, "wysyłanie zakończone");
        Assert.Equal(string.Empty, vm.Draft); // rewizja niezmieniona → czysto po sukcesie
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Okno Retry-After: wspólny gate wszystkich wejść odczytu
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task RateLimited_HoldsManualRefreshAndResume_UntilWindowExpires()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.RateLimited,
            ErrorCode = CommunityChatErrorCodes.RateLimited,
            RetryAfterSeconds = 60
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromMilliseconds(15));

        vm.RefreshCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.HasBanner, "pierwszy odczyt z 429");
        Assert.Equal(1, fake.GetCalls);

        // manualny Refresh w oknie Retry-After → żadnych kolejnych wywołań serwisu
        vm.RefreshCommand.Execute().Subscribe();
        await Task.Delay(100);
        Assert.Equal(1, fake.GetCalls);

        // kolejka odświeżeń też jest tłumiona
        vm.RefreshCommand.Execute().Subscribe();
        vm.RefreshCommand.Execute().Subscribe();
        await Task.Delay(100);
        Assert.Equal(1, fake.GetCalls);

        // hide/show (Pause/Resume) nie obchodzi blokady
        vm.Pause();
        await Task.Delay(60);
        vm.Resume();
        await Task.Delay(150); // pętla z interwałem 15 ms nie wywołała odczytu wbrew oknu
        Assert.Equal(1, fake.GetCalls);

        vm.Dispose();
    }

    [Fact]
    public async Task RateLimited_ReadAllowedAgainAfterHoldExpires()
    {
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.RateLimited,
            ErrorCode = CommunityChatErrorCodes.RateLimited,
            RetryAfterSeconds = 1
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.RefreshCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.HasBanner, "429 z Retry-After 1s");

        vm.RefreshCommand.Execute().Subscribe();
        await Task.Delay(60);
        Assert.Equal(1, fake.GetCalls); // wciąż w oknie

        await Task.Delay(1200); // okno minęło
        vm.RefreshCommand.Execute().Subscribe();
        await WaitUntilAsync(() => fake.GetCalls >= 2, "odczyt dozwolony po oknie serwera");
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Zapis kończący się po pauzie/zamknięciu — brak odczytów i mutacji
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task GatedWrite_CompletingAfterPause_TriggersNoReadAndNoMutation()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeCommunityChatService();
        fake.SendDelay = () => gate.Task;
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.Success,
            Message = FakeCommunityChatService.Msg("late1", "Późny sukces 12345", own: true)
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Późny sukces 12345";
        vm.SendCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.IsSending, "POST w toku");

        vm.Pause();       // ukrycie okna zanim POST się skończy
        gate.SetResult(); // zapis "dopływa" już po pauzie

        await WaitUntilAsync(() => !vm.IsSending, "handler zapisu zakończony");
        await Task.Delay(80);

        Assert.Equal(0, fake.GetCalls);           // odświeżenie po zapisie stłumione
        Assert.Empty(vm.Messages);                // bez mutacji UI w pauzie — prawdę przyniesie odczyt po wznowieniu
        Assert.Equal("Późny sukces 12345", vm.Draft); // szkic nietknięty (brak kasowania po pauzie)

        vm.Resume();
        await WaitUntilAsync(() => fake.GetCalls >= 1, "wznowienie wykonuje kontrolowany odczyt");
        vm.Dispose();
    }

    [Fact]
    public async Task GatedWrite_CompletingAfterDispose_TriggersNoRead()
    {
        var gate = new TaskCompletionSource();
        var fake = new FakeCommunityChatService();
        fake.SendDelay = () => gate.Task;
        fake.SendFactory = _ => new CommunityChatSendResult
        {
            Status = CommunityChatStatus.Success,
            Message = FakeCommunityChatService.Msg("late2", "Późny sukces 12345", own: true)
        };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Draft = "Późny sukces 12345";
        vm.SendCommand.Execute().Subscribe();
        await WaitUntilAsync(() => vm.IsSending, "POST w toku");

        vm.Dispose();     // zamknięcie okna w trakcie żądania
        gate.SetResult();

        await Task.Delay(120);
        Assert.Equal(0, fake.GetCalls); // Dispose blokuje odczyty zawsze
    }

    [Fact]
    public async Task Refresh_WithoutStart_IsAllowed_LifeStateActiveByDefault()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.RefreshCommand.Execute().Subscribe();
        await WaitUntilAsync(() => fake.GetCalls >= 1, "stan żywy domyślnie — bez Start");
        vm.Dispose();
    }

    [Fact]
    public async Task Refresh_WhilePaused_IsSuppressed_AllowedAfterResume()
    {
        var fake = new FakeCommunityChatService();
        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));

        vm.Pause();
        vm.RefreshCommand.Execute().Subscribe();
        await Task.Delay(80);
        Assert.Equal(0, fake.GetCalls);

        vm.Resume(); // wznowienie.startuje pętlę z natychmiastowym odczytem
        await WaitUntilAsync(() => fake.GetCalls >= 1, "odczyt dozwolony po wznowieniu");
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Usuwanie i zgłaszanie
    // ══════════════════════════════════════════════════════

    private static async Task<(CommunityChatPanelViewModel vm, FakeCommunityChatService fake, CommunityChatMessageViewModel ownItem)>
        LoadedWithOwnMessageAsync(FakeCommunityChatService fake)
    {
        fake.GetFactory = call => call == 1
            ? new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[]
                {
                    FakeCommunityChatService.Msg("own1", own: true),
                    FakeCommunityChatService.Msg("other1", own: false)
                }
            }
            // odświeżenie po usunięciu: serwer nie zwraca już "own1"
            : new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.Success,
                Messages = new[] { FakeCommunityChatService.Msg("other1", own: false) }
            };

        var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 2, "załadowano wpisy");
        var own = vm.Messages.Single(m => m.Id == "own1");
        return (vm, fake, own);
    }

    [Fact]
    public async Task Delete_ConfirmationDeclined_NoServiceCall_ItemStays()
    {
        var fake = new FakeCommunityChatService();
        var (vm, _, own) = await LoadedWithOwnMessageAsync(fake);
        vm.DeleteConfirmation = _ => Task.FromResult(false);

        vm.DeleteCommand.Execute(own).Subscribe();
        await Task.Delay(120);

        Assert.Equal(0, fake.DeleteCalls);
        Assert.Contains(own, vm.Messages);
        vm.Dispose();
    }

    [Fact]
    public async Task Delete_Confirmed_CallsServiceAndRemovesItem()
    {
        var fake = new FakeCommunityChatService();
        var (vm, _, own) = await LoadedWithOwnMessageAsync(fake);
        vm.DeleteConfirmation = _ => Task.FromResult(true);

        vm.DeleteCommand.Execute(own).Subscribe();
        await WaitUntilAsync(() => fake.DeleteCalls == 1, "usunięcie wysłane");

        Assert.Equal("own1", fake.LastDeletedId);
        await WaitUntilAsync(() => !vm.Messages.Any(m => m.Id == "own1"), "pozycja usunięta z UI");
        Assert.Single(vm.Messages);
        vm.Dispose();
    }

    [Fact]
    public async Task Delete_WithoutConfirmationProvider_IsRefused()
    {
        var fake = new FakeCommunityChatService();
        var (vm, _, own) = await LoadedWithOwnMessageAsync(fake);
        vm.DeleteConfirmation = null; // bezpieczny domyślny: brak dialogu = brak usunięcia

        vm.DeleteCommand.Execute(own).Subscribe();
        await Task.Delay(120);

        Assert.Equal(0, fake.DeleteCalls);
        Assert.Contains(own, vm.Messages);
        vm.Dispose();
    }

    [Fact]
    public async Task Delete_ForeignMessage_RefusedWithForbidden_AndNoServiceCall()
    {
        var fake = new FakeCommunityChatService();
        var (vm, _, _) = await LoadedWithOwnMessageAsync(fake);
        vm.DeleteConfirmation = _ => Task.FromResult(true);
        var foreign = vm.Messages.Single(m => m.Id == "other1");

        vm.DeleteCommand.Execute(foreign).Subscribe();
        await WaitUntilAsync(() => vm.HasBanner, "odmowa dla cudzej wiadomości");

        Assert.Equal(0, fake.DeleteCalls);
        Assert.Equal("UI.CommunityChat.Error.Forbidden", vm.BannerText);
        vm.Dispose();
    }

    [Fact]
    public async Task Report_Success_ShowsInfoBanner_WithoutPromiseOfRemoval()
    {
        var fake = new FakeCommunityChatService();
        var (vm, _, own) = await LoadedWithOwnMessageAsync(fake);

        vm.ReportCommand.Execute(own).Subscribe();
        await WaitUntilAsync(() => vm.HasBanner, "banner zgłoszenia");

        Assert.Equal("UI.CommunityChat.Report.Done", vm.BannerText);
        Assert.True(vm.BannerIsInfo);
        Assert.Contains(own, vm.Messages); // zgłoszenie nie usuwa treści
        vm.Dispose();
    }

    // ══════════════════════════════════════════════════════
    // Kody błędów i język
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task ServerErrorCodes_MapToStableLocalizedKeys()
    {
        var cases = new (string Code, string Key)[]
        {
            ("USER_BANNED", "UI.CommunityChat.Error.Banned"),
            ("CONTENT_BLOCKED", "UI.CommunityChat.Error.Blocked"),
            ("DUPLICATE_MESSAGE", "UI.CommunityChat.Error.Duplicate"),
            ("CHAT_UNAUTHORIZED", "UI.CommunityChat.Error.Auth"),
            ("CHAT_PROTOCOL_ERROR", "UI.CommunityChat.Error.Protocol"),
            ("SOMETHING_UNEXPECTED", "UI.CommunityChat.Error.Unknown"),
        };

        foreach (var (code, key) in cases)
        {
            var fake = new FakeCommunityChatService();
            fake.GetFactory = _ => new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.RequestFailed,
                ErrorCode = code,
                ErrorMessage = "raw/moderation text that must never be shown"
            };

            var vm = NewVm(fake, new FakeLocalizationService(), TimeSpan.FromSeconds(30));
            vm.Start();
            await WaitUntilAsync(() => vm.HasBanner, $"banner dla {code}");

            Assert.Equal(key, vm.BannerText);
            Assert.DoesNotContain("raw/moderation", vm.BannerText, StringComparison.Ordinal);
            vm.Dispose();
        }
    }

    [Fact]
    public async Task LanguageChange_KeepsHistoryAndDraft_AndRecomputesLabels()
    {
        var loc = new FakeLocalizationService();
        var fake = new FakeCommunityChatService();
        fake.GetFactory = _ => new CommunityChatMessagesResult
        {
            Status = CommunityChatStatus.Success,
            Messages = new[] { FakeCommunityChatService.Msg("a") }
        };

        var vm = NewVm(fake, loc, TimeSpan.FromSeconds(30));
        vm.Start();
        await WaitUntilAsync(() => vm.Messages.Count == 1, "załadowano");

        vm.Draft = "Niedokończony szkic";
        loc.ChangeCulture("en");

        Assert.Single(vm.Messages);
        Assert.Equal("Niedokończony szkic", vm.Draft);
        Assert.Equal("UI.CommunityChat.NewMessages|0", vm.NewMessagesText); // rekoмputed, nie zerowane
        vm.Dispose();
    }
}
