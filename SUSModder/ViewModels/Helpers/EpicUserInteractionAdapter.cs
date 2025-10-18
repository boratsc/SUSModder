using SUSModder.Core.GameIntegration;
using SUSModder.Core.Services;

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
    }
}
