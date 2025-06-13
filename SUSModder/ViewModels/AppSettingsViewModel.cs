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
using SUSModder.Views; // Dodaj dla dialogów

namespace SUSModder.ViewModels
{
    public class AppSettingsViewModel : ViewModelBase
    {
        private readonly Window _window;
        private string _modsInstallPath = string.Empty;
        private string _originalModsInstallPath = string.Empty;
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

        private void LoadCurrentSettings()
        {
            try
            {
                _modsInstallPath = PathSettings.ModsInstallPath;
                _originalModsInstallPath = _modsInstallPath;
                System.Diagnostics.Debug.WriteLine($"Loaded current ModsInstallPath: {_modsInstallPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                _modsInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Among Us - Mody");
                _originalModsInstallPath = _modsInstallPath;
            }
        }

        private void CheckForChanges()
        {
            HasUnsavedChanges = !string.Equals(_modsInstallPath, _originalModsInstallPath, StringComparison.OrdinalIgnoreCase);
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

                // Zapisz do appsettings.json
                await SaveToAppSettings();

                _originalModsInstallPath = ModsInstallPath;
                HasUnsavedChanges = false;

                // Powiadom o zapisaniu ustawień
                SettingsSaved?.Invoke();

                await ShowInfoAsync("Sukces", "Ustawienia zostały zapisane pomyślnie.\n\nZmiany będą widoczne przy następnych operacjach.");

                System.Diagnostics.Debug.WriteLine($"Settings saved successfully. New path: {ModsInstallPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                await ShowErrorAsync("Błąd zapisu", $"Nie udało się zapisać ustawień:\n{ex.Message}");
            }
        }

        private async Task SaveToAppSettings()
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
    }
}
