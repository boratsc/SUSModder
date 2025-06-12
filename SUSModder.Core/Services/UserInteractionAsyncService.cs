using SUSModder.Core.Utilities;
using System.Threading.Tasks;

namespace SUSModder.Core.Services
{
    public class UserInteractionAsyncService : IUserInteractionAsync
    {
        private readonly Func<string, string, Task<bool>> _confirmDialog;
        private readonly Func<string, string, Task> _infoDialog;
        private readonly Func<string, string, Task> _errorDialog;
        private readonly Func<string, string, Task<string?>> _promptDialog;
        private readonly Func<string, string, Task<string?>> _selectFileDialog;

        public UserInteractionAsyncService(
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

        public async Task<bool> ConfirmAsync(string message, string title = "")
        {
            return await _confirmDialog(message, title);
        }

        public async Task ShowInfoAsync(string message, string title = "")
        {
            await _infoDialog(message, title);
        }

        public async Task ShowErrorAsync(string message, string title = "")
        {
            await _errorDialog(message, title);
        }

        public async Task<string?> PromptAsync(string message, string title = "")
        {
            return await _promptDialog(message, title);
        }

        public async Task<string?> SelectFileAsync(string filter, string initialDirectory = "")
        {
            return await _selectFileDialog(filter, initialDirectory);
        }
    }
}
