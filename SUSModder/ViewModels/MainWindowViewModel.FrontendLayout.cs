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
        /// Przy dokowaniu czat jest stałą kolumną (IsChatDocked) — ten stan dotyczy tylko nakładki.
        /// </summary>
        public bool IsChatOpen
        {
            get => _isChatOpen;
            set => this.RaiseAndSetIfChanged(ref _isChatOpen, value);
        }

        private bool _chatAnchored;

        /// <summary>Preferencja zakotwiczenia czatu (z ustawień, po zapisie). Efektywny tryb wymaga też szerokości okna.</summary>
        public bool ChatAnchored
        {
            get => _chatAnchored;
            set
            {
                this.RaiseAndSetIfChanged(ref _chatAnchored, value);
                this.RaisePropertyChanged(nameof(ChatAnchorHintVisible));
            }
        }

        private bool _isChatDocked;

        /// <summary>
        /// Dokowanie efektywne: preferencja + szerokość okna >= 1420 DIP. Ustawiane przez MainWindow
        /// z układu; wyłączenie zostawia otwarty czat jako nakładkę (bez mark-read i fokusu).
        /// </summary>
        public bool IsChatDocked
        {
            get => _isChatDocked;
            set
            {
                if (_isChatDocked == value)
                    return;

                var wasDocked = _isChatDocked;
                this.RaiseAndSetIfChanged(ref _isChatDocked, value);
                this.RaisePropertyChanged(nameof(ChatAnchorHintVisible));
                CommunityChat.IsDockedVisible = value;

                // Rozdokowanie nie zamyka rozmowy: kolumna staje się nakładką automatycznie
                // (auto=true — bez kasowania nieprzeczytanych, fokusu i wymuszonego przewinięcia).
                if (wasDocked && !value && !_isChatOpen)
                {
                    CommunityChat.SetOverlayOpen(true, auto: true);
                    _isChatOpen = true;
                    this.RaisePropertyChanged(nameof(IsChatOpen));
                }
            }
        }

        /// <summary>Neutralny hint w ustawieniach: preferencja włączona, ale okno za wąskie na kolumnę.</summary>
        public bool ChatAnchorHintVisible => ChatAnchored && !IsChatDocked;

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
            // Dokowany czat: przycisk belki przewija do najnowszych; nakładka: toggle jak dotąd.
            ToggleChatCommand = ReactiveCommand.Create(() =>
            {
                if (IsChatDocked)
                    CommunityChat.OnNewMessagesButton();
                else
                    IsChatOpen = !IsChatOpen;
            });
        }

        /// <summary>
        /// E5: mapuje plan polityki toastów czatu na ToastService.
        /// ≥2 wiadomości: toast grupowy + najwyżej 2 najnowsze podglądy (wzorzec hosta: max 3 widoczne).
        /// Klik toastu: nakładka otwiera się; przy dokowaniu przewija do najnowszych (bez drugiego czatu).
        /// X usuwa tylko toast (obsługa hosta).
        /// </summary>
        private void OnChatToastsRequested(ChatToastPlan plan)
        {
            if (plan.GroupCount is int count)
                ToastService.ShowChat(
                    _localizationService.GetFormatted("UI.CommunityChat.NewMessages", count),
                    _localizationService.Get("UI.CommunityChat.Title"),
                    _localizationService.Get("UI.CommunityChat.Toast.GroupHint"),
                    OpenChatFromToast);

            foreach (var message in plan.Previews)
                ToastService.ShowChat(
                    _localizationService.Get("UI.CommunityChat.Toast.SingleTitle"),
                    message.AuthorDisplay,
                    message.DisplayContent,
                    OpenChatFromToast);
        }

        private void OpenChatFromToast()
        {
            if (IsChatDocked)
                CommunityChat.OnNewMessagesButton();
            else
                IsChatOpen = true;
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

            // Ustawienia to panel treści (nie modal): otwarcie modala narzędziowego zamyka ustawienia.
            if (_isAppSettingsVisible && IsAnyToolModalOpen)
                IsAppSettingsVisible = false;

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
            IsRepairOptionsVisible;
    }
}
