using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;
using SUSModder.Core.Configuration;
using SUSModder.Core.Repositories;
using SUSModder.Services;
using System;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using SUSModder.Views;
using Avalonia.Platform.Storage;
using SUSModder.Core.Services;
using System.Linq;
using SUSModder.Core.Utilities;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;

namespace SUSModder.Views
{
    public partial class AdditionalActionsPanel : UserControl
    {
        private ConfigRepository? _configRepository;
        private UserInteractionService? _userInteractionService;

        public AdditionalActionsPanel()
        {
            InitializeComponent();
            InitializeServices();
        }

        private void InitializeServices()
        {
            try
            {
                // Inicjalizuj ConfigRepository
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                _configRepository = new ConfigRepository(exeDir);

                // Inicjalizuj UserInteractionService
                _userInteractionService = new UserInteractionService(
                    ShowConfirmDialogAsync,
                    ShowMessageAsync,
                    ShowErrorDialogAsync,
                    ShowPromptDialogAsync,
                    ShowSelectFileDialogAsync
                );

                // Inicjalizuj ModConfigHandler
                ModConfigHandler.Initialize(_configRepository, _userInteractionService);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AdditionalActionsPanel services: {ex.Message}");
            }
        }

        // Event handlers dla przycisków
        public async void OnSaveLocalConfigClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Zapytaj o nazwę konfiguracji
                string? configName = await ShowPromptDialogAsync(
                    "Wpisz nazwę konfiguracji (lub zostaw puste dla domyślnej nazwy z datą):",
                    "Nazwa konfiguracji");

                // Jeśli użytkownik anulował dialog, nie rób nic
                if (configName == null)
                    return;

