using System.Threading.Tasks;

namespace SUSModder.Core.Utilities
{
    public interface IUserInteractionAsync
    {
        Task<bool> ConfirmAsync(string message, string title = "");
        Task ShowInfoAsync(string message, string title = "");
        Task ShowErrorAsync(string message, string title = "");
        Task<string?> PromptAsync(string message, string title = "");
        Task<string?> SelectFileAsync(string filter, string initialDirectory = "");
    }
}
