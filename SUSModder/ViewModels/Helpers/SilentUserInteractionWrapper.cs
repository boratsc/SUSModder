using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels.Helpers
{
    /// <summary>
    /// Wrapper dla UserInteractionService - pomija niektóre komunikaty informacyjne
    /// </summary>
    public class SilentUserInteractionWrapper : IUserInteraction
    {
        private readonly UserInteractionService _inner;

        public SilentUserInteractionWrapper(UserInteractionService inner)
        {
            _inner = inner;
        }

        public bool Confirm(string message, string title = "") => _inner.Confirm(message, title);
        public void ShowInfo(string message, string title = "") => System.Diagnostics.Debug.WriteLine($"[Silent] Info: {message}");
        public void ShowError(string message, string title = "") => _inner.ShowError(message, title);
        public string? Prompt(string message, string title = "") => _inner.Prompt(message, title);
        public string? SelectFile(string filter, string initialDirectory = "") => _inner.SelectFile(filter, initialDirectory);

        public Task ShowInfoAsync(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Silent] InfoAsync: {message}");
            return Task.CompletedTask;
        }

        public async Task ShowErrorAsync(string message, string title = "") => await _inner.ShowErrorAsync(message, title);
        public async Task<bool> ShowConfirmAsync(string message, string title = "") => await _inner.ShowConfirmAsync(message, title);
        public async Task<string?> ShowPromptAsync(string message, string title = "") => await _inner.ShowPromptAsync(message, title);
        public async Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "") => await _inner.ShowSelectFileDialogAsync(filter, initialDirectory);
    }
}
