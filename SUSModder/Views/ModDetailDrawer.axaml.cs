using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using SUSModder.Core.Models;
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

    private async void InstallVersionFlyout_Opening(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.EnsureInstallVersionFlyoutLoadedAsync();
    }

    private async void InstallVersionItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        ModVersionHistory? version = null;
        if (sender is Button btn)
        {
            version = btn.Tag as ModVersionHistory ?? btn.DataContext as ModVersionHistory;
            if (btn.Flyout != null)
                btn.Flyout.Hide();
        }

        if (InstallVersionSplit.Flyout is FlyoutBase flyout)
            flyout.Hide();

        if (version == null)
            return;

        await vm.InstallVersionFromFlyoutAsync(version);
    }
}
