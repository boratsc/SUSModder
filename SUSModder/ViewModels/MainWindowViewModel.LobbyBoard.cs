using System;
using System.ComponentModel;
using ReactiveUI;
using SUSModder.Core.Configuration;
using SUSModder.Core.Lobby;
using SUSModder.Core.Services;
using SUSModder.Core.Services.Localization;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private LobbyBoardPanelViewModel? _inspectorLobbyEmbedViewModel;

    public LobbyBoardPanelViewModel? InspectorLobbyEmbedViewModel
    {
        get => _inspectorLobbyEmbedViewModel;
        private set => this.RaiseAndSetIfChanged(ref _inspectorLobbyEmbedViewModel, value);
    }

    private LobbyBoardPanelViewModel CreateLobbyBoardViewModel(LobbyBoardPanelMode mode)
    {
        var mod = SelectedMod;
        if (mod == null)
            throw new InvalidOperationException("Lobby board requires a selected mod.");

        var lobbyService = App.GetService<ILobbyBoardService>();
        var locService = App.GetService<ILocalizationService>();
        var bridgeReader = mode == LobbyBoardPanelMode.Full
            ? App.GetService<LobbyBridgeFileReader>()
            : null;

        return new LobbyBoardPanelViewModel(
            lobbyService,
            locService,
            ModItemAdapter.ToConfig(mod),
            bridgeReader,
            mode);
    }

    private LobbyBoardPanelViewModel? _lobbyTickerSource;
    private PropertyChangedEventHandler? _lobbyTickerHandler;

    private void SetLobbyTickerSource(LobbyBoardPanelViewModel? vm)
    {
        if (_lobbyTickerSource != null && _lobbyTickerHandler != null)
            _lobbyTickerSource.PropertyChanged -= _lobbyTickerHandler;

        _lobbyTickerSource = vm;
        if (vm == null)
        {
            LobbyCodesTickerText = "";
            this.RaisePropertyChanged(nameof(HasLobbyCodesTicker));
            return;
        }

        if (_lobbyTickerHandler == null)
        {
            _lobbyTickerHandler = (_, e) =>
            {
                if (_lobbyTickerSource == null)
                    return;
                if (e.PropertyName is nameof(LobbyBoardPanelViewModel.TickerText) or null)
                {
                    LobbyCodesTickerText = _lobbyTickerSource.TickerText ?? "";
                    this.RaisePropertyChanged(nameof(HasLobbyCodesTicker));
                }
            };
        }

        vm.PropertyChanged += _lobbyTickerHandler;
        LobbyCodesTickerText = vm.TickerText ?? "";
        this.RaisePropertyChanged(nameof(HasLobbyCodesTicker));
    }

    private void EnsureLobbyBoardViewModel()
    {
        if (SelectedMod == null)
            return;

        LobbyBoardViewModel?.Dispose();
        var vm = CreateLobbyBoardViewModel(LobbyBoardPanelMode.Full);
        LobbyBoardViewModel = vm;
        SetLobbyTickerSource(vm);
        this.RaisePropertyChanged(nameof(LobbyBoardViewModel));
    }

    private void EnsureInspectorLobbyEmbedViewModel()
    {
        if (SelectedMod == null)
            return;

        InspectorLobbyEmbedViewModel?.Dispose();
        var vm = CreateLobbyBoardViewModel(LobbyBoardPanelMode.InspectorEmbed);
        InspectorLobbyEmbedViewModel = vm;
        if (!IsLobbyBoardVisible)
            SetLobbyTickerSource(vm);
        this.RaisePropertyChanged(nameof(InspectorLobbyEmbedViewModel));
    }

    private void DisposeInspectorLobbyEmbedViewModel()
    {
        if (InspectorLobbyEmbedViewModel == _lobbyTickerSource)
            SetLobbyTickerSource(null);
        InspectorLobbyEmbedViewModel?.Dispose();
        InspectorLobbyEmbedViewModel = null;
        this.RaisePropertyChanged(nameof(InspectorLobbyEmbedViewModel));
    }
}
