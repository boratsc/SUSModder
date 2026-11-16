using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SUSModder.ViewModels;

namespace SUSModder.Views;

/// <summary>
/// E14: panel zakładki Role w kolumnie listy MainWindow (poziomy A/B).
/// Klik wiersza poziomu A od razu otwiera role moda (jedna akcja, bez dwukliku);
/// strzałki przesuwają zaznaczenie, Enter aktywuje — wzorzec ModsListBox_KeyDown.
/// </summary>
public partial class RolesPanel : UserControl
{
    public RolesPanel()
    {
        InitializeComponent();
        // ListBoxItem zaznacza wiersz i oznacza PointerPressed jako obsłużony,
        // więc nawigację podpinamy z handledEventsToo (klik = od razu role moda).
        RoleModsListBox.AddHandler(PointerPressedEvent, RoleModsListBox_PointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        RoleModsListBox.AddHandler(KeyDownEvent, RoleModsListBox_KeyDown,
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void RoleModsListBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control control)
            return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is not Visual source || source.FindAncestorOfType<ListBoxItem>()?.DataContext is not RoleModGroup group)
            return;

        vm.SelectRoleModCommand.Execute(group).Subscribe();
        e.Handled = true;
    }

    private void RoleModsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox listBox || listBox.SelectedItem is not RoleModGroup group)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.SelectRoleModCommand.Execute(group).Subscribe();
        e.Handled = true;
    }
}