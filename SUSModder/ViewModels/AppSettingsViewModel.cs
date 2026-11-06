using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using System;
using System.IO;
using SUSModder.Core.Utilities;
using SUSModder.Core.Repositories;
using SUSModder.Core.Services;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System.Collections.Generic;
using System.Linq;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services.Localization;
using SUSModder.Views;
using SUSModder.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;

namespace SUSModder.ViewModels
{
    public class AppSettingsViewModel : ViewModelBase
    {
        private readonly Control _control;
        private readonly ILocalizationService? _localizationService;
        private readonly UserSettingsService _userSettingsService;
        private string _modsInstallPath = string.Empty;
        private bool _developerMode = false;
        private string _updateChannel = "release";
        private string _gameMode = "steam";
        private LanguageOption? _selectedLanguage;
        private string _originalModsInstallPath = string.Empty;
        private bool _originalDeveloperMode = false;
        private string _originalUpdateChannel = "release";
        private string _originalGameMode = "steam";
        private string _originalLanguage = "pl";
        private bool _hasUnsavedChanges = false;
        private bool _telemetryEnabled = true;
        private bool _originalTelemetryEnabled = true;

        // Ustawienia system tray (domyślnie włączone od v2.4.0)
        private bool _minimizeToTray = true;
        private bool _originalMinimizeToTray = true;
        private bool _glassReduceTransparency;
        private bool _originalGlassReduceTransparency;
        private bool _showQuickLaunchInTray = true;
        private bool _originalShowQuickLaunchInTray = true;
        private bool _preferDepotDownloader;
        private bool _originalPreferDepotDownloader;

        // Stała lista dostępnych języków (aby uniknąć problemów z referencjami)
        private static readonly List<LanguageOption> _availableLanguages = new()
        {
            new LanguageOption { Code = "pl", DisplayName = "Polski" },
            new LanguageOption { Code = "en", DisplayName = "English" }
        };

        // Lista dostępnych kanałów aktualizacji (nazwy będą pobierane z lokalizacji)
        private List<UpdateChannelOption> _availableUpdateChannels = null!;

        // Dodaj event dla powiadomienia o zapisaniu
        public event Action? SettingsSaved;

        // Dodaj event dla powiadomienia o zmianie trybu gry
        public static event Action? GameModeChanged;
        
        // Dodaj event dla powiadomienia o zmianie kanału aktualizacji
        public event Action<string>? UpdateChannelChanged;

        public AppSettingsViewModel(Control control)
        {
            _control = control;
            _userSettingsService = new UserSettingsService();

            // Pobierz serwis lokalizacji
            _localizationService = App.GetService<ILocalizationService>();

            _availableUpdateChannels = BuildUpdateChannelOptions();

            // Załaduj obecne ustawienia
            LoadCurrentSettings();

            // Załaduj kanał aktualizacji
            var userSettings = _userSettingsService.LoadUserSettings();
            _updateChannel = userSettings.UpdateChannel;
            _originalUpdateChannel = _updateChannel;
            _selectedUpdateChannel = AvailableUpdateChannels.FirstOrDefault(c => c.Code == _updateChannel);
            this.RaisePropertyChanged(nameof(SelectedUpdateChannel));
            this.RaisePropertyChanged(nameof(AvailableUpdateChannels));

            // Kontynuuj inicjalizację z serwisem lokalizacji
            try
            {
                var currentCulture = _localizationService?.CurrentCulture ?? "pl";

                Console.WriteLine($"[AppSettingsViewModel] CurrentCulture z serwisu: {currentCulture}");
                Console.WriteLine($"[AppSettingsViewModel] Dostępne języki: {string.Join(", ", AvailableLanguages.Select(l => $"{l.DisplayName}({l.Code})"))}");

                _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == currentCulture);

                if (_selectedLanguage == null)
                {
                    Console.WriteLine($"[AppSettingsViewModel] Nie znaleziono języka dla kodu: {currentCulture}, ustawiam domyślny");
                    _selectedLanguage = AvailableLanguages[0];
                }

                _originalLanguage = currentCulture;

                Console.WriteLine($"[AppSettingsViewModel] Ustawiono SelectedLanguage: {_selectedLanguage?.DisplayName} ({_selectedLanguage?.Code})");
                Console.WriteLine($"[AppSettingsViewModel] SelectedLanguage == null? {_selectedLanguage == null}");

                // Ważne: powiadom UI o ustawieniu początkowej wartości
                this.RaisePropertyChanged(nameof(SelectedLanguage));
                this.RaisePropertyChanged(nameof(AvailableLanguages));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppSettingsViewModel] BŁĄD przy inicjalizacji języka: {ex.Message}");
                Console.WriteLine($"[AppSettingsViewModel] Stack trace: {ex.StackTrace}");
                _selectedLanguage = AvailableLanguages[0];
                this.RaisePropertyChanged(nameof(SelectedLanguage));
                this.RaisePropertyChanged(nameof(AvailableLanguages));
            }

            // Wczytaj ustawienia telemetrii
            LoadTelemetrySettings();

            // Komendy
            BrowseFolderCommand = ReactiveCommand.CreateFromTask(BrowseFolder);
            SaveCommand = ReactiveCommand.CreateFromTask(SaveSettings);
            CancelCommand = ReactiveCommand.Create(Cancel);
            ResetToDefaultCommand = ReactiveCommand.Create(ResetToDefault);
            FactoryResetCommand = ReactiveCommand.CreateFromTask(FactoryResetAsync);

