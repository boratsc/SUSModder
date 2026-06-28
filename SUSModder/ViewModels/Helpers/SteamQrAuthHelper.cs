using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SUSModder.Core.Models;
using SUSModder.Views;

namespace SUSModder.ViewModels.Helpers;

public static class SteamQrAuthHelper
{
    public static Task<bool> ShowDialogAsync(SteamQrDownloadContext context)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ShowDialogCoreAsync(context);

        return Dispatcher.UIThread.InvokeAsync(() => ShowDialogCoreAsync(context));
    }

    private static async Task<bool> ShowDialogCoreAsync(SteamQrDownloadContext context)
    {
        Window? owner = null;
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            owner = desktop.MainWindow;
        }

        var dialog = new SteamQrAuthDialog(context);
        if (owner is not null)
            return await dialog.ShowDialog<bool>(owner);

        return await dialog.ShowDialog<bool>(dialog);
    }
}
