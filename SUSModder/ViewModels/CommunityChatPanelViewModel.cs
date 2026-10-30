using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Validators;

namespace SUSModder.ViewModels;

/// <summary>
/// ViewModel jednego, stałego panelu globalnego komunikatora ("Czat ogólny").
/// Należy do głównego okna i żyje niezależnie od zaznaczenia moda — zmiana
/// lub odznaczenie elementu nie resetuje historii ani szkicu.
///
/// Zasady (PLAN 2026-10-23-global-chat-replaces-lobby-board):
/// - polling 30 s z wykładniczym backoffem po awarii i poszanowaniem Retry-After;
/// - okno Retry-After obowiązuje WEJŚCIA ODCZYTU niezależnie od źródła
///   (timer, manualny Refresh, odświeżenie po zapisie, wznowienie po pauzie);
/// - wspólny token cyklu życia: Pause/Dispose tłumią odczyty i mutacje UI,
///   także dla zapisów kończących się już po pauzie/zamknięciu;
/// - jeden aktywny odczyt naraz, pełne anulowanie przy pauzie i zamknięciu;
/// - awaria odświeżania zachowuje historię + szkic i oznacza dane jako nieaktualne;
/// - deduplikacja po ID, kolejność chronologiczna, najnowsze na dole;
/// - auto-przewijanie tylko gdy użytkownik był przy końcu, inaczej licznik "Nowe wiadomości";
/// - blokada wielokrotnego wysłania podczas trwającej operacji; sukces czyści wyłącznie
///   niezmienioną rewizję szkicu (edycja w trakcie POST zostaje zachowana);
/// - usuwanie wyłącznie własnej wiadomości, po potwierdzeniu;
/// - błędy prezentowane jako zlokalizowane komunikaty ze stabilnych kodów, nigdy surowy tekst serwera.
///
/// Pętla startuje na wątku UI (kontekst wywołania jest zachowywany przez await),
/// więc aktualizacje kolekcji trafiają do powiązań Avalonii na wątku UI.
/// </summary>
public sealed class CommunityChatPanelViewModel : ReactiveObject, IDisposable
{
    private readonly ICommunityChatService _service;
    private readonly ILocalizationService _loc;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maxBackoff;

    // Wspólny token całego cyklu życia panelu: aktywny od konstrukcji (bez Start),
    // kasowany przy Pause i przy Dispose; Resume tworzy nowy. Dzięki temu opóźniony
    // zapis (POST/DELETE) kończący się po pauzie/zamknięciu nie wywołuje odczytów
    // ani nie mutuje stanu po Dispose.
    private CancellationTokenSource? _lifecycleCts;
    private bool _paused;
    private bool _disposed;
    private bool _started;

    // Single-flight odświeżania: równoległe żądania (timer, wysłanie, usunięcie)
    // są koalescowane — trwa jeden odczyt, ewentualnie jedna dodatkowa tura.
    private bool _refreshRunning;
    private bool _refreshQueued;

    private int _consecutiveFailures;
    private int? _lastRetryAfterSeconds;
    private bool _lastFailureRateLimited;

    /// <summary>
    /// Serwerowe okno Retry-After (HTTP 429) — każdy wspólny punkt wejścia odczytu
    /// respektuje je niezależnie od źródła (polling, manual Refresh, odświeżenie po
    /// zapisie, wznowienie po Pause). 0 (default) = brak blokady.
    /// </summary>
    private DateTimeOffset _nextAllowedReadUtc;

    // Strażniki jednoczesnych operacji zmieniających dane (Enter double-send guard).
    private bool _sendInFlight;
    private bool _deleteInFlight;

    // Rewizja szkicu: sukces wysłania czyści tylko tę wersję, która faktycznie została
    // wysłana — edycja pola w trakcie trwającego POST nie jest kasowana.
    private int _draftRevision;

    private readonly Dictionary<string, CommunityChatMessageViewModel> _itemsById =
        new(StringComparer.Ordinal);

