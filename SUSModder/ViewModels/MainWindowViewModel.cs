using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using System;
using SUSModder.Core.Services;
using SUSModder.ViewModels;
using SUSModder.Core.Configuration;
using Avalonia.Controls;
using DynamicData;
using System.Text.Json;
using SUSModder.Core.Repositories;
using SUSModder.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using System.IO;
using Avalonia.Threading;
using SUSModder.Core.GameIntegration;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using SUSModder.Services;
using System.Windows.Input;


namespace SUSModder.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private bool _isPaneOpen;
        private ModItem? _selectedMod;
        private ThemeType _currentTheme = ThemeType.Dark;
        private ResourceDictionary? _currentThemeDictionary;
        private readonly Uri _darkThemeUri = new Uri("avares://SUSModder/Themes/DarkTheme.axaml");
        private readonly Uri _lightThemeUri = new Uri("avares://SUSModder/Themes/LightTheme.axaml");
        private readonly Uri _pinkThemeUri = new Uri("avares://SUSModder/Themes/PinkTheme.axaml");

        private bool _isInfoPanelVisible = false;
        private string _appVersion = string.Empty;
        public bool IsModPanelVisible => IsModSelected && !IsInfoPanelVisible && !IsAdditionalActionsVisible;
        private readonly ToUConfigService _touConfigService;
        private bool _isAdditionalActionsVisible = false;
        private List<ModConfiguration> _loadedConfigs = new();
        private UserInteractionService _userInteractionService;
        private readonly DllModificationService _dllModificationService;
        private bool _isDllModificationsVisible = false;
        private ObservableCollection<ModItem> _dllMods = new();
        private ModItem? _selectedDllMod;
        private ObservableCollection<ModItem> _availableFullMods = new();
        private bool _isDllInstallDialogVisible = false;
        public ReactiveCommand<Unit, Unit> ShowAppSettingsCommand { get; }
        public bool IsDeveloperMode => DeveloperModeSettings.IsEnabled;
        public ReactiveCommand<Unit, Unit> OpenDonationPageCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowSUStatsConfigCommand { get; }

        public ReactiveCommand<Unit, Unit> LobbySetCommand { get; }
        public ICommand ShowRolesCommand { get; }

        public bool IsAdditionalActionsVisible
        {
            get => _isAdditionalActionsVisible;
            set => this.RaiseAndSetIfChanged(ref _isAdditionalActionsVisible, value);
        }

        public ReactiveCommand<Unit, Unit> ShowAdditionalActionsCommand { get; }

        // Komendy dla akcji ToU
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
        private ObservableCollection<ModItem> _modsWithDllInstalled = new();
        private ObservableCollection<ModItem> _modsWithoutDllInstalled = new();
        public ReactiveCommand<Unit, Unit> ShowRecommendedDiscordsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDllSelectionCommand { get; }

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

        public bool IsDllModificationsVisible
        {
            get => _isDllModificationsVisible;
            set => this.RaiseAndSetIfChanged(ref _isDllModificationsVisible, value);
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

        public ReactiveCommand<Unit, Unit> ShowInfoCommand { get; }

        public ObservableCollection<ModItem> Mods { get; } = new();

        public MainWindowViewModel()
        {
            _touConfigService = new ToUConfigService();
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

            TogglePaneCommand = ReactiveCommand.Create(TogglePane);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            InstallCommand = ReactiveCommand.Create(Install);
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
            OpenDonationPageCommand = ReactiveCommand.Create(OpenDonationPage);
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
            CheckForAppUpdatesOnStartup();
            ApplyTheme(CurrentTheme);

            ShowDllSelectionCommand = ReactiveCommand.Create(() => {
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

        }

        private void HandleCommandError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Command error: {ex.Message}");

            Dispatcher.UIThread.InvokeAsync(async () =>
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

        private void OpenDonationPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://liberapay.com/boracik/donate",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Nie udało się otworzyć strony dotacji: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync($"Nie udało się otworzyć strony dotacji: {ex.Message}", "Błąd");
                });
            }
        }
        private void ShowRecommendedDiscords()
        {
            try
            {
                var discordsWindow = new RecommendedDiscordsWindow();
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    discordsWindow.Show(mainWindow);
                }
                else
                {
                    discordsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening Discord servers window: {ex.Message}");
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync($"Nie udało się otworzyć okna Discord serwerów: {ex.Message}", "Błąd");
                });
            }
        }

        private async void ShowSUStatsConfig()
        {
            try
            {
                var suStatsWindow = new SUStatsConfigWindow();
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await suStatsWindow.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening SUStats config window: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć okna konfiguracji SUStats: {ex.Message}", "Błąd");
            }
        }
        private async void InitializeApplicationAsync()
        {
            try
            {
                // KROK 1: Ładowanie konfiguracji modów
                await Task.Run(() =>
                {
                    var configService = new ConfigService();
                    var configs = configService.LoadConfig();
                    System.Diagnostics.Debug.WriteLine($"Loaded {configs.Count} configs from service");

                    // Zapisz do pola klasy
                    _loadedConfigs = configs;
                });

                // KROK 2: Wyszukiwanie i konfiguracja Vanilla Among Us - PRZED sprawdzaniem aktualizacji
                bool vanillaSetupSuccess = await SetupVanillaGameAsync();

                if (vanillaSetupSuccess)
                {
                    // KROK 3: Przeładuj konfigurację po dodaniu Vanilla
                    await Task.Run(() =>
                    {
                        var configService = new ConfigService();
                        _loadedConfigs = configService.LoadConfig();
                    });

                    // KROK 4: Sprawdzanie aktualizacji modów
                    await CheckForModUpdatesAsync();
                }

                // KROK 5: Odświeżenie interfejsu (zawsze, niezależnie od sukcesu Vanilla)
                await RefreshModsListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during application initialization: {ex.Message}");
                await ShowDetailedErrorDialogAsync("Błąd podczas inicjalizacji aplikacji", ex);
            }
        }

        private async void CheckForAppUpdatesOnStartup()
        {
            try
            {
                // Poczekaj chwilę żeby UI się załadował
                await Task.Delay(2000);

                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[AppUpdate] {message}");
                });

                var updateService = new AppUpdateService(AppVersion, configuration, diagnosticsOutput);
                var updateCheck = await updateService.CheckForUpdateAsync();

                if (updateCheck.Success && updateCheck.IsUpdateAvailable)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var updateDialog = new AppUpdateDialog(updateCheck.CurrentVersion, updateCheck.LatestVersion);
                        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                        if (mainWindow != null)
                        {
                            await updateDialog.ShowDialog(mainWindow);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for app updates: {ex.Message}");
                // Nie pokazujemy błędu użytkownikowi - aktualizacje nie są krytyczne
            }
        }
        private void OpenFolder()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SelectedMod.InstallPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się otworzyć folderu: {ex.Message}");
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await ShowErrorDialogAsync($"Nie udało się otworzyć folderu: {ex.Message}", "Błąd");
                    });
                }
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowErrorDialogAsync("Folder instalacji nie istnieje lub mod nie jest zainstalowany.", "Błąd");
                });
            }
        }

        private void CreateShortcut()
        {
            if (SelectedMod?.InstallPath != null && Directory.Exists(SelectedMod.InstallPath))
            {
                try
                {
                    string amongUsExePath = Path.Combine(SelectedMod.InstallPath, "Among Us.exe");
                    if (File.Exists(amongUsExePath))
                    {
                        // Sprawdź czy jesteśmy na Windows
                        if (OperatingSystem.IsWindows())
                        {
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string shortcutPath = Path.Combine(desktopPath, $"{SelectedMod.Name}.lnk");

                            CreateWindowsShortcut(amongUsExePath, shortcutPath, SelectedMod.InstallPath);
                        }
                        else
                        {
                            // Na innych platformach możesz pokazać komunikat lub zaimplementować inne rozwiązanie
                            System.Diagnostics.Debug.WriteLine("Shortcut creation is only supported on Windows");
                            // Opcjonalnie: pokaż komunikat użytkownikowi
                            _ = ShowMessageAsync("Informacja", "Tworzenie skrótów jest dostępne tylko na Windows.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Nie udało się utworzyć skrótu: {ex.Message}");
                }
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void CreateWindowsShortcut(string targetPath, string shortcutPath, string workingDirectory)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    await ShowErrorDialogAsync("Nie można uzyskać dostępu do WScript.Shell", "Błąd");
                    return;
                }

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    await ShowErrorDialogAsync("Nie można utworzyć instancji WScript.Shell", "Błąd");
                    return;
                }

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.Description = $"Skrót do {Path.GetFileNameWithoutExtension(targetPath)}";
                shortcut.Save();

                System.Diagnostics.Debug.WriteLine($"Shortcut created: {shortcutPath}");

                // Dialog sukcesu
                await ShowMessageAsync("Sukces", $"Skrót został utworzony na pulpicie:\n{Path.GetFileName(shortcutPath)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating Windows shortcut: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas tworzenia skrótu: {ex.Message}", "Błąd");
            }
        }

        private void CreateShortcutWithPowerShell(string targetPath, string shortcutPath, string workingDirectory, string shortcutName)
        {
            string powershellScript = $@"
            $WshShell = New-Object -comObject WScript.Shell
            $Shortcut = $WshShell.CreateShortcut('{shortcutPath}')
            $Shortcut.TargetPath = '{targetPath}'
            $Shortcut.WorkingDirectory = '{workingDirectory}'
            $Shortcut.Description = 'Skrót do {shortcutName}'
            $Shortcut.Save()
        ";

            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{powershellScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start PowerShell process");
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException($"PowerShell error: {error}");
                }
            }
        }

        private async Task<bool> SetupVanillaGameAsync()
        {
            while (true)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Starting Vanilla game setup...");

                    // Stwórz IConfiguration z appsettings.json
                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                    var configuration = configBuilder.Build();

                    // Wywołaj asynchroniczną wersję z interfejsem użytkownika
                    bool success = await GameLocator.CheckAndSetupVanillaModAsync(
                        _loadedConfigs,
                        configuration,
                        _userInteractionService
                    );

                    System.Diagnostics.Debug.WriteLine($"Vanilla game setup completed with result: {success}");

                    if (success)
                        return true;

                    // Jeśli niepowodzenie, przejdź do obsługi poniżej
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during Vanilla setup: {ex.Message}");
                }

                // Dialog z wyborem: Spróbuj ponownie / Zamknij
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var dialog = new ConfirmDialog(
                    "Nie wybrano pliku Among Us.exe",
                    "Nie udało się wykryć poprawnej instalacji gry Among Us. Wybierz poprawny plik Among Us.exe lub zamknij aplikację."
                );
                dialog.OkButtonText = "Spróbuj ponownie";
                dialog.CancelButtonText = "Zamknij";

                if (mainWindow != null)
                    await dialog.ShowDialog(mainWindow);

                if (dialog.Result)
                {
                    // Spróbuj ponownie
                    continue;
                }
                else
                {
                    // Usuń config.json i zamknij aplikację
                    try
                    {
                        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                        string configPath = Path.Combine(exeDir, "config.json");
                        if (File.Exists(configPath))
                            File.Delete(configPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Błąd podczas kasowania config.json: {ex.Message}");
                    }
                    Environment.Exit(0);
                    return false;
                }
            }
        }

        private async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory)
        {
            try
            {
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow?.StorageProvider == null)
                    return null;

                // Przygotuj opcje filtra
                var fileTypeFilters = new List<Avalonia.Platform.Storage.FilePickerFileType>();

                if (!string.IsNullOrEmpty(filter))
                {
                    var parts = filter.Split('|');
                    if (parts.Length >= 2)
                    {
                        var extension = parts[1].Replace("*.", "").Replace("*.", "");
                        fileTypeFilters.Add(new Avalonia.Platform.Storage.FilePickerFileType(parts[0])
                        {
                            Patterns = new[] { $"*.{extension}" }
                        });
                    }
                }

                // Dodaj opcję "Wszystkie pliki"
                fileTypeFilters.Add(Avalonia.Platform.Storage.FilePickerFileTypes.All);

                var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Wybierz plik Among Us.exe",
                    AllowMultiple = false,
                    FileTypeFilter = fileTypeFilters
                };

                // Ustaw folder początkowy jeśli podano
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var folder = await mainWindow.StorageProvider.TryGetFolderFromPathAsync(initialDirectory);
                    if (folder != null)
                    {
                        options.SuggestedStartLocation = folder;
                    }
                }

                var result = await mainWindow.StorageProvider.OpenFilePickerAsync(options);

                return result?.FirstOrDefault()?.Path.LocalPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in file dialog: {ex.Message}");
                return null;
            }
        }



        private async Task CheckForModUpdatesAsync()
        {
            try
            {
                var updateManager = new ModUpdateManager();
                var result = await updateManager.CheckForUpdatesAsync();

                if (!result.Success)
                {
                    await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {result.ErrorMessage}", "Błąd");
                    return;
                }

                // Jeśli config został zaktualizowany, odśwież listę modów
                if (result.ConfigWasUpdated)
                {
                    await RefreshModsListAsync();
                }

                // Pokaż dialog aktualizacji tylko jeśli są dostępne aktualizacje dla zainstalowanych modów
                if (result.InstalledModUpdates.Any())
                {
                    await ShowUpdateDialogAsync(result.InstalledModUpdates);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during update check: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {ex.Message}", "Błąd");
            }
        }

        private async Task ShowUpdateDialogAsync(List<ModUpdateInfo> availableUpdates)
        {
            var updateDialog = new UpdateDialog(availableUpdates);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                updateDialog.Show(mainWindow);

                while (!updateDialog.DialogResult && updateDialog.IsVisible)
                {
                    await Task.Delay(100);
                }

                if (updateDialog.DialogResult && updateDialog.IsVisible)
                {
                    var selectedMods = updateDialog.GetSelectedMods();
                    if (selectedMods.Any())
                    {
                        await ProcessSelectedUpdatesWithProgressAsync(selectedMods, updateDialog);
                    }
                }

                if (updateDialog.IsVisible)
                {
                    updateDialog.Close();
                }

                await RefreshModsListAsync();
            }
        }





        private async Task ProcessSelectedUpdatesWithProgressAsync(List<ModUpdateInfo> selectedMods, UpdateDialog dialog)
        {
            int totalMods = selectedMods.Count;
            int currentMod = 0;
            var successfulUpdates = new List<string>();
            var failedUpdates = new List<string>();

            foreach (var modUpdate in selectedMods)
            {
                currentMod++;

                try
                {
                    // Aktualizuj ogólny progress
                    dialog.UpdateOverallProgress(currentMod, totalMods, modUpdate.ModName);

                    // Stwórz lub znajdź ModItem
                    var modItem = await GetOrCreateModItemAsync(modUpdate);

                    // Wykonaj aktualizację z progress callbackami
                    bool success = await UpdateSingleModWithProgressAsync(modItem, modUpdate, dialog);

                    if (success)
                    {
                        successfulUpdates.Add($"{modUpdate.ModName} ({modUpdate.CurrentVersion} → {modUpdate.NewVersion})");
                    }
                    else
                    {
                        failedUpdates.Add(modUpdate.ModName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating {modUpdate.ModName}: {ex.Message}");
                    dialog.UpdateCurrentModProgress(100, $"Błąd: {ex.Message}");
                    failedUpdates.Add(modUpdate.ModName);
                    await Task.Delay(1000);
                }
            }

            // Przygotuj końcową wiadomość
            var finalMessageBuilder = new System.Text.StringBuilder();
            finalMessageBuilder.AppendLine("🎉 Aktualizacja zakończona!");
            finalMessageBuilder.AppendLine();

            if (successfulUpdates.Any())
            {
                finalMessageBuilder.AppendLine($"✅ Pomyślnie zaktualizowano ({successfulUpdates.Count}):");
                foreach (var update in successfulUpdates)
                {
                    finalMessageBuilder.AppendLine($"   • {update}");
                }
                finalMessageBuilder.AppendLine();
            }

            if (failedUpdates.Any())
            {
                finalMessageBuilder.AppendLine($"❌ Nie udało się zaktualizować ({failedUpdates.Count}):");
                foreach (var failure in failedUpdates)
                {
                    finalMessageBuilder.AppendLine($"   • {failure}");
                }
                finalMessageBuilder.AppendLine();
            }

            finalMessageBuilder.AppendLine("Możesz teraz zamknąć to okno.");

            // Przekaż końcową wiadomość do dialogu
            dialog.ShowFinalSummary(finalMessageBuilder.ToString());
        }




        private async Task<ModItem> GetOrCreateModItemAsync(ModUpdateInfo modUpdate)
        {
            var existingModItem = Mods.FirstOrDefault(m => m.Name == modUpdate.ModName);

            if (existingModItem == null)
            {
                var modItem = new ModItem
                {
                    Id = modUpdate.LocalMod?.Id ?? 0,
                    Name = modUpdate.LocalMod?.ModName ?? modUpdate.ModName,
                    ModVersion = modUpdate.LocalMod?.ModVersion ?? modUpdate.CurrentVersion,
                    InstallPath = modUpdate.LocalMod?.InstallPath ?? "",
                    Description = modUpdate.LocalMod?.Description ?? "",
                };

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Mods.Add(modItem);
                });

                return modItem;
            }

            return existingModItem;
        }

        private async Task<bool> UpdateSingleModWithProgressAsync(ModItem modItem, ModUpdateInfo updateInfo, UpdateDialog dialog)
        {
            try
            {
                var configService = new ConfigService();

                // 1. Pobierz zaktualizowaną konfigurację
                dialog.UpdateCurrentModProgress(10, "Pobieranie nowej konfiguracji...");
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(modItem.Name);
                if (updatedModConfig == null) return false;

                // 2. Aktualizuj konfigurację w pliku
                dialog.UpdateCurrentModProgress(20, "Aktualizowanie konfiguracji...");
                await configService.UpdateSingleModConfigAsync(updatedModConfig);

                // 3. Zaktualizuj właściwości ModItem
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    modItem.ModVersion = updatedModConfig.ModVersion;
                    modItem.AmongVersion = updatedModConfig.AmongVersion;
                    modItem.Description = updatedModConfig.Description;
                    modItem.GitHubRepoOrLink = updatedModConfig.GitHubRepoOrLink;
                    modItem.EpicGitHubRepoOrLink = updatedModConfig.EpicGitHubRepoOrLink;
                });

                dialog.UpdateCurrentModProgress(30, "Przygotowywanie do reinstalacji...");

                // 4. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(modItem.InstallPath))
                {
                    // UNINSTALL
                    dialog.UpdateCurrentModProgress(40, "Odinstalowywanie starej wersji...");

                    if (Directory.Exists(modItem.InstallPath))
                    {
                        bool deleteSuccess = await SafeDeleteDirectoryAsync(modItem.InstallPath, modItem.Name);
                        if (!deleteSuccess)
                        {
                            throw new InvalidOperationException($"Nie udało się usunąć starej wersji moda '{modItem.Name}'. Aktualizacja została przerwana.");
                        }
                    }

                    // Aktualizuj konfigurację - usuń ścieżkę instalacji
                    var configs = configService.LoadConfig();
                    var modConfig = configs.FirstOrDefault(c => c.ModName == modItem.Name);
                    if (modConfig != null)
                    {
                        modConfig.InstallPath = string.Empty;
                        ConfigManager.SaveConfig(configs);
                    }

                    modItem.InstallPath = string.Empty;
                    dialog.UpdateCurrentModProgress(50, "Rozpoczynanie instalacji nowej wersji...");

                    // INSTALL
                    // Pobierz zaktualizowaną konfigurację
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == modItem.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter dla instalacji
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            // Mapuj progress 50-100% dla install
                            int mappedProgress = 50 + (percentage * 50 / 100);
                            dialog.UpdateCurrentModProgress(mappedProgress, $"Instalowanie: {message}");
                        });

                        // Diagnostics output
                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-{modItem.Name}] {message}");
                        });

                        // Silent user interaction
                        var silentUserInteraction = new InstallationSilentUserInteraction();


                        // Sprawdź platformę
                        var configBuilder = new ConfigurationBuilder()
                            .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                        var configuration = configBuilder.Build();

                        string platform = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_userInteractionService == null)
                            {
                                System.Diagnostics.Debug.WriteLine("UserInteractionService is null - cannot proceed with Epic update");
                                return false;
                            }

                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                            // Przekaż puste obiekty zamiast null
                            await epicManager.ModifyEpicAsync(updatedConfig, new object(), new object());
                        }
                        else
                        {
                            var modManager = new ModManager(configuration);
                            var callbacks = new ModManagerUserCallbacks
                            {
                                ConfirmAsync = silentUserInteraction.ShowConfirmAsync,
                                ShowErrorAsync = silentUserInteraction.ShowErrorAsync,
                                ShowInfoAsync = silentUserInteraction.ShowInfoAsync
                            };

                            await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                callbacks,
                                "steam"
                            );
                        }

                        // Aktualizuj ścieżkę instalacji w UI
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            modItem.InstallPath = updatedConfig.InstallPath;
                        });
                    }
                }

                dialog.UpdateCurrentModProgress(100, "Aktualizacja zakończona");
                await Task.Delay(500); // Krótka pauza żeby użytkownik zobaczył ukończenie
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating single mod {modItem.Name}: {ex.Message}");
                dialog.UpdateCurrentModProgress(100, $"Błąd: {ex.Message}");
                return false;
            }
        }


        private async Task ShowDetailedErrorDialogAsync(string title, Exception ex)
        {
            // Stwórz szczegółowy komunikat błędu
            var errorMessage = $"Komunikat: {ex.Message}\n\n";
            errorMessage += $"Typ błędu: {ex.GetType().Name}\n\n";

            if (ex.InnerException != null)
            {
                errorMessage += $"Błąd wewnętrzny: {ex.InnerException.Message}\n\n";
            }

            errorMessage += $"Stack Trace:\n{ex.StackTrace}";

            var dialog = new ErrorDialog(title, errorMessage);

            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
            }
        }

        private async Task RefreshModsListAsync()
        {
            await Task.Run(() =>
            {
                // Przeładuj konfigurację - może się zmieniła po dodaniu Vanilla
                var configService = new ConfigService();
                var configs = configService.LoadConfig();

                var filtered = configs
                    .Where(m => m.ModType == "full" || m.ModType == "Vanilla")
                    .ToList();

                var modItems = filtered.Select(ModItemAdapter.FromConfig).ToList();

                var vanilla = modItems.FirstOrDefault(m => m.Name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase));
                var installed = modItems.Where(m => !string.IsNullOrEmpty(m.InstallPath) && !m.Name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Name);
                var notInstalled = modItems.Where(m => string.IsNullOrEmpty(m.InstallPath) && !m.Name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)).OrderBy(m => m.Name);

                // Aktualizuj UI
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Mods.Clear();

                    if (vanilla != null)
                        Mods.Add(vanilla);

                    foreach (var mod in installed)
                        Mods.Add(mod);

                    foreach (var mod in notInstalled)
                        Mods.Add(mod);

                    System.Diagnostics.Debug.WriteLine($"UI updated with {Mods.Count} mods");
                });
            });
        }



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
                this.RaiseAndSetIfChanged(ref _selectedMod, value);
                this.RaisePropertyChanged(nameof(IsModSelected));
                this.RaisePropertyChanged(nameof(IsModPanelVisible));

                // Resetuj panele tylko gdy wybieramy inny mod (nie ten sam)
                if (value != null && (previousMod == null || previousMod.Name != value.Name))
                {
                    IsInfoPanelVisible = false;
                    IsAdditionalActionsVisible = false;
                    IsDllModificationsVisible = false;  
                    IsDllInstallDialogVisible = false;  
                }
            }
        }

        public bool IsModSelected => SelectedMod != null;

        public ReactiveCommand<Unit, Unit> TogglePaneCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallCommand { get; }
        public ReactiveCommand<Unit, Unit> UninstallCommand { get; }

        private void TogglePane()
        {
            IsPaneOpen = !IsPaneOpen;
        }

        private void LoadSavedTheme()
        {
            try
            {
                var savedTheme = ConfigManager.GetThemeSetting();
                _currentTheme = savedTheme switch
                {
                    "light" => ThemeType.Light,
                    "pink" => ThemeType.Pink,
                    _ => ThemeType.Dark
                };
                System.Diagnostics.Debug.WriteLine($"Wczytano motyw: {savedTheme} -> {_currentTheme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd wczytywania motywu: {ex.Message}");
                _currentTheme = ThemeType.Dark; // domyślnie ciemny
            }
        }

        private void ToggleTheme()
        {
            // Przełącz między trzema motywami cyklicznie
            CurrentTheme = CurrentTheme switch
            {
                ThemeType.Dark => ThemeType.Light,
                ThemeType.Light => ThemeType.Pink,
                ThemeType.Pink => ThemeType.Dark,
                _ => ThemeType.Dark
            };

            // Zapisz nowy motyw
            try
            {
                var themeValue = CurrentTheme switch
                {
                    ThemeType.Light => "light",
                    ThemeType.Pink => "pink",
                    _ => "dark"
                };
                ConfigManager.SaveThemeSetting(themeValue);
                System.Diagnostics.Debug.WriteLine($"Zapisano motyw: {themeValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd zapisywania motywu: {ex.Message}");
            }
        }

        private void ApplyTheme(ThemeType theme)
        {
            try
            {
                if (Application.Current == null)
                    return;

                // Usuń poprzedni słownik jeśli był załadowany
                if (_currentThemeDictionary != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_currentThemeDictionary);

                var uri = theme switch
                {
                    ThemeType.Light => _lightThemeUri,
                    ThemeType.Pink => _pinkThemeUri,
                    _ => _darkThemeUri
                };

                var loaded = AvaloniaXamlLoader.Load(uri);

                if (loaded is ResourceDictionary newDict)
                {
                    Application.Current.Resources.MergedDictionaries.Add(newDict);
                    _currentThemeDictionary = newDict;
                }

                // Ustaw również systemowy motyw dla kompatybilności
                var systemTheme = theme switch
                {
                    ThemeType.Light => ThemeVariant.Light,
                    ThemeType.Pink => ThemeVariant.Light, // Różowy bazuje na jasnym
                    _ => ThemeVariant.Dark
                };

                Application.Current.RequestedThemeVariant = systemTheme;

                System.Diagnostics.Debug.WriteLine($"Applied theme: {theme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}");
                // Fallback - użyj domyślnego motywu
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                }
            }
        }


        private void LoadAppVersion()
        {
            var configService = new ConfigService();
            AppVersion = configService.GetAppVersion();
        }

        private void ShowAdditionalActions()
        {
            IsAdditionalActionsVisible = !IsAdditionalActionsVisible;

            if (IsAdditionalActionsVisible)
            {
                IsInfoPanelVisible = false;
                IsDllModificationsVisible = false;  // Dodaj
                IsDllInstallDialogVisible = false;  // Dodaj
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void ShowInfo()
        {
            IsInfoPanelVisible = !IsInfoPanelVisible;

            if (IsInfoPanelVisible)
            {
                IsAdditionalActionsVisible = false;
                IsDllModificationsVisible = false;  // Dodaj
                IsDllInstallDialogVisible = false;  // Dodaj
                SelectedMod = null;
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private async Task ShowLobbySetDialog()
        {
            var dialog = new LobbySetDialog();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                if (dialog.DialogResult)
                {
                    await ShowMessageAsync("Sukces", $"Ustawiono liczbę graczy na {dialog.PlayerCount}");
                }
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ShowMessageAsync called - Title: '{title}', Message: '{message}'");
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: StackTrace: {Environment.StackTrace}");

            var dialog = new MessageDialog(title, message);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
        }

        private async Task ExecuteFixBlackScreenAsync()
        {
            try
            {
                // Dialog potwierdzenia na UI thread
                var confirmResult = await ShowConfirmDialogAsync(
                    "Czy jesteś pewny, że chcesz zrestartować ustawienia gry?",
                    "Potwierdzenie");

                if (!confirmResult)
                    return;

                // Operacje na plikach w background thread
                await Task.Run(() => FixBlackScreen.ExecuteFixCore());

                // Dialog sukcesu na UI thread
                await ShowMessageAsync("Sukces", "Ustawienia gry zostały zresetowane.");
            }
            catch (Exception ex)
            {
                // Dialog błędu na UI thread
                await ShowErrorDialogAsync($"Wystąpił błąd podczas resetowania ustawień: {ex.Message}", "Błąd");
            }
        }

        private async Task<bool> ShowConfirmDialogAsync(string message, string title)
        {
            var dialog = new ConfirmDialog(title, message);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                return dialog.Result;
            }
            return false;
        }

        private async Task ShowErrorDialogAsync(string message, string title)
        {
            var dialog = new MessageDialog(title, message);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
        }

        // Placeholder implementacje dla Prompt i SelectFile
        private async Task<string?> ShowPromptDialogAsync(string message, string title)
        {
            await Task.CompletedTask;
            return null;
        }

        public async Task LaunchAsync()
        {
            await Task.Run(() => Launch());
        }

        private async void Launch()
        {
            // 1) Walidacja wyboru
            if (SelectedMod == null)
            {
                await ShowErrorDialogAsync("Nie wybrano wersji gry do uruchomienia.", "Błąd");
                return;
            }

            // Pobierz konfigurację wybranego moda
            var configService = new ConfigService();
            var configs = configService.LoadConfig();
            var modConfig = configs.FirstOrDefault(c => c.ModName == SelectedMod.Name);

            if (modConfig == null)
            {
                await ShowErrorDialogAsync("Brak wybranej wersji do uruchomienia.", "Błąd");
                return;
            }

            // Sprawdź czy mod jest zainstalowany
            if (string.IsNullOrEmpty(modConfig.InstallPath))
            {
                await ShowErrorDialogAsync("Wybrany mod nie jest zainstalowany.", "Błąd");
                return;
            }

            // 2) Włączamy UI „busy"
            var currentSelectedMod = SelectedMod;
            currentSelectedMod.ShowProgress = true;
            currentSelectedMod.IsInstalling = true; // Używamy tej flagi do wyłączenia przycisków
            var statsChoice = await HandleSUStatsChoice(modConfig);
            await RemoveApiSetFileIfExists(modConfig);

            if (statsChoice == null)
            {
                // Użytkownik anulował - przerwij uruchamianie
                
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;
                return;
            }

            if (statsChoice.Value)
            {
                // Użytkownik chce statystyki - utwórz plik
                await CreateApiSetFileIfNeeded(modConfig);
            }
            else
            {
                // Użytkownik nie chce statystyk - usuń plik i wyczyść wybór
                await RemoveApiSetFileIfExists(modConfig);
                ClearSUStatsSelection();
            }
            try
            {
                // 3) Ustalamy tryb uruchomienia
                var configBuilder = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                var configuration = configBuilder.Build();

                string mode = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                if (mode.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    // 4) Obsługa Epic z progress parserem
                    currentSelectedMod.InstallStatusMessage = "Uruchamianie Epic...";
                    currentSelectedMod.InstallProgress = 5;

                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Launch Epic] {message}");
                    });

                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    epicManager.ResetErrorState();

                    epicManager.EpicLaunchError += async (modName, logContent) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await ShowEpicErrorDialogAsync(currentSelectedMod.Name, logContent);
                        });
                    };

                    // NOWE: Subskrybuj progress z legendary
                    epicManager.ProgressChanged += (percentage, message) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            currentSelectedMod.InstallProgress = percentage;
                            currentSelectedMod.InstallStatusMessage = message;
                        });
                    };

                    // Podpinamy event do przekazywania linii do debug output
                    epicManager.LegendaryOutput += (message) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                            System.Diagnostics.Debug.WriteLine($"[Legendary] {message}")
                        );
                    };

                    // Wywołujemy Epic
                    await epicManager.HandleEpicGameAsync(modConfig);
                }
                else
                {
                    // 5) Obsługa Steam / vanilla
                    currentSelectedMod.InstallStatusMessage = "Uruchamiam Steam...";
                    currentSelectedMod.InstallProgress = 25;

                    string exePath = Path.Combine(modConfig.InstallPath, "Among Us.exe");
                    string steamAppIdPath = Path.Combine(modConfig.InstallPath, "steam_appid.txt");

                    try
                    {
                        // Zapisz steam_appid.txt
                        await File.WriteAllTextAsync(steamAppIdPath, "945360");

                        currentSelectedMod.InstallProgress = 50;

                        if (File.Exists(exePath))
                        {
                            currentSelectedMod.InstallStatusMessage = "Uruchamiam grę...";
                            currentSelectedMod.InstallProgress = 75;

                            // Uruchom Steam
                            Process.Start(new ProcessStartInfo("steam://") { UseShellExecute = true });

                            // Poczekaj chwilę i uruchom grę
                            await Task.Delay(1000);
                            Process.Start(exePath);

                            currentSelectedMod.InstallProgress = 100;
                            currentSelectedMod.InstallStatusMessage = "Gra uruchomiona";

                            // Poczekaj chwilę żeby użytkownik zobaczył komunikat
                            await Task.Delay(1500);
                        }
                        else
                        {
                            await ShowErrorDialogAsync(
                                "Nie znaleziono pliku Among Us.exe w wybranej ścieżce.",
                                "Błąd");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Problem z utworzeniem pliku steam_appid.txt: {ex.Message}");

                        await ShowErrorDialogAsync(
                            $"Problem z utworzeniem pliku steam_appid.txt: {ex.Message}. " +
                            "Próba uruchomienia przez Steam URI.",
                            "Błąd");

                        try
                        {
                            currentSelectedMod.InstallStatusMessage = "Uruchamiam przez Steam URI...";
                            currentSelectedMod.InstallProgress = 75;

                            Process.Start(new ProcessStartInfo("steam://rungameid/945360")
                            {
                                UseShellExecute = true
                            });

                            currentSelectedMod.InstallProgress = 100;
                            currentSelectedMod.InstallStatusMessage = "Uruchomiono przez Steam";
                            await Task.Delay(1500);
                        }
                        catch (Exception uriEx)
                        {
                            await ShowErrorDialogAsync(
                                $"Nie udało się uruchomić gry przez Steam URI: {uriEx.Message}",
                                "Błąd");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Launch] Exception: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas uruchamiania gry: {ex.Message}", "Błąd uruchamiania");
            }
            finally
            {
                // 6) Wyłączamy UI „busy"
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;
            }
        }

        private async Task ShowEpicErrorDialogAsync(string modName, string logContent)
        {
            try
            {
                var dialog = new EpicErrorDialog(modName, logContent);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing Epic error dialog: {ex.Message}");
                // Fallback - pokaż standardowy dialog błędu
                await ShowErrorDialogAsync($"Błąd uruchamiania gry Epic: {ex.Message}\n\nLog:\n{logContent}", "Błąd Epic Games");
            }
        }

        private void ShowRoles()
        {
            if (SelectedMod != null)
            {
                // SelectedMod.Id to ID moda z config.json
                // Przekazujemy to samo ID jako configId i modId
                var rolesWindow = new RolesWindow(SelectedMod.Id, SelectedMod.Id, SelectedMod.Name);
                rolesWindow.Show();
            }
        }

        private async void Install()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;

            try
            {
                // Ustaw flagę instalacji
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.IsInstalling = true;
                    currentSelectedMod.ShowProgress = true;
                });

                // Pobierz konfigurację moda
                var configService = new ConfigService();
                var allConfigs = configService.LoadConfig();
                var modConfig = allConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig == null)
                {
                    await _userInteractionService.ShowErrorAsync("Nie znaleziono konfiguracji moda.", "Błąd");
                    return;
                }

                string platform = DeterminePlatform();
                bool success = false;

                // W metodzie Install(), w sekcji Epic, zamień tę część:

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Install Epic] {message}");
                    });

                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    // NOWE: Wyczyść log przed instalacją
                    epicManager.ClearLegendaryLog();

                    // Subskrybuj progress
                    epicManager.ProgressChanged += (percentage, message) => {
                        Dispatcher.UIThread.InvokeAsync(() => {
                            currentSelectedMod.InstallProgress = percentage;
                            currentSelectedMod.InstallStatusMessage = message;
                            System.Diagnostics.Debug.WriteLine($"[Epic Progress] {percentage}% - {message}");
                        });
                    };

                    // Subskrybuj zakończenie instalacji
                    epicManager.InstallationCompleted += (completedModConfig) => {
                        Dispatcher.UIThread.InvokeAsync(async () => {
                            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: InstallationCompleted callback for: {completedModConfig.ModName}");
                            await RefreshModsListAsync();
                            SelectedMod = Mods.FirstOrDefault(m => m.Name == completedModConfig.ModName);
                        });
                    };

                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Calling ModifyEpicAsync for {modConfig.ModName}");
                        // Przekaż null zamiast epicProgressCallback
                        await epicManager.ModifyEpicAsync(modConfig, null, null);
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync returned successfully");
                        var dllSelectionWindow = new Window
                        {
                            Title = $"Dodatkowe modyfikacje DLL dla {currentSelectedMod.Name}",
                            Width = 650,
                            Height = 600,
                            Content = new DllModSelectionView
                            {
                                DataContext = new DllModSelectionViewModel(
                                    _dllModificationService, 
                                    new ModConfiguration 
                                    {
                                        ModName = currentSelectedMod.Name,
                                        Description = currentSelectedMod.Description,
                                        InstallPath = currentSelectedMod.InstallPath ?? ""
                                    }, 
                                    "epic"
                                )
                            }
                        };
                        System.Diagnostics.Debug.WriteLine($"DEBUG Epic Path: {currentSelectedMod.InstallPath}");
                        dllSelectionWindow.Show();
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync threw exception: {ex.Message}");
                        success = false;
                    }
                }

                else
                {
                    // Steam installation
                    var progressReporter = new UIProgressReporter((percentage, message) =>
                    {
                        currentSelectedMod.InstallProgress = percentage;
                        currentSelectedMod.InstallStatusMessage = message;
                    });

                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Install Steam] {message}");
                    });

                    var configBuilder = new ConfigurationBuilder()
                        .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    var configuration = configBuilder.Build();

                    var modManager = new ModManager(configuration);

                    try
                    {
                        var callbacks = new ModManagerUserCallbacks
                        {
                            ConfirmAsync = _userInteractionService.ShowConfirmAsync,
                            ShowErrorAsync = _userInteractionService.ShowErrorAsync,
                            ShowInfoAsync = _userInteractionService.ShowInfoAsync
                        };

                        await modManager.ModifyAsync(
                            modConfig,
                            allConfigs,
                            progressReporter,
                            diagnosticsOutput,
                            callbacks,
                            "steam"
                        );

                        success = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Install Steam] Exception: {ex.Message}");
                        success = false;
                    }

                    // Aktualizuj ścieżkę instalacji w UI
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        currentSelectedMod.InstallPath = modConfig.InstallPath;
                    });

                    // Odświeżenie dla Steam
                    if (success)
                    {
                        RefreshModsSortingKeepSelection(currentSelectedMod);
                        var dllSelectionWindow = new Window
                        {
                            Title = $"Dodatkowe modyfikacje DLL dla {currentSelectedMod.Name}",
                            Width = 650,
                            Height = 600,
                            Content = new DllModSelectionView
                            {
                                DataContext = new DllModSelectionViewModel(
                                    _dllModificationService, 
                                    ModItemAdapter.ToConfig(currentSelectedMod),
                                    "steam" // Jawnie przekaż "steam"
                                )
                            }
                        };
                        dllSelectionWindow.Show();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Install] Exception: {ex.Message}");
            }
            finally
            {
                // Ukryj progress bar
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.ShowProgress = false;
                    currentSelectedMod.InstallProgress = 0;
                    currentSelectedMod.InstallStatusMessage = string.Empty;
                    currentSelectedMod.IsInstalling = false;
                });
            }
        }


        public string DeterminePlatform()
        {
            try
            {
                // Użyj ConfigManager który już istnieje
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configRepo = new ConfigRepository(exeDir);
                var appSettings = configRepo.LoadAppSettings();

                if (appSettings != null &&
                    appSettings.TryGetValue("Configuration", out var configObj) &&
                    configObj is JsonElement configElement &&
                    configElement.TryGetProperty("Mode", out var modeElement))
                {
                    string mode = modeElement.GetString() ?? "steam";
                    return mode.Equals("epic", StringComparison.OrdinalIgnoreCase) ? "Epic" : "Steam";
                }

                return "Steam"; // Fallback
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DeterminePlatform: {ex.Message}");
                return "Steam"; // Fallback
            }
        }

        private async void Update()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;

            try
            {
                // Pokaż progress bar
                currentSelectedMod.ShowProgress = true;
                currentSelectedMod.IsInstalling = true;
                currentSelectedMod.InstallStatusMessage = "Sprawdzanie aktualizacji...";
                currentSelectedMod.InstallProgress = 10;

                // 1. Sprawdź czy jest dostępna aktualizacja
                var configService = new ConfigService();
                var updatedModConfig = await configService.CheckSingleModUpdateAsync(currentSelectedMod.Name);

                if (updatedModConfig == null)
                {
                    currentSelectedMod.InstallStatusMessage = "Brak dostępnych aktualizacji";
                    currentSelectedMod.InstallProgress = 100;

                    await Task.Delay(2000);
                    await ShowMessageAsync("Informacja", $"Mod '{currentSelectedMod.Name}' jest już w najnowszej wersji.");
                    return;
                }

                // 2. Potwierdź aktualizację z użytkownikiem
                bool confirmed = await ShowConfirmDialogAsync(
                    $"Dostępna jest nowa wersja moda '{currentSelectedMod.Name}':\n\n" +
                    $"Obecna wersja: {currentSelectedMod.ModVersion}\n" +
                    $"Nowa wersja: {updatedModConfig.ModVersion}\n\n" +
                    $"Czy chcesz zaktualizować mod?",
                    "Dostępna aktualizacja"
                );

                if (!confirmed)
                    return;

                currentSelectedMod.InstallProgress = 20;

                // 3. Aktualizuj konfigurację w pliku
                currentSelectedMod.InstallStatusMessage = "Aktualizowanie konfiguracji...";
                bool configUpdated = await configService.UpdateSingleModConfigAsync(updatedModConfig);

                if (!configUpdated)
                {
                    await ShowErrorDialogAsync("Nie udało się zaktualizować konfiguracji moda.", "Błąd aktualizacji");
                    return;
                }

                currentSelectedMod.InstallProgress = 30;

                // 4. Przeładuj konfigurację i zaktualizuj UI
                currentSelectedMod.InstallStatusMessage = "Przeładowywanie konfiguracji...";
                await Task.Run(() =>
                {
                    var configs = configService.LoadConfig();
                    _loadedConfigs = configs;
                });

                // Zaktualizuj właściwości ModItem z nową konfiguracją
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    currentSelectedMod.ModVersion = updatedModConfig.ModVersion;
                    currentSelectedMod.AmongVersion = updatedModConfig.AmongVersion;
                    currentSelectedMod.Description = updatedModConfig.Description;
                    currentSelectedMod.GitHubRepoOrLink = updatedModConfig.GitHubRepoOrLink;
                    currentSelectedMod.EpicGitHubRepoOrLink = updatedModConfig.EpicGitHubRepoOrLink;
                });

                currentSelectedMod.InstallProgress = 40;

                // 5. Jeżeli mod jest zainstalowany - wykonaj reinstalację
                if (!string.IsNullOrEmpty(currentSelectedMod.InstallPath))
                {
                    // UNINSTALL - użyj tej samej logiki co w metodzie Uninstall()
                    currentSelectedMod.InstallStatusMessage = "Odinstalowywanie starej wersji...";

                    if (Directory.Exists(currentSelectedMod.InstallPath))
                    {
                        Directory.Delete(currentSelectedMod.InstallPath, true);
                        System.Diagnostics.Debug.WriteLine($"Usunięto katalog: {currentSelectedMod.InstallPath}");
                    }

                    // Aktualizuj konfigurację - usuń ścieżkę instalacji
                    var configs = configService.LoadConfig();
                    var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);
                    if (modConfig != null)
                    {
                        modConfig.InstallPath = string.Empty;
                        ConfigManager.SaveConfig(configs);
                    }

                    currentSelectedMod.InstallPath = string.Empty;
                    currentSelectedMod.InstallProgress = 60;

                    // INSTALL - użyj tej samej logiki co w metodzie Install()
                    currentSelectedMod.InstallStatusMessage = "Instalowanie nowej wersji...";

                    // Pobierz zaktualizowaną konfigurację
                    var updatedConfigs = configService.LoadConfig();
                    var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                    if (updatedConfig != null)
                    {
                        // Progress reporter
                        var progressReporter = new UIProgressReporter((percentage, message) =>
                        {
                            // Mapuj progress 60-100% dla install
                            currentSelectedMod.InstallProgress = 60 + (percentage * 40 / 100);
                            currentSelectedMod.InstallStatusMessage = $"Instalowanie: {message}";
                        });

                        // Diagnostics output
                        var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Update-Install] {message}");
                        });

                        // Silent user interaction
                        var silentUserInteraction = new InstallationSilentUserInteraction();

                        // Sprawdź platformę
                        var configBuilder = new ConfigurationBuilder()
                            .SetBasePath(Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory)
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                        var configuration = configBuilder.Build();

                        string platform = configuration.GetSection("Configuration")["Mode"] ?? "steam";

                        if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                        {
                            // Epic installation
                            var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                            var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                            await epicManager.ModifyEpicAsync(updatedConfig, null, null);
                        }
                        else
                        {
                            // Steam installation
                            var modManager = new ModManager(configuration);
                            var callbacks = new ModManagerUserCallbacks
                            {
                                ConfirmAsync = silentUserInteraction.ShowConfirmAsync,
                                ShowErrorAsync = silentUserInteraction.ShowErrorAsync,
                                ShowInfoAsync = silentUserInteraction.ShowInfoAsync
                            };

                            await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                callbacks,
                                "steam"
                            );
                        }

                        // Aktualizuj ścieżkę instalacji w UI
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            currentSelectedMod.InstallPath = updatedConfig.InstallPath;
                        });
                    }
                }

                // 6. Finalizacja
                currentSelectedMod.InstallProgress = 100;
                currentSelectedMod.InstallStatusMessage = "Aktualizacja zakończona";

                // Odśwież sortowanie zachowując zaznaczenie
                RefreshModsSortingKeepSelection(currentSelectedMod);

                await Task.Delay(1500);
                await ShowMessageAsync("Sukces", $"Mod '{currentSelectedMod.Name}' został pomyślnie zaktualizowany do wersji {updatedModConfig.ModVersion}.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Exception: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas aktualizacji: {ex.Message}", "Błąd aktualizacji");
            }
            finally
            {
                // Ukryj progress bar
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;
            }
        }

        private void ClearEpicLogsOnStartup()
        {
            try
            {
                string platform = DeterminePlatform();
                if (platform.Equals("Epic", StringComparison.OrdinalIgnoreCase))
                {
                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Epic Startup] {message}");
                    });

                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    epicManager.ClearLegendaryLog();
                    System.Diagnostics.Debug.WriteLine("Epic legendary log cleared on startup");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear Epic logs on startup: {ex.Message}");
            }
        }

        private async void Uninstall()
        {
            if (SelectedMod == null || SelectedMod.IsInstalling)
                return;

            var currentSelectedMod = SelectedMod;

            try
            {
                if (string.IsNullOrEmpty(currentSelectedMod.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("Mod nie jest zainstalowany.");
                    return;
                }

                // Pokaż progress bar
                currentSelectedMod.ShowProgress = true;
                currentSelectedMod.IsInstalling = true;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = "Rozpoczynanie odinstalowywania...";

                // Sprawdź czy użytkownik potwierdza
                bool confirmed = await _userInteractionService.ShowConfirmAsync(
                    $"Czy na pewno chcesz odinstalować mod '{currentSelectedMod.Name}'?",
                    "Potwierdzenie odinstalowania"
                );

                if (!confirmed)
                    return;

                currentSelectedMod.InstallProgress = 25;
                currentSelectedMod.InstallStatusMessage = "Usuwanie plików...";

                // Usuń katalog instalacji
                bool deleteSuccess = await SafeDeleteDirectoryAsync(currentSelectedMod.InstallPath, currentSelectedMod.Name);
                if (!deleteSuccess)
                {
                    await ShowErrorDialogAsync(
                        $"Nie udało się całkowicie usunąć katalogu moda '{currentSelectedMod.Name}'.\n\n" +
                        $"Katalog: {currentSelectedMod.InstallPath}\n\n" +
                        "Niektóre pliki mogą nadal istnieć. Spróbuj usunąć je ręcznie lub zrestartować komputer i spróbować ponownie.",
                        "Ostrzeżenie - Niepełne usunięcie"
                    );

                    // Mimo niepełnego usunięcia, kontynuuj proces odinstalowania
                    System.Diagnostics.Debug.WriteLine($"OSTRZEŻENIE: Niepełne usunięcie katalogu: {currentSelectedMod.InstallPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Pomyślnie usunięto katalog: {currentSelectedMod.InstallPath}");
                }
            

                currentSelectedMod.InstallProgress = 75;
                currentSelectedMod.InstallStatusMessage = "Aktualizowanie konfiguracji...";

                // Aktualizuj konfigurację
                var configService = new ConfigService();
                var configs = configService.LoadConfig();
                var modConfig = configs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                if (modConfig != null)
                {
                    modConfig.InstallPath = string.Empty;
                    ConfigManager.SaveConfig(configs);
                }

                // Aktualizuj UI
                currentSelectedMod.InstallPath = string.Empty;
                currentSelectedMod.InstallProgress = 100;
                currentSelectedMod.InstallStatusMessage = "Odinstalowanie zakończone";

                // Odśwież sortowanie bez utraty zaznaczenia
                RefreshModsSortingKeepSelection(currentSelectedMod);

                System.Diagnostics.Debug.WriteLine($"[Uninstall] SUCCESS: Odinstalowanie moda '{currentSelectedMod.Name}' zakończone pomyślnie");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Uninstall] Exception: {ex.Message}");
                currentSelectedMod.InstallStatusMessage = $"Błąd: {ex.Message}";
            }
            finally
            {
                // Ukryj progress bar
                currentSelectedMod.ShowProgress = false;
                currentSelectedMod.InstallProgress = 0;
                currentSelectedMod.InstallStatusMessage = string.Empty;
                currentSelectedMod.IsInstalling = false;
            }
        }

        private async Task<bool> SafeDeleteDirectoryAsync(string directoryPath, string modName = "")
        {
            if (!Directory.Exists(directoryPath))
            {
                System.Diagnostics.Debug.WriteLine($"Katalog nie istnieje: {directoryPath}");
                return true;
            }

            try
            {
                // Pierwsza próba - standardowe usunięcie
                System.Diagnostics.Debug.WriteLine($"Próba standardowego usunięcia katalogu: {directoryPath}");
                Directory.Delete(directoryPath, true);

                // Sprawdź czy katalog został usunięty
                await Task.Delay(500); // Krótka pauza na operacje systemowe

                if (!Directory.Exists(directoryPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Katalog został pomyślnie usunięty: {directoryPath}");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"Katalog nadal istnieje po standardowym usunięciu: {directoryPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas standardowego usuwania: {ex.Message}");
            }

            // Druga próba - force delete
            bool forceDeleteSuccess = await ForceDeleteDirectoryAsync(directoryPath);
            if (forceDeleteSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"Katalog został usunięty przez force delete: {directoryPath}");
                return true;
            }

            // Trzecia próba - z podniesieniem uprawnień
            bool elevatedSuccess = await TryDeleteWithElevatedPermissionsAsync(directoryPath, modName);
            if (elevatedSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"Katalog został usunięty z podwyższonymi uprawnieniami: {directoryPath}");
                return true;
            }

            // Jeśli wszystko zawiodło
            System.Diagnostics.Debug.WriteLine($"BŁĄD: Nie udało się usunąć katalogu: {directoryPath}");
            return false;
        }

        private async Task<bool> ForceDeleteDirectoryAsync(string directoryPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Rozpoczynanie force delete dla: {directoryPath}");

                // Usuń atrybut tylko do odczytu ze wszystkich plików i folderów
                await Task.Run(() => RemoveReadOnlyAttributes(directoryPath));

                // Zamknij wszystkie procesy które mogą blokować pliki
                await Task.Run(() => KillProcessesUsingDirectory(directoryPath));

                // Poczekaj chwilę na zwolnienie zasobów
                await Task.Delay(1000);

                // Spróbuj usunąć ponownie
                Directory.Delete(directoryPath, true);

                // Sprawdź rezultat
                await Task.Delay(500);
                return !Directory.Exists(directoryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas force delete: {ex.Message}");
                return false;
            }
        }

        private void RemoveReadOnlyAttributes(string directoryPath)
        {
            try
            {
                // Usuń atrybut ReadOnly z katalogu głównego
                var dirInfo = new DirectoryInfo(directoryPath);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }

                // Usuń atrybut ReadOnly ze wszystkich plików
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Nie udało się usunąć atrybutu ReadOnly z pliku {file}: {ex.Message}");
                    }
                }

                // Usuń atrybut ReadOnly ze wszystkich podkatalogów
                foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var subDirInfo = new DirectoryInfo(dir);
                        if (subDirInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            subDirInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Nie udało się usunąć atrybutu ReadOnly z katalogu {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania atrybutów ReadOnly: {ex.Message}");
            }
        }

        private void KillProcessesUsingDirectory(string directoryPath)
        {
            try
            {
                // Lista procesów które mogą blokować pliki Among Us
                string[] processesToKill = { "Among Us", "AmongUs", "Among_Us" };

                foreach (string processName in processesToKill)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Sprawdź czy proces używa plików z naszego katalogu
                            if (process.MainModule?.FileName?.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                System.Diagnostics.Debug.WriteLine($"Zamykanie procesu {processName} (PID: {process.Id})");
                                process.Kill();
                                process.WaitForExit(5000); // Czekaj maksymalnie 5 sekund
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Nie udało się zamknąć procesu {processName}: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas zamykania procesów: {ex.Message}");
            }
        }

        private async Task<bool> TryDeleteWithElevatedPermissionsAsync(string directoryPath, string modName)
        {
            try
            {
                // Pokaż dialog użytkownikowi
                bool userConfirmed = await ShowConfirmDialogAsync(
                    $"Nie udało się usunąć katalogu moda '{modName}' standardowymi metodami.\n\n" +
                    $"Katalog: {directoryPath}\n\n" +
                    "Czy chcesz spróbować usunąć go z podwyższonymi uprawnieniami?\n" +
                    "(Może pojawić się okno UAC)",
                    "Wymagane podwyższone uprawnienia"
                );

                if (!userConfirmed)
                {
                    return false;
                }

                // Tylko na Windows
                if (!OperatingSystem.IsWindows())
                {
                    await ShowErrorDialogAsync("Usuwanie z podwyższonymi uprawnieniami jest dostępne tylko na Windows.", "Błąd");
                    return false;
                }

                return await DeleteWithElevatedPermissionsWindows(directoryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas próby usunięcia z podwyższonymi uprawnieniami: {ex.Message}");
                return false;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task<bool> DeleteWithElevatedPermissionsWindows(string directoryPath)
        {
            try
            {
                // Użyj PowerShell z uprawnieniami administratora
                string script = $@"
            try {{
                if (Test-Path '{directoryPath}') {{
                    Remove-Item -Path '{directoryPath}' -Recurse -Force -ErrorAction Stop
                    Write-Output 'SUCCESS: Directory deleted'
                }} else {{
                    Write-Output 'SUCCESS: Directory does not exist'
                }}
            }} catch {{
                Write-Output ""ERROR: $($_.Exception.Message)""
            }}
        ";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{script}\"",
                    UseShellExecute = true,
                    Verb = "runas", // Wymusza uruchomienie jako administrator
                    CreateNoWindow = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Nie udało się uruchomić PowerShell z uprawnieniami administratora");
                        return false;
                    }

                    // Czekaj na zakończenie procesu
                    await Task.Run(() => process.WaitForExit());

                    // Sprawdź czy katalog został usunięty
                    await Task.Delay(1000);
                    bool success = !Directory.Exists(directoryPath);

                    System.Diagnostics.Debug.WriteLine($"PowerShell z uprawnieniami administratora - sukces: {success}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania z PowerShell: {ex.Message}");

                // Jeśli PowerShell zawiódł, spróbuj cmd
                return await DeleteWithCmdElevated(directoryPath);
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async Task<bool> DeleteWithCmdElevated(string directoryPath)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir /s /q \"{directoryPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null) return false;

                    await Task.Run(() => process.WaitForExit());
                    await Task.Delay(1000);

                    return !Directory.Exists(directoryPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd podczas usuwania z CMD: {ex.Message}");
                return false;
            }
        }


        private async void ShowAppSettings()
        {
            try
            {
                var settingsWindow = new AppSettingsWindow();

                // Bezpieczne rzutowanie z sprawdzeniem null
                if (settingsWindow.DataContext is not AppSettingsViewModel settingsViewModel)
                {
                    System.Diagnostics.Debug.WriteLine("Error: AppSettingsWindow DataContext is not AppSettingsViewModel");
                    await ShowErrorDialogAsync("Błąd inicjalizacji okna ustawień.", "Błąd");
                    return;
                }

                // Subskrybuj event zapisania ustawień
                settingsViewModel.SettingsSaved += OnSettingsSaved;

                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await settingsWindow.ShowDialog(mainWindow);
                }

                // Odsubskrybuj po zamknięciu okna
                settingsViewModel.SettingsSaved -= OnSettingsSaved;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening settings window: {ex.Message}");
                await ShowErrorDialogAsync($"Nie udało się otworzyć okna ustawień: {ex.Message}", "Błąd");
            }
        }


        private void OnSettingsSaved()
        {
            System.Diagnostics.Debug.WriteLine("Settings were saved - refreshing application state");

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    // Wymuś przeładowanie ustawień ścieżki
                    PathSettings.RefreshSettings();

                    // Wymuś przeładowanie ustawień deweloperskich
                    DeveloperModeSettings.RefreshSettings();

                    // Przeładuj listę modów
                    await RefreshModsListAsync();
                    this.RaisePropertyChanged(nameof(IsDeveloperMode));



                    System.Diagnostics.Debug.WriteLine($"Application refreshed with new settings - ModsInstallPath: {PathSettings.ModsInstallPath}, DeveloperMode: {DeveloperModeSettings.IsEnabled}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error refreshing after settings change: {ex.Message}");
                }
            });
        }

        private void ShowDllModifications()
        {
            IsDllModificationsVisible = !IsDllModificationsVisible;

            if (IsDllModificationsVisible)
            {
                IsInfoPanelVisible = false;
                IsAdditionalActionsVisible = false;
                SelectedMod = null;
                IsDllInstallDialogVisible = false;

                LoadDllMods();
            }

            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void SelectDllMod(ModItem dllMod)
        {
            SelectedDllMod = dllMod;
            IsDllInstallDialogVisible = true;
            LoadAvailableFullMods();
        }

        private void CloseDllDialog()
        {
            IsDllInstallDialogVisible = false;
            SelectedDllMod = null;
            ModsWithDllInstalled.Clear();
            ModsWithoutDllInstalled.Clear();
        }

        private void LoadDllMods()
        {
            try
            {
                var dllConfigs = _dllModificationService.GetDllMods();
                var dllModItems = dllConfigs.Select(ModItemAdapter.FromConfig).ToList();

                DllMods.Clear();
                foreach (var mod in dllModItems)
                {
                    DllMods.Add(mod);
                }

                System.Diagnostics.Debug.WriteLine($"Loaded {DllMods.Count} DLL mods");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading DLL mods: {ex.Message}");
            }
        }

        private void LoadAvailableFullMods()
        {
            if (SelectedDllMod == null) return;

            try
            {
                string platform = DeterminePlatform();
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);

                // Załaduj mody z zainstalowaną DLL
                var modsWithDll = _dllModificationService.GetModsWithDllInstalled(dllConfig, platform);
                var modsWithDllItems = modsWithDll.Select(ModItemAdapter.FromConfig).ToList();

                ModsWithDllInstalled.Clear();
                foreach (var mod in modsWithDllItems)
                {
                    ModsWithDllInstalled.Add(mod);
                }

                // Załaduj mody bez zainstalowanej DLL
                var modsWithoutDll = _dllModificationService.GetModsWithoutDllInstalled(dllConfig, platform);
                var modsWithoutDllItems = modsWithoutDll.Select(ModItemAdapter.FromConfig).ToList();

                ModsWithoutDllInstalled.Clear();
                foreach (var mod in modsWithoutDllItems)
                {
                    ModsWithoutDllInstalled.Add(mod);
                }

                System.Diagnostics.Debug.WriteLine($"Found {ModsWithDllInstalled.Count} mods with DLL installed");
                System.Diagnostics.Debug.WriteLine($"Found {ModsWithoutDllInstalled.Count} mods without DLL installed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading available full mods: {ex.Message}");
            }
        }

        private async Task InstallDllToMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null) return;

            try
            {
                // Konwertuj ModItem na ModConfiguration
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);

                string platform = DeterminePlatform();

                bool success = await _dllModificationService.InstallDllToModAsync(dllConfig, targetConfig, platform);

                if (success)
                {
                    LoadAvailableFullMods(); // Odśwież listę
                }
                else
                {
                    await ShowErrorDialogAsync("Nie udało się zainstalować modyfikacji DLL.", "Błąd instalacji");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error installing DLL: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd instalacji");
            }
        }

        private async Task UninstallDllFromMod(ModItem targetMod)
        {
            if (SelectedDllMod == null || targetMod == null) return;

            try
            {
                bool confirm = await ShowConfirmDialogAsync(
                    $"Czy na pewno chcesz usunąć mod DLL '{SelectedDllMod.Name}' z '{targetMod.Name}'?",
                    "Potwierdzenie usunięcia");

                if (!confirm) return;

                // Konwertuj ModItem na ModConfiguration
                var dllConfig = ModItemAdapter.ToConfig(SelectedDllMod);
                var targetConfig = ModItemAdapter.ToConfig(targetMod);

                string platform = DeterminePlatform();

                bool success = await _dllModificationService.UninstallDllFromModAsync(dllConfig, targetConfig, platform);

                if (success)
                {
                    LoadAvailableFullMods(); // Odśwież listę
                }
                else
                {
                    await ShowErrorDialogAsync("Nie udało się usunąć modyfikacji DLL.", "Błąd usuwania");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error uninstalling DLL: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas usuwania: {ex.Message}", "Błąd usuwania");
            }
        }

        private async Task CreateApiSetFileIfNeeded(ModConfiguration modConfig)
        {
            try
            {
                // Sprawdź czy użytkownik wybrał jakiś serwer SUStats
                if (!SUStatsConfigViewModel.HasSelectedServer)
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ⚠️ Brak wybranego serwera SUStats - pomijam tworzenie ApiSet.ini");
                    return;
                }

                // Pobierz dane wybranego serwera
                var serverData = SUStatsConfigViewModel.GetSelectedServerData();
                if (!serverData.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ❌ Nie udało się pobrać danych wybranego serwera");
                    return;
                }

                var (id, serverName, token, secret, endpoint) = serverData.Value;
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📊 Tworzenie konfiguracji SUStats dla serwera: {serverName}");

                // Sprawdź czy mod jest zainstalowany
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ❌ Mod nie jest zainstalowany - brak ścieżki instalacji");
                    return;
                }

                if (!Directory.Exists(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Katalog moda nie istnieje: {modConfig.InstallPath}");
                    return;
                }

                // Stwórz ścieżkę do BepInEx\plugins
                string bepInExPluginsPath = Path.Combine(modConfig.InstallPath, "BepInEx", "plugins");
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📁 Ścieżka BepInEx\\plugins: {bepInExPluginsPath}");

                // Sprawdź czy katalog BepInEx\plugins istnieje
                if (!Directory.Exists(bepInExPluginsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ⚠️ Katalog BepInEx\\plugins nie istnieje: {bepInExPluginsPath}");
                    System.Diagnostics.Debug.WriteLine("[ApiSet] 📁 Tworzenie katalogu BepInEx\\plugins...");

                    try
                    {
                        Directory.CreateDirectory(bepInExPluginsPath);
                        System.Diagnostics.Debug.WriteLine("[ApiSet] ✅ Katalog BepInEx\\plugins utworzony pomyślnie");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się utworzyć katalogu BepInEx\\plugins: {ex.Message}");
                        return;
                    }
                }

                // Ścieżka do pliku ApiSet.ini
                string apiSetPath = Path.Combine(bepInExPluginsPath, "ApiSet.ini");
                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📄 Ścieżka pliku ApiSet.ini: {apiSetPath}");

                // Diagnostics output do logowania
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] {message}");
                });

                // Zapisz plik ApiSet.ini
                bool success = await Task.Run(() =>
                    SUSModder.Core.Configuration.ApiSetManager.SaveApiSetFile(
                        apiSetPath,
                        token,
                        endpoint,
                        secret,
                        diagnosticsOutput
                    )
                );

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ✅ Konfiguracja SUStats zapisana pomyślnie");
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] 🎯 Serwer: {serverName}");
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] 📍 Lokalizacja: {apiSetPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się zapisać konfiguracji SUStats");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ BŁĄD podczas tworzenia ApiSet.ini: {ex.Message}");
            }
        }

        // Dodaj tę metodę obok CreateApiSetFileIfNeeded:

        private async Task RemoveApiSetFileIfExists(ModConfiguration modConfig)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ApiSet] 🗑️ Sprawdzanie czy usunąć plik ApiSet.ini...");

                // Sprawdź czy mod jest zainstalowany
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine("[ApiSet] ⚠️ Mod nie jest zainstalowany - brak ścieżki instalacji");
                    return;
                }

                if (!Directory.Exists(modConfig.InstallPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ⚠️ Katalog moda nie istnieje: {modConfig.InstallPath}");
                    return;
                }

                // Stwórz ścieżkę do BepInEx\plugins\ApiSet.ini
                string bepInExPluginsPath = Path.Combine(modConfig.InstallPath, "BepInEx", "plugins");
                string apiSetPath = Path.Combine(bepInExPluginsPath, "ApiSet.ini");

                System.Diagnostics.Debug.WriteLine($"[ApiSet] 📄 Sprawdzanie pliku: {apiSetPath}");

                // Diagnostics output do logowania
                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] {message}");
                });

                // Usuń plik ApiSet.ini jeśli istnieje
                bool success = await Task.Run(() =>
                    SUSModder.Core.Configuration.ApiSetManager.RemoveApiSetFile(
                        apiSetPath,
                        diagnosticsOutput
                    )
                );

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ✅ Operacja usuwania zakończona pomyślnie");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ Nie udało się usunąć pliku ApiSet.ini");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ApiSet] ❌ BŁĄD podczas usuwania ApiSet.ini: {ex.Message}");
            }
        }

        private async Task<bool?> HandleSUStatsChoice(ModConfiguration modConfig)
        {
            try
            {
                // Sprawdź czy użytkownik wybrał jakiś serwer SUStats
                if (!SUStatsConfigViewModel.HasSelectedServer)
                {
                    System.Diagnostics.Debug.WriteLine("[SUStats Choice] ⚠️ Brak wybranego serwera SUStats - pomijam dialog");
                    return true; // Kontynuuj bez statystyk
                }

                // Pobierz dane wybranego serwera
                var serverData = SUStatsConfigViewModel.GetSelectedServerData();
                if (!serverData.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine("[SUStats Choice] ❌ Nie udało się pobrać danych serwera");
                    return true; // Kontynuuj bez statystyk
                }

                var (id, serverName, token, secret, endpoint) = serverData.Value;
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] 🤔 Pokazuję dialog wyboru dla serwera: {serverName}");

                // Pokaż dialog wyboru
                var dialog = new SUStatsConfirmDialog(serverName);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);

                    if (dialog.DialogResult == true)
                    {
                        if (dialog.UseStats)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ✅ Użytkownik wybrał: TAK - uruchom z statystykami");
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SUStats Choice] 🗑️ Użytkownik wybrał: NIE - skasuj i uruchom");
                            return false;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ Użytkownik anulował uruchamianie");
                        return null; // Anulowano
                    }
                }

                return true; // Fallback - kontynuuj
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ BŁĄD podczas obsługi wyboru: {ex.Message}");
                return true; // W przypadku błędu - kontynuuj bez statystyk
            }
        }

        private void ClearSUStatsSelection()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[SUStats Choice] 🧹 Czyszczenie wyboru serwera SUStats...");

                // Wyczyść globalny wybór
                SUStatsConfigViewModel.ClearGlobalSelection();

                System.Diagnostics.Debug.WriteLine("[SUStats Choice] ✅ Wybór serwera SUStats wyczyszczony");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Choice] ❌ BŁĄD podczas czyszczenia wyboru: {ex.Message}");
            }
        }


        private void RefreshModsSortingKeepSelection(ModItem selectedMod)
        {
            var currentMods = Mods.ToList();

            var sorted = currentMods
                .OrderBy(m => !m.Name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(m => string.IsNullOrEmpty(m.InstallPath) ? 1 : 0)
                .ThenBy(m => m.Name)
                .ToList();

            Mods.Clear();
            foreach (var mod in sorted)
            {
                Mods.Add(mod);
            }

            // Przywróć zaznaczenie
            SelectedMod = Mods.FirstOrDefault(m => m.Name == selectedMod.Name);
        }

        // Dodaj klasę DebugDiagnosticsOutput
        private class DebugDiagnosticsOutput : IDiagnosticsOutput
        {
            public void Write(string message)
            {
                System.Diagnostics.Debug.WriteLine($"[ModUpdater] {message}");
            }
        }
    }
}
public class UIProgressReporter : IProgressReporter
{
    private readonly Action<int, string> _progressCallback;

