using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Models;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views;

public partial class PackInstanceCloneDialog : Window
{
    private readonly ILocalizationService _loc;
    public ModInstanceCloneOptions? Options { get; private set; }

    public PackInstanceCloneDialog()
    {
        _loc = null!;
        InitializeComponent();
    }

    public PackInstanceCloneDialog(ILocalizationService loc, string suggestedName)
        : this()
    {
        _loc = loc;
        DisplayNameBox.Text = suggestedName;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void CloneButton_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var name = DisplayNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = _loc.Get("UI.Packs.DisplayNameRequired");
            ErrorText.IsVisible = true;
            return;
        }

        Options = new ModInstanceCloneOptions
        {
            NewDisplayName = name,
            CopyDlls = CopyDllsCheck.IsChecked == true,
            CopyTouConfig = CopyTouConfigCheck.IsChecked == true,
            CopyIntegrationDll = CopyIntegrationCheck.IsChecked == true,
            CopyPinnedVersion = CopyPinnedVersionCheck.IsChecked == true
        };
        Close(Options);
    }
}
