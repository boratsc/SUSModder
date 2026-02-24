using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający wszystkie metody do obsługi dialogów i okien interakcji z użytkownikiem
    /// </summary>
    public partial class MainWindowViewModel
    {
        private enum InlineDialogMode
        {
            Message,
            Confirm,
            Prompt
        }

        private sealed record InlineDialogResult(bool Accepted, string? InputText);

        private readonly SemaphoreSlim _inlineDialogSemaphore = new(1, 1);
        private TaskCompletionSource<InlineDialogResult>? _inlineDialogCompletionSource;

        private bool _isInlineDialogVisible;
        private bool _isInlineDialogConfirm;
        private bool _isInlineDialogPrompt;
        private string _inlineDialogTitle = string.Empty;
        private string _inlineDialogMessage = string.Empty;
        private string _inlineDialogInputText = string.Empty;
        private string _inlineDialogPrimaryButtonText = "OK";
        private string _inlineDialogSecondaryButtonText = "Cancel";

        public bool IsInlineDialogVisible
        {
            get => _isInlineDialogVisible;
            set => this.RaiseAndSetIfChanged(ref _isInlineDialogVisible, value);
        }

        public bool IsInlineDialogConfirm
        {
            get => _isInlineDialogConfirm;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isInlineDialogConfirm, value);
                this.RaisePropertyChanged(nameof(IsInlineDialogSecondaryVisible));
                this.RaisePropertyChanged(nameof(IsInlineDialogDismissible));
            }
        }

        public bool IsInlineDialogPrompt
        {
            get => _isInlineDialogPrompt;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isInlineDialogPrompt, value);
                this.RaisePropertyChanged(nameof(IsInlineDialogSecondaryVisible));
                this.RaisePropertyChanged(nameof(IsInlineDialogDismissible));
            }
        }

        public bool IsInlineDialogSecondaryVisible => IsInlineDialogConfirm || IsInlineDialogPrompt;
        public bool IsInlineDialogDismissible => IsInlineDialogSecondaryVisible;

        public string InlineDialogTitle
        {
            get => _inlineDialogTitle;
            private set => this.RaiseAndSetIfChanged(ref _inlineDialogTitle, value);
        }

        public string InlineDialogMessage
        {
            get => _inlineDialogMessage;
            private set => this.RaiseAndSetIfChanged(ref _inlineDialogMessage, value);
        }

        public string InlineDialogInputText
        {
            get => _inlineDialogInputText;
            set => this.RaiseAndSetIfChanged(ref _inlineDialogInputText, value);
        }

        public string InlineDialogPrimaryButtonText
        {
            get => _inlineDialogPrimaryButtonText;
            private set => this.RaiseAndSetIfChanged(ref _inlineDialogPrimaryButtonText, value);
        }

        public string InlineDialogSecondaryButtonText
        {
            get => _inlineDialogSecondaryButtonText;
            private set => this.RaiseAndSetIfChanged(ref _inlineDialogSecondaryButtonText, value);
        }

        public Task ShowInlineMessageAsync(string title, string message) => ShowMessageAsync(title, message);

        public Task ShowInlineErrorAsync(string title, string message) => ShowErrorDialogAsync(message, title);

        public Task<bool> ShowInlineConfirmAsync(string title, string message) => ShowConfirmDialogAsync(message, title);

        public Task<bool> ShowInlineConfirmAsync(string title, string message, string yesButtonText, string noButtonText) =>
            ShowConfirmDialogAsync(message, title, yesButtonText, noButtonText);

        public Task<string?> ShowInlinePromptAsync(string title, string message) => ShowPromptDialogAsync(message, title);

        public void ResolveInlineDialog(bool accepted)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_inlineDialogCompletionSource == null)
                {
                    return;
                }

                var completionSource = _inlineDialogCompletionSource;
                _inlineDialogCompletionSource = null;

                var input = accepted ? InlineDialogInputText : null;
                IsInlineDialogVisible = false;
                completionSource.TrySetResult(new InlineDialogResult(accepted, input));
            });
        }

        private async Task<InlineDialogResult> ShowInlineDialogAsync(
            string title,
            string message,
            InlineDialogMode mode,
            string? primaryButtonText = null,
            string? secondaryButtonText = null)
        {
            await _inlineDialogSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var completionSource = new TaskCompletionSource<InlineDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _inlineDialogCompletionSource = completionSource;
                    InlineDialogTitle = title;
                    InlineDialogMessage = message;
                    InlineDialogInputText = string.Empty;

                    IsInlineDialogConfirm = mode == InlineDialogMode.Confirm;
                    IsInlineDialogPrompt = mode == InlineDialogMode.Prompt;

                    InlineDialogPrimaryButtonText = primaryButtonText ?? (mode == InlineDialogMode.Confirm
                        ? _localizationService.Get("UI.Buttons.Yes")
                        : _localizationService.Get("UI.Buttons.OK"));

                    InlineDialogSecondaryButtonText = secondaryButtonText ?? (mode == InlineDialogMode.Confirm
                        ? _localizationService.Get("UI.Buttons.No")
                        : _localizationService.Get("UI.Buttons.Cancel"));

                    IsInlineDialogVisible = true;
                });

                return await completionSource.Task.ConfigureAwait(false);
            }
            finally
            {
                _inlineDialogSemaphore.Release();
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            await ShowInlineDialogAsync(title, message, InlineDialogMode.Message);
        }

        private async Task ShowErrorDialogAsync(string message, string title)
        {
            await ShowInlineDialogAsync(title, message, InlineDialogMode.Message);
        }

        private Task<bool> ShowConfirmDialogAsync(string message, string title)
        {
            return ShowConfirmDialogAsync(message, title, null, null);
        }

        private async Task<bool> ShowConfirmDialogAsync(string message, string title, string? yesButtonText, string? noButtonText)
        {
            var result = await ShowInlineDialogAsync(title, message, InlineDialogMode.Confirm, yesButtonText, noButtonText);
            return result.Accepted;
        }

        private async Task<string?> ShowPromptDialogAsync(string message, string title)
        {
            var result = await ShowInlineDialogAsync(title, message, InlineDialogMode.Prompt);
            return result.Accepted ? result.InputText : null;
        }

        private async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory)
        {
            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
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
            });
        }

        private async Task ShowDetailedErrorDialogAsync(string title, Exception ex)
        {
            var errorMessage = $"Komunikat: {ex.Message}\n\n";
            errorMessage += $"Typ błędu: {ex.GetType().Name}\n\n";

            if (ex.InnerException != null)
            {
                errorMessage += $"Błąd wewnętrzny: {ex.InnerException.Message}\n\n";
            }

            errorMessage += $"Stack Trace:\n{ex.StackTrace}";
            await ShowInlineDialogAsync(title, errorMessage, InlineDialogMode.Message);
        }
    }
}
