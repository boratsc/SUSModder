using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Views;

/// <summary>
/// Panel szczegółów moda (master-detail v3). Hostowany w <see cref="MainWindow"/> jako ModDetailDrawerHost.
/// </summary>
public partial class ModDetailDrawer : UserControl
{
    public ModDetailDrawer()
    {
        InitializeComponent();
    }

    private void ModDeveloperMenuButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedMod != null)
            Debug.WriteLine($"Developer menu opened for mod: {vm.SelectedMod.Name}");
    }

    private async void AutoUpdateToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedMod == null)
            return;

        var modItem = vm.SelectedMod;
        await vm.ToggleAutoUpdateAsync(modItem, modItem.AutoUpdateEnabled);
    }
}
