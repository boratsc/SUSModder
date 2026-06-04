using System;
using System.Reactive;
using ReactiveUI;
using SUSModder.Core.Services.Localization;
using SUSModder.Core.Utilities;

namespace SUSModder.ViewModels;

public class ModPackCodeEntryViewModel : ViewModelBase
{
    private string _codeText = string.Empty;

    public event EventHandler<string?>? Completed;

    public string CodeText
    {
        get => _codeText;
        set => this.RaiseAndSetIfChanged(ref _codeText, value);
    }

    public string Prompt { get; }
    public string Placeholder { get; }
    public string CancelButton { get; }
    public string OkButton { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> OkCommand { get; }

    public ModPackCodeEntryViewModel(ILocalizationService localizationService)
    {
        Prompt = localizationService.Get("ModPacks.CodeEntryPrompt");
        Placeholder = localizationService.Get("ModPacks.CodeEntryPlaceholder");
        CancelButton = localizationService.Get("UI.Buttons.Cancel");
        OkButton = localizationService.Get("UI.Buttons.OK");

        CancelCommand = ReactiveCommand.Create(() => Completed?.Invoke(this, null));
        OkCommand = ReactiveCommand.Create(Submit);
    }

    private void Submit()
    {
        var code = CodeText?.Trim();
        if (!ModPackCodeValidator.IsValid(code))
        {
            Completed?.Invoke(this, null);
            return;
        }

        Completed?.Invoke(this, ModPackCodeValidator.Normalize(code!));
    }
}
