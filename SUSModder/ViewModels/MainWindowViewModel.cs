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

namespace SUSModder.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private bool _isPaneOpen;
        private ModItem? _selectedMod;
        private bool _isDarkTheme = true;
        private ResourceDictionary? _currentThemeDictionary;
        private readonly Uri _darkThemeUri = new Uri("avares://SUSModder/Themes/DarkTheme.axaml");
        private readonly Uri _lightThemeUri = new Uri("avares://SUSModder/Themes/LightTheme.axaml");
        private bool _isInfoPanelVisible = false;
        private string _appVersion = string.Empty;
        public bool IsModPanelVisible => IsModSelected && !IsInfoPanelVisible && !IsAdditionalActionsVisible;
        private readonly ToUConfigService _touConfigService;
        private bool _isAdditionalActionsVisible = false;
        private List<ModConfiguration> _loadedConfigs = new();
        private UserInteractionService _userInteractionService;


        public ReactiveCommand<Unit, Unit> LobbySetCommand { get; }

        public bool IsAdditionalActionsVisible
        {
            get => _isAdditionalActionsVisible;
            set => this.RaiseAndSetIfChanged(ref _isAdditionalActionsVisible, value);
        }

        public ReactiveCommand<Unit, Unit> ShowAdditionalActionsCommand { get; }

        // Komendy dla akcji ToU
        public ReactiveCommand<Unit, Unit> SaveLocalConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> LoadLocalConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveServerConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> LoadServerConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> LoadLocalTxtConfigCommand { get; }
        public ReactiveCommand<Unit, Unit> ChangePresetNamesCommand { get; }
        public ReactiveCommand<Unit, Unit> FixBlackScreenCommand { get; }
        public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowRolesCommand { get; }

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


        // Dodaj komendę
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

 

            TogglePaneCommand = ReactiveCommand.Create(TogglePane);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            InstallCommand = ReactiveCommand.Create(Install);
            UninstallCommand = ReactiveCommand.Create(Uninstall);
            LaunchCommand = ReactiveCommand.Create(Launch);
            UpdateCommand = ReactiveCommand.Create(Update);
            ShowRolesCommand = ReactiveCommand.Create(ShowRoles);
            ShowInfoCommand = ReactiveCommand.Create(ShowInfo);
            ShowAdditionalActionsCommand = ReactiveCommand.Create(ShowAdditionalActions);

            SaveLocalConfigCommand = ReactiveCommand.Create(() => _touConfigService.SaveLocalConfig());
            LoadLocalConfigCommand = ReactiveCommand.Create(() => _touConfigService.LoadLocalConfig());
            SaveServerConfigCommand = ReactiveCommand.CreateFromTask(() => _touConfigService.SaveServerConfigAsync());
            LoadServerConfigCommand = ReactiveCommand.CreateFromTask(() => _touConfigService.LoadServerConfigAsync());
            LoadLocalTxtConfigCommand = ReactiveCommand.Create(() => _touConfigService.LoadLocalTxtConfig());
            ChangePresetNamesCommand = ReactiveCommand.Create(() => _touConfigService.ChangePresetNames());
            LobbySetCommand = ReactiveCommand.CreateFromTask(ShowLobbySetDialog);
            FixBlackScreenCommand = ReactiveCommand.CreateFromTask(ExecuteFixBlackScreenAsync);

            FixBlackScreenCommand.ThrownExceptions.Subscribe(HandleCommandError);
            LobbySetCommand.ThrownExceptions.Subscribe(HandleCommandError);
            SaveLocalConfigCommand.ThrownExceptions.Subscribe(HandleCommandError);
            LoadLocalConfigCommand.ThrownExceptions.Subscribe(HandleCommandError);
            SaveServerConfigCommand.ThrownExceptions.Subscribe(HandleCommandError);
            LoadServerConfigCommand.ThrownExceptions.Subscribe(HandleCommandError);
            LoadLocalTxtConfigCommand.ThrownExceptions.Subscribe(HandleCommandError);
            ChangePresetNamesCommand.ThrownExceptions.Subscribe(HandleCommandError);

            LoadSavedTheme();
            InitializeApplicationAsync();
            LoadAppVersion();
            ApplyTheme(IsDarkTheme);
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

        private async Task<bool> SetupVanillaGameAsync()
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
                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during Vanilla setup: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas konfiguracji gry: {ex.Message}", "Błąd");
                return false;
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
                System.Diagnostics.Debug.WriteLine("Checking for mod updates...");

                var configService = new ConfigService();
                var currentConfigs = configService.LoadConfig();
                var installedConfigs = currentConfigs.Where(c => !string.IsNullOrEmpty(c.InstallPath)).ToList();

                var availableUpdates = new List<ModUpdateInfo>();

                foreach (var config in installedConfigs)
                {
                    var updatedConfig = await configService.CheckSingleModUpdateAsync(config.ModName);
                    if (updatedConfig != null)
                    {
                        availableUpdates.Add(new ModUpdateInfo
                        {
                            ModName = config.ModName,
                            CurrentVersion = config.ModVersion ?? "Nieznana",
                            NewVersion = updatedConfig.ModVersion ?? "Nieznana",
                            Description = updatedConfig.Description ?? "",
                            IsSelected = true,
                            LocalMod = config,
                            RemoteMod = updatedConfig
                        });
                    }
                }

                if (!availableUpdates.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No updates available");
                    return;
                }

                // Pokaż dialog
                var updateDialog = new UpdateDialog(availableUpdates);
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                if (mainWindow != null)
                {
                    // Pokaż dialog bez czekania
                    updateDialog.Show(mainWindow);

                    // Czekaj aż użytkownik kliknie Update lub Cancel
                    while (!updateDialog.DialogResult && updateDialog.IsVisible)
                    {
                        await Task.Delay(100);
                    }

                    // Jeśli użytkownik potwierdził aktualizację
                    if (updateDialog.DialogResult && updateDialog.IsVisible)
                    {
                        var selectedMods = updateDialog.GetSelectedMods();
                        if (selectedMods.Any())
                        {
                            // Wykonaj aktualizację używając TYLKO logiki z MainWindowViewModel
                            await ProcessSelectedUpdatesWithProgressAsync(selectedMods, updateDialog);
                        }
                    }

                    // Zamknij dialog jeśli jeszcze jest otwarty
                    if (updateDialog.IsVisible)
                    {
                        updateDialog.Close();
                    }

                    // Odśwież listę modów
                    await RefreshModsListAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during update check: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd podczas sprawdzania aktualizacji: {ex.Message}", "Błąd");
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
                        Directory.Delete(modItem.InstallPath, true);
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
                            await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                silentUserInteraction,
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

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
                this.RaisePropertyChanged(nameof(ThemeButtonText));
                ApplyTheme(_isDarkTheme);
            }
        }

        public string ThemeButtonText => IsDarkTheme ? "☀️ Motyw jasny" : "🌙 Motyw ciemny";

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
                _isDarkTheme = savedTheme == "dark";
                System.Diagnostics.Debug.WriteLine($"Wczytano motyw: {savedTheme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd wczytywania motywu: {ex.Message}");
                _isDarkTheme = true; // domyślnie ciemny
            }
        }

        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;

            // Zapisz nowy motyw
            try
            {
                var themeValue = IsDarkTheme ? "dark" : "light";
                ConfigManager.SaveThemeSetting(themeValue);
                System.Diagnostics.Debug.WriteLine($"Zapisano motyw: {themeValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd zapisywania motywu: {ex.Message}");
            }
        }

        private void ApplyTheme(bool isDark)
        {
            try
            {
                if (Application.Current == null)
                    return;

                // Usuń poprzedni słownik jeśli był załadowany
                if (_currentThemeDictionary != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_currentThemeDictionary);

                var uri = isDark ? _darkThemeUri : _lightThemeUri;
                var loaded = AvaloniaXamlLoader.Load(uri);

                if (loaded is ResourceDictionary newDict)
                {
                    Application.Current.Resources.MergedDictionaries.Add(newDict);
                    _currentThemeDictionary = newDict;
                }

                Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}");
                // Fallback - użyj domyślnego motywu
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
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



        private void ShowRoles()
        {
            System.Diagnostics.Debug.WriteLine("ShowRoles command executed");
            // TODO: Implementuj pokazywanie ról
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

                if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
                {
                    // Epic installation
                    var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[Install Epic] {message}");
                    });

                    var epicUserInteraction = new EpicUserInteractionAdapter(_userInteractionService);
                    var epicManager = new EpicVersionManager(diagnosticsOutput, epicUserInteraction);

                    Action<int, string> epicProgressCallback = (percentage, message) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            currentSelectedMod.InstallProgress = percentage;
                            currentSelectedMod.InstallStatusMessage = message;
                        });
                    };

                    epicManager.ProgressChanged += (percentage, message) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            currentSelectedMod.InstallProgress = percentage;
                            currentSelectedMod.InstallStatusMessage = message;
                        });
                    };

                    try
                    {
                        await epicManager.ModifyEpicAsync(modConfig, epicProgressCallback, null);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Install Epic] Exception: {ex.Message}");
                        success = false;
                    }

                    if (success)
                    {
                        // Przeładuj konfigurację z pliku
                        var updatedConfigs = configService.LoadConfig();
                        var updatedConfig = updatedConfigs.FirstOrDefault(c => c.ModName == currentSelectedMod.Name);

                        if (updatedConfig != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                currentSelectedMod.InstallPath = updatedConfig.InstallPath;
                                System.Diagnostics.Debug.WriteLine($"[Epic Install] Updated InstallPath: {updatedConfig.InstallPath}");
                            });
                        }
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
                        await modManager.ModifyAsync(
                            modConfig,
                            allConfigs, 
                            progressReporter,
                            diagnosticsOutput,
                            _userInteractionService,
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
                }

                // Odświeżenie dla obu platform
                if (success)
                {
                    RefreshModsSortingKeepSelection(currentSelectedMod);
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

        private string DeterminePlatform()
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
                            await modManager.ModifyAsync(
                                updatedConfig,
                                updatedConfigs,
                                progressReporter,
                                diagnosticsOutput,
                                silentUserInteraction,
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
                if (Directory.Exists(currentSelectedMod.InstallPath))
                {
                    Directory.Delete(currentSelectedMod.InstallPath, true);
                    System.Diagnostics.Debug.WriteLine($"Usunięto katalog: {currentSelectedMod.InstallPath}");
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
        var task = _userInteractionService.ShowConfirmAsync(message, "Potwierdzenie");
        task.Wait();
        return task.Result;
    }

    public void ShowError(string message)
    {
        var task = _userInteractionService.ShowErrorAsync(message, "Błąd");
        task.Wait();
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