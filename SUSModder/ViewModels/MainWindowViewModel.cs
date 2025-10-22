using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using System;
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
    public partial class MainWindowViewModel : ViewModelBase
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

        // Zarządzanie wielokrotnymi instalacjami i dialogami DLL
        private int _activeInstallationsCount = 0;
        private readonly object _installationLock = new object();
        private readonly List<(ModItem mod, string platform)> _pendingDllDialogs = new List<(ModItem, string)>();

        #endregion

        #region Public Properties

        public bool IsModPanelVisible => IsModSelected && !IsInfoPanelVisible && !IsAdditionalActionsVisible;
        public bool IsDeveloperMode => DeveloperModeSettings.IsEnabled;

        public bool IsAdditionalActionsVisible
        {
            get => _isAdditionalActionsVisible;
            set => this.RaiseAndSetIfChanged(ref _isAdditionalActionsVisible, value);
        }

        public bool IsInfoPanelVisible
        {
            get => _isInfoPanelVisible;
            set => this.RaiseAndSetIfChanged(ref _isInfoPanelVisible, value);
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
            set => this.RaiseAndSetIfChanged(ref _isDllModificationsVisible, value);
        }

        public bool IsSUStatsConfigVisible
        {
            get => _isSUStatsConfigVisible;
            set => this.RaiseAndSetIfChanged(ref _isSUStatsConfigVisible, value);
        }

        public bool IsAppSettingsVisible
        {
            get => _isAppSettingsVisible;
            set => this.RaiseAndSetIfChanged(ref _isAppSettingsVisible, value);
        }

        public ObservableCollection<ModItem> DllMods
        {
            get => _dllMods;
            set => this.RaiseAndSetIfChanged(ref _dllMods, value);
        }

        public ModItem? SelectedDllMod
        {
            get => _selectedDllMod;
            set => this.RaiseAndSetIfChanged(ref _selectedDllMod, value);
        }

        public ObservableCollection<ModItem> AvailableFullMods
        {
            get => _availableFullMods;
            set => this.RaiseAndSetIfChanged(ref _availableFullMods, value);
        }

        public bool IsDllInstallDialogVisible
        {
            get => _isDllInstallDialogVisible;
            set => this.RaiseAndSetIfChanged(ref _isDllInstallDialogVisible, value);
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

        public bool IsPaneOpen
        {
            get => _isPaneOpen;
            set => this.RaiseAndSetIfChanged(ref _isPaneOpen, value);
        }

        public string ThemeButtonText => CurrentTheme switch
        {
            ThemeType.Dark => "Motyw jasny",
            ThemeType.Light => "Motyw różowy",
            ThemeType.Pink => "Motyw ciemny",
            _ => "Motyw ciemny"
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
                            IsDllInstallDialogVisible = false;
                            
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
                    IsDllInstallDialogVisible = false;
                    
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
        public ReactiveCommand<Unit, Unit> LobbySetCommand { get; }
        public ICommand ShowRolesCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowAdditionalActionsCommand { get; }
        public ReactiveCommand<Unit, Unit> FixBlackScreenCommand { get; }
        public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CreateShortcutCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDllModificationsCommand { get; }
        public ReactiveCommand<ModItem, Unit> SelectDllModCommand { get; }
        public ReactiveCommand<ModItem, Unit> InstallDllToModCommand { get; }
        public ReactiveCommand<ModItem, Unit> UninstallDllFromModCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseDllDialogCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRecommendedDiscordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDllSelectionCommand { get; }
        public ReactiveCommand<ModItem, Unit> ModDoubleClickCommand { get; }

        #endregion

        #region Constructor

        public MainWindowViewModel()
        {
            _userInteractionService = new UserInteractionService(
                ShowConfirmDialogAsync,
                ShowMessageAsync,
                ShowErrorDialogAsync,
                ShowPromptDialogAsync,
                ShowSelectFileDialogAsync
            );

            var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
            {
                System.Diagnostics.Debug.WriteLine($"[DLL Service] {message}");
            });

            var configService = new ConfigService();
            _dllModificationService = new DllModificationService(configService, diagnosticsOutput);

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

            LobbySetCommand = ReactiveCommand.CreateFromTask(ShowLobbySetDialog);
            FixBlackScreenCommand = ReactiveCommand.CreateFromTask(ExecuteFixBlackScreenAsync);

            FixBlackScreenCommand.ThrownExceptions.Subscribe(HandleCommandError);
            LobbySetCommand.ThrownExceptions.Subscribe(HandleCommandError);

            System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Starting Discord icon preloader...");
            _ = Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Preloader task started");
                await DiscordIconPreloader.PreloadDiscordIconsAsync();
                System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Preloader task completed");
            });

            ClearEpicLogsOnStartup();
            LoadSavedTheme();
            InitializeApplicationAsync();
            LoadAppVersion();
            LoadWindowTitle();
            CheckForAppUpdatesOnStartup();
            ApplyTheme(CurrentTheme);

            // Migracja istniejących instalacji do Installation Map System
            _ = Task.Run(async () => await MigrateExistingInstallationsAsync());

            // Subskrybuj do zmiany trybu gry
            AppSettingsViewModel.GameModeChanged += LoadWindowTitle;

            ShowDllSelectionCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedMod == null || string.IsNullOrEmpty(SelectedMod.InstallPath))
                    return;

                // Zamiast używać właściwości IsEpic, użyj funkcji DeterminePlatform
                string platform = DeterminePlatform().ToLower(); // ToLower() żeby było zgodne z wartościami "epic" i "steam"

                // Tworzymy nowe okno DllModSelectionView
                var dllSelectionWindow = new Window
                {
                    Title = $"Dodatkowe modyfikacje DLL dla {SelectedMod.Name}",
                    Width = 650,
                    Height = 600,
                    Content = new DllModSelectionView
                    {
                        DataContext = new DllModSelectionViewModel(
                            _dllModificationService,
                            ModItemAdapter.ToConfig(SelectedMod),
                            platform // Użyj zmiennej platform zamiast SelectedMod.IsEpic
                        )
                    }
                };

                System.Diagnostics.Debug.WriteLine($"DEBUG: Otwieranie okna DLL dla platformy: {platform}");
                dllSelectionWindow.Show();
            });

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
    }
}
