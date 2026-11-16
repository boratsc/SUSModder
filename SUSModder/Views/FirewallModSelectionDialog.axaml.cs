using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;

namespace SUSModder.Views;

/// <summary>
/// Reprezentuje grę lub mod do wyboru w dialogu firewalla.
/// ExePath to docelowa ścieżka programu; IsPredicted = gra jeszcze nie pobrana (Epic).
/// </summary>
public class FirewallModItem
{
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public bool IsPredicted { get; set; }
    public bool IsVanilla { get; set; }
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
