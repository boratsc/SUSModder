using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using System;
using System.IO;
using SUSModder.Core.Utilities;
using SUSModder.Core.Repositories;
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
using System.Diagnostics;

namespace SUSModder.ViewModels
{
    public class AppSettingsViewModel : ViewModelBase
    {
        private readonly Control _control;
        private readonly ILocalizationService? _localizationService;
        private string _modsInstallPath = string.Empty;
        private bool _developerMode = false;
        private string _gameMode = "steam";
        private LanguageOption? _selectedLanguage;
        private string _originalModsInstallPath = string.Empty;
        private bool _originalDeveloperMode = false;
        private string _originalGameMode = "steam";
        private string _originalLanguage = "pl";
        private bool _hasUnsavedChanges = false;

        // Stała lista dostępnych języków (aby uniknąć problemów z referencjami)
        private static readonly List<LanguageOption> _availableLanguages = new()
        {
            new LanguageOption { Code = "pl", DisplayName = "Polski" },
            new LanguageOption { Code = "en", DisplayName = "English" }
        };

        // Dodaj event dla powiadomienia o zapisaniu
        public event Action? SettingsSaved;

        // Dodaj event dla powiadomienia o zmianie trybu gry
        public static event Action? GameModeChanged;

        public AppSettingsViewModel(Control control)
        {
            _control = control;

            // Załaduj obecne ustawienia
            LoadCurrentSettings();

            // Pobierz serwis lokalizacji z DI
            try
            {
                _localizationService = App.GetService<ILocalizationService>();
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

            // Komendy
            BrowseFolderCommand = ReactiveCommand.CreateFromTask(BrowseFolder);
            SaveCommand = ReactiveCommand.CreateFromTask(SaveSettings);
            CancelCommand = ReactiveCommand.Create(Cancel);
            ResetToDefaultCommand = ReactiveCommand.Create(ResetToDefault);
            FactoryResetCommand = ReactiveCommand.CreateFromTask(FactoryResetAsync);
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

        public string GameMode
        {
            get => _gameMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _gameMode, value);
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

            // Zapisz wybór do appsettings.json natychmiast (live switch)
            ConfigManager.SaveLanguageSetting(_selectedLanguage.Code);

            System.Diagnostics.Debug.WriteLine($"Język zmieniony na: {_selectedLanguage.Code}");
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
        }

        public string WindowTitle => HasUnsavedChanges ? "Ustawienia aplikacji *" : "Ustawienia aplikacji";

        public ReactiveCommand<Unit, Unit> BrowseFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; }
        public ReactiveCommand<Unit, Unit> FactoryResetCommand { get; }

        private void LoadCurrentSettings()
        {
            try
            {
                // Załaduj ModsInstallPath
                _modsInstallPath = PathSettings.ModsInstallPath;
                _originalModsInstallPath = _modsInstallPath;

                // Załaduj DeveloperMode
                _developerMode = DeveloperModeSettings.IsEnabled;
                _originalDeveloperMode = _developerMode;

                // Załaduj GameMode z appsettings.json
                LoadGameModeFromAppSettings();

                System.Diagnostics.Debug.WriteLine($"Loaded current settings - ModsInstallPath: {_modsInstallPath}, DeveloperMode: {_developerMode}, GameMode: {_gameMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                _modsInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody");
                _originalModsInstallPath = _modsInstallPath;
                _developerMode = false;
                _originalDeveloperMode = false;
                _gameMode = "steam";
                _originalGameMode = "steam";
            }
        }

        private void LoadGameModeFromAppSettings()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                string appSettingsPath = Path.Combine(exeDir, "appsettings.json");

