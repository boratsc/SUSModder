using ReactiveUI;

namespace SUSModder.ViewModels;

/// <summary>
/// Docelowy full mod lub lokalny zestaw dla instalacji DLL w inspektorze.
/// </summary>
public class DllInstallTargetItem : ReactiveObject
{
    private bool _isSelected;

    public DllInstallTargetItem(ModItem target, bool wasInstalled)
    {
        Target = target;
        WasInstalled = wasInstalled;
        _isSelected = wasInstalled;
    }

    public ModItem Target { get; }

    public bool WasInstalled { get; }

    public string DisplayName => Target.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public bool HasPendingChange => IsSelected != WasInstalled;
}
