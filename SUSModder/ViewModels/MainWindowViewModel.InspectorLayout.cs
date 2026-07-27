using System.Reactive;
using ReactiveUI;

namespace SUSModder.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isInspectorLobbyExpanded;

    public bool IsInspectorLobbyExpanded
    {
        get => _isInspectorLobbyExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isInspectorLobbyExpanded, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleInspectorLobbyCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> OpenInspectorLobbyCommand { get; private set; } = null!;

    private void InitializeInspectorLayout()
    {
        ToggleInspectorLobbyCommand = ReactiveCommand.Create(ToggleInspectorLobby);
        OpenInspectorLobbyCommand = ReactiveCommand.Create(OpenInspectorLobby);
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
        IsInspectorLobbyExpanded = false;
        IsCatalogCompatibleDllSectionExpanded = false;
        DisposeInspectorLobbyEmbedViewModel();
    }
}