            // Selektor wersji legendary.exe (tylko Epic) — interakcja odblokowana dopiero po udanym wczytaniu stanu
            ApplyLegendaryVersionCommand = ReactiveCommand.CreateFromTask(
                ApplyLegendaryVersionAsync,
                this.WhenAnyValue(
                    x => x.IsLegendarySwitching,
                    x => x.SelectedLegendaryRelease,
                    x => x.LegendaryModeManual,
                    x => x.IsLegendarySectionReady,
                    (busy, selected, manual, ready) => ready && !busy && selected != null && manual));
            ReloadLegendaryReleasesCommand = ReactiveCommand.CreateFromTask(ReloadLegendaryReleasesAsync);
            RetryLegendaryStateCommand = ReactiveCommand.CreateFromTask(RetryLegendaryStateAsync);
            _ = InitializeLegendarySectionAsync();
        }

        public string ModsInstallPath
        {
            get => _modsInstallPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _modsInstallPath, value);
                CheckForChanges();
            }
        }

        public bool DeveloperMode
        {
            get => _developerMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _developerMode, value);
                CheckForChanges();
            }
        }

        public string UpdateChannel
        {
            get => _updateChannel;
            set
            {
                this.RaiseAndSetIfChanged(ref _updateChannel, value);
                this.RaisePropertyChanged(nameof(CurrentUpdateChannelDisplayName));
                this.RaisePropertyChanged(nameof(CurrentUpdateChannelSummary));
                CheckForChanges();
            }
        }

        public List<UpdateChannelOption> AvailableUpdateChannels
        {
            get => _availableUpdateChannels;
            set => this.RaiseAndSetIfChanged(ref _availableUpdateChannels, value);
        }

        private UpdateChannelOption? _selectedUpdateChannel;

        public UpdateChannelOption? SelectedUpdateChannel
        {
            get => _selectedUpdateChannel;
            set
            {
                var oldValue = _selectedUpdateChannel;
                this.RaiseAndSetIfChanged(ref _selectedUpdateChannel, value);
                if (oldValue != value && value != null)
                {
                    UpdateChannel = value.Code;
                    this.RaisePropertyChanged(nameof(CurrentUpdateChannelDisplayName));
                    this.RaisePropertyChanged(nameof(CurrentUpdateChannelSummary));
                }
            }
        }

        public string CurrentUpdateChannelDisplayName =>
            AvailableUpdateChannels.FirstOrDefault(channel => channel.Code == UpdateChannel)?.DisplayName
            ?? UpdateChannel;

        public string CurrentUpdateChannelSummary => _localizationService?.GetFormatted(
            "Settings.UpdateChannel.CurrentSummary",
            CurrentUpdateChannelDisplayName) ?? $"Current update channel: {CurrentUpdateChannelDisplayName}";

        public string GameMode
        {
            get => _gameMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _gameMode, value);
                this.RaisePropertyChanged(nameof(IsSteamDepotDownloaderSectionVisible));
                this.RaisePropertyChanged(nameof(IsEpicLegendarySectionVisible));
                CheckForChanges();
            }
        }

        public bool IsSteamMode
        {
            get => _gameMode == "steam";
            set
            {
                if (value && _gameMode != "steam")
                {
                    GameMode = "steam";
                    this.RaisePropertyChanged(nameof(IsEpicMode));
                }
            }
        }

        public bool IsEpicMode
        {
            get => _gameMode == "epic";
            set
            {
                if (value && _gameMode != "epic")
                {
                    GameMode = "epic";
                    this.RaisePropertyChanged(nameof(IsSteamMode));
                }
            }
        }

        public List<LanguageOption> AvailableLanguages => _availableLanguages;

        public bool TelemetryEnabled
        {
            get => _telemetryEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _telemetryEnabled, value);
                OnTelemetryEnabledChanged(value);
                CheckForChanges();
            }
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                this.RaiseAndSetIfChanged(ref _minimizeToTray, value);
                CheckForChanges();
            }
        }

        public bool ShowQuickLaunchInTray
        {
            get => _showQuickLaunchInTray;
            set
            {
                this.RaiseAndSetIfChanged(ref _showQuickLaunchInTray, value);
                CheckForChanges();
            }
        }

        public bool GlassReduceTransparency
        {
            get => _glassReduceTransparency;
            set
            {
                this.RaiseAndSetIfChanged(ref _glassReduceTransparency, value);
                CheckForChanges();
            }
        }

        public bool PreferDepotDownloader
        {
            get => _preferDepotDownloader;
            set
            {
                this.RaiseAndSetIfChanged(ref _preferDepotDownloader, value);
                CheckForChanges();
            }
        }

        public bool IsGlassAppearanceSectionVisible => _currentTheme == "glass";

        public bool IsSteamDepotDownloaderSectionVisible => IsSteamMode;

        #region Epic: selektor wersji narzędzia legendary.exe

        private ILegendaryVersionService? _legendaryVersionService;
        private CancellationTokenSource? _legendaryLoadCts;
        private LegendaryReleaseInfo? _legendaryApplied;
        private LegendaryReleaseInfo? _legendarySelected;
        private LegendaryReleaseInfo? _legendaryPinned;
        private bool _legendaryStateLoaded;
        private bool _isLegendaryChecking;
        private bool _hasLegendaryStateError;
        private bool _legendaryModePinned = true;
        private bool _legendaryModeManual;
        private bool _isLegendaryReleasesLoading;
        private bool _hasLegendaryReleasesError;
        private string _legendaryReleasesErrorKey = string.Empty;
        private bool _isLegendarySwitching;
        private string _legendarySwitchDisplay = string.Empty;
        private string? _legendarySwitchPhaseCode;
        private LegendaryReleaseInfo? _selectedLegendaryRelease;

        public bool IsEpicLegendarySectionVisible => IsEpicMode;

        public bool IsLegendaryManualVisible => _legendaryModeManual;

        /// <summary>Cała interakcja zablokowana, aż GetStateAsync zakończy się sukcesem (protects mode/Apply from pre-load races).</summary>
        public bool IsLegendarySectionReady => _legendaryStateLoaded;

        public bool IsLegendaryChecking
        {
            get => _isLegendaryChecking;
            private set => this.RaiseAndSetIfChanged(ref _isLegendaryChecking, value);
        }

        public bool HasLegendaryStateError
        {
            get => _hasLegendaryStateError;
            private set => this.RaiseAndSetIfChanged(ref _hasLegendaryStateError, value);
        }

        // Liczone z klucza — odświeża się przy zmianie języka na żywo.
        public string LegendaryStateError => HasLegendaryStateError
            ? L("Settings.LegendaryVersion.StateError")
            : string.Empty;

        /// <summary>Tryb przypiętej wersji (domyślny). Przełączenie na niego przy aktywmanual wymaga potwierdzonego powrotu.</summary>
        public bool LegendaryModePinned
        {
            get => _legendaryModePinned;
            set
            {
                if (value && !_legendaryModePinned)
                    SetLegendaryMode(pinned: true);
            }
        }

        public bool LegendaryModeManual
        {
            get => _legendaryModeManual;
            set
            {
                if (value && !_legendaryModeManual)
                    SetLegendaryMode(pinned: false);
            }
        }

        public ObservableCollection<LegendaryReleaseInfo> LegendaryReleases { get; } = new();

        public LegendaryReleaseInfo? SelectedLegendaryRelease
        {
            get => _selectedLegendaryRelease;
            set => this.RaiseAndSetIfChanged(ref _selectedLegendaryRelease, value);
        }

        public bool IsLegendaryReleasesLoading
        {
            get => _isLegendaryReleasesLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLegendaryReleasesLoading, value);
        }

        public bool HasLegendaryReleasesError
        {
            get => _hasLegendaryReleasesError;
            private set => this.RaiseAndSetIfChanged(ref _hasLegendaryReleasesError, value);
        }

        // Napisy liczone z kluczy przy każdym odczycie — zmiana języka w ustawieniach odświeża je na żywo.
        public string LegendaryReleasesError =>
            _legendaryReleasesErrorKey.Length == 0 ? string.Empty : L(_legendaryReleasesErrorKey);

        public bool IsLegendarySwitching
        {
            get => _isLegendarySwitching;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isLegendarySwitching, value);
                this.RaisePropertyChanged(nameof(LegendarySwitchStatus));
            }
        }

        public string LegendarySwitchStatus => !_isLegendarySwitching
            ? string.Empty
            : LegendaryPhaseCodes.MapToKey(_legendarySwitchPhaseCode) is { } phaseKey
                ? L(phaseKey)
                : LF("Settings.LegendaryVersion.Switching", _legendarySwitchDisplay);

        // Liczone z kluczy przy każdym odczycie — zmiana języka w ustawieniach odświeża je na żywo.
        // Installed == null = brak pliku lub wersja nie do ustalenia (np. niezgodny SHA) — komunikat wprost, nie "Nieznana".
        public string LegendaryCurrentSummary => _legendaryApplied is null
            ? L("Settings.LegendaryVersion.NoBinary")
            : LF("Settings.LegendaryVersion.CurrentFormat", _legendaryApplied.DisplayName);

        public ReactiveCommand<Unit, Unit> ApplyLegendaryVersionCommand { get; }
        public ReactiveCommand<Unit, Unit> ReloadLegendaryReleasesCommand { get; }
        public ReactiveCommand<Unit, Unit> RetryLegendaryStateCommand { get; }

        private void SetLegendaryMode(bool pinned)
        {
            if (!_legendaryStateLoaded)
                return; // interakcja dozwolona dopiero po udanym wczytaniu stanu (P2 race)

            // Powrót do trybu pinned przy zastosowanym manual = operacja na narzędziu, wymaga potwierdzenia.
            if (pinned && _legendaryModeManual && !IsLegendarySwitching)
            {
                _ = RevertLegendaryToPinnedAsync();
                return;
            }

            _legendaryModePinned = pinned;
            _legendaryModeManual = !pinned;
            RaiseLegendaryMode();
            if (!pinned)
                _ = EnsureLegendaryReleasesLoadedAsync();
        }

        private void RaiseLegendaryMode()
        {
            this.RaisePropertyChanged(nameof(LegendaryModePinned));
            this.RaisePropertyChanged(nameof(LegendaryModeManual));
            this.RaisePropertyChanged(nameof(IsLegendaryManualVisible));
        }

        private async Task InitializeLegendarySectionAsync()
        {
            HasLegendaryStateError = false;
            IsLegendaryChecking = true;
            try
            {
                _legendaryVersionService ??= App.GetService<ILegendaryVersionService>();
                var state = await _legendaryVersionService.GetStateAsync();
                _legendaryStateLoaded = true;
                _legendaryPinned = state.Pinned;
                _legendaryModePinned = state.UsePinned;
                _legendaryModeManual = !state.UsePinned;
                // Tylko Installed reprezentuje faktyczną binarkę. Selected to intencja użytkownika —
                // nigdy nie może blokować ponownego zastosowania, gdy binarki brak albo jest niezgodna.
                _legendarySelected = state.UsePinned ? null : state.Selected;
                _legendaryApplied = state.Installed;
                RaiseLegendaryMode();
                UpdateLegendarySummary();
                this.RaisePropertyChanged(nameof(IsLegendarySectionReady));

                if (!state.UsePinned)
                    await EnsureLegendaryReleasesLoadedAsync();
            }
            catch (Exception ex)
            {
                // Brak stanu = brak zgadywania trybu — sekcja zostaje zablokowana, użytkownik widzi błąd i retry.
                Debug.WriteLine($"[LegendaryVersion] Nie udało się wczytać stanu: {ex.Message}");
                HasLegendaryStateError = true;
            }
            finally
            {
                IsLegendaryChecking = false;
            }
        }

        private async Task RetryLegendaryStateAsync() => await InitializeLegendarySectionAsync();

        private async Task EnsureLegendaryReleasesLoadedAsync()
        {
            if (_legendaryVersionService == null
                || _isLegendaryReleasesLoading
                || (LegendaryReleases.Count > 0 && !_hasLegendaryReleasesError))
                return;

            _legendaryLoadCts?.Cancel();
            _legendaryLoadCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = _legendaryLoadCts.Token;

            IsLegendaryReleasesLoading = true;
            SetLegendaryReleasesError(string.Empty);

            try
            {
                var releases = await _legendaryVersionService.GetReleasesAsync(token);

                LegendaryReleases.Clear();
                foreach (var release in releases)
                    LegendaryReleases.Add(release);

                if (releases.Count == 0)
                {
                    SetLegendaryReleasesError("Settings.LegendaryVersion.NoReleases");
                }
                else
                {
                    // Podświetl intencję użytkownika (Selected), a przy jej braku faktycznie zainstalowane wydanie
                    var current = LegendaryReleases.FirstOrDefault(r => r.SameId(_legendarySelected ?? _legendaryApplied));
                    if (current != null)
                        SelectedLegendaryRelease = current;
                }
            }
            catch (OperationCanceledException)
            {
                SetLegendaryReleasesError("Settings.LegendaryVersion.LoadError");
            }
            catch (Exception)
            {
                SetLegendaryReleasesError("Settings.LegendaryVersion.LoadError");
            }
            finally
            {
                IsLegendaryReleasesLoading = false;
            }
        }

        private async Task ReloadLegendaryReleasesAsync()
        {
            SetLegendaryReleasesError(string.Empty);
            await EnsureLegendaryReleasesLoadedAsync();
        }

        private async Task ApplyLegendaryVersionAsync()
        {
            var release = SelectedLegendaryRelease;
            var service = _legendaryVersionService;
            if (service == null || release == null || IsLegendarySwitching)
                return;
            if (release.SameId(_legendaryApplied))
                return;

            var confirmed = await ShowConfirmAsync(
                L("Dialogs.Confirm.Title"),
                LF("Settings.LegendaryVersion.ConfirmSwitch", release.DisplayName));
            if (!confirmed)
                return;

            await RunLegendarySwitchAsync(service, release, release.DisplayName);
        }

        private async Task RevertLegendaryToPinnedAsync()
        {
            var service = _legendaryVersionService;
            if (service == null)
                return;

            // Do czasu rozstrzygnięcia potwierdzenia radio wraca do Manual (stan źródła).
            RaiseLegendaryMode();

            var pinnedDisplay = _legendaryPinned?.DisplayName ?? L("VersionSelection.UnknownVersion");
            var confirmed = await ShowConfirmAsync(
                L("Dialogs.Confirm.Title"),
                LF("Settings.LegendaryVersion.ConfirmPinned", pinnedDisplay));
            if (!confirmed)
                return;

            await RunLegendarySwitchAsync(service, null, pinnedDisplay);
        }

        /// <summary>release = null → powrót do wydania przypiętego.</summary>
        private async Task RunLegendarySwitchAsync(ILegendaryVersionService service, LegendaryReleaseInfo? release, string targetDisplay)
        {
            _legendarySwitchDisplay = targetDisplay;
            _legendarySwitchPhaseCode = null;
            IsLegendarySwitching = true;
            try
            {
                var result = await service.SwitchAsync(
                    release,
                    new Progress<string>(code =>
                    {
                        _legendarySwitchPhaseCode = code;
                        this.RaisePropertyChanged(nameof(LegendarySwitchStatus));
                    }));

                if (result.Success)
                {
                    _legendaryApplied = result.Installed ?? release ?? _legendaryPinned;
                    _legendarySelected = release; // intencja = zastosowane; null przy powrocie do zalecanej
                    _legendaryModePinned = release == null;
                    _legendaryModeManual = release != null;
                    RaiseLegendaryMode();
                    UpdateLegendarySummary();
                    await ShowInfoAsync(
                        L("Dialogs.Success.Title"),
                        LF("Settings.LegendaryVersion.Success", _legendaryApplied?.DisplayName ?? targetDisplay));
                }
                else
                {
                    var key = LegendaryErrorCodes.MapErrorKey(result.ErrorCode);
                    var message = key.EndsWith("Unknown", StringComparison.Ordinal)
                        ? LF("Settings.LegendaryVersion.Errors.Unknown", result.ErrorCode ?? "unknown")
                        : L(key);
                    await ShowErrorAsync(L("Dialogs.Error.Title"), message);
                    // Radio pozostaje na poprzednim trybie (źródło niezmienione) — odśwież widok.
                    RaiseLegendaryMode();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(
                    L("Dialogs.Error.Title"),
                    LF("Settings.LegendaryVersion.Errors.Unknown", ex.Message));
                RaiseLegendaryMode();
            }
            finally
            {
                _legendarySwitchPhaseCode = null;
                IsLegendarySwitching = false;
            }
        }

        private void SetLegendaryReleasesError(string key)
        {
            _legendaryReleasesErrorKey = key;
            HasLegendaryReleasesError = key.Length > 0;
            this.RaisePropertyChanged(nameof(LegendaryReleasesError));
        }

        private void UpdateLegendarySummary() =>
            this.RaisePropertyChanged(nameof(LegendaryCurrentSummary));

        private string L(string key) => _localizationService?.Get(key) ?? key;

        private string LF(string key, params object[] args) =>
            _localizationService?.GetFormatted(key, args) ?? key;

        #endregion

        private string _currentTheme = "dark";

        public LanguageOption? SelectedLanguage
        {
            get
            {
                Console.WriteLine($"[AppSettingsViewModel] SelectedLanguage GET: {_selectedLanguage?.DisplayName} ({_selectedLanguage?.Code})");
                return _selectedLanguage;
            }
            set
            {
                Console.WriteLine($"[AppSettingsViewModel] SelectedLanguage SET: {value?.DisplayName} ({value?.Code})");
                var oldValue = _selectedLanguage;
                this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
                if (oldValue != value && value != null)
                {
                    Console.WriteLine($"[AppSettingsViewModel] Język zmieniony z {oldValue?.Code} na {value.Code}");
                    OnLanguageChanged();
                    CheckForChanges();
                }
            }
        }

        private void OnLanguageChanged()
        {
            if (_selectedLanguage == null) return;

            // Zmień język w serwisie (live switch)
            _localizationService?.ChangeCulture(_selectedLanguage.Code);

            // Zapisz wybór do user-settings.json natychmiast (live switch)
            _userSettingsService.UpdateUserSetting(settings => settings.Language = _selectedLanguage.Code);

            // Selektor legendary: wyliczane napisy mają wskoczyć w nowy język bez przeładowania ustawień
            this.RaisePropertyChanged(nameof(LegendaryCurrentSummary));
            this.RaisePropertyChanged(nameof(LegendaryReleasesError));
            this.RaisePropertyChanged(nameof(LegendarySwitchStatus));
            this.RaisePropertyChanged(nameof(LegendaryStateError));

            System.Diagnostics.Debug.WriteLine($"Język zmieniony na: {_selectedLanguage.Code}");
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
        }

        public string WindowTitle => HasUnsavedChanges ? "Ustawienia aplikacji *" : "Ustawienia aplikacji";

        private List<UpdateChannelOption> BuildUpdateChannelOptions()
        {
            return new List<UpdateChannelOption>
            {
                new UpdateChannelOption
                {
                    Code = "release",
                    DisplayName = _localizationService?.Get("Settings.UpdateChannel.Channels.Release") ?? "Stable"
                },
                new UpdateChannelOption
                {
                    Code = "beta",
                    DisplayName = _localizationService?.Get("Settings.UpdateChannel.Channels.Beta") ?? "Beta"
                }
            };
        }

        public ReactiveCommand<Unit, Unit> BrowseFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; }
        public ReactiveCommand<Unit, Unit> FactoryResetCommand { get; }

        private void LoadCurrentSettings()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();

                // Załaduj ModsInstallPath
                _modsInstallPath = string.IsNullOrEmpty(userSettings.ModsInstallPath) 
                    ? PathSettings.DefaultModsPath 
                    : userSettings.ModsInstallPath;
                _originalModsInstallPath = _modsInstallPath;

                // Załaduj DeveloperMode
                _developerMode = DeveloperModeSettings.IsEnabled;
                _originalDeveloperMode = _developerMode;

                // Załaduj ustawienia system tray
                _minimizeToTray = userSettings.MinimizeToTray;
                _originalMinimizeToTray = _minimizeToTray;
                _showQuickLaunchInTray = userSettings.ShowQuickLaunchInTray;
                _originalShowQuickLaunchInTray = _showQuickLaunchInTray;

                _currentTheme = userSettings.Theme ?? "dark";
                _glassReduceTransparency = userSettings.GlassReduceTransparency;
                _originalGlassReduceTransparency = _glassReduceTransparency;
                _preferDepotDownloader = userSettings.PreferDepotDownloader;
                _originalPreferDepotDownloader = _preferDepotDownloader;
                this.RaisePropertyChanged(nameof(IsGlassAppearanceSectionVisible));
                this.RaisePropertyChanged(nameof(GlassReduceTransparency));
                this.RaisePropertyChanged(nameof(PreferDepotDownloader));
                this.RaisePropertyChanged(nameof(IsSteamDepotDownloaderSectionVisible));

                // Załaduj GameMode z user settings
                _gameMode = userSettings.Mode;
                _originalGameMode = _gameMode;

                System.Diagnostics.Debug.WriteLine($"Loaded current settings - ModsInstallPath: {_modsInstallPath}, DeveloperMode: {_developerMode}, GameMode: {_gameMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                _modsInstallPath = PathSettings.DefaultModsPath;
                _originalModsInstallPath = _modsInstallPath;
                _developerMode = false;
                _originalDeveloperMode = false;
                _gameMode = "steam";
                _originalGameMode = "steam";
                _minimizeToTray = true;
                _originalMinimizeToTray = true;
                _showQuickLaunchInTray = true;
                _originalShowQuickLaunchInTray = true;
            }
        }

        private void LoadGameModeFromAppSettings()
        {
            // Ta metoda jest już nieużywana - GameMode jest wczytywany z UserSettings
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                _gameMode = userSettings.Mode;
                _originalGameMode = _gameMode;

                // Powiadom o zmianie właściwości radio buttonów
                this.RaisePropertyChanged(nameof(IsSteamMode));
                this.RaisePropertyChanged(nameof(IsEpicMode));

                System.Diagnostics.Debug.WriteLine($"Loaded GameMode: {_gameMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading GameMode: {ex.Message}");
                _gameMode = "steam";
                _originalGameMode = "steam";
            }
        }

        private void CheckForChanges()
        {
            HasUnsavedChanges = !string.Equals(_modsInstallPath, _originalModsInstallPath, StringComparison.OrdinalIgnoreCase) ||
                               _developerMode != _originalDeveloperMode ||
                               _updateChannel != _originalUpdateChannel ||
                               _gameMode != _originalGameMode ||
                               (_selectedLanguage?.Code ?? "pl") != _originalLanguage ||
                               _telemetryEnabled != _originalTelemetryEnabled ||
                               _minimizeToTray != _originalMinimizeToTray ||
                               _showQuickLaunchInTray != _originalShowQuickLaunchInTray ||
                               _glassReduceTransparency != _originalGlassReduceTransparency ||
                               _preferDepotDownloader != _originalPreferDepotDownloader;
            this.RaisePropertyChanged(nameof(WindowTitle));
        }

        private void LoadTelemetrySettings()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                _telemetryEnabled = userSettings.TelemetryEnabled;
                _originalTelemetryEnabled = _telemetryEnabled;

                this.RaisePropertyChanged(nameof(TelemetryEnabled));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load telemetry settings: {ex.Message}");
                _telemetryEnabled = true;
                _originalTelemetryEnabled = true;
            }
        }

        private void OnTelemetryEnabledChanged(bool enabled)
        {
            try
            {
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var appSettingsPath = Path.Combine(exeDir, "appsettings.json");

                if (File.Exists(appSettingsPath))
                {
                    var jsonContent = File.ReadAllText(appSettingsPath);
                    var jsonDocument = JsonDocument.Parse(jsonContent);
                    var root = jsonDocument.RootElement;

                    var updatedSettings = new Dictionary<string, object>();

                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Name == "Configuration")
                        {
                            var configuration = new Dictionary<string, object>();

                            foreach (var configProperty in property.Value.EnumerateObject())
                            {
                                if (configProperty.Name == "TelemetryEnabled")
                                {
                                    configuration[configProperty.Name] = enabled;
                                }
                                else
                                {
                                    configuration[configProperty.Name] = GetJsonValue(configProperty.Value);
                                }
                            }

                            updatedSettings[property.Name] = configuration;
                        }
                        else
                        {
                            updatedSettings[property.Name] = GetJsonValue(property.Value);
                        }
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = JsonSerializer.Serialize(updatedSettings, options);
                    File.WriteAllText(appSettingsPath, updatedJson);

                    System.Diagnostics.Debug.WriteLine($"Telemetry {(enabled ? "enabled" : "disabled")}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save telemetry setting: {ex.Message}");
            }
        }

        private async Task BrowseFolder()
        {
            try
            {
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow?.StorageProvider == null)
                    return;

                var options = new FolderPickerOpenOptions
                {
                    Title = "Wybierz katalog instalacji modów",
                    AllowMultiple = false
                };

                // Ustaw folder początkowy jeśli ścieżka istnieje
                if (!string.IsNullOrEmpty(ModsInstallPath) && Directory.Exists(ModsInstallPath))
                {
                    var folder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(ModsInstallPath);
                    if (folder != null)
                    {
                        options.SuggestedStartLocation = folder;
                    }
                }

                var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(options);

                if (result?.Count > 0)
                {
                    ModsInstallPath = result[0].Path.LocalPath;
                    System.Diagnostics.Debug.WriteLine($"Selected folder: {ModsInstallPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in folder picker: {ex.Message}");
                await ShowErrorAsync("Błąd podczas wybierania folderu", ex.Message);
            }
        }


        private async Task SaveSettings()
        {
            try
            {
                // Walidacja ścieżki
                if (string.IsNullOrWhiteSpace(ModsInstallPath))
                {
                    var title = _localizationService?.Get("AppSettings.ValidationError") ?? "Validation error";
                    var message = _localizationService?.Get("Settings.Paths.ModsInstallPath.Empty") ?? "Mods install path cannot be empty.";
                    await ShowErrorAsync(title, message);
                    return;
                }

                var modsPathValidation = ModsInstallPathGuard.Validate(ModsInstallPath);
                if (!modsPathValidation.IsValid)
                {
                    var title = _localizationService?.Get("AppSettings.ValidationError") ?? "Validation error";
                    var message = GetLocalizedValidationMessage(modsPathValidation.ErrorCode);
                    await ShowErrorAsync(title, message);
                    return;
                }

                ModsInstallPath = modsPathValidation.Message;

                // Sprawdź czy ścieżka jest dostępna (stwórz jeśli nie istnieje)
                try
                {
                    if (!Directory.Exists(ModsInstallPath))
                    {
                        Directory.CreateDirectory(ModsInstallPath);
                    }
                }
                catch (Exception ex)
                {
                    var title = _localizationService?.Get("AppSettings.AccessError") ?? "Access error";
                    var messageTemplate = _localizationService?.Get("AppSettings.CannotAccessDirectory") ?? "Cannot create or access directory:\n{0}\n\nError: {1}";
                    var message = string.Format(messageTemplate, ModsInstallPath, ex.Message);
                    await ShowErrorAsync(title, message);
                    return;
                }

                // Sprawdź czy tryb gry się zmienił
                bool gameModeChanged = _gameMode != _originalGameMode;
                
                // Sprawdź czy kanał aktualizacji się zmienił
                bool updateChannelChanged = _updateChannel != _originalUpdateChannel;

                // Zapisz wszystkie ustawienia do UserSettingsService
                _userSettingsService.UpdateUserSetting(settings =>
                {
                    settings.Mode = GameMode;
                    settings.ModsInstallPath = ModsInstallPath;
                    settings.TelemetryEnabled = TelemetryEnabled;
                    settings.UpdateChannel = UpdateChannel;
                    settings.MinimizeToTray = MinimizeToTray;
                    settings.ShowQuickLaunchInTray = ShowQuickLaunchInTray;
                    settings.GlassReduceTransparency = GlassReduceTransparency;
                    settings.PreferDepotDownloader = PreferDepotDownloader;
                    if (_selectedLanguage != null)
                    {
                        settings.Language = _selectedLanguage.Code;
                    }
                });

                // Zapisz DeveloperMode używając nowej klasy
                DeveloperModeSettings.SetDeveloperMode(DeveloperMode);

                _originalModsInstallPath = ModsInstallPath;
                _originalDeveloperMode = DeveloperMode;
                _originalUpdateChannel = UpdateChannel;
                _originalGameMode = GameMode;
                _originalTelemetryEnabled = TelemetryEnabled;
                _originalMinimizeToTray = MinimizeToTray;
                _originalShowQuickLaunchInTray = ShowQuickLaunchInTray;
                _originalGlassReduceTransparency = GlassReduceTransparency;
                _originalPreferDepotDownloader = PreferDepotDownloader;
                HasUnsavedChanges = false;

                // Powiadom o zapisaniu ustawień
                SettingsSaved?.Invoke();
                
                // Jeśli kanał aktualizacji się zmienił, powiadom o tym
                if (updateChannelChanged)
                {
                    UpdateChannelChanged?.Invoke(UpdateChannel);
                }

                // Jeśli tryb gry się zmienił, pokaż komunikat o restarcie i restartuj aplikację
                if (gameModeChanged)
                {
                    // Powiadom o zmianie trybu gry przed restartem
                    GameModeChanged?.Invoke();
                    
                    await ShowInfoAsync("Restart wymagany", 
                        "Zmiana trybu gry wymaga restartu aplikacji.\n\nAplikacja zostanie zrestartowana automatycznie.");

                    // Restart aplikacji
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath!,
                        UseShellExecute = true
                    });
                    Environment.Exit(0);
                }
                else
                {
                    // Jeśli zmienił się tylko tryb bez restartu, powiadom o zmianie
                    if (_gameMode != _originalGameMode)
                    {
                        GameModeChanged?.Invoke();
                    }
                    
                    await ShowInfoAsync("Sukces", "Ustawienia zostały zapisane pomyślnie.\n\nZmiany będą widoczne przy następnych operacjach.");
                }

                System.Diagnostics.Debug.WriteLine($"Settings saved successfully. ModsInstallPath: {ModsInstallPath}, DeveloperMode: {DeveloperMode}, GameMode: {GameMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                var title = _localizationService?.Get("AppSettings.SaveError") ?? "Save error";
                var message = _localizationService?.Get("AppSettings.SaveErrorMessage") ?? "Failed to save settings.";
                await ShowErrorAsync(title, string.Format(message, ex.Message));
            }
        }

        private string GetLocalizedValidationMessage(string? errorCode)
        {
            return errorCode switch
            {
                ModsInstallPathGuard.ErrorEmpty =>
                    _localizationService?.Get("Settings.Paths.ModsInstallPath.Empty") ?? "Mods install path cannot be empty.",
                ModsInstallPathGuard.ErrorInvalidPath =>
                    _localizationService?.Get("Settings.Paths.ModsInstallPath.Invalid") ?? "Invalid path.",
                ModsInstallPathGuard.ErrorProgramFiles or
                ModsInstallPathGuard.ErrorSteamGameInstall or
                ModsInstallPathGuard.ErrorGameInstallDirectory =>
                    _localizationService?.Get("Settings.Paths.ModsInstallPath.ProtectedLocation") ?? "The mods install folder cannot be under Program Files, a Steam game folder, or an Among Us installation.",
                ModsInstallPathGuard.ErrorNotWritable =>
                    _localizationService?.Get("Settings.Paths.ModsInstallPath.NotWritable") ?? "The selected folder is not writable. Choose a folder in your user profile.",
                _ => "Unknown validation error."
            };
        }

        private async Task SaveAllSettingsToAppSettings()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                string appSettingsPath = Path.Combine(exeDir, "appsettings.json");

                // Wczytaj istniejący plik
                string jsonContent = await File.ReadAllTextAsync(appSettingsPath);
                var jsonDocument = JsonDocument.Parse(jsonContent);
                var root = jsonDocument.RootElement;

                // Stwórz nowy obiekt z zaktualizowanymi ustawieniami
                var updatedSettings = new Dictionary<string, object>();

                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name == "AppSettings")
                    {
                        var appSettings = new Dictionary<string, object>();

                        foreach (var appProperty in property.Value.EnumerateObject())
                        {
                            if (appProperty.Name == "ModsInstallPath")
                            {
                                appSettings[appProperty.Name] = ModsInstallPath;
                            }
                            else
                            {
                                appSettings[appProperty.Name] = GetJsonValue(appProperty.Value);
                            }
                        }

                        updatedSettings[property.Name] = appSettings;
                    }
                    else if (property.Name == "Configuration")
                    {
                        var configuration = new Dictionary<string, object>();

                        foreach (var configProperty in property.Value.EnumerateObject())
                        {
                            if (configProperty.Name == "Mode")
                            {
                                configuration[configProperty.Name] = GameMode;
                            }
                            else
                            {
                                configuration[configProperty.Name] = GetJsonValue(configProperty.Value);
                            }
                        }

                        updatedSettings[property.Name] = configuration;
                    }
                    else
                    {
                        updatedSettings[property.Name] = GetJsonValue(property.Value);
                    }
                }

                // Zapisz zaktualizowany plik
                var options = new JsonSerializerOptions { WriteIndented = true };
                string updatedJson = JsonSerializer.Serialize(updatedSettings, options);
                await File.WriteAllTextAsync(appSettingsPath, updatedJson);

                System.Diagnostics.Debug.WriteLine($"Updated appsettings.json with ModsInstallPath: {ModsInstallPath} and GameMode: {GameMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating appsettings.json: {ex.Message}");
                throw;
            }
        }

        private object GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => GetJsonValue(p.Value)),
                JsonValueKind.Array => element.EnumerateArray().Select(GetJsonValue).ToArray(),
                _ => element.ToString()
            };
        }

        private void ResetToDefault()
        {
            ModsInstallPath = PathSettings.GetDefaultModsPath();
        }

        private void Cancel()
        {
            // Dla UserControl - zamknięcie jest obsłużone przez CancelButton_Click
            // Dla Window - zamykamy okno
            if (_control is Window window)
            {
                window.Close();
            }
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            if (TryGetMainWindowViewModel(out var mainWindowViewModel))
            {
                await mainWindowViewModel.ShowInlineErrorAsync(title, message);
                return;
            }

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var dialog = new MessageDialog(title, message);
                await dialog.ShowDialog(parentWindow);
            }
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            if (TryGetMainWindowViewModel(out var mainWindowViewModel))
            {
                await mainWindowViewModel.ShowInlineMessageAsync(title, message);
                return;
            }

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var dialog = new MessageDialog(title, message);
                await dialog.ShowDialog(parentWindow);
            }
        }

        /// <summary>Potwierdzenie operacji na legendary. Brak hosta dialogów = brak zgody (operacja wstrzymana).</summary>
        private async Task<bool> ShowConfirmAsync(string title, string message)
        {
            if (TryGetMainWindowViewModel(out var mainWindowViewModel))
                return await mainWindowViewModel.ShowInlineConfirmAsync(title, message);

            Debug.WriteLine($"[AppSettingsViewModel] Brak MainWindow do pokazania potwierdzenia: {message}");
            return false;
        }

        private Window? GetParentWindow()
        {
            if (_control is Window window)
                return window;
            
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }

        private bool TryGetMainWindowViewModel(out MainWindowViewModel mainWindowViewModel)
        {
            mainWindowViewModel = null!;

            if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is MainWindow mainWindow &&
                mainWindow.DataContext is MainWindowViewModel vm)
            {
                mainWindowViewModel = vm;
                return true;
            }

            return false;
        }

        private async Task FactoryResetAsync()
        {
            try
            {
                string appSettingsPath = ApplicationPaths.AppSettingsPath;
                var factoryResetService = new ApplicationFactoryResetService();
                var directoriesToDelete = factoryResetService.CollectDataPathsToDelete();

                // Pokaż dedykowany dialog potwierdzenia
                var dialog = new FactoryResetConfirmDialog(directoriesToDelete);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                    
                    if (!dialog.Result)
                        return;
                }
                else
                {
                    return;
                }

                await factoryResetService.DeleteModsDirectoriesAsync(directoriesToDelete);
                factoryResetService.DeleteRuntimeConfigFile();
                factoryResetService.ScheduleApplicationDataResetOnNextStartup();

                // Wyczyść Configuration:Mode z appsettings.json - dzięki temu migracja
                // przy starcie nie odczyta starego trybu i wymusi dialog wyboru platformy
                if (File.Exists(appSettingsPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(appSettingsPath);
                        using var doc = JsonDocument.Parse(json);
                        using var stream = new System.IO.MemoryStream();
                        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });

                        writer.WriteStartObject();
                        foreach (var section in doc.RootElement.EnumerateObject())
                        {
                            if (section.Name == "Configuration")
                            {
                                writer.WritePropertyName("Configuration");
                                writer.WriteStartObject();
                                foreach (var prop in section.Value.EnumerateObject())
                                {
                                    if (prop.Name == "Mode")
                                    {
                                        writer.WriteString("Mode", string.Empty);
                                    }
                                    else
                                    {
                                        prop.WriteTo(writer);
                                    }
                                }
                                writer.WriteEndObject();
                            }
                            else
                            {
                                section.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                        writer.Flush();

                        await File.WriteAllBytesAsync(appSettingsPath, stream.ToArray());
                    }
                    catch
                    {
                        // Nie blokuj resetu jeśli nie udało się wyczyścić Mode z appsettings.json
                    }
                }

                // Restart aplikacji
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Błąd resetu", $"Wystąpił błąd podczas resetowania aplikacji:\n{ex.Message}");
            }
        }
    }

    /// <summary>
    /// Model dla opcji języka w ComboBox.
    /// </summary>
    public class LanguageOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model dla opcji kanału aktualizacji w ComboBox.
    /// </summary>
    public class UpdateChannelOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
