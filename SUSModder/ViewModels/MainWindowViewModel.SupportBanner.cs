using System;
using System.Reactive;
using ReactiveUI;
using SUSModder.Services;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Belka dobrowolnego wsparcia (suppi.pl) nad katalogiem modów.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private bool _isSupportBannerVisible;

        public bool IsSupportBannerVisible
        {
            get => _isSupportBannerVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSupportBannerVisible, value);
        }

        public ReactiveCommand<Unit, Unit> OpenSupportLinkCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        public ReactiveCommand<Unit, Unit> DismissSupportBannerCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        private void InitializeSupportBanner()
        {
            OpenSupportLinkCommand = ReactiveCommand.Create(ProjectSupport.Open);
            DismissSupportBannerCommand = ReactiveCommand.Create(DismissSupportBanner);
            RefreshSupportBannerVisibility();
        }

        private void RefreshSupportBannerVisibility()
        {
            try
            {
                var settings = _userSettingsService.LoadUserSettings();
                IsSupportBannerVisible = ProjectSupport.ShouldShowBanner(settings.SupportBannerDismissedAt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SupportBanner] Błąd odczytu widoczności: {ex.Message}");
                IsSupportBannerVisible = true;
            }
        }

        private void DismissSupportBanner()
        {
            try
            {
                _userSettingsService.UpdateUserSetting(s =>
                    s.SupportBannerDismissedAt = DateTimeOffset.UtcNow.ToString("O"));
                IsSupportBannerVisible = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SupportBanner] Błąd dismiss: {ex.Message}");
                IsSupportBannerVisible = false;
            }
        }
    }
}
