using System;
using System.Reactive;
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
    public string CloseButton { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

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
        CloseButton = localizationService.Get("Dialogs.PostInstallFailure.CloseButton");

        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }
}
