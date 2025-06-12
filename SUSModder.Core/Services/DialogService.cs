using System.Threading.Tasks;

namespace SUSModder.Core.Services
{
    public interface IDialogService
    {
        Task<bool> ShowLobbySetDialogAsync();
        Task ShowMessageAsync(string title, string message);
    }

    public class DialogService : IDialogService
    {
        public async Task<bool> ShowLobbySetDialogAsync()
        {
            // Ta metoda będzie wywołana z ViewModelu
            // Implementacja będzie w MainWindowViewModel
            await Task.CompletedTask;
            return false;
        }

        public async Task ShowMessageAsync(string title, string message)
        {
            // Implementacja będzie w MainWindowViewModel
            await Task.CompletedTask;
        }
    }
}
