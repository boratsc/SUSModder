using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Lobby;
using SUSModder.Core.Models;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Validators;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// ViewModel dla panelu Lobby Board (kody + ogłoszenia).
    /// </summary>
    public class LobbyBoardPanelViewModel : ReactiveObject, IDisposable
    {
        private readonly ILobbyBoardService _lobbyService;
        private readonly ILocalizationService _loc;
        private readonly ModConfiguration _mod;
        internal readonly CompositeDisposable Disposables = new();

        // ══════════════════════════════════════════════════════
        // Kolekcje
        // ══════════════════════════════════════════════════════
        public ObservableCollection<LobbyBoardItemViewModel> ActiveCodes { get; } = new();
        public ObservableCollection<LobbyBoardItemViewModel> ActiveMessages { get; } = new();

        // ══════════════════════════════════════════════════════
        // Inputy (Reactive)
        // ══════════════════════════════════════════════════════
        private string _codeInput = "";
        public string CodeInput
        {
            get => _codeInput;
            set => this.RaiseAndSetIfChanged(ref _codeInput, value?.ToUpperInvariant() ?? "");
        }

        private string _messageInput = "";
        public string MessageInput
        {
            get => _messageInput;
            set => this.RaiseAndSetIfChanged(ref _messageInput, value ?? "");
        }

        private string _selectedRegion = "Modded EU";
        public string SelectedRegion
        {
            get => _selectedRegion;
            set => this.RaiseAndSetIfChanged(ref _selectedRegion, value);
        }

        private int _currentPlayers;
        public int CurrentPlayers
        {
            get => _currentPlayers;
            set => this.RaiseAndSetIfChanged(ref _currentPlayers, value);
        }

        private int _maxPlayers = 15;
        public int MaxPlayers
        {
            get => _maxPlayers;
            set => this.RaiseAndSetIfChanged(ref _maxPlayers, value);
        }

        private int _selectedTab;
        public int SelectedTab
        {
            get => _selectedTab;
            set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
        }

        // ══════════════════════════════════════════════════════
        // Stan
        // ══════════════════════════════════════════════════════
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        private string? _statusMessage;
        public string? StatusMessage
        {
            get => _statusMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref _statusMessage, value);
                HasStatusMessage = !string.IsNullOrWhiteSpace(value);
            }
        }

        private bool _isStatusError;
        public bool IsStatusError
        {
            get => _isStatusError;
            set => this.RaiseAndSetIfChanged(ref _isStatusError, value);
        }

        // ══════════════════════════════════════════════════════
        // Computed
        // ══════════════════════════════════════════════════════
        private readonly ObservableAsPropertyHelper<int> _messageCharCount;
        public int MessageCharCount => _messageCharCount.Value;

        private readonly ObservableAsPropertyHelper<bool> _isCodesTabVisible;
        public bool IsCodesTabVisible => _isCodesTabVisible.Value;

        private readonly ObservableAsPropertyHelper<bool> _isMessagesTabVisible;
        public bool IsMessagesTabVisible => _isMessagesTabVisible.Value;

        private bool _hasActiveCodes;
        public bool HasActiveCodes
        {
            get => _hasActiveCodes;
            set => this.RaiseAndSetIfChanged(ref _hasActiveCodes, value);
        }

        private bool _hasActiveMessages;
        public bool HasActiveMessages
        {
            get => _hasActiveMessages;
            set => this.RaiseAndSetIfChanged(ref _hasActiveMessages, value);
        }

        private bool _hasStatusMessage;
        public bool HasStatusMessage
        {
            get => _hasStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _hasStatusMessage, value);
        }

        private string _tickerText = "";
        public string TickerText
        {
            get => _tickerText;
            set => this.RaiseAndSetIfChanged(ref _tickerText, value);
        }

        public string? CodeTabHeader => string.Format(
            _loc.Get("Lobby.Panel.TabCodes") ?? "Kody ({0})",
            ActiveCodes.Count);

        public string? MessageTabHeader => string.Format(
            _loc.Get("Lobby.Panel.TabMessages") ?? "Ogłoszenia ({0})",
            ActiveMessages.Count);

        // ══════════════════════════════════════════════════════
        // Regiony
        // ══════════════════════════════════════════════════════
        public static IReadOnlyList<string> AvailableRegions { get; } = new[]
        {
            "Modded EU", "Modded NA", "Modded Asia"
        };

        // ══════════════════════════════════════════════════════
        // Komendy
        // ══════════════════════════════════════════════════════
        public ReactiveCommand<Unit, Unit> PublishCodeCommand { get; }
        public ReactiveCommand<Unit, Unit> PublishMessageCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectCodesTabCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectMessagesTabCommand { get; }
        public ReactiveCommand<LobbyBoardItemViewModel, Unit> ReportCommand { get; }
        public ReactiveCommand<LobbyBoardItemViewModel, Unit> DeleteOwnCommand { get; }
        public ReactiveCommand<LobbyBoardItemViewModel, Unit> CopyCodeCommand { get; }

        public LobbyBoardPanelViewModel(
            ILobbyBoardService lobbyService,
            ILocalizationService loc,
            ModConfiguration mod,
            LobbyBridgeFileReader? bridgeReader = null)
        {
            _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _mod = mod ?? throw new ArgumentNullException(nameof(mod));

            // Subskrypcja na auto-detekcję kodu z DLL bridge
            if (bridgeReader != null)
            {
                bridgeReader.LobbyCodeDetected += OnLobbyCodeDetected;
                Disposable.Create(() => bridgeReader.LobbyCodeDetected -= OnLobbyCodeDetected)
                    .DisposeWith(Disposables);
            }

            // Computed: MessageCharCount
            _messageCharCount = this
                .WhenAnyValue(x => x.MessageInput)
                .Select(input => 280 - (input?.Length ?? 0))
                .ToProperty(this, x => x.MessageCharCount)
                .DisposeWith(Disposables);

            // Computed: tab visibility
            _isCodesTabVisible = this
                .WhenAnyValue(x => x.SelectedTab)
                .Select(tab => tab == 0)
                .ToProperty(this, x => x.IsCodesTabVisible)
                .DisposeWith(Disposables);

            _isMessagesTabVisible = this
                .WhenAnyValue(x => x.SelectedTab)
                .Select(tab => tab == 1)
                .ToProperty(this, x => x.IsMessagesTabVisible)
                .DisposeWith(Disposables);

            // Komendy
            PublishCodeCommand = ReactiveCommand.CreateFromTask(PublishCodeAsync);
            PublishMessageCommand = ReactiveCommand.CreateFromTask(PublishMessageAsync);
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
            SelectCodesTabCommand = ReactiveCommand.Create(() => { SelectedTab = 0; });
            SelectMessagesTabCommand = ReactiveCommand.Create(() => { SelectedTab = 1; });
            ReportCommand = ReactiveCommand.CreateFromTask<LobbyBoardItemViewModel>(ReportAsync);
            DeleteOwnCommand = ReactiveCommand.CreateFromTask<LobbyBoardItemViewModel>(DeleteOwnAsync);
            CopyCodeCommand = ReactiveCommand.Create<LobbyBoardItemViewModel>(item => { /* clipboard handled in view */ });

            // Auto-refresh co 30s
            Observable.Interval(TimeSpan.FromSeconds(30), RxApp.MainThreadScheduler)
                .Select(_ => Unit.Default)
                .InvokeCommand(RefreshCommand)
                .DisposeWith(Disposables);

            // Initial load
            RxApp.MainThreadScheduler.Schedule(async () => await RefreshAsync());
        }

        // ══════════════════════════════════════════════════════
        // Handlery
        // ══════════════════════════════════════════════════════

        private async Task PublishCodeAsync()
        {
            var (valid, errorCode) = LobbyEntryValidator.ValidateCode(CodeInput);
            if (!valid)
            {
                ShowError(MapErrorToLocalized(errorCode!));
                return;
            }

            IsLoading = true;
            StatusMessage = null;

            var result = await _lobbyService.PublishCodeAsync(
                CodeInput, _mod.Id, SelectedRegion,
                MaxPlayers, CurrentPlayers > 0 ? CurrentPlayers : null);

            if (result.Success)
            {
                CodeInput = "";
                StatusMessage = _loc.Get("Lobby.Code.PublishSuccess") ?? "Kod udostępniony!";
                IsStatusError = false;
                await RefreshAsync();
            }
            else
            {
                ShowError(MapErrorToLocalized(result.ErrorCode ?? "UNKNOWN_ERROR"));
            }

            IsLoading = false;
        }

        private async Task PublishMessageAsync()
        {
            var (valid, errorCode) = LobbyEntryValidator.ValidateMessage(MessageInput);
            if (!valid)
            {
                ShowError(MapErrorToLocalized(errorCode!));
                return;
            }

            IsLoading = true;
            StatusMessage = null;

            var result = await _lobbyService.PublishMessageAsync(MessageInput, _mod.Id);

            if (result.Success)
            {
                MessageInput = "";
                StatusMessage = _loc.Get("Lobby.Message.PublishSuccess") ?? "Ogłoszenie opublikowane!";
                IsStatusError = false;
                if (result.ModerationWarning)
                {
                    StatusMessage += " " + (_loc.Get("Lobby.Message.ModerationWarning") ?? "");
                    IsStatusError = true;
                }
                await RefreshAsync();
            }
            else
            {
                ShowError(MapErrorToLocalized(result.ErrorCode ?? "UNKNOWN_ERROR"));
            }

            IsLoading = false;
        }

        private async Task RefreshAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = null;

                var entries = await _lobbyService.GetEntriesAsync(modId: _mod.Id);

                ActiveCodes.Clear();
                ActiveMessages.Clear();

                foreach (var entry in entries)
                {
                    var vm = new LobbyBoardItemViewModel(entry, null);
                    if (entry.Type == LobbyEntryType.Code)
                        ActiveCodes.Add(vm);
                    else if (entry.Type == LobbyEntryType.Message)
                        ActiveMessages.Add(vm);
                }

                HasActiveCodes = ActiveCodes.Count > 0;
                HasActiveMessages = ActiveMessages.Count > 0;
                this.RaisePropertyChanged(nameof(CodeTabHeader));
                this.RaisePropertyChanged(nameof(MessageTabHeader));

                // Aktualizuj ticker (ostatnie 3 kody)
                var recent = ActiveCodes.Take(3);
                TickerText = string.Join(" | ",
                    recent.Select(c => $"{(c.CurrentPlayers.HasValue ? "🟢" : "⚪")} {c.Code} ({c.PlayerCountDisplay})"));

                if (entries.Count == 0)
                {
                    StatusMessage = _loc.Get(SelectedTab == 0
                        ? "Lobby.Panel.NoCodes"
                        : "Lobby.Panel.NoMessages");
                    IsStatusError = false;
                }
                else
                {
                    StatusMessage = null;
                }
            }
            catch (Exception)
            {
                StatusMessage = _loc.Get("Lobby.Panel.ServiceUnavailable") ?? "Usługa niedostępna";
                IsStatusError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ReportAsync(LobbyBoardItemViewModel item)
        {
            // reason będzie ustawiony z UI dropdown
            // Na razie domyślnie "spam"
            await _lobbyService.ReportEntryAsync(item.Id, "spam");
            StatusMessage = _loc.Get("Lobby.Report.Done") ?? "Zgłoszono";
            IsStatusError = false;
        }

        private async Task DeleteOwnAsync(LobbyBoardItemViewModel item)
        {
            var ok = await _lobbyService.DeleteOwnEntryAsync(item.Id);
            if (ok)
            {
                ActiveCodes.Remove(item);
                ActiveMessages.Remove(item);
                this.RaisePropertyChanged(nameof(CodeTabHeader));
                this.RaisePropertyChanged(nameof(MessageTabHeader));
            }
        }

        // ══════════════════════════════════════════════════════
        // Auto-detekcja kodu z DLL bridge
        // ══════════════════════════════════════════════════════

        private void OnLobbyCodeDetected(object? sender, LobbyCodeDetectedEventArgs e)
        {
            // Sprawdź czy kod dotyczy tego moda
            if (e.ModId != _mod.Id)
                return;

            // Auto-fill formularza na głównym wątku UI
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                CodeInput = e.Code;
                SelectedRegion = e.Region;
                MaxPlayers = e.MaxPlayers;
                CurrentPlayers = 0; // Nie znamy liczby graczy z bridge

                // Przełącz na zakładkę kody
                SelectedTab = 0;
            });
        }

        // ══════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════

        private void ShowError(string message)
        {
            StatusMessage = message;
            IsStatusError = true;
        }

        private string MapErrorToLocalized(string errorCode) => errorCode switch
        {
            "INVALID_LOBBY_CODE" => _loc.Get("Lobby.Code.InvalidFormat") ?? "Nieprawidłowy format kodu",
            "CONTENT_TOO_SHORT" => _loc.Get("Lobby.Message.TooShort") ?? "Wiadomość za krótka",
            "CONTENT_TOO_LONG" => _loc.Get("Lobby.Message.TooLong") ?? "Wiadomość za długa",
            "RATE_LIMITED" => _loc.Get("Lobby.Code.RateLimited") ?? "Poczekaj przed kolejnym udostępnieniem",
            "DAILY_LIMIT_REACHED" => _loc.Get("Lobby.Message.DailyLimitReached") ?? "Osiągnięto dzienny limit",
            "DUPLICATE_MESSAGE" => _loc.Get("Lobby.Message.Duplicate") ?? "Identyczna treść już istnieje",
            "DISALLOWED_URL" => _loc.Get("Lobby.Message.DisallowedUrl") ?? "Dozwolone tylko discord.gg",
            "TOO_MANY_LINKS" => _loc.Get("Lobby.Message.TooManyLinks") ?? "Maksymalnie 1 link Discord",
            "CONTENT_BLOCKED" => _loc.Get("Lobby.Message.ContentBlocked") ?? "Treść nie przeszła moderacji",
            "USER_BANNED" => _loc.Get("Lobby.Message.UserBanned") ?? "Konto tymczasowo zablokowane",
            _ => errorCode
        };

        public void Dispose()
        {
            Disposables.Dispose();
        }
    }
}
