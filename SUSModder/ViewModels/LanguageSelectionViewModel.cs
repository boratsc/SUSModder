using System;
using System.Windows.Input;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services.Localization;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    public class LanguageSelectionViewModel : ReactiveObject
    {
        private readonly LanguageSelectionDialog _dialog;

        public ICommand SelectPolishCommand { get; }
        public ICommand SelectEnglishCommand { get; }

        public LanguageSelectionViewModel(LanguageSelectionDialog dialog)
        {
            _dialog = dialog;

            SelectPolishCommand = ReactiveCommand.Create(SelectPolish);
            SelectEnglishCommand = ReactiveCommand.Create(SelectEnglish);
        }

        private void SelectPolish()
        {
            SelectLanguage("pl");
        }

        private void SelectEnglish()
        {
            SelectLanguage("en");
        }

        private void SelectLanguage(string languageCode)
        {
            try
            {
                // Zapisz wybrany język do appsettings.json
                ConfigManager.SaveLanguageSetting(languageCode);

                // Zastosuj zmianę w LocalizationService
                var locService = App.GetService<ILocalizationService>();
                if (locService != null && locService.IsCultureAvailable(languageCode))
                {
                    locService.ChangeCulture(languageCode);
                }

                // Zamknij dialog
                _dialog.Close(languageCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd przy zapisywaniu języka: {ex.Message}");
                // W przypadku błędu, zamknij dialog z domyślnym językiem
                _dialog.Close("pl");
            }
        }
    }
}
