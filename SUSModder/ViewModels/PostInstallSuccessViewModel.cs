using System;
using System.Reactive;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Wynik wybrany przez użytkownika w modalnym panelu sukcesu instalacji.
    /// </summary>
    public enum PostInstallAction
    {
        /// <summary>Użytkownik wybrał uruchomienie gry</summary>
        Launch,
        /// <summary>Użytkownik wybrał dodanie modyfikacji DLL</summary>
        AddDll,
        /// <summary>Użytkownik zamknął panel bez akcji</summary>
        Dismiss
    }

    /// <summary>
    /// ViewModel dla modala sukcesu po instalacji moda.
    /// Pokazuje się jako panel w głównym oknie (nie osobne okienko).
    /// </summary>
    public class PostInstallSuccessViewModel : ViewModelBase
    {
        private bool _dontShowAgain;
        private PostInstallAction _result = PostInstallAction.Dismiss;

        /// <summary>Event wołany gdy użytkownik zamyka modal (przycisk lub zewnętrzne zamknięcie).</summary>
        public event EventHandler? CloseRequested;

        /// <summary>Nazwa zainstalowanego moda.</summary>
        public string ModName { get; }

        /// <summary>Czy mod obsługuje modyfikacje DLL.</summary>
        public bool SupportsDll { get; }

        /// <summary>Czy zaznaczono "Nie pokazuj więcej".</summary>
        public bool DontShowAgain
        {
            get => _dontShowAgain;
            set => this.RaiseAndSetIfChanged(ref _dontShowAgain, value);
        }

        /// <summary>Wybrana akcja — ustawiana przed wywołaniem CloseRequested.</summary>
        public PostInstallAction Result
        {
            get => _result;
            private set => this.RaiseAndSetIfChanged(ref _result, value);
        }

        // Lokalizowane stringi (ustawiane w konstruktorze, nie zmieniają się)
        public string Title { get; }
        public string TitleWithName { get; }
        public string Message { get; }
        public string LaunchButton { get; }
        public string AddDllButton { get; }
        public string DontShowAgainCheckbox { get; }

        // Komendy
        public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
        public ReactiveCommand<Unit, Unit> AddDllCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissCommand { get; }

        public PostInstallSuccessViewModel(
            string modName,
            bool supportsDll,
            ILocalizationService localizationService)
        {
            ModName = modName;
            SupportsDll = supportsDll;

            Title = localizationService.Get("Dialogs.PostInstallSuccess.Title");
            TitleWithName = localizationService.GetFormatted("Dialogs.PostInstallSuccess.TitleWithName", modName);
            Message = supportsDll
                ? localizationService.Get("Dialogs.PostInstallSuccess.Message")
                : localizationService.Get("Dialogs.PostInstallSuccess.MessageNoDll");
            LaunchButton = localizationService.Get("Dialogs.PostInstallSuccess.LaunchButton");
            AddDllButton = localizationService.Get("Dialogs.PostInstallSuccess.AddDllButton");
            DontShowAgainCheckbox = localizationService.Get("Dialogs.PostInstallSuccess.DontShowAgain");

            LaunchCommand = ReactiveCommand.Create(() =>
            {
                Result = PostInstallAction.Launch;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });

            AddDllCommand = ReactiveCommand.Create(() =>
            {
                Result = PostInstallAction.AddDll;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });

            DismissCommand = ReactiveCommand.Create(() =>
            {
                Result = PostInstallAction.Dismiss;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
        }
    }
}
