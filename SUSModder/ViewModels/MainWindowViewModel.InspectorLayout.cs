using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isInspectorMoreActionsExpanded;
    private bool _isInspectorLobbyExpanded;

    public bool IsInspectorMoreActionsExpanded
    {
        get => _isInspectorMoreActionsExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isInspectorMoreActionsExpanded, value);
    }

    public bool IsInspectorLobbyExpanded
    {
        get => _isInspectorLobbyExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isInspectorLobbyExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleInspectorMoreActionsCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ToggleInspectorLobbyCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> OpenInspectorLobbyCommand { get; private set; } = null!;

    private void InitializeInspectorLayout()
    {
        ToggleInspectorMoreActionsCommand = ReactiveCommand.Create(ToggleInspectorMoreActions);
        ToggleInspectorLobbyCommand = ReactiveCommand.Create(ToggleInspectorLobby);
        OpenInspectorLobbyCommand = ReactiveCommand.Create(OpenInspectorLobby);
    }

    private void ToggleInspectorMoreActions()
    {
        IsInspectorMoreActionsExpanded = !IsInspectorMoreActionsExpanded;
    }

    private void ToggleInspectorLobby()
    {
        IsInspectorLobbyExpanded = !IsInspectorLobbyExpanded;
        if (IsInspectorLobbyExpanded && InspectorLobbyEmbedViewModel == null)
            EnsureInspectorLobbyEmbedViewModel();
    }

    private void OpenInspectorLobby()
    {
        if (SelectedMod == null)
            return;

        ShowLobbyBoardFromMenu();
    }

    private void ResetInspectorSections()
    {
        IsInspectorMoreActionsExpanded = false;
        IsInspectorLobbyExpanded = false;
        DisposeInspectorLobbyEmbedViewModel();
    }
}
