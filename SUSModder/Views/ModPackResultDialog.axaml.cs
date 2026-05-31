using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace SUSModder.Views;

public partial class ModPackResultDialog : Window
{
    public ModPackResultDialog()
    {
        InitializeComponent();
    }

    public ModPackResultDialog(string packCode, string? shareUrl)
        : this()
    {
        CodeTextBox.Text = packCode;
        LinkTextBox.Text = shareUrl ?? $"https://susmodder.app/pack/{packCode}";
    }

    private async void CopyCode_Click(object? sender, RoutedEventArgs e) =>
        await CopyToClipboardAsync(CodeTextBox.Text);

    private async void CopyLink_Click(object? sender, RoutedEventArgs e) =>
        await CopyToClipboardAsync(LinkTextBox.Text);

    private static async Task CopyToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow
                : null)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(text);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