    public UIProgressReporter(Action<int, string> progressCallback)
    {
        _progressCallback = progressCallback;
    }

    public void Report(int percentage, string? message = null)
    {
        var safeMessage = message ?? "Przetwarzanie...";
        Dispatcher.UIThread.InvokeAsync(() => _progressCallback(percentage, safeMessage));
    }
}

public class UIDiagnosticsOutput : IDiagnosticsOutput
{
    private readonly Action<string> _messageCallback;

    public UIDiagnosticsOutput(Action<string> messageCallback)
    {
        _messageCallback = messageCallback;
    }

    public void Write(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => _messageCallback(message));
    }
}

public class SilentUserInteractionWrapper : IUserInteraction
{
    private readonly UserInteractionService _inner;

    public SilentUserInteractionWrapper(UserInteractionService inner)
    {
        _inner = inner;
    }

    public bool Confirm(string message, string title = "") => _inner.Confirm(message, title);
    public void ShowInfo(string message, string title = "") => System.Diagnostics.Debug.WriteLine($"[Silent] Info: {message}");
    public void ShowError(string message, string title = "") => _inner.ShowError(message, title);
    public string? Prompt(string message, string title = "") => _inner.Prompt(message, title);
    public string? SelectFile(string filter, string initialDirectory = "") => _inner.SelectFile(filter, initialDirectory);