                // Wywołaj metodę z podaną nazwą
                ModConfigHandler.SaveLocalConfig(configName);
                await ShowMessageAsync("Sukces", "Konfiguracja została zapisana lokalnie.");
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Błąd podczas zapisywania lokalnej konfiguracji: {ex.Message}", "Błąd");
            }
        }

        public async void OnLoadLocalConfigClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Sprawdź dostępne pliki
                var availableFiles = ModConfigHandler.GetAvailableConfigFiles();

                if (availableFiles.Length == 0)
                {
                    await ShowErrorDialogAsync("Nie znaleziono zapisanych konfiguracji.", "Błąd");
                    return;
                }

                string? selectedFile = null;

                if (availableFiles.Length == 1)
                {
                    // Jeśli jest tylko jeden plik, użyj go
                    selectedFile = availableFiles[0];
                }
                else
                {
                    // Jeśli jest więcej plików, pokaż dialog wyboru
                    string configDir = Path.Combine(PathSettings.ModsInstallPath, "Konfiguracje");
                    selectedFile = await ShowSelectFileDialogAsync("ZIP files (*.zip)|*.zip", configDir);
                }

                if (!string.IsNullOrWhiteSpace(selectedFile))
                {
                    ModConfigHandler.LoadLocalConfig(selectedFile);
                    await ShowMessageAsync("Sukces", "Konfiguracja została wczytana.");
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                await ShowErrorDialogAsync(ex.Message, "Błąd");
            }
            catch (FileNotFoundException ex)
            {
                await ShowErrorDialogAsync(ex.Message, "Błąd");
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Błąd podczas ładowania lokalnej konfiguracji: {ex.Message}", "Błąd");
            }
        }

        public async void OnSaveServerConfigClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[UI] Rozpoczynam zapisywanie konfiguracji na serwerze...");

                // Wywołaj metodę która zwraca hash
                string hash = await ModConfigHandler.SaveServerConfigAsync();

                System.Diagnostics.Debug.WriteLine($"[UI] Zapisywanie zakończone sukcesem, hash: {hash}");

                // Pokaż dialog z hashem
                var hashDialog = new HashDisplayDialog(hash);
                var mainWindow = GetMainWindow();

                if (mainWindow != null)
                {
                    await hashDialog.ShowDialog(mainWindow);
                }
            }
            catch (FileNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] FileNotFoundException: {ex.Message}");
                await ShowErrorDialogAsync(ex.Message, "Brak plików");
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] InvalidOperationException: {ex.Message}");
                await ShowErrorDialogAsync(ex.Message, "Błąd konfiguracji");
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] HttpRequestException: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd połączenia z serwerem: {ex.Message}", "Błąd sieci");
            }
            catch (TimeoutException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] TimeoutException: {ex.Message}");
                await ShowErrorDialogAsync("Przekroczono limit czasu połączenia z serwerem.", "Timeout");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] Exception: {ex}");
                await ShowErrorDialogAsync($"Wystąpił nieoczekiwany błąd: {ex.Message}", "Błąd");
            }
        }

        public async void OnLoadServerConfigClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[UI] Otwieranie dialogu wczytywania konfiguracji...");

                // Pokaż dialog wyboru hash
                var loadDialog = new LoadServerConfigDialog();
                var mainWindow = GetMainWindow();

                if (mainWindow != null)
                {
                    await loadDialog.ShowDialog(mainWindow);

                    if (loadDialog.DialogResult && !string.IsNullOrWhiteSpace(loadDialog.ResultHash))
                    {
                        System.Diagnostics.Debug.WriteLine($"[UI] Rozpoczynam wczytywanie konfiguracji, hash: {loadDialog.ResultHash}");

                        // Wywołaj metodę wczytywania z hash
                        await ModConfigHandler.LoadServerConfigAsync(loadDialog.ResultHash);

                        System.Diagnostics.Debug.WriteLine("[UI] Konfiguracja wczytana pomyślnie");

                        // Pokaż komunikat sukcesu
                        var successDialog = new MessageDialog("Sukces", "Konfiguracja z serwera została pomyślnie wczytana.");
                        await successDialog.ShowDialog(mainWindow);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] ArgumentException: {ex.Message}");
                await ShowErrorDialogAsync("Nieprawidłowy kod konfiguracji.", "Błąd");
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] InvalidOperationException: {ex.Message}");
                await ShowErrorDialogAsync(ex.Message, "Błąd konfiguracji");
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] HttpRequestException: {ex.Message}");
                await ShowErrorDialogAsync($"Błąd połączenia z serwerem: {ex.Message}", "Błąd sieci");
            }
            catch (TimeoutException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] TimeoutException: {ex.Message}");
                await ShowErrorDialogAsync("Przekroczono limit czasu połączenia z serwerem.", "Timeout");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] Exception: {ex}");
                await ShowErrorDialogAsync($"Wystąpił nieoczekiwany błąd: {ex.Message}", "Błąd");
            }
        }


        public async void OnLoadLocalTxtConfigClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Pokaż dialog wyboru pliku TXT
                var selectedFile = await ShowSelectFileDialogAsync("TXT files (*.txt)|*.txt", "");

                if (!string.IsNullOrWhiteSpace(selectedFile))
                {
                    ModConfigHandler.LoadLocalTxtConfig(selectedFile);
                    await ShowMessageAsync("Sukces", "Konfiguracja została wczytana z pliku txt.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Błąd podczas ładowania konfiguracji z pliku txt: {ex.Message}", "Błąd");
            }
        }

        public async void OnChangePresetNamesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[UI] Otwieranie dialogu zmiany nazw presetów...");

                var changeNamesDialog = new ChangePresetNamesDialog();
                var mainWindow = GetMainWindow();

                if (mainWindow != null)
                {
                    await changeNamesDialog.ShowDialog(mainWindow);

                    if (changeNamesDialog.DialogResult)
                    {
                        System.Diagnostics.Debug.WriteLine("[UI] Dialog zamknięty z sukcesem");
                        // Dialog już pokazał swoje komunikaty o sukcesie/błędach
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[UI] Dialog anulowany przez użytkownika");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UI] Exception w OnChangePresetNamesClick: {ex}");
                await ShowErrorDialogAsync($"Wystąpił nieoczekiwany błąd: {ex.Message}", "Błąd");
            }
        }


        public async void OnLobbySetClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new LobbySetDialog();
                var mainWindow = GetMainWindow();

                if (mainWindow != null)
                {
                    await dialog.ShowDialog(mainWindow);
                    if (dialog.DialogResult)
                    {
                        await ShowMessageAsync("Sukces", $"Ustawiono liczbę graczy na {dialog.PlayerCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Błąd podczas ustawiania lobby: {ex.Message}", "Błąd");
            }
        }

        // Pomocnicze metody dla UserInteractionService
        private async Task<bool> ShowConfirmDialogAsync(string message, string title)
        {
            var dialog = new ConfirmDialog(title, message);
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                return dialog.Result;
            }
            return false;
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
        }

        private async Task ShowErrorDialogAsync(string message, string title)
        {
            var dialog = new MessageDialog(title, message);
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
        }

        private async Task<string?> ShowPromptDialogAsync(string message, string title)
        {
            var dialog = new PromptDialog(title, message);
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                if (dialog.DialogResult)
                {
                    return dialog.InputText;
                }
            }
            return null;
        }

        private async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory)
        {
            try
            {
                var mainWindow = GetMainWindow();
                if (mainWindow?.StorageProvider == null)
                    return null;

                // Przygotuj filtry na podstawie parametru filter
                var fileTypeFilters = new List<Avalonia.Platform.Storage.FilePickerFileType>();

                if (!string.IsNullOrEmpty(filter))
                {
                    if (filter.Contains("*.zip"))
                    {
                        fileTypeFilters.Add(new Avalonia.Platform.Storage.FilePickerFileType("ZIP files")
                        {
                            Patterns = new[] { "*.zip" }
                        });
                    }
                    else if (filter.Contains("*.txt"))
                    {
                        fileTypeFilters.Add(new Avalonia.Platform.Storage.FilePickerFileType("TXT files")
                        {
                            Patterns = new[] { "*.txt" }
                        });
                    }
                }

                // Dodaj opcję "Wszystkie pliki"
                fileTypeFilters.Add(Avalonia.Platform.Storage.FilePickerFileTypes.All);

                var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Wybierz plik",
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

        private Window? GetMainWindow()
        {
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
    }
}
