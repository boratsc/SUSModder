using SUSModder.Core.Utilities;
using System.Threading.Tasks;

namespace SUSModder.Core.Services
{
    public class UserInteractionService : IUserInteraction
    {
        private readonly Func<string, string, Task<bool>> _confirmDialog;
        private readonly Func<string, string, Task> _infoDialog;
        private readonly Func<string, string, Task> _errorDialog;
        private readonly Func<string, string, Task<string?>> _promptDialog;
        private readonly Func<string, string, Task<string?>> _selectFileDialog;

        public UserInteractionService(
            Func<string, string, Task<bool>> confirmDialog,
            Func<string, string, Task> infoDialog,
            Func<string, string, Task> errorDialog,
            Func<string, string, Task<string?>> promptDialog,
            Func<string, string, Task<string?>> selectFileDialog)
        {
            _confirmDialog = confirmDialog;
            _infoDialog = infoDialog;
            _errorDialog = errorDialog;
            _promptDialog = promptDialog;
            _selectFileDialog = selectFileDialog;
        }

        // Synchroniczne metody (zachowane dla kompatybilności)
        public bool Confirm(string message, string title = "")
        {
            return _confirmDialog(message, title).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public void ShowInfo(string message, string title = "")
        {
            _infoDialog(message, title).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public void ShowError(string message, string title = "")
        {
            _errorDialog(message, title).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public string? Prompt(string message, string title = "")
        {
            return _promptDialog(message, title).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public string? SelectFile(string filter, string initialDirectory = "")
        {
            return _selectFileDialog(filter, initialDirectory).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        // Asynchroniczne metody (nowe)
        public async Task ShowInfoAsync(string message, string title = "")
        {
            await _infoDialog(title, message);
        }

        public async Task ShowErrorAsync(string message, string title = "")
        {
            await _errorDialog(message, title);
        }

        public async Task<bool> ShowConfirmAsync(string message, string title = "")
        {
            return await _confirmDialog(message, title);
        }

        public async Task<string?> ShowPromptAsync(string message, string title = "")
        {
            return await _promptDialog(message, title);
        }

        public async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "")
        {
            return await _selectFileDialog(filter, initialDirectory);
        }
    }
}