    public Task ShowInfoAsync(string message, string title = "")
    {
        System.Diagnostics.Debug.WriteLine($"[Silent] InfoAsync: {message}");
        return Task.CompletedTask;
    }
    public async Task ShowErrorAsync(string message, string title = "") => await _inner.ShowErrorAsync(message, title);
    public async Task<bool> ShowConfirmAsync(string message, string title = "") => await _inner.ShowConfirmAsync(message, title);
    public async Task<string?> ShowPromptAsync(string message, string title = "") => await _inner.ShowPromptAsync(message, title);
    public async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "") => await _inner.ShowSelectFileDialogAsync(filter, initialDirectory);
}

public class EpicUserInteractionAdapter : IEpicUserInteraction
{
    private readonly UserInteractionService _userInteractionService;

    public EpicUserInteractionAdapter(UserInteractionService userInteractionService)
    {
        _userInteractionService = userInteractionService;
    }

    public bool Confirm(string message)
    {
        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.Confirm called with: {message}");
        System.Diagnostics.Debug.WriteLine($"[EpicUserInteractionAdapter] Auto-confirm: {message}");
        return true;
    }

    public void ShowError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.ShowError called with: {message}");

        System.Diagnostics.Debug.WriteLine($"[EpicUserInteractionAdapter] Error: {message}");

