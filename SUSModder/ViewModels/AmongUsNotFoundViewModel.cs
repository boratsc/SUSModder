using System;
using System.Reactive;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    public enum AmongUsNotFoundResult
    {
        Close,
        Browse
    }

    public class AmongUsNotFoundViewModel : ViewModelBase
    {
        private AmongUsNotFoundResult _result = AmongUsNotFoundResult.Close;

        public event EventHandler? CloseRequested;

        public AmongUsNotFoundResult Result
        {
            get => _result;
            private set => this.RaiseAndSetIfChanged(ref _result, value);
        }

        public string Title { get; }
        public string Message { get; }
        public string BrowseButton { get; }
        public string CloseButton { get; }

        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        public AmongUsNotFoundViewModel(ILocalizationService localizationService)
        {
            Title = localizationService.Get("Dialogs.AmongUsNotFound.Title");
            Message = localizationService.Get("Dialogs.AmongUsNotFound.Message");
            BrowseButton = localizationService.Get("Dialogs.AmongUsNotFound.BrowseButton");
            CloseButton = localizationService.Get("Dialogs.AmongUsNotFound.CloseButton");

            BrowseCommand = ReactiveCommand.Create(() =>
            {
                Result = AmongUsNotFoundResult.Browse;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });

            CloseCommand = ReactiveCommand.Create(() =>
            {
                Result = AmongUsNotFoundResult.Close;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}
