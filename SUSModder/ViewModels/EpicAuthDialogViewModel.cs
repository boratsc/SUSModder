using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SUSModder.ViewModels
{
    public class EpicAuthDialogViewModel : ViewModelBase
    {
        private readonly Window _window;
        private readonly string _browserUrl;
        private string _authorizationInput = string.Empty;
        private string _autoOpenMessage = "Przeglądarka otworzy się automatycznie za 5 sekund...";
        private bool _browserOpened = false;

        public EpicAuthDialogViewModel(Window window, string browserUrl)
        {
            _window = window;
            _browserUrl = browserUrl;

            OpenBrowserCommand = ReactiveCommand.Create(OpenBrowser);
            ConfirmCommand = ReactiveCommand.Create(Confirm);
            CancelCommand = ReactiveCommand.Create(Cancel);

            // Automatycznie otwórz przeglądarkę po 5 sekundach z countdown
            _ = AutoOpenBrowserAfterDelay();
        }

        private async Task AutoOpenBrowserAfterDelay()
        {
            try
            {
                // Countdown 5 -> 1
                for (int i = 5; i > 0; i--)
                {
                    AutoOpenMessage = $"🌐 Przeglądarka otworzy się automatycznie za {i} sekund...";
                    Debug.WriteLine($"[EpicAuthDialog] Countdown: {i}");
                    await Task.Delay(1000);

                    // Sprawdź czy dialog jest nadal otwarty
                    if (!_window.IsVisible)
                        return;
                }

                // Otwórz przeglądarkę
                if (_window.IsVisible && !_browserOpened)
                {
                    Debug.WriteLine("[EpicAuthDialog] Automatyczne otwieranie przeglądarki...");
                    AutoOpenMessage = "✅ Przeglądarka została otwarta!";
                    OpenBrowser();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd podczas auto-open: {ex.Message}");
            }
        }

        public string AutoOpenMessage
        {
            get => _autoOpenMessage;
            set => this.RaiseAndSetIfChanged(ref _autoOpenMessage, value);
        }

        public string AuthorizationInput
        {
            get => _authorizationInput;
            set => this.RaiseAndSetIfChanged(ref _authorizationInput, value);
        }

        public ReactiveCommand<Unit, Unit> OpenBrowserCommand { get; }
        public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        /// <summary>
        /// Wynik dialogu - authorization input od użytkownika lub null jeśli anulowano
        /// </summary>
        public string? Result { get; private set; }

        private void OpenBrowser()
        {
            try
            {
                if (!_browserOpened)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _browserUrl,
                        UseShellExecute = true
                    });
                    _browserOpened = true;
                    AutoOpenMessage = "✅ Przeglądarka została otwarta!";
                    Debug.WriteLine("[EpicAuthDialog] Przeglądarka otwarta przez użytkownika lub auto-open");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Błąd podczas otwierania przeglądarki: {ex.Message}");
                AutoOpenMessage = "❌ Błąd podczas otwierania przeglądarki";
            }
        }

        private void Confirm()
        {
            if (!string.IsNullOrWhiteSpace(AuthorizationInput))
            {
                Result = AuthorizationInput.Trim();
                _window.Close(true);
            }
        }

        private void Cancel()
        {
            Result = null;
            _window.Close(false);
        }
    }
}
