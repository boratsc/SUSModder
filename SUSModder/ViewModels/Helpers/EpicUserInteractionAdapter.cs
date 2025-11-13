using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;
using SUSModder.Views;
using Avalonia.Threading;
using System.Threading.Tasks;

namespace SUSModder.ViewModels.Helpers
{
    /// <summary>
    /// Adapter dla interakcji użytkownika w kontekście operacji Epic Games
    /// </summary>
    public class EpicUserInteractionAdapter : IEpicUserInteraction
    {
        private readonly UserInteractionService _userInteractionService;

        public EpicUserInteractionAdapter(UserInteractionService userInteractionService)
        {
            _userInteractionService = userInteractionService;
        }

        public bool Confirm(string message)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.Confirm called with: {message}");
            System.Diagnostics.Debug.WriteLine($"[EpicUserInteractionAdapter] Auto-confirm: {message}");
            return true;
        }

        public void ShowError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.ShowError called with: {message}");
            System.Diagnostics.Debug.WriteLine($"[EpicUserInteractionAdapter] Error: {message}");
        }

        public string? Prompt(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.Prompt called with: {message}");
            return _userInteractionService.Prompt(message, title);
        }

        public async Task<string?> ShowEpicAuthDialogAsync(string browserUrl)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: EpicUserInteractionAdapter.ShowEpicAuthDialogAsync called with URL: {browserUrl}");

            // WAŻNE: Sprawdź czy jesteśmy na UI thread
            if (Dispatcher.UIThread.CheckAccess())
            {
                // Już jesteśmy na UI thread - wywołaj bezpośrednio (async)
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Already on UI thread - showing dialog directly");

                var dialog = new EpicAuthDialog(browserUrl);

                // Znajdź główne okno jako parent
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                // ShowDialog jest async - await bezpośrednio (nie blokujemy message loop!)
                var dialogResult = mainWindow != null
                    ? await dialog.ShowDialog<bool?>(mainWindow)
                    : await dialog.ShowDialog<bool?>(dialog);

                if (dialogResult == true && dialog.DataContext is EpicAuthDialogViewModel vm)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Dialog returned result");
                    return vm.Result;
                }

                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Dialog cancelled or no result");
                return null;
            }
            else
            {
                // Nie jesteśmy na UI thread - użyj Dispatcher (async)
                System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Not on UI thread - using Dispatcher");

                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialog = new EpicAuthDialog(browserUrl);

                    var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                        ? desktop.MainWindow
                        : null;

                    var dialogResult = mainWindow != null
                        ? await dialog.ShowDialog<bool?>(mainWindow)
                        : await dialog.ShowDialog<bool?>(dialog);

                    if (dialogResult == true && dialog.DataContext is EpicAuthDialogViewModel vm)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Dialog returned result from Dispatcher");
                        return vm.Result;
                    }

                    System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: Dialog cancelled from Dispatcher");
                    return null;
                });
            }
        }
    }
}