    // Banner stanu trzymany jako kod + argumenty, aby zmiana języka nie wymagała
    // ponownego odświeżenia (tekst liczony na żądanie).
    private string? _bannerCode;
    private object?[]? _bannerArgs;
    private CommunityChatBannerSeverity _bannerSeverity = CommunityChatBannerSeverity.Info;

    /// <summary>True, gdy banner pochodzi z pętli odświeżania (offline/stale) — czyszczony po udanym odczycie.</summary>
    private bool _bannerFromRefresh;
    private readonly TimeSpan _bannerDisplayDuration;
    private CancellationTokenSource? _bannerCts;
    private double _bannerOpacity = 1;
    public double BannerOpacity => _bannerOpacity;

    public CommunityChatPanelViewModel(
        ICommunityChatService service,
        ILocalizationService loc,
        TimeSpan? pollInterval = null,
        TimeSpan? maxBackoff = null,
        TimeSpan? bannerDisplayDuration = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        _maxBackoff = maxBackoff ?? TimeSpan.FromMinutes(5);
        _bannerDisplayDuration = bannerDisplayDuration ?? TimeSpan.FromSeconds(5);
        _lifecycleCts = new CancellationTokenSource();

        ReportCommand = ReactiveCommand.CreateFromTask<CommunityChatMessageViewModel>(ReportAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask<CommunityChatMessageViewModel>(DeleteAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
            await RefreshOnceAsync(ActiveToken));

        var canSend = this.WhenAnyValue(
            x => x.Draft,
            x => x.IsSending,
            (draft, sending) => !sending && !string.IsNullOrWhiteSpace(draft));
        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);

        Messages.CollectionChanged += OnMessagesCollectionChanged;

        if (_loc is INotifyPropertyChanged locNotify)
            locNotify.PropertyChanged += OnLocalizationChanged;
    }

    // ══════════════════════════════════════════════════════
    // Stan widoczny dla widoku
    // ══════════════════════════════════════════════════════

    /// <summary>Historia wspólnego kanału — chronologicznie, najnowsze na końcu.</summary>
    public ObservableCollection<CommunityChatMessageViewModel> Messages { get; } = new();

    private string _draft = string.Empty;

