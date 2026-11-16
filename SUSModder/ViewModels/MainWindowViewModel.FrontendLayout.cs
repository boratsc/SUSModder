using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels
{
    public partial class MainWindowViewModel
    {
        public bool IsDllModalVisible => IsDllModificationsVisible || IsDllInstallDialogVisible;

        private bool _isChatOpen;

        /// <summary>
        /// E5: stan nakładki czatu społeczności (dymek na belce, klik toastu, X, Escape).
        /// Czat nie ma stałej kolumny; domyślnie zamknięty.
        /// </summary>
        public bool IsChatOpen
        {
            get => _isChatOpen;
            set => this.RaiseAndSetIfChanged(ref _isChatOpen, value);
        }

        public ReactiveCommand<Unit, Unit> ToggleChatCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        public bool IsAnyToolModalOpen =>
            IsInfoPanelVisible ||
            IsAdditionalActionsVisible ||
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

        /// <summary>
        /// E6: modale stanu (sukces/błąd instalacji, pierwsze uruchomienie) są węższe
        /// niż panele narzędziowe — jak karty na makietach 08–12.
        /// </summary>
        public double ToolModalCardWidth =>
            IsPostInstallSuccessVisible || IsPostInstallFailureVisible || IsAmongUsNotFoundVisible
                ? 560
                : 760;

        public ReactiveCommand<Unit, Unit> CloseToolModalCommand { get; private set; } =
            ReactiveCommand.Create(() => { });

        private void InitializeFrontendLayout()
        {
            CloseToolModalCommand = ReactiveCommand.Create(CloseToolModal);
            ToggleChatCommand = ReactiveCommand.Create(() => { IsChatOpen = !IsChatOpen; });
        }

        /// <summary>
        /// E5: mapuje plan polityki toastów czatu na ToastService.
        /// ≥2 wiadomości: toast grupowy + najwyżej 2 najnowsze podglądy (wzorzec hosta: max 3 widoczne).
        /// Klik toastu otwiera nakładkę; X usuwa tylko toast (obsługa hosta).
        /// </summary>
        private void OnChatToastsRequested(ChatToastPlan plan)
        {
            if (plan.GroupCount is int count)
                ToastService.ShowChat(
                    _localizationService.GetFormatted("UI.CommunityChat.NewMessages", count),
                    _localizationService.Get("UI.CommunityChat.Title"),
                    _localizationService.Get("UI.CommunityChat.Toast.GroupHint"),
                    () => IsChatOpen = true);

            foreach (var message in plan.Previews)
                ToastService.ShowChat(
                    _localizationService.Get("UI.CommunityChat.Toast.SingleTitle"),
                    message.AuthorDisplay,
                    message.DisplayContent,
                    () => IsChatOpen = true);
        }

        /// <summary>E11: unread spadło do 0 / przeczytano — zdejmij aktywne toasty czatu.</summary>
        private void OnChatToastsDismissRequested() => ToastService.DismissChatToasts();

        private void CloseToolModal()
        {
            CloseToolModalCore();
            _ = FlushPendingModsListRefreshAsync();
        }

        private void CloseToolModalCore()
        {
            if (IsModPackCreatorVisible && ModPackCreatorContent is SUSModder.Views.ModPackCreatorView creator &&
                !creator.TryBeginHostClose())
                return;

            IsInfoPanelVisible = false;
            IsAdditionalActionsVisible = false;
            IsDllModificationsVisible = false;
            IsSUStatsConfigVisible = false;
            IsAppSettingsVisible = false;
            IsRecommendedDiscordsVisible = false;
            IsRepairOptionsVisible = false;
            IsPostInstallSuccessVisible = false;
            PostInstallSuccessViewModel = null;
            IsPostInstallFailureVisible = false;
            PostInstallFailureViewModel = null;
            _postInstallFailureRetry = null;
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
            this.RaisePropertyChanged(nameof(IsRolePanelVisible));
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
            this.RaisePropertyChanged(nameof(ToolModalCardWidth));
            NotifyModDetailPanelLayoutChanged();
        }

        private bool IsAnyNonAiSupportToolModalOpen() =>
            IsInfoPanelVisible ||
            IsAdditionalActionsVisible ||
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
