using System;
using System.Windows.Input;
using ReactiveUI;
using SUSModder.Core.Services;
using SUSModder.Views;

namespace SUSModder.ViewModels
{
    public class PlatformSelectionViewModel : ReactiveObject
    {
        private readonly PlatformSelectionDialog _dialog;
        private readonly UserSettingsService _userSettingsService;

        public ICommand SelectSteamCommand { get; }
        public ICommand SelectEpicCommand { get; }

        public PlatformSelectionViewModel(PlatformSelectionDialog dialog)
        {
            _dialog = dialog;
            _userSettingsService = new UserSettingsService();

            SelectSteamCommand = ReactiveCommand.Create(SelectSteam);
            SelectEpicCommand = ReactiveCommand.Create(SelectEpic);
        }

        private void SelectSteam()
        {
            SelectPlatform("steam");
        }

        private void SelectEpic()
        {
            SelectPlatform("epic");
        }

        private void SelectPlatform(string platform)
        {
            try
            {
                // Zapisz wybraną platformę do user-settings.json
                _userSettingsService.UpdateUserSetting(settings => settings.Mode = platform);

                System.Diagnostics.Debug.WriteLine($"[PlatformSelection] Wybrano platformę: {platform}");

                // Zamknij dialog
                _dialog.Close(platform);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd przy zapisywaniu platformy: {ex.Message}");
                // W przypadku błędu, zamknij dialog z domyślną platformą
                _dialog.Close("steam");
            }
        }
    }
}
