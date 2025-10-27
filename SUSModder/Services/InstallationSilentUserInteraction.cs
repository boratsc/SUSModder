using System.Collections.Generic;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;

namespace SUSModder.Services
{
    /// <summary>
    /// Implementacja IUserInteraction dla automatycznych instalacji z obsługą retry
    /// </summary>
    public class InstallationSilentUserInteraction : IUserInteraction
    {
        private readonly Dictionary<string, int> _retryCounters = new();
        private const int MAX_RETRIES = 3;

        public bool Confirm(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Installation] Confirm request: {message}");

            // Sprawdź czy to pytanie o retry
            if (message.Contains("spróbować ponownie") || message.Contains("Czy chcesz spróbować"))
            {
                // Stwórz klucz na podstawie typu błędu
                string errorKey = GetErrorKey(message);

                if (!_retryCounters.ContainsKey(errorKey))
                    _retryCounters[errorKey] = 0;

                _retryCounters[errorKey]++;

                if (_retryCounters[errorKey] <= MAX_RETRIES)
                {
                    System.Diagnostics.Debug.WriteLine($"[Installation] Auto-retry {_retryCounters[errorKey]}/{MAX_RETRIES} for: {errorKey}");
                    return true; // Spróbuj ponownie
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Installation] Max retries reached for: {errorKey}");
                    // Rzuć wyjątek żeby przerwać instalację i pokazać błąd użytkownikowi
                    throw new System.InvalidOperationException($"Przekroczono maksymalną liczbę prób ({MAX_RETRIES}) dla: {GetUserFriendlyError(message)}");
                }
            }

            // Dla innych pytań - zawsze potwierdź
            return true;
        }

        private string GetErrorKey(string message)
        {
            if (message.Contains("vanilla")) return "vanilla_download";
            if (message.Contains("moda")) return "mod_download";
            if (message.Contains("rozpakowywania")) return "extract_error";
            return "unknown_error";
        }

        private string GetUserFriendlyError(string message)
        {
            if (message.Contains("vanilla")) return "pobierania pliku gry vanilla";
            if (message.Contains("moda")) return "pobierania pliku moda";
            if (message.Contains("rozpakowywania")) return "rozpakowywania archiwum";
            return "nieznanego błędu";
        }

        public void ShowInfo(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Installation] Info: {message}");
        }

        public void ShowError(string message, string title = "")
        {
            System.Diagnostics.Debug.WriteLine($"[Installation] Error: {message}");
            // Rzuć wyjątek żeby błąd został obsłużony w głównym try/catch
            throw new System.InvalidOperationException(message);
        }

        public string? Prompt(string message, string title = "") => null;
        public string? SelectFile(string filter, string initialDirectory = "") => null;

        public Task ShowInfoAsync(string message, string title = "")
        {
            ShowInfo(message, title);
            return Task.CompletedTask;
        }

        public Task ShowErrorAsync(string message, string title = "")
        {
            ShowError(message, title);
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmAsync(string message, string title = "")
        {
            return Task.FromResult(Confirm(message, title));
        }

        public Task<string?> ShowPromptAsync(string message, string title = "")
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "")
        {
            return Task.FromResult<string?>(null);
        }

        /// <summary>
        /// Resetuje liczniki retry - wywołaj przed nową instalacją
        /// </summary>
        public void ResetRetryCounters()
        {
            _retryCounters.Clear();
        }
    }
}

