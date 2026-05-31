using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Utilities;

namespace SUSModder.Views;

public partial class ModPackCodeEntryDialog : Window
{
    public string? EnteredPackCode { get; private set; }

    public ModPackCodeEntryDialog()
    {
        InitializeComponent();
        CodeTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                OkButton_Click(null, new RoutedEventArgs());
        };
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var code = CodeTextBox.Text?.Trim();
        if (!ModPackCodeValidator.IsValid(code))
        {
            Close(null);
            return;
        }

        EnteredPackCode = ModPackCodeValidator.Normalize(code!);
        Close(EnteredPackCode);
    }
}
