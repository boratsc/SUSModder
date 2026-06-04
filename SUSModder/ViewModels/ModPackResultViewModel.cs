using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using ReactiveUI;

namespace SUSModder.ViewModels;

public class ModPackResultViewModel : ViewModelBase
{
    public event EventHandler? CloseRequested;

    public string PackCode { get; }
    public string ShareUrl { get; }
    public string PackCodeLabel { get; }
    public string ShareLinkLabel { get; }
    public string CopyCodeButton { get; }
    public string CopyLinkButton { get; }
    public string CloseButton { get; }

    public ReactiveCommand<Unit, Unit> CopyCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLinkCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ModPackResultViewModel(
        string packCode,
        string? shareUrl,
        string packCodeLabel,
        string shareLinkLabel,
        string copyCodeButton,
        string copyLinkButton,
        string closeButton)
    {
        PackCode = packCode;
        ShareUrl = shareUrl ?? $"https://susmodder.app/pack/{packCode}";
        PackCodeLabel = packCodeLabel;
        ShareLinkLabel = shareLinkLabel;
        CopyCodeButton = copyCodeButton;
        CopyLinkButton = copyLinkButton;
        CloseButton = closeButton;

        CopyCodeCommand = ReactiveCommand.CreateFromTask(() => CopyToClipboardAsync(PackCode));
        CopyLinkCommand = ReactiveCommand.CreateFromTask(() => CopyToClipboardAsync(ShareUrl));
        CloseCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }
}
