using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        public enum StatusBarMode
        {
            DiscordPromo,
            SystemStatus
        }

        private StatusBarMode _currentStatusBarMode = StatusBarMode.DiscordPromo;

        public StatusBarMode CurrentStatusBarMode
        {
            get => _currentStatusBarMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentStatusBarMode, value);
                this.RaisePropertyChanged(nameof(IsDiscordPromoStatusBarMode));
                this.RaisePropertyChanged(nameof(IsSystemStatusBarMode));
            }
        }

        public bool IsDiscordPromoStatusBarMode => CurrentStatusBarMode == StatusBarMode.DiscordPromo;
        public bool IsSystemStatusBarMode => CurrentStatusBarMode == StatusBarMode.SystemStatus;

        public bool IsDllModalVisible => IsDllModificationsVisible || IsDllInstallDialogVisible;

        public bool IsAnyToolModalOpen =>
            IsInfoPanelVisible ||
            IsAdditionalActionsVisible ||
            IsLobbyBoardVisible ||
            IsDllModalVisible ||
            IsDllSelectionModalVisible ||
            IsVersionSelectionModalVisible ||
            IsPostInstallSuccessVisible ||
            IsPostInstallFailureVisible ||
            IsLaunchDiagnosticsVisible ||
            IsAmongUsNotFoundVisible ||
            IsModPackCodeEntryVisible ||
            IsModPackCreatorVisible ||
            IsModPackResultVisible ||
            IsModPackPreviewVisible ||
            IsModPackManagerVisible ||
            IsAiSupportVisible ||
            IsSUStatsConfigVisible ||
            IsAppSettingsVisible ||
            IsRecommendedDiscordsVisible ||
            IsRepairOptionsVisible;

        public string ToolModalTitle
        {
            get
            {
                if (IsLobbyBoardVisible)
                {
                    return _localizationService.Get("UI.Menu.LobbyBoard");
                }

                if (IsDllModalVisible)
                {
                    return _localizationService.Get("UI.Menu.DllMods");
                }

                if (IsDllSelectionModalVisible)
                {
                    if (DllSelectionModalViewModel != null)
                    {
                        var modName = DllSelectionModalViewModel.TargetModName;
                        return _localizationService.GetFormatted("DllManager.ViewTitleForMod", modName);
                    }
                    return _localizationService.Get("DllManager.ViewTitle");
                }

                if (IsVersionSelectionModalVisible)
                {
                    return _localizationService.Get("Tools.VersionSelection.WindowTitle");
                }

                if (IsAdditionalActionsVisible)
                {
                    return _localizationService.Get("UI.Menu.ToUConfigs");
                }

                if (IsSUStatsConfigVisible)
                {
                    return _localizationService.Get("UI.Menu.SUStats");
                }

                if (IsAppSettingsVisible)
                {
                    return _localizationService.Get("UI.Menu.Settings");
                }

                if (IsRecommendedDiscordsVisible)
                {
                    return _localizationService.Get("UI.Menu.RecommendedDiscords");
                }

                if (IsRepairOptionsVisible)
                {
                    return _localizationService.Get("UI.Repair.DialogTitle");
                }

                if (IsPostInstallSuccessVisible && PostInstallSuccessViewModel != null)
                {
                    return PostInstallSuccessViewModel.Title;
                }

                if (IsPostInstallFailureVisible && PostInstallFailureViewModel != null)
                {
                    return PostInstallFailureViewModel.Title;
                }

                if (IsLaunchDiagnosticsVisible)
                {
                    return LaunchDiagnosticsTitle;
                }

                if (IsAiSupportVisible)
                {
                    return AiSupportTitle;
                }

                if (IsAmongUsNotFoundVisible && AmongUsNotFoundViewModel != null)
                {
                    return AmongUsNotFoundViewModel.Title;
                }

                if (IsModPackCodeEntryVisible)
                {
                    return _localizationService.Get("ModPacks.CodeEntryTitle");
                }

                if (IsModPackCreatorVisible)
                {
                    return string.IsNullOrEmpty(ModPackCreatorTitle)
                        ? _localizationService.Get("ModPacks.CreatorTitle")
                        : ModPackCreatorTitle;
                }

                if (IsModPackResultVisible)
                {
                    return _localizationService.Get("ModPacks.CreatedTitle");
                }

                if (IsModPackPreviewVisible)
                {
                    return _localizationService.Get("ModPacks.PreviewTitle");
                }

                return _localizationService.Get("UI.Menu.Info");
            }
        }

        public ReactiveCommand<Unit, Unit> ToggleStatusBarModeCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        public ReactiveCommand<Unit, Unit> CloseToolModalCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        private void InitializeFrontendLayout()
        {
            ToggleStatusBarModeCommand = ReactiveCommand.Create(ToggleStatusBarMode);
            CloseToolModalCommand = ReactiveCommand.Create(CloseToolModal);
        }

        private void ToggleStatusBarMode()
        {
            CurrentStatusBarMode = CurrentStatusBarMode == StatusBarMode.DiscordPromo
                ? StatusBarMode.SystemStatus
                : StatusBarMode.DiscordPromo;
        }

        private void CloseToolModal()
        {
            CloseToolModalCore();
            _ = FlushPendingModsListRefreshAsync();
        }

        private void CloseToolModalCore()
        {
            IsInfoPanelVisible = false;
            IsAdditionalActionsVisible = false;
            IsLobbyBoardVisible = false;
            LobbyBoardViewModel?.Dispose();
            LobbyBoardViewModel = null;
            SetLobbyTickerSource(InspectorLobbyEmbedViewModel);
            IsDllModificationsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsRecommendedDiscordsVisible = false;
            IsRepairOptionsVisible = false;
            IsPostInstallSuccessVisible = false;
            PostInstallSuccessViewModel = null;
            IsPostInstallFailureVisible = false;
            PostInstallFailureViewModel = null;
            IsLaunchDiagnosticsVisible = false;
            IsAiSupportVisible = false;
            DismissAmongUsNotFoundModal(AmongUsNotFoundResult.Close);
            DismissActiveModPackModal();
            if (IsVersionSelectionModalVisible)
            {
                VersionSelectionModalViewModel?.CancelSelection();
                IsVersionSelectionModalVisible = false;
                VersionSelectionModalViewModel = null;
            }
            CloseDllDialog();
            CloseDllSelectionModal();
            ShowNextQueuedDllSelectionIfNeeded();
            RestoreModDetailPanelAfterToolModal();
        }

        /// <summary>
        /// Przywraca widoczność treści i layout panelu szczegółów moda po zamknięciu modala narzędziowego.
        /// </summary>
        private void RestoreModDetailPanelAfterToolModal()
        {
            if (SelectedMod != null)
                IsModContentVisible = true;

            NotifyModDetailPanelLayoutChanged();
        }

        private void NotifyModDetailPanelLayoutChanged()
        {
            this.RaisePropertyChanged(nameof(IsModPanelVisible));
            this.RaisePropertyChanged(nameof(IsBrowserDetailPanelVisible));
            this.RaisePropertyChanged(nameof(IsDllPanelVisible));
        }

        private void NotifyToolModalStateChanged()
        {
            if (_isAiSupportVisible && IsAnyNonAiSupportToolModalOpen())
            {
                _isAiSupportVisible = false;
                this.RaisePropertyChanged(nameof(IsAiSupportVisible));
            }

            this.RaisePropertyChanged(nameof(IsDllModalVisible));
            this.RaisePropertyChanged(nameof(IsAnyToolModalOpen));
            this.RaisePropertyChanged(nameof(ToolModalTitle));
            NotifyModDetailPanelLayoutChanged();
        }

        private bool IsAnyNonAiSupportToolModalOpen() =>
            IsInfoPanelVisible ||
            IsAdditionalActionsVisible ||
            IsLobbyBoardVisible ||
            IsDllModalVisible ||
            IsDllSelectionModalVisible ||
            IsVersionSelectionModalVisible ||
            IsPostInstallSuccessVisible ||
            IsPostInstallFailureVisible ||
            IsLaunchDiagnosticsVisible ||
            IsAmongUsNotFoundVisible ||
            IsModPackCodeEntryVisible ||
            IsModPackCreatorVisible ||
            IsModPackResultVisible ||
            IsModPackPreviewVisible ||
            IsModPackManagerVisible ||
            IsSUStatsConfigVisible ||
            IsAppSettingsVisible ||
            IsRecommendedDiscordsVisible ||
            IsRepairOptionsVisible;
    }
}
