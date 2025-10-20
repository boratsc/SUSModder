using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający wszystkie metody do obsługi dialogów i okien interakcji z użytkownikiem
    /// </summary>
    public partial class MainWindowViewModel
    {
        private async Task ShowMessageAsync(string title, string message)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ShowMessageAsync called - Title: '{title}', Message: '{message}'");
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: StackTrace: {Environment.StackTrace}");

            var dialog = new MessageDialog(title, message);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
        }

        private async Task ShowErrorDialogAsync(string message, string title)
        {
            var dialog = new MessageDialog(title, message);
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
                await dialog.ShowDialog(mainWindow);
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

        private async Task<string?> ShowPromptDialogAsync(string message, string title)
        {
            await Task.CompletedTask;
            return null;
        }

        private async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory)
        {
            try
            {
                var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow?.StorageProvider == null)
                    return null;

                // Przygotuj opcje filtra
                var fileTypeFilters = new List<FilePickerFileType>();

                if (!string.IsNullOrEmpty(filter))
                {
                    var parts = filter.Split('|');
                    if (parts.Length >= 2)
                    {
                        var extension = parts[1].Replace("*.", "").Replace("*.", "");
                        fileTypeFilters.Add(new FilePickerFileType(parts[0])
                        {
                            Patterns = new[] { $"*.{extension}" }
                        });
                    }
                }

                // Dodaj opcję "Wszystkie pliki"
                fileTypeFilters.Add(FilePickerFileTypes.All);

                var options = new FilePickerOpenOptions
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
    }
}
