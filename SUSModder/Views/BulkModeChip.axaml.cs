using Avalonia.Controls;
using System;

namespace SUSModder.Views;

public partial class BulkModeChip : UserControl
{
    public BulkModeChip()
    {
        InitializeComponent();
    }

    private bool _searchOpen;

    private void SearchFlyout_Opened(object? sender, EventArgs e)
    {
        _searchOpen = true;
        UpdateSearchHighlight();
        SearchTextBox.Focus();
    }

    private void SearchFlyout_Closed(object? sender, EventArgs e)
    {
        _searchOpen = false;
        UpdateSearchHighlight();
    }

    private void SearchTextBox_TextChanged(object? sender, TextChangedEventArgs e) => UpdateSearchHighlight();

    private void UpdateSearchHighlight() =>
        SearchButton?.Classes.Set("active", _searchOpen || !string.IsNullOrWhiteSpace(SearchTextBox?.Text));
}