        // Rzuć wyjątek który zostanie obsłużony w MainWindowViewModel
        //throw new InvalidOperationException(message);
    }
}

public class InstallationSilentUserInteraction : IUserInteraction
{
    private readonly Dictionary<string, int> _retryCounters = new();
    private const int MAX_RETRIES = 3;

    public bool Confirm(string message, string title = "")
    {
        System.Diagnostics.Debug.WriteLine($"[Installation] Confirm request: {message}");

        // Sprawdź czy to pytanie o retry
        if (message.Contains("spróbować ponownie") || message.Contains("Czy chcesz spróbować"))
        {
            // Stwórz klucz na podstawie typu błędu
            string errorKey = GetErrorKey(message);

            if (!_retryCounters.ContainsKey(errorKey))
                _retryCounters[errorKey] = 0;

            _retryCounters[errorKey]++;

            if (_retryCounters[errorKey] <= MAX_RETRIES)
            {
                System.Diagnostics.Debug.WriteLine($"[Installation] Auto-retry {_retryCounters[errorKey]}/{MAX_RETRIES} for: {errorKey}");
                return true; // Spróbuj ponownie
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Installation] Max retries reached for: {errorKey}");
                // Rzuć wyjątek żeby przerwać instalację i pokazać błąd użytkownikowi
                throw new InvalidOperationException($"Przekroczono maksymalną liczbę prób ({MAX_RETRIES}) dla: {GetUserFriendlyError(message)}");
            }
        }