                if (File.Exists(appSettingsPath))
                {
                    string jsonContent = File.ReadAllText(appSettingsPath);
                    var jsonDocument = JsonDocument.Parse(jsonContent);
                    var root = jsonDocument.RootElement;

                    if (root.TryGetProperty("Configuration", out var config) && 
                        config.TryGetProperty("Mode", out var mode))
                    {
                        _gameMode = mode.GetString() ?? "steam";
                        _originalGameMode = _gameMode;
                    }
                    else
                    {
                        _gameMode = "steam";
                        _originalGameMode = "steam";
                    }
                }
                else
                {
                    _gameMode = "steam";
                    _originalGameMode = "steam";
                }

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
                               _gameMode != _originalGameMode ||
                               (_selectedLanguage?.Code ?? "pl") != _originalLanguage;
            this.RaisePropertyChanged(nameof(WindowTitle));
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
                    await ShowErrorAsync("Błąd walidacji", "Ścieżka instalacji modów nie może być pusta.");
                    return;
                }

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
                    await ShowErrorAsync("Błąd dostępu", $"Nie można utworzyć lub uzyskać dostępu do katalogu:\n{ModsInstallPath}\n\nBłąd: {ex.Message}");
                    return;
                }

                // Sprawdź czy tryb gry się zmienił
                bool gameModeChanged = _gameMode != _originalGameMode;

                // Zapisz wszystkie ustawienia do appsettings.json
                await SaveAllSettingsToAppSettings();

                // Zapisz DeveloperMode używając nowej klasy
                DeveloperModeSettings.SetDeveloperMode(DeveloperMode);

                _originalModsInstallPath = ModsInstallPath;
                _originalDeveloperMode = DeveloperMode;
                _originalGameMode = GameMode;
                HasUnsavedChanges = false;

                // Powiadom o zapisaniu ustawień
                SettingsSaved?.Invoke();

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
                await ShowErrorAsync("Błąd zapisu", $"Nie udało się zapisać ustawień:\n{ex.Message}");
            }
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
            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var dialog = new MessageDialog(title, message);
                await dialog.ShowDialog(parentWindow);
            }
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var dialog = new MessageDialog(title, message);
                await dialog.ShowDialog(parentWindow);
            }
        }

        private Window? GetParentWindow()
        {
            if (_control is Window window)
                return window;
            
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }

        private async Task<bool?> ShowConfirmAsync(string title, string message, string ok, string cancel)
        {
            var parentWindow = GetParentWindow();
            if (parentWindow == null) return null;

            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = parentWindow.Background,
                Icon = parentWindow.Icon,
                FontFamily = parentWindow.FontFamily
            };

            var cancelButton = new Button
            {
                Content = cancel,
                IsCancel = true,
                Width = 100
            };
            cancelButton.Click += (_, __) => dialog.Close(false);

            var resetButton = new Button
            {
                Content = ok,
                IsDefault = true,
                Width = 100,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.IndianRed),
                Foreground = Avalonia.Media.Brushes.White
            };
            resetButton.Click += (_, __) => dialog.Close(true);

            dialog.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        Margin = new Thickness(10),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        MaxWidth = 360
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Spacing = 10,
                        Children =
                        {
                            cancelButton,
                            resetButton
                        }
                    }
                }
            };

            return await dialog.ShowDialog<bool?>(parentWindow);
        }

        private async Task FactoryResetAsync()
        {
            try
            {
                // Pobierz ścieżki z appsettings.json
                string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                string appSettingsPath = Path.Combine(exeDir, "appsettings.json");
                string configPath = Path.Combine(exeDir, "config.json");

                string modsInstallPath = string.Empty;
                string defaultModsPath = string.Empty;

                if (File.Exists(appSettingsPath))
                {
                    var json = await File.ReadAllTextAsync(appSettingsPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("AppSettings", out var appSettings))
                    {
                        if (appSettings.TryGetProperty("ModsInstallPath", out var modsPathElem))
                            modsInstallPath = modsPathElem.GetString() ?? string.Empty;
                        if (appSettings.TryGetProperty("DefaultModsPath", out var defPathElem))
                            defaultModsPath = defPathElem.GetString() ?? string.Empty;
                    }
                }

                // Pokaż dedykowany dialog potwierdzenia
                var dialog = new FactoryResetConfirmDialog(modsInstallPath, defaultModsPath);
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

                void ForceDeleteDirectory(string path)
                {
                    if (!Directory.Exists(path))
                        return;

                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var attr = File.GetAttributes(file);
                            if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                                File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                        }
                        catch { /* ignoruj pojedyncze błędy */ }
                    }
                    Directory.Delete(path, true);
                }

                // Usuń katalogi modów
                if (!string.IsNullOrWhiteSpace(modsInstallPath) && Directory.Exists(modsInstallPath))
                {
                    ForceDeleteDirectory(modsInstallPath);
                }
                if (!string.IsNullOrWhiteSpace(defaultModsPath))
                {
                    string expandedDefault = Environment.ExpandEnvironmentVariables(defaultModsPath);
                    if (Directory.Exists(expandedDefault))
                    {
                        ForceDeleteDirectory(expandedDefault);
                    }
                }

                // Usuń config.json
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }

                // Opcjonalnie: przywróć domyślne appsettings.json (możesz tu dodać kod jeśli chcesz nadpisać plik domyślną wersją)

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
}
