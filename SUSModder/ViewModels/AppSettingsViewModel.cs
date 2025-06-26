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
using SUSModder.Views;
using System.Diagnostics;

namespace SUSModder.ViewModels
{
    public class AppSettingsViewModel : ViewModelBase
    {
        private readonly Window _window;
        private string _modsInstallPath = string.Empty;
        private bool _developerMode = false;
        private string _originalModsInstallPath = string.Empty;
        private bool _originalDeveloperMode = false;
        private bool _hasUnsavedChanges = false;

        // Dodaj event dla powiadomienia o zapisaniu
        public event Action? SettingsSaved;

        public AppSettingsViewModel(Window window)
        {
            _window = window;

            // Załaduj obecne ustawienia
            LoadCurrentSettings();

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

                System.Diagnostics.Debug.WriteLine($"Loaded current settings - ModsInstallPath: {_modsInstallPath}, DeveloperMode: {_developerMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                _modsInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody");
                _originalModsInstallPath = _modsInstallPath;
                _developerMode = false;
                _originalDeveloperMode = false;
            }
        }

        private void CheckForChanges()
        {
            HasUnsavedChanges = !string.Equals(_modsInstallPath, _originalModsInstallPath, StringComparison.OrdinalIgnoreCase) ||
                               _developerMode != _originalDeveloperMode;
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

                // Zapisz ModsInstallPath do appsettings.json
                await SaveModsInstallPathToAppSettings();

                // Zapisz DeveloperMode używając nowej klasy
                DeveloperModeSettings.SetDeveloperMode(DeveloperMode);

                _originalModsInstallPath = ModsInstallPath;
                _originalDeveloperMode = DeveloperMode;
                HasUnsavedChanges = false;

                // Powiadom o zapisaniu ustawień
                SettingsSaved?.Invoke();

                await ShowInfoAsync("Sukces", "Ustawienia zostały zapisane pomyślnie.\n\nZmiany będą widoczne przy następnych operacjach.");

                System.Diagnostics.Debug.WriteLine($"Settings saved successfully. ModsInstallPath: {ModsInstallPath}, DeveloperMode: {DeveloperMode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                await ShowErrorAsync("Błąd zapisu", $"Nie udało się zapisać ustawień:\n{ex.Message}");
            }
        }

        private async Task SaveModsInstallPathToAppSettings()
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
                    else
                    {
                        updatedSettings[property.Name] = GetJsonValue(property.Value);
                    }
                }

                // Zapisz zaktualizowany plik
                var options = new JsonSerializerOptions { WriteIndented = true };
                string updatedJson = JsonSerializer.Serialize(updatedSettings, options);
                await File.WriteAllTextAsync(appSettingsPath, updatedJson);

                System.Diagnostics.Debug.WriteLine($"Updated appsettings.json with ModsInstallPath: {ModsInstallPath}");
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
            _window.Close();
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(_window);
        }

        private async Task ShowInfoAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(_window);
        }

        private async Task<bool?> ShowConfirmAsync(string title, string message, string ok, string cancel)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = _window.Background,
                Icon = _window.Icon,
                FontFamily = _window.FontFamily
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

            return await dialog.ShowDialog<bool?>(_window);
        }

        private async Task FactoryResetAsync()
        {
            var result = await ShowConfirmAsync(
                "Reset do ustawień fabrycznych",
                "Ta operacja usunie WSZYSTKIE mody oraz przywróci ustawienia fabryczne aplikacji. Kontynuować?",
                "Resetuj", "Anuluj");

            if (result != true)
                return;

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
}