        // Dla innych pytań - zawsze potwierdź
        return true;
    }

    private string GetErrorKey(string message)
    {
        if (message.Contains("vanilla")) return "vanilla_download";
        if (message.Contains("moda")) return "mod_download";
        if (message.Contains("rozpakowywania")) return "extract_error";
        return "unknown_error";
    }

    private string GetUserFriendlyError(string message)
    {
        if (message.Contains("vanilla")) return "pobierania pliku gry vanilla";
        if (message.Contains("moda")) return "pobierania pliku moda";
        if (message.Contains("rozpakowywania")) return "rozpakowywania archiwum";
        return "nieznanego błędu";
    }

    public void ShowInfo(string message, string title = "")
    {
        System.Diagnostics.Debug.WriteLine($"[Installation] Info: {message}");
    }

    public void ShowError(string message, string title = "")
    {
        System.Diagnostics.Debug.WriteLine($"[Installation] Error: {message}");
        // Rzuć wyjątek żeby błąd został obsłużony w głównym try/catch
        throw new InvalidOperationException(message);
    }

    public string? Prompt(string message, string title = "") => null;
    public string? SelectFile(string filter, string initialDirectory = "") => null;

    public Task ShowInfoAsync(string message, string title = "")
    {
        ShowInfo(message, title);
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string message, string title = "")
    {
        ShowError(message, title);
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string message, string title = "")
    {
        return Task.FromResult(Confirm(message, title));
    }

    public Task<string?> ShowPromptAsync(string message, string title = "")
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "")
    {
        return Task.FromResult<string?>(null);
    }


    // Metoda do resetowania liczników (wywołaj przed nową instalacją)
    public void ResetRetryCounters()
    {
        _retryCounters.Clear();
    }
}