using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;
using SUSModder.Core.Services;
using SUSModder.Core.Configuration;
using Avalonia.Controls;
using DynamicData;
using SUSModder.Core.Repositories;
using SUSModder.Views;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;
using System.IO;
using SUSModder.Core.GameIntegration;
using System.Collections.Generic;
using System.Diagnostics;
using SUSModder.Services;
using System.Windows.Input;
using SUSModder.ViewModels.Helpers;
using Microsoft.Extensions.Configuration;
using FluentIcons.Common;
using SUSModder.Core.Diagnostics;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Main ViewModel dla głównego okna aplikacji SUSModder.
    /// Zarządza listą modów, instalacją, aktualizacją i uruchamianiem gry Among Us.
    ///
    /// Podzielony na partial classes dla lepszej organizacji:
    /// - MainWindowViewModel.Main.cs - Properties, Fields, Constructor
    /// - MainWindowViewModel.Helpers.cs - Platform detection, Refresh logic
    /// - MainWindowViewModel.ModOperations.cs - Install, Update, Uninstall
    /// - MainWindowViewModel.GameLaunch.cs - Launch logic (Steam/Epic)
    /// - MainWindowViewModel.Updates.cs - Update checking and processing
    /// - MainWindowViewModel.DllManagement.cs - DLL mod management
    /// - MainWindowViewModel.Dialogs.cs - Dialog methods
    /// - MainWindowViewModel.Initialization.cs - App initialization
    /// - MainWindowViewModel.ThemeManagement.cs - Theme switching
    /// - MainWindowViewModel.AppSettings.cs - Settings management
    /// - MainWindowViewModel.ExternalActions.cs - External actions (Discord, donations, etc.)
    /// </summary>
    public partial class MainWindowViewModel : ViewModelBase, IDisposable
    {
        #region Private Fields

        private bool _isPaneOpen;
        private ModItem? _selectedMod;
        private ThemeType _currentTheme = ThemeType.Dark;
        private ResourceDictionary? _currentThemeDictionary;
        private readonly Uri _darkThemeUri = new Uri("avares://SUSModder/Themes/DarkTheme.axaml");
        private readonly Uri _lightThemeUri = new Uri("avares://SUSModder/Themes/LightTheme.axaml");
        private readonly Uri _pinkThemeUri = new Uri("avares://SUSModder/Themes/PinkTheme.axaml");

        private bool _isInfoPanelVisible = false;
        private string _appVersion = string.Empty;
        private string _windowTitle = "SUSModder";
        private bool _isAdditionalActionsVisible = false;
        private List<ModConfiguration> _loadedConfigs = new();
        private UserInteractionService _userInteractionService;
        private readonly DllModificationService _dllModificationService;
        private readonly SUSModder.Core.Services.Localization.ILocalizationService _localizationService;
        private Microsoft.Extensions.Configuration.IConfiguration? _configuration;
        private SUSModder.Core.Diagnostics.IDiagnosticsOutput? _diagnosticsOutput;
        private readonly UserSettingsService _userSettingsService;
        private bool _isDllModificationsVisible = false;
        private ObservableCollection<ModItem> _dllMods = new();
        private ModItem? _selectedDllMod;
        private ObservableCollection<ModItem> _availableFullMods = new();
        private bool _isDllInstallDialogVisible = false;
        private ObservableCollection<ModItem> _modsWithDllInstalled = new();
        private ObservableCollection<ModItem> _modsWithoutDllInstalled = new();
        private bool _isModContentVisible = false;
        private bool _isSUStatsConfigVisible = false;
        private bool _isAppSettingsVisible = false;
        private bool _isRecommendedDiscordsVisible = false;
        private bool _isRepairOptionsVisible = false;
        private bool _isRepairSteamPlatform = true;
        private bool _isDllSelectionModalVisible = false;
        private DllModSelectionViewModel? _dllSelectionModalViewModel;
        private bool _isVersionSelectionModalVisible = false;
        private VersionSelectionViewModel? _versionSelectionModalViewModel;
        private bool _isPostInstallSuccessVisible = false;
        private PostInstallSuccessViewModel? _postInstallSuccessViewModel;

        // Flaga blokująca interakcję podczas inicjalizacji aplikacji
        private bool _isInitializing = true;

        // Zarządzanie wielokrotnymi instalacjami i dialogami DLL
        private int _activeInstallationsCount = 0;
        private readonly object _installationLock = new object();
        private readonly List<(ModItem mod, string platform)> _pendingDllDialogs = new List<(ModItem, string)>();

        /// <summary>
        /// Synchronizuje IsAnyModInstalling z _activeInstallationsCount.
        /// Wywoływana po każdej zmianie licznika instalacji.
        /// </summary>
        private void SyncIsAnyModInstalling()
        {
            IsAnyModInstalling = _activeInstallationsCount > 0;
        }

        // Velopack update service - musi być jako pole aby móc reinicjalizować po zmianie kanału
        private VelopackUpdateService? _velopackUpdateService;

        #endregion

        #region Public Properties

        public bool IsModPanelVisible => IsModSelected && !IsAnyToolModalOpen;
        public bool IsDeveloperMode => DeveloperModeSettings.IsEnabled;

        /// <summary>
        /// Serwis powiadomień toast (singleton).
        /// </summary>
        public ToastService ToastService { get; }

        public bool IsAdditionalActionsVisible
        {
            get => _isAdditionalActionsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isAdditionalActionsVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsInfoPanelVisible
        {
            get => _isInfoPanelVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInfoPanelVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public string AppVersion
        {
            get => _appVersion;
            set => this.RaiseAndSetIfChanged(ref _appVersion, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => this.RaiseAndSetIfChanged(ref _windowTitle, value);
        }

        public bool IsModContentVisible
        {
            get => _isModContentVisible;
            set => this.RaiseAndSetIfChanged(ref _isModContentVisible, value);
        }

        public bool IsDllModificationsVisible
        {
            get => _isDllModificationsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDllModificationsVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsSUStatsConfigVisible
        {
            get => _isSUStatsConfigVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSUStatsConfigVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsAppSettingsVisible
        {
            get => _isAppSettingsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isAppSettingsVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsRecommendedDiscordsVisible
        {
            get => _isRecommendedDiscordsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRecommendedDiscordsVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsRepairOptionsVisible
        {
            get => _isRepairOptionsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRepairOptionsVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public bool IsRepairSteamPlatform
        {
            get => _isRepairSteamPlatform;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRepairSteamPlatform, value);
                this.RaisePropertyChanged(nameof(IsRepairEpicPlatform));
            }
        }

        public bool IsRepairEpicPlatform => !IsRepairSteamPlatform;

        public bool IsDllSelectionModalVisible
        {
            get => _isDllSelectionModalVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDllSelectionModalVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public DllModSelectionViewModel? DllSelectionModalViewModel
        {
            get => _dllSelectionModalViewModel;
            set => this.RaiseAndSetIfChanged(ref _dllSelectionModalViewModel, value);
        }

        public bool IsVersionSelectionModalVisible
        {
            get => _isVersionSelectionModalVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isVersionSelectionModalVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public VersionSelectionViewModel? VersionSelectionModalViewModel
        {
            get => _versionSelectionModalViewModel;
            set => this.RaiseAndSetIfChanged(ref _versionSelectionModalViewModel, value);
        }

        public bool IsPostInstallSuccessVisible
        {
            get => _isPostInstallSuccessVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isPostInstallSuccessVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public PostInstallSuccessViewModel? PostInstallSuccessViewModel
        {
            get => _postInstallSuccessViewModel;
            set => this.RaiseAndSetIfChanged(ref _postInstallSuccessViewModel, value);
        }

        public ObservableCollection<ModItem> DllMods
        {
            get => _dllMods;
            set => this.RaiseAndSetIfChanged(ref _dllMods, value);
        }

        public ModItem? SelectedDllMod
        {
            get => _selectedDllMod;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedDllMod, value);
                this.RaisePropertyChanged(nameof(SelectedDllModName));
                this.RaisePropertyChanged(nameof(SelectedDllModPngFileName));
            }
        }

        public string SelectedDllModName => SelectedDllMod?.Name ?? string.Empty;
        public string SelectedDllModPngFileName => SelectedDllMod?.PngFileName ?? string.Empty;

        public ObservableCollection<ModItem> AvailableFullMods
        {
            get => _availableFullMods;
            set => this.RaiseAndSetIfChanged(ref _availableFullMods, value);
        }

        public bool IsDllInstallDialogVisible
        {
            get => _isDllInstallDialogVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDllInstallDialogVisible, value);
                NotifyToolModalStateChanged();
            }
        }

        public ObservableCollection<ModItem> ModsWithDllInstalled
        {
            get => _modsWithDllInstalled;
            set => this.RaiseAndSetIfChanged(ref _modsWithDllInstalled, value);
        }

        public ObservableCollection<ModItem> ModsWithoutDllInstalled
        {
            get => _modsWithoutDllInstalled;
            set => this.RaiseAndSetIfChanged(ref _modsWithoutDllInstalled, value);
        }

        public enum ThemeType
        {
            Dark,
            Light,
            Pink
        }

        public ThemeType CurrentTheme
        {
            get => _currentTheme;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentTheme, value);
                this.RaisePropertyChanged(nameof(ThemeButtonText));
                this.RaisePropertyChanged(nameof(ThemeButtonIcon));
                ApplyTheme(_currentTheme);
            }
        }

        public ObservableCollection<ModItem> Mods { get; } = new();

        private bool _isModsLoading;
        public bool IsModsLoading
        {
            get => _isModsLoading;
            set => this.RaiseAndSetIfChanged(ref _isModsLoading, value);
        }

        public bool IsPaneOpen
        {
            get => _isPaneOpen;
            set => this.RaiseAndSetIfChanged(ref _isPaneOpen, value);
        }
        // FAB – badge and contextual icon
        private bool _isAnyModInstalling;

        public bool IsAnyModInstalling
        {
            get => _isAnyModInstalling;
            set
            {
                this.RaiseAndSetIfChanged(ref _isAnyModInstalling, value);
                this.RaisePropertyChanged(nameof(FabIconSymbol));
            }
        }

        public int FabBadgeCount => AvailableUpdatesCount;
        public bool FabHasBadge => AvailableUpdatesCount > 0;

        public string FabBadgeTooltip => AvailableUpdatesCount > 0
            ? _localizationService.GetFormatted("UI.Fab.UpdatesBadgeTooltip", AvailableUpdatesCount)
            : string.Empty;

        public Symbol FabIconSymbol
        {
            get
            {
                if (IsAnyModInstalling)
                    return Symbol.ArrowSync;
                if (AvailableUpdatesCount > 0)
                    return Symbol.ArrowDownload;
                return Symbol.Navigation;
            }
        }


        public string ThemeButtonText => CurrentTheme switch
        {
            ThemeType.Dark => _localizationService.Get("UI.Theme.SwitchToLight"),
            ThemeType.Light => _localizationService.Get("UI.Theme.SwitchToPink"),
            ThemeType.Pink => _localizationService.Get("UI.Theme.SwitchToDark"),
            _ => _localizationService.Get("UI.Theme.SwitchToDark")
        };

        public string ThemeButtonIcon => CurrentTheme switch
        {
            ThemeType.Dark => "☀️",
            ThemeType.Light => "💖",
            ThemeType.Pink => "🌙",
            _ => "🌙"
        };

        public bool IsDarkTheme => CurrentTheme == ThemeType.Dark;

        public ModItem? SelectedMod
        {
            get => _selectedMod;
            set
            {
                var previousMod = _selectedMod;
                
                // Jeśli zmieniamy mod (nie ten sam), najpierw ukryj zawartość (fade out)
                if (value != null && previousMod != null && previousMod.Name != value.Name)
                {
                    IsModContentVisible = false;
                    
                    // Poczekaj na połowę fade out (400ms na fade out, czekamy 225ms aby była pewność że się ukryje)
                    Task.Delay(225).ContinueWith(_ =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            // Teraz zmień mod
                            this.RaiseAndSetIfChanged(ref _selectedMod, value);
                            this.RaisePropertyChanged(nameof(IsModSelected));
                            this.RaisePropertyChanged(nameof(IsModPanelVisible));
                            
                            IsInfoPanelVisible = false;
                            IsAdditionalActionsVisible = false;
                            IsDllModificationsVisible = false;
                            IsSUStatsConfigVisible = false;
                            IsAppSettingsVisible = false;
                            IsRecommendedDiscordsVisible = false;
                            IsRepairOptionsVisible = false;
                            IsDllInstallDialogVisible = false;
                            IsVersionSelectionModalVisible = false;
                            VersionSelectionModalViewModel = null;
                            CloseDllSelectionModal();
                            
                            // Pokaż nową zawartość (fade in)
                            IsModContentVisible = true;
                        });
                    });
                    
                    return; // Nie kontynuuj dalej
                }
                
                // Dla pierwszego wyboru lub tego samego moda - bez animacji fade out
                this.RaiseAndSetIfChanged(ref _selectedMod, value);
                this.RaisePropertyChanged(nameof(IsModSelected));
                this.RaisePropertyChanged(nameof(IsModPanelVisible));

                if (value != null && previousMod == null)
                {
                    // Pierwszy wybór - krótkie opóźnienie
                    IsInfoPanelVisible = false;
                    IsAdditionalActionsVisible = false;
                    IsDllModificationsVisible = false;
                    IsSUStatsConfigVisible = false;
                    IsAppSettingsVisible = false;
                    IsRecommendedDiscordsVisible = false;
                    IsRepairOptionsVisible = false;
                    IsDllInstallDialogVisible = false;
                    IsVersionSelectionModalVisible = false;
                    VersionSelectionModalViewModel = null;
                    CloseDllSelectionModal();
                    
                    Task.Delay(50).ContinueWith(_ =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            IsModContentVisible = true;
                        });
                    });
                }
                else if (value != null)
                {
                    // Ten sam mod - pokaż natychmiast
                    IsModContentVisible = true;
                }
                else
                {
                    // Deselect
                    IsModContentVisible = false;
                }
            }
        }

        public bool IsModSelected => SelectedMod != null;

        #endregion

        #region Commands

        public ReactiveCommand<Unit, Unit> TogglePaneCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallWithVersionSelectionCommand { get; }
        public ReactiveCommand<Unit, Unit> UninstallCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowInfoCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAppSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSUStatsConfigCommand { get; }
        public ICommand ShowRolesCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdditionalActionsCommand { get; }
        public ReactiveCommand<Unit, Unit> FixBlackScreenCommand { get; }
        public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> CheckDllUpdatesCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CreateShortcutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowDllModificationsCommand { get; }
    public ReactiveCommand<ModItem, Unit> SelectDllModCommand { get; }
    public ReactiveCommand<ModItem, Unit> InstallDllToModCommand { get; }
    public ReactiveCommand<ModItem, Unit> UninstallDllFromModCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseDllDialogCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowRecommendedDiscordsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowDllSelectionCommand { get; }
    public ReactiveCommand<Unit, Unit> CheckForAppUpdatesCommand { get; }
        public ReactiveCommand<Unit, Unit> CheckForModUpdatesFromMenuCommand { get; private set; }
    public ReactiveCommand<string, Unit> ExecuteRepairOptionCommand { get; }
    public ReactiveCommand<ModItem, Unit> ModDoubleClickCommand { get; }

        #endregion

        #region Constructor

        public MainWindowViewModel()
        {
            _localizationService = App.GetService<SUSModder.Core.Services.Localization.ILocalizationService>();

            // Inicjalizuj serwis powiadomień toast (singleton z DI)
            ToastService = App.GetService<ToastService>();

            // Inicjalizuj UserSettingsService
            _userSettingsService = new UserSettingsService();
            
            _userInteractionService = new UserInteractionService(
                ShowConfirmDialogAsync,
                ShowMessageAsync,
                ShowErrorDialogAsync,
                ShowPromptDialogAsync,
                ShowSelectFileDialogAsync
            );

            _diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DLL Service] {message}");
            });

            var configService = new ConfigService();
            _dllModificationService = new DllModificationService(configService, _diagnosticsOutput);
            
            // Załaduj konfigurację z DI (cache'owana - budowana raz w App.ConfigureServices)
            _configuration = App.GetService<Microsoft.Extensions.Configuration.IConfiguration>();

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            var configRepository = new ConfigRepository(exeDir);
            ModConfigHandler.Initialize(configRepository, _userInteractionService);

            // Initialize commands
            TogglePaneCommand = ReactiveCommand.Create(TogglePane);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            InstallCommand = ReactiveCommand.Create(Install);
            InstallWithVersionSelectionCommand = ReactiveCommand.CreateFromTask(InstallWithVersionSelection);
            UninstallCommand = ReactiveCommand.Create(Uninstall);
            LaunchCommand = ReactiveCommand.Create(Launch);
            UpdateCommand = ReactiveCommand.Create(Update);
            CheckDllUpdatesCommand = ReactiveCommand.CreateFromTask(CheckDllUpdates);
            CheckForAppUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForAppUpdatesManuallyAsync);
                        CheckForModUpdatesFromMenuCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                IsPaneOpen = false;
                await CheckForModUpdatesAsync();
            });
ShowRolesCommand = ReactiveCommand.Create(ShowRoles);
            ShowInfoCommand = ReactiveCommand.Create(ShowInfo);
            ShowAdditionalActionsCommand = ReactiveCommand.Create(ShowAdditionalActions);
            OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
            CreateShortcutCommand = ReactiveCommand.Create(CreateShortcut);
            ShowDllModificationsCommand = ReactiveCommand.Create(ShowDllModifications);
            SelectDllModCommand = ReactiveCommand.Create<ModItem>(SelectDllMod);
            InstallDllToModCommand = ReactiveCommand.CreateFromTask<ModItem>(InstallDllToMod);
            UninstallDllFromModCommand = ReactiveCommand.CreateFromTask<ModItem>(UninstallDllFromMod);
            CloseDllDialogCommand = ReactiveCommand.Create(CloseDllDialog);
            ShowAppSettingsCommand = ReactiveCommand.Create(ShowAppSettings);
            this.RaisePropertyChanged(nameof(IsDeveloperMode));
            ShowRecommendedDiscordsCommand = ReactiveCommand.Create(ShowRecommendedDiscords);
            ShowSUStatsConfigCommand = ReactiveCommand.Create(ShowSUStatsConfig);
            ExecuteRepairOptionCommand = ReactiveCommand.CreateFromTask<string>(ExecuteRepairOptionFromModalAsync);
            InitializeFrontendLayout();
            
            // Subscribe to language changes to update theme button text
            if (_localizationService is INotifyPropertyChanged localizationNotify)
            {
                localizationNotify.PropertyChanged += (s, e) =>
                {
                    this.RaisePropertyChanged(nameof(ThemeButtonText));
                    this.RaisePropertyChanged(nameof(ToolModalTitle));
                };
            }

            FixBlackScreenCommand = ReactiveCommand.CreateFromTask(ExecuteFixBlackScreenAsync);

            FixBlackScreenCommand.ThrownExceptions.Subscribe(HandleCommandError);
            CheckForAppUpdatesCommand.ThrownExceptions.Subscribe(HandleCommandError);

            System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Starting Discord icon preloader...");
            _ = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Preloader task started");
                await DiscordIconPreloader.PreloadDiscordIconsAsync();
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Preloader task completed");
            });
            InitializeDiscordPromo();

            // ClearEpicLogsOnStartup przeniesione do InitializeServicesAsync (tworzy ciężki EpicVersionManager)
            LoadSavedTheme();
            // LoadAppVersion() jest teraz wywoływane wewnątrz InitializeApplicationAsync() (KROK 0)
            // ApplyTheme jest wywoływane po LoadSavedTheme
            ApplyTheme(CurrentTheme);
            // InitializeApplicationAsync() jest teraz wywoływane z App.axaml.cs po pokazaniu splash screen

            // Migracja istniejących instalacji jest teraz wewnątrz InitializeApplicationAsync()

            // Subskrybuj do zmiany trybu gry
            AppSettingsViewModel.GameModeChanged += LoadWindowTitle;

            ShowDllSelectionCommand = ReactiveCommand.Create(ShowDllSelectionFromSelectedMod);

            // Komenda dla dwukliku na modzie
            ModDoubleClickCommand = ReactiveCommand.Create<ModItem>(async (mod) =>
            {
                if (mod == null || mod.IsInstalling)
                    return;

                // Sprawdź czy mod jest zainstalowany
                if (!string.IsNullOrEmpty(mod.InstallPath))
                {
                    // Mod zainstalowany - uruchom grę
                    SelectedMod = mod;
                    await LaunchAsync();
                }
                else
                {
                    // Mod niezainstalowany - zainstaluj
                    SelectedMod = mod;
                    Install();
                }
            });
        }

        #endregion

        #region Simple UI Methods

        private void TogglePane()
        {
            IsPaneOpen = !IsPaneOpen;
        }

        private void HandleCommandError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");

            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await ShowErrorDialogAsync($"Wystąpił błąd: {ex.Message}", "Błąd");
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error showing error dialog: {innerEx.Message}");
                }
            });
        }

        #endregion

        #region Dispose

        private bool _disposed;

        /// <summary>
        /// Zwalnia zasoby: timery, background taski, bitmapy, serwisy IDisposable.
        /// Wywoływane przy zamykaniu aplikacji przez MainWindow.OnClosing.
        /// Bezpieczne do wielokrotnego wywołania (_disposed guard).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            DisposeDiscordPromoTimer();
            CancelStatusBarBackgroundTask();
            DisposeVelopackService();
            DisposeDiscordBitmaps();

            GC.SuppressFinalize(this);
        }

        private void DisposeVelopackService()
        {
            if (_velopackUpdateService != null)
            {
                _velopackUpdateService.Dispose();
                _velopackUpdateService = null;
            }
        }

        #endregion
    }
}
