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
                    return "🎮 Lobby";
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
            IsDllModificationsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsRecommendedDiscordsVisible = false;
            IsRepairOptionsVisible = false;
            IsPostInstallSuccessVisible = false;
            PostInstallSuccessViewModel = null;
            if (IsVersionSelectionModalVisible)
            {
                VersionSelectionModalViewModel?.CancelSelection();
                IsVersionSelectionModalVisible = false;
                VersionSelectionModalViewModel = null;
            }
            CloseDllDialog();
            CloseDllSelectionModal();
            ShowNextQueuedDllSelectionIfNeeded();
            this.RaisePropertyChanged(nameof(IsModPanelVisible));
        }

        private void NotifyToolModalStateChanged()
        {
            this.RaisePropertyChanged(nameof(IsDllModalVisible));
            this.RaisePropertyChanged(nameof(IsAnyToolModalOpen));
            this.RaisePropertyChanged(nameof(ToolModalTitle));
        }
    }
}