    /// <summary>Szkic wiadomości. Błędy wysłania/odświeżania nigdy go nie kasują.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_draft, next, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _draft, next);
            _draftRevision++;
            this.RaisePropertyChanged(nameof(CharCountText));
            this.RaisePropertyChanged(nameof(CanShowCharCounterWarning));
        }
    }

    /// <summary>Licznik znaków w tej samej metryce co walidator (jednostki UTF-16 po trim).</summary>
    public string CharCountText =>
        $"{CommunityChatMessageValidator.MeasureLength(Draft)}/{CommunityChatLimits.MaxMessageLength}";

    public bool CanShowCharCounterWarning =>
        CommunityChatMessageValidator.MeasureLength(Draft) > CommunityChatLimits.MaxMessageLength;

    private bool _isSending;
    public bool IsSending
    {
        get => _isSending;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSending, value);
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    private bool _isDeleting;
    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDeleting, value);
            this.RaisePropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsBusy => IsSending || IsDeleting;

    private bool _isInitialLoading = true;

    /// <summary>Pierwsze ładowanie historii (brak danych w kolekcji).</summary>
    public bool IsInitialLoading
    {
        get => _isInitialLoading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isInitialLoading, value);
            RaiseViewStates();
        }
    }

    private bool _hasLoadedSuccessfully;

    /// <summary>Czy kiedykolwiek wykonano udany odczyt (różne od „brak danych, awaria”).</summary>
    public bool HasLoadedSuccessfully
    {
        get => _hasLoadedSuccessfully;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasLoadedSuccessfully, value);
            RaiseViewStates();
        }
    }

    private bool _isStale;

    /// <summary>Historia zachowana, ale ostatnie odświeżanie się nie powiodło.</summary>
    public bool IsStale
    {
        get => _isStale;
        private set => this.RaiseAndSetIfChanged(ref _isStale, value);
    }

    /// <summary>Udany odczyt, ale pusty kanał — różnie od awarii.</summary>
    public bool ShowEmptyState => HasLoadedSuccessfully && Messages.Count == 0;

    /// <summary>Brak danych i brak udanego odczytu — pokazujemy stan offline, nie „brak wiadomości”.</summary>
    public bool ShowDisconnectedState => !IsInitialLoading && !HasLoadedSuccessfully && Messages.Count == 0;

    public bool HasBanner => !string.IsNullOrEmpty(_bannerCode);
    public bool BannerIsError => _bannerSeverity == CommunityChatBannerSeverity.Error;
    public bool BannerIsWarning => _bannerSeverity == CommunityChatBannerSeverity.Warning;
    public bool BannerIsInfo => _bannerSeverity == CommunityChatBannerSeverity.Info;

    /// <summary>Zlokalizowany komunikat stanu (null = brak bannera). Nigdy surowy tekst serwera.</summary>
    public string? BannerText => _bannerCode is null ? null : LocalizeError(_bannerCode, _bannerArgs);

    private int _newMessageCount;

    /// <summary>Liczba wiadomości, które pojawiły się, gdy użytkownik czytał starsze treści.</summary>
    public int NewMessageCount
    {
        get => _newMessageCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _newMessageCount, value);
            this.RaisePropertyChanged(nameof(HasNewMessages));
            this.RaisePropertyChanged(nameof(NewMessagesText));
        }
    }

    public bool HasNewMessages => NewMessageCount > 0;

    public string NewMessagesText => _loc.GetFormatted("UI.CommunityChat.NewMessages", NewMessageCount);

    /// <summary>
    /// Użytkownik jest przy końcu listy. Widok zgłasza to przez <see cref="NotifyUserAtBottom"/>.
    /// Startowo: true — pierwsze załadowanie przewija do najnowszych wiadomości.
    /// </summary>
    public bool UserAtBottom { get; private set; } = true;

    /// <summary>Widok ma przewinąć listę na sam dół (nowe wiadomości przy pozycji na dole).</summary>
    public event Action? ScrollToBottomRequested;

    /// <summary>
    /// Potwierdzenie usunięcia własnej wiadomości (treść dialogu → wynik).
    /// Bez dostarczyciela usunięcie jest odmówione — bezpieczny domyślny;
    /// MainWindow podpinuje dialog potwierdzenia.
    /// </summary>
    public Func<string, Task<bool>>? DeleteConfirmation { get; set; }

    // ══════════════════════════════════════════════════════
    // Komendy
    // ══════════════════════════════════════════════════════

    public ReactiveCommand<CommunityChatMessageViewModel, Unit> ReportCommand { get; }
    public ReactiveCommand<CommunityChatMessageViewModel, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    /// <summary>Przycisk „Nowe wiadomości” — kasuje licznik i zleca przewinięcie.</summary>
    public void OnNewMessagesButton()
    {
        NewMessageCount = 0;
        UserAtBottom = true;
        ScrollToBottomRequested?.Invoke();
    }

    /// <summary>Widok informuje, czy użytkownik jest przy końcu przewijania.</summary>
    public void NotifyUserAtBottom(bool atBottom) => UserAtBottom = atBottom;

    // ══════════════════════════════════════════════════════
    // Lifecycle pętli odświeżania
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Aktualny token cyklu życia. Aktywny od konstrukcji (bez Start), anulowany
    /// przy Pause i Dispose; null/po anulowaniu → token już skasowany, więc każda
    /// ścieżka (odczyt, zapis, odświeżenie po zapisie) jest tłumiona.
    /// </summary>
    private CancellationToken ActiveToken =>
        _lifecycleCts is { IsCancellationRequested: false } cts
            ? cts.Token
            : new CancellationToken(canceled: true);

    /// <summary>Uruchamia natychmiastowy odczyt i okresowy polling. Wołać raz, na wątku UI.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (_started || _paused)
            return;

        _started = true;
        _ = RunPollLoopAsync(ActiveToken);
    }

    /// <summary>
    /// Pauzuje polling i anuluje operacje w toku (okno ukryte/zminimalizowane).
    /// Historia i szkic pozostają; odświeżenia z tej chwili NIE wywołują serwisu.
    /// </summary>
    public void Pause()
    {
        if (_disposed || _paused)
            return;

        _paused = true;
        CancelLifecycle();
        SetBanner(null, null, CommunityChatBannerSeverity.Info, fromRefresh: false);
    }

    /// <summary>Wznawia polling z kontrolowanym natychmiastowym odświeżeniem.</summary>
    public void Resume()
    {
        if (_disposed || !_paused)
            return;

        _paused = false;
        _lifecycleCts ??= new CancellationTokenSource();
        Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_loc is INotifyPropertyChanged locNotify)
            locNotify.PropertyChanged -= OnLocalizationChanged;
        CancelLifecycle();
    }

    private void CancelLifecycle()
    {
        _bannerCts?.Cancel();
        var cts = _lifecycleCts;
        _lifecycleCts = null;
        _started = false;
        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // już zlikwidowany — ignoruj
        }
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshOnceAsync(ct);

            var wait = ComputeNextWait();
            try
            {
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TimeSpan ComputeNextWait()
    {
        if (_consecutiveFailures == 0)
            return _pollInterval;

        // Wykładniczy backoff: interval × 2^(n-1), capped do _maxBackoff.
        var exponent = Math.Min(_consecutiveFailures - 1, 10);
        var backoff = TimeSpan.FromTicks(_pollInterval.Ticks * (1L << exponent));
        if (backoff > _maxBackoff)
            backoff = _maxBackoff;

        if (_lastFailureRateLimited && _lastRetryAfterSeconds is int retrySeconds && retrySeconds > 0)
        {
            // Serwer wskazał Retry-After — respektujemy go bez własnych limitów,
            // z dolną granicą równą podstawie interwału (brak agresywnego pollingu).
            var serverWait = TimeSpan.FromSeconds(retrySeconds + 1d);
            if (serverWait > backoff)
                return serverWait;
        }

        return backoff;
    }

    // ══════════════════════════════════════════════════════
    // Odświeżanie / merge
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// One-shot odświeżanie (timery, wysłanie, usunięcie). Współbieżne żądania
    /// są koalescowane do jednej ponownej tury — nigdy dwa aktywne odczyty naraz.
    /// </summary>
    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        if (_refreshRunning)
        {
            _refreshQueued = true;
            return;
        }

        _refreshRunning = true;
        try
        {
            do
            {
                _refreshQueued = false;
                await RefreshCoreAsync(ct);
            }
            while (_refreshQueued && !ct.IsCancellationRequested);
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        // Wspólny punkt wejścia ODCZYTU — respektowany przez polling, manualny Refresh,
        // odświeżenie po zapisie oraz wznowienie po Pause/Resume:
        // 1) pauza/dispose → tłumimy odczyt bez wywołania serwisu;
        // 2) serwerowe okno Retry-After (429) → odczyt przed czasem niedozwolony.
        if (_disposed || ct.IsCancellationRequested)
            return;

        if (DateTimeOffset.UtcNow < _nextAllowedReadUtc)
            return;

        var result = await _service.GetMessagesAsync(CommunityChatLimits.MaxHistorySize, ct);

        if (ct.IsCancellationRequested || _disposed)
            return;

        if (result.Status == CommunityChatStatus.Cancelled)
            return;

        if (result.IsSuccess && result.Messages.Any(message => !message.IsComplete))
            result = new CommunityChatMessagesResult
            {
                Status = CommunityChatStatus.ProtocolError, ErrorCode = CommunityChatErrorCodes.ProtocolError
            };

        if (result.IsSuccess)
        {
            MergeMessages(result.Messages);

            _consecutiveFailures = 0;
            _lastFailureRateLimited = false;
            _lastRetryAfterSeconds = null;
            IsStale = false;
            HasLoadedSuccessfully = true;
            IsInitialLoading = false;

            // Udany odczyt czyści wyłącznie bannery awarii odświeżania.
            // Komunikaty operacyjne (zgłoszenie, ostrzeżenie moderacji) zostają.
            if (_bannerFromRefresh)
                SetBanner(null, null, CommunityChatBannerSeverity.Info, fromRefresh: false);
        }
        else
        {
            // Awaria: historia i szkic nietknięte. Oznacz dane jako nieaktualne.
            _consecutiveFailures++;
            _lastFailureRateLimited = result.Status == CommunityChatStatus.RateLimited;
            _lastRetryAfterSeconds = result.RetryAfterSeconds;
            NoteServerReadHold(result.Status, result.RetryAfterSeconds);
            IsStale = Messages.Count > 0;

            var code = result.ErrorCode ?? MapStatusToCode(result.Status);
            object?[]? args = result.Status == CommunityChatStatus.RateLimited && result.RetryAfterSeconds is int secs
                ? new object?[] { secs }
                : null;
            SetBanner(code, args, CommunityChatBannerSeverity.Error, fromRefresh: true);
            IsInitialLoading = false;
        }

        RaiseViewStates();
    }

    /// <summary>
    /// Rejestruje serwerowe okno Retry-After. Nie wymyślamy własnych limitów —
    /// tylko respektujemy to, co podał serwer (wydłużenie, nigdy skrócenie).
    /// </summary>
    private void NoteServerReadHold(CommunityChatStatus status, int? retryAfterSeconds)
    {
        if (status != CommunityChatStatus.RateLimited || retryAfterSeconds is not int secs || secs <= 0)
            return;

        var hold = DateTimeOffset.UtcNow.AddSeconds(secs);
        if (hold > _nextAllowedReadUtc)
            _nextAllowedReadUtc = hold;
    }

    /// <summary>
    /// Scalanie po ID z zachowaniem instancji (zaznaczony tekst nie znika)
    /// i kolejności chronologicznej. Usunięte/wygasłe wiadomości znikają z listy.
    /// </summary>
    private void MergeMessages(IReadOnlyList<CommunityChatMessage> snapshot)
    {
        var target = new List<CommunityChatMessageViewModel>(snapshot.Count);
        var newCount = 0;

        foreach (var message in snapshot)
        {
            if (!message.IsComplete)
                continue;
            if (_itemsById.TryGetValue(message.Id, out var existing))
            {
                existing.UpdateFrom(message);
                target.Add(existing);
            }
            else
            {
                var created = new CommunityChatMessageViewModel(message, _loc);
                _itemsById[created.Id] = created;
                target.Add(created);
                newCount++;
            }
        }

        // Wyrównanie kolejności kolekcji do snapshotu — minimalne Move/Insert dla stabilnego przewijania.
        for (var i = 0; i < target.Count; i++)
        {
            if (i < Messages.Count && ReferenceEquals(Messages[i], target[i]))
                continue;

            var index = Messages.IndexOf(target[i]);
            if (index >= 0)
                Messages.Move(index, i);
            else
                Messages.Insert(i, target[i]);
        }

        for (var i = Messages.Count - 1; i >= target.Count; i--)
            Messages.RemoveAt(i);

        _itemsById.Clear();
        foreach (var item in Messages)
            _itemsById[item.Id] = item;

        if (newCount > 0)
        {
            if (UserAtBottom)
                ScrollToBottomRequested?.Invoke();
            else
                NewMessageCount += newCount;
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Cofnięcie do ostatniej wiadomości ręcznie (np. usunięcie pozycji końcowej).
        RaiseViewStates();
    }

    // ══════════════════════════════════════════════════════
    // Wysyłanie
    // ══════════════════════════════════════════════════════

    private async Task SendAsync()
    {
        if (_sendInFlight || _disposed)
            return;

        var content = Draft;
        if (string.IsNullOrWhiteSpace(content))
            return;

        var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);
        if (!isValid)
        {
            // Jawna walidacja — szkic pozostaje, użytkownik poprawia treść.
            SetBanner(errorCode ?? CommunityChatErrorCodes.ContentTooShort,
                      BuildValidationErrorArgs(errorCode),
                      CommunityChatBannerSeverity.Error,
                      fromRefresh: false);
            return;
        }

        var ct = ActiveToken;
        var submittedRevision = _draftRevision;
        _sendInFlight = true;
        IsSending = true;
        try
        {
            var result = await _service.SendMessageAsync(content, ct);

            // Pauza/dispose w trakcie POST: nie kasujemy szkicu, nie mutujemy UI,
            // nie odświeżamy. Prawda o losie wiadomości spadnie na odczyt po wznowieniu.
            if (_disposed || ct.IsCancellationRequested)
                return;

            if (result.IsSuccess && result.Message?.IsComplete != true)
                result = new CommunityChatSendResult
                {
                    Status = CommunityChatStatus.ProtocolError, ErrorCode = CommunityChatErrorCodes.ProtocolError
                };

            if (result.IsSuccess)
            {
                // Czyść tylko niezmienioną wersję szkicu — edycja w trakcie POST zostaje.
                if (_draftRevision == submittedRevision)
                    Draft = string.Empty;

                if (result.Message is { IsComplete: true } sent)
                {
                    // Optymistyczne wstawienie po ID — merge z serwera nie duplikuje.
                    if (!_itemsById.TryGetValue(sent.Id, out var item))
                    {
                        item = new CommunityChatMessageViewModel(sent, _loc);
                        _itemsById[item.Id] = item;
                        Messages.Add(item);
                    }
                    else
                    {
                        item.UpdateFrom(sent);
                    }

                    ScrollToBottomRequested?.Invoke();
                }

                if (result.ModerationWarning)
                {
                    SetBanner(CommunityChatErrorCodes.ContentBlocked, null,
                        CommunityChatBannerSeverity.Warning, fromRefresh: false);
                }

                // Natychmiastowa, pojedyncza tura odświeżania po publikacji
                // (ten sam token — wejście odczytu stłumi je po Pause/Dispose).
                await RefreshOnceAsync(ct);
            }
            else
            {
                // Błąd wysłania — szkic zachowany, bez automatycznego ponawiania.
                var uncertain = result.Status is CommunityChatStatus.NetworkError or CommunityChatStatus.ProtocolError;
                var code = uncertain ? "CHAT_SEND_UNCERTAIN" : result.ErrorCode ?? MapStatusToCode(result.Status);
                object?[]? args = result.Status == CommunityChatStatus.RateLimited && result.RetryAfterSeconds is int secs
                    ? new object?[] { secs }
                    : null;
                SetBanner(code, args, CommunityChatBannerSeverity.Error, fromRefresh: false);
                NoteServerReadHold(result.Status, result.RetryAfterSeconds);
                if (uncertain)
                    await RefreshOnceAsync(ct);
            }
        }
        finally
        {
            IsSending = false;
            _sendInFlight = false;
        }
    }

    // ══════════════════════════════════════════════════════
    // Moderacja: zgłoszenie i usunięcie własnej
    // ══════════════════════════════════════════════════════

    private async Task ReportAsync(CommunityChatMessageViewModel? item)
    {
        if (item is null || _disposed)
            return;

        var ct = ActiveToken;
        var result = await _service.ReportMessageAsync(item.Id, reason: null, ct);

        if (_disposed || ct.IsCancellationRequested)
            return;

        if (result.IsSuccess)
        {
            // Zgłoszenie nie jest obietnicą usunięcia — tekst to komunikuje.
            SetBanner("REPORT_DONE", null, CommunityChatBannerSeverity.Info, fromRefresh: false);
        }
        else
        {
            SetBanner(result.ErrorCode ?? MapStatusToCode(result.Status), null,
                CommunityChatBannerSeverity.Error, fromRefresh: false);
            NoteServerReadHold(result.Status, result.RetryAfterSeconds);
        }
    }

    private async Task DeleteAsync(CommunityChatMessageViewModel? item)
    {
        if (item is null || _disposed || _deleteInFlight)
            return;

        if (!item.CanDelete || !item.IsOwn)
        {
            SetBanner(CommunityChatErrorCodes.Forbidden, null,
                CommunityChatBannerSeverity.Error, fromRefresh: false);
            return;
        }

        var confirmation = DeleteConfirmation;
        if (confirmation is null)
            return; // Bez dialogu nie usuwamy — bezpieczny domyślny.

        var confirmed = await confirmation(_loc.Get("UI.CommunityChat.Delete.Confirm"));
        if (!confirmed || _disposed)
            return;

        var ct = ActiveToken;
        if (ct.IsCancellationRequested)
            return; // spauzowane w trakcie dialogu — nie wykonujemy zapisu

        _deleteInFlight = true;
        IsDeleting = true;
        try
        {
            var result = await _service.DeleteMessageAsync(item.Id, ct);

            if (_disposed || ct.IsCancellationRequested)
                return;

            if (result.IsSuccess)
            {
                RemoveLocal(item.Id);
                await RefreshOnceAsync(ct);
            }
            else
            {
                SetBanner(result.ErrorCode ?? MapStatusToCode(result.Status), null,
                    CommunityChatBannerSeverity.Error, fromRefresh: false);
                NoteServerReadHold(result.Status, result.RetryAfterSeconds);
            }
        }
        finally
        {
            IsDeleting = false;
            _deleteInFlight = false;
        }
    }

    private bool RemoveLocal(string id)
    {
        if (!_itemsById.TryGetValue(id, out var item))
            return false;

        var index = Messages.IndexOf(item);
        if (index >= 0)
            Messages.RemoveAt(index);

        _itemsById.Remove(id);
        return true;
    }

    // ══════════════════════════════════════════════════════
    // Stan bannera + lokalizacja błędów
    // ══════════════════════════════════════════════════════

    private void SetBanner(string? code, object?[]? args, CommunityChatBannerSeverity severity, bool fromRefresh)
    {
        _bannerCts?.Cancel();
        _bannerCts = null;
        _bannerOpacity = 1;
        this.RaisePropertyChanged(nameof(BannerOpacity));
        _bannerCode = code;
        _bannerArgs = args;
        _bannerSeverity = code is null ? CommunityChatBannerSeverity.Info : severity;
        _bannerFromRefresh = code is not null && fromRefresh;

        this.RaisePropertyChanged(nameof(HasBanner));
        this.RaisePropertyChanged(nameof(BannerText));
        this.RaisePropertyChanged(nameof(BannerIsError));
        this.RaisePropertyChanged(nameof(BannerIsWarning));
        this.RaisePropertyChanged(nameof(BannerIsInfo));

        if (code is not null && !_disposed && !_paused)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ActiveToken);
            _bannerCts = cts;
            _ = DismissBannerAsync(cts);
        }
    }

    private async Task DismissBannerAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_bannerDisplayDuration, cts.Token);
            if (cts.IsCancellationRequested || _disposed || _paused || _bannerCts != cts) return;
            _bannerOpacity = 0;
            this.RaisePropertyChanged(nameof(BannerOpacity));
            // Keep the banner in layout until the view's opacity transition finishes.
            await Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token);
            if (cts.IsCancellationRequested || _disposed || _paused || _bannerCts != cts) return;
            SetBanner(null, null, CommunityChatBannerSeverity.Info, fromRefresh: false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_bannerCts == cts) _bannerCts = null;
            cts.Dispose();
        }
    }

    private void RaiseViewStates()
    {
        this.RaisePropertyChanged(nameof(ShowEmptyState));
        this.RaisePropertyChanged(nameof(ShowDisconnectedState));
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ILocalizationService.CurrentCulture) or null))
            return;

        // Etykiety i względny czas — historia i szkic zostają nietknięte.
        this.RaisePropertyChanged(nameof(BannerText));
        this.RaisePropertyChanged(nameof(NewMessagesText));
        foreach (var item in Messages)
            item.RefreshLocalizedDisplay();
    }

    private static object?[]? BuildValidationErrorArgs(string? errorCode) => errorCode switch
    {
        CommunityChatErrorCodes.ContentTooShort => new object?[] { CommunityChatLimits.MinMessageLength },
        CommunityChatErrorCodes.ContentTooLong => new object?[] { CommunityChatLimits.MaxMessageLength },
        _ => null
    };

    /// <summary>Mapuje stabilny kod błędu na zlokalizowany komunikat. Bez surowych tekstów serwera.</summary>
    private string LocalizeError(string code, object?[]? args)
    {
        var key = code switch
        {
            CommunityChatErrorCodes.ContentTooShort => "UI.CommunityChat.Error.TooShort",
            CommunityChatErrorCodes.ContentTooLong => "UI.CommunityChat.Error.TooLong",
            CommunityChatErrorCodes.ContentContainsNewline => "UI.CommunityChat.Error.Newline",
            CommunityChatErrorCodes.ContentContainsControlCharacter => "UI.CommunityChat.Error.ControlCharacters",
            CommunityChatErrorCodes.DisallowedUrl => "UI.CommunityChat.Error.DisallowedUrl",
            CommunityChatErrorCodes.TooManyLinks => "UI.CommunityChat.Error.TooManyLinks",
            CommunityChatErrorCodes.Unauthorized or "UNAUTHORIZED" => "UI.CommunityChat.Error.Auth",
            CommunityChatErrorCodes.Forbidden or "FORBIDDEN" or "NOT_OWNER" => "UI.CommunityChat.Error.Forbidden",
            CommunityChatErrorCodes.RateLimited or "RATE_LIMITED" => args is not null
                ? "UI.CommunityChat.Error.RateLimitedSeconds"
                : "UI.CommunityChat.Error.RateLimited",
            CommunityChatErrorCodes.ServerError or "CHAT_UNAVAILABLE" or "INTERNAL_ERROR" => "UI.CommunityChat.Error.Server",
            CommunityChatErrorCodes.NetworkError => "UI.CommunityChat.Error.Network",
            CommunityChatErrorCodes.ProtocolError or "CHAT_TYPE_NOT_SUPPORTED" => "UI.CommunityChat.Error.Protocol",
            "CHAT_SEND_UNCERTAIN" => "UI.CommunityChat.Error.SendUncertain",
            "CHAT_AUTHOR_UNAVAILABLE" => "UI.CommunityChat.Error.AuthorUnavailable",
            CommunityChatErrorCodes.NotFound => "UI.CommunityChat.Error.NotFound",
            CommunityChatErrorCodes.RequestFailed or "VALIDATION_ERROR" => "UI.CommunityChat.Error.Rejected",
            "USER_BANNED" => "UI.CommunityChat.Error.Banned",
            "CONTENT_BLOCKED" => "UI.CommunityChat.Error.Blocked",
            "DUPLICATE_MESSAGE" => "UI.CommunityChat.Error.Duplicate",
            "DAILY_LIMIT_REACHED" => "UI.CommunityChat.Error.DailyLimit",
            "REPORT_DONE" => "UI.CommunityChat.Report.Done",
            _ => "UI.CommunityChat.Error.Unknown"
        };

        return args is { Length: > 0 }
            ? _loc.GetFormatted(key, args!)
            : _loc.Get(key);
    }

    private static string MapStatusToCode(CommunityChatStatus status) => status switch
    {
        CommunityChatStatus.Unauthorized => CommunityChatErrorCodes.Unauthorized,
        CommunityChatStatus.Forbidden => CommunityChatErrorCodes.Forbidden,
        CommunityChatStatus.RateLimited => CommunityChatErrorCodes.RateLimited,
        CommunityChatStatus.ServerError => CommunityChatErrorCodes.ServerError,
        CommunityChatStatus.NetworkError => CommunityChatErrorCodes.NetworkError,
        CommunityChatStatus.ProtocolError => CommunityChatErrorCodes.ProtocolError,
        CommunityChatStatus.NotFound => CommunityChatErrorCodes.NotFound,
        CommunityChatStatus.RequestFailed => CommunityChatErrorCodes.RequestFailed,
        CommunityChatStatus.Cancelled => CommunityChatErrorCodes.Cancelled,
        _ => CommunityChatErrorCodes.UnknownError
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CommunityChatPanelViewModel));
    }

    /// <summary>Klasyfikacja wizualna bannera stanu.</summary>
    private enum CommunityChatBannerSeverity
    {
        Info,
        Warning,
        Error
    }
}
