using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;

namespace SUSModder.Views;

/// <summary>
/// Reprezentuje mod do wyboru w dialogu firewalla
/// </summary>
public class FirewallModItem
{
    public string Name { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
}

/// <summary>
/// Dialog wyboru moda dla którego chcemy dodać wyjątki firewalla
/// </summary>
public partial class FirewallModSelectionDialog : Window
{
    /// <summary>
    /// Wybrany mod (null jeśli anulowano)
    /// </summary>
    public FirewallModItem? SelectedMod { get; private set; }
    
    public FirewallModSelectionDialog()
    {
        InitializeComponent();
    }

    public FirewallModSelectionDialog(IEnumerable<FirewallModItem> mods) : this()
    {
        ModsListBox.ItemsSource = mods.ToList();
        ModsListBox.SelectionChanged += OnModSelectionChanged;
    }
    
    private void OnModSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = ModsListBox.SelectedItem != null;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        SelectedMod = null;
        Close();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        SelectedMod = ModsListBox.SelectedItem as FirewallModItem;
        Close();
    }
}
