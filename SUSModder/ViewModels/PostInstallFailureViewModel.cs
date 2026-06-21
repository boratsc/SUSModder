using System;
using System.Reactive;
using Avalonia.Input.Platform;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

/// <summary>
/// Modal błędu instalacji moda FULL — analogiczny do PostInstallSuccessViewModel.
/// </summary>
public class PostInstallFailureViewModel : ViewModelBase
{
    public event EventHandler? CloseRequested;

    public string ModName { get; }
    public string Title { get; }
    public string TitleWithName { get; }
    public string Message { get; }
    public string LogLabel { get; }
    public string LogText { get; }
    public string DiagnosticActionsTitle { get; }
    public string OpenAiSupportButton { get; }
    public string CopyLogButton { get; }
    public string CloseButton { get; }

    public event EventHandler? AiSupportRequested;
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenAiSupportCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLogCommand { get; }

    public PostInstallFailureViewModel(
        string modName,
        string errorMessage,
        string logText,
        ILocalizationService localizationService)
    {
        ModName = modName;
        Title = localizationService.Get("Dialogs.PostInstallFailure.Title");
        TitleWithName = localizationService.GetFormatted("Dialogs.PostInstallFailure.TitleWithName", modName);
        Message = string.IsNullOrWhiteSpace(errorMessage)
            ? localizationService.Get("Dialogs.Error.InstallFailed")
            : errorMessage;
        LogLabel = localizationService.Get("Dialogs.PostInstallFailure.LogLabel");
        LogText = string.IsNullOrWhiteSpace(logText)
            ? localizationService.Get("Dialogs.PostInstallFailure.LogEmpty")
            : logText;
        DiagnosticActionsTitle = localizationService.Get("Dialogs.PostInstallFailure.DiagnosticActionsTitle");
        OpenAiSupportButton = localizationService.Get("Dialogs.PostInstallFailure.OpenAiSupportButton");
        CopyLogButton = localizationService.Get("Dialogs.PostInstallFailure.CopyLogButton");
        CloseButton = localizationService.Get("Dialogs.PostInstallFailure.CloseButton");

        OpenAiSupportCommand = ReactiveCommand.Create(() => AiSupportRequested?.Invoke(this, EventArgs.Empty));
        CopyLogCommand = ReactiveCommand.CreateFromTask(CopyLogToClipboardAsync);
        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    private async System.Threading.Tasks.Task CopyLogToClipboardAsync()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(LogText);
            }
        }
        catch
        {
            // Clipboard is best-effort; the visible log remains selectable in the dialog.
        }
    }
}
