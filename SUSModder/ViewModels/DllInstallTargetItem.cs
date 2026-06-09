using ReactiveUI;
using SUSModder.Core.Models;
using SUSModder.Services;

namespace SUSModder.ViewModels;

/// <summary>
/// Docelowy full mod lub lokalny zestaw dla instalacji DLL w inspektorze.
/// </summary>
public class DllInstallTargetItem : ReactiveObject
{
    private bool _isSelected;

    public DllInstallTargetItem(
        ModItem target,
        bool wasInstalled,
        CompatibilityInfo? compatibility = null,
        string? compatibilityTooltip = null)
    {
        Target = target;
        WasInstalled = wasInstalled;
        Compatibility = compatibility;
        CompatibilityTooltip = compatibilityTooltip;
        _isSelected = wasInstalled;
    }

    public ModItem Target { get; }

    public bool WasInstalled { get; }

    public CompatibilityInfo? Compatibility { get; }

    public string? CompatibilityTooltip { get; }

    public CompatibilityStatus CompatibilityStatus =>
        Compatibility?.Status ?? CompatibilityStatus.NotTested;

    public bool IsInstallBlocked => CompatibilityStatus == CompatibilityStatus.NotWork;

    public string DisplayName => Target.Name;

    public string CompatibilityEmoji => CompatibilityDisplayHelper.GetEmoji(Compatibility);

    public string DisplayLabel =>
        string.IsNullOrEmpty(CompatibilityEmoji)
            ? DisplayName
            : $"{CompatibilityEmoji} {DisplayName}";

    public bool IsCheckboxEnabled => !IsInstallBlocked || WasInstalled;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && IsInstallBlocked && !WasInstalled)
                return;

            this.RaiseAndSetIfChanged(ref _isSelected, value);
            this.RaisePropertyChanged(nameof(HasPendingChange));
        }
    }

    public bool HasPendingChange
    {
        get
        {
            if (IsSelected == WasInstalled)
                return false;

            if (IsSelected && !WasInstalled && IsInstallBlocked)
                return false;

            return true;
        }
    }
}
