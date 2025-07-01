using System.Threading.Tasks;

namespace SUSModder.Core.Utilities
{
    public interface IUserInteraction
    {
        // Synchroniczne metody (zachowane dla kompatybilności)
        bool Confirm(string message, string title = "");
        void ShowInfo(string message, string title = "");
        void ShowError(string message, string title = "");
        string? Prompt(string message, string title = "");
        string? SelectFile(string filter, string initialDirectory = "");

        // Asynchroniczne metody (nowe)
        Task ShowInfoAsync(string message, string title = "");
        Task ShowErrorAsync(string message, string title = "");
        Task<bool> ShowConfirmAsync(string message, string title = "");
        Task<string?> ShowPromptAsync(string message, string title = "");
        Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "");

    }
}
