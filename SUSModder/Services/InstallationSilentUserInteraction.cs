using System.Threading.Tasks;
using SUSModder.Core.Utilities;

namespace SUSModder.Services
{
    public class InstallationSilentUserInteraction : IUserInteraction
    {
        public bool Confirm(string message, string title = "")
        {
            // Zawsze potwierdź podczas automatycznej aktualizacji
            return true;
        }

        public void ShowInfo(string message, string title = "")
        {
            // Nie pokazuj komunikatów info podczas aktualizacji
            System.Diagnostics.Debug.WriteLine($"[Silent] Info: {message}");
        }

        public void ShowError(string message, string title = "")
        {
            // Loguj błędy ale nie pokazuj dialogów
            System.Diagnostics.Debug.WriteLine($"[Silent] Error: {message}");
        }

        public string? Prompt(string message, string title = "")
        {
            return null;
        }

        public string? SelectFile(string filter, string initialDirectory = "")
        {
            return null;
        }

        public async Task ShowInfoAsync(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Silent] InfoAsync: {message}");
            await Task.CompletedTask;
        }

        public async Task ShowErrorAsync(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Silent] ErrorAsync: {message}");
            await Task.CompletedTask;
        }

        public async Task<bool> ShowConfirmAsync(string message, string title = "")
        {
            return await Task.FromResult(true);
        }

        public async Task<string?> ShowPromptAsync(string message, string title = "")
        {
            return await Task.FromResult<string?>(null);
        }

        public async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "")
        {
            return await Task.FromResult<string?>(null);
        }
    }
}
