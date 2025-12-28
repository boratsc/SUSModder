using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
        private CancellationTokenSource? _clipboardMonitorCts;
        private string _lastClipboardContent = string.Empty;
        private bool _isMonitoringClipboard = false;
        private string _clipboardMonitorStatus = string.Empty;

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
                    
                    // Rozpocznij monitorowanie schowka po otwarciu przeglądarki
                    StartClipboardMonitoring();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd podczas auto-open: {ex.Message}");
            }
        }

        /// <summary>
        /// Rozpoczyna monitorowanie schowka w poszukiwaniu kodu autoryzacji
        /// </summary>
        private void StartClipboardMonitoring()
        {
            if (_isMonitoringClipboard)
                return;

            _isMonitoringClipboard = true;
            _clipboardMonitorCts = new CancellationTokenSource();

            Debug.WriteLine("[EpicAuthDialog] Rozpoczynam monitorowanie schowka...");
            ClipboardMonitorStatus = "📋 Monitorowanie schowka aktywne - skopiuj kod!";

            _ = MonitorClipboardAsync(_clipboardMonitorCts.Token);
        }

        /// <summary>
        /// Zatrzymuje monitorowanie schowka
        /// </summary>
        private void StopClipboardMonitoring()
        {
            _isMonitoringClipboard = false;
            _clipboardMonitorCts?.Cancel();
            _clipboardMonitorCts?.Dispose();
            _clipboardMonitorCts = null;
            Debug.WriteLine("[EpicAuthDialog] Zatrzymano monitorowanie schowka");
        }

        /// <summary>
        /// Pętla monitorująca schowek
        /// </summary>
        private async Task MonitorClipboardAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _window.IsVisible)
                {
                    await Task.Delay(500, cancellationToken); // Sprawdzaj co 500ms

                    // Pobierz zawartość schowka na UI thread
                    string? clipboardText = null;
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            var topLevel = TopLevel.GetTopLevel(_window);
                            var clipboard = topLevel?.Clipboard;
                            if (clipboard != null)
                            {
                                // Użyj TryGetTextAsync zamiast przestarzałego GetTextAsync
                                clipboardText = await clipboard.TryGetTextAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[EpicAuthDialog] Błąd odczytu schowka: {ex.Message}");
                        }
                    });

                    // Sprawdź czy zawartość się zmieniła i czy zawiera kod
                    if (!string.IsNullOrWhiteSpace(clipboardText) && 
                        clipboardText != _lastClipboardContent)
                    {
                        _lastClipboardContent = clipboardText;
                        Debug.WriteLine($"[EpicAuthDialog] Wykryto nową zawartość schowka ({clipboardText.Length} znaków)");

                        // Sprawdź czy to może być kod autoryzacji
                        var extractedCode = ExtractAuthorizationCode(clipboardText);
                        if (extractedCode != null)
                        {
                            Debug.WriteLine($"[EpicAuthDialog] Wykryto kod autoryzacji: {extractedCode.Substring(0, Math.Min(10, extractedCode.Length))}...");

                            // Ustaw wartość w polu tekstowym i automatycznie zaloguj
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                AuthorizationInput = clipboardText;
                                ClipboardMonitorStatus = "✅ Wykryto kod autoryzacji! Loguję...";
                                AutoOpenMessage = "✅ Kod autoryzacji wykryty w schowku!";
                            });

                            // Poczekaj chwilę dla wizualnego feedbacku
                            await Task.Delay(800, cancellationToken);

                            // Automatycznie zaloguj
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                Confirm();
                            });

                            return; // Zakończ monitoring
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normalne anulowanie
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd monitorowania schowka: {ex.Message}");
            }
        }

        /// <summary>
        /// Wyciąga authorization code z różnych formatów wejściowych:
        /// - JSON response od Epic Games
        /// - Redirect URL (https://localhost/launcher/authorized?code=...)
        /// - Bezpośredni kod autoryzacji
        /// </summary>
        private string? ExtractAuthorizationCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim();

            // 1. Spróbuj JSON
            try
            {
                using var jsonDoc = JsonDocument.Parse(input);
                if (jsonDoc.RootElement.TryGetProperty("authorizationCode", out var codeElement))
                {
                    var code = codeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        Debug.WriteLine($"[EpicAuthDialog] Wyciągnięto kod z JSON");
                        return code;
                    }
                }
            }
            catch
            {
                // Nie jest JSON, kontynuuj
            }

            // 2. Spróbuj URL z query parameter ?code=
            if (input.Contains("?code=") || input.Contains("&code="))
            {
                try
                {
                    var codeIndex = input.IndexOf("?code=");
                    if (codeIndex < 0)
                        codeIndex = input.IndexOf("&code=");

                    if (codeIndex >= 0)
                    {
                        var startIndex = input.IndexOf('=', codeIndex) + 1;
                        var endIndex = input.IndexOf('&', startIndex);
                        var code = endIndex > startIndex
                            ? input.Substring(startIndex, endIndex - startIndex)
                            : input.Substring(startIndex);

                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            Debug.WriteLine($"[EpicAuthDialog] Wyciągnięto kod z URL");
                            return code.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EpicAuthDialog] Błąd parsowania URL: {ex.Message}");
                }
            }

            // 3. Użyj bezpośrednio jako kod (usuń whitespace i cudzysłowy)
            var cleanCode = input.Trim().Trim('"', '\'').Trim();
            // Epic authorization codes są zazwyczaj 32-znakowe hex stringi, ale akceptujmy 20-64 znaki alfanumeryczne
            if (cleanCode.Length >= 20 && cleanCode.Length <= 64 &&
                !cleanCode.Contains(" ") && !cleanCode.Contains("\n") && !cleanCode.Contains("\r"))
            {
                // Sprawdź czy to wygląda jak authorization code (alfanumeryczny)
                if (Regex.IsMatch(cleanCode, @"^[a-zA-Z0-9]+$"))
                {
                    Debug.WriteLine($"[EpicAuthDialog] Użyto bezpośredniego kodu");
                    return cleanCode;
                }
            }

            return null;
        }

        public string ClipboardMonitorStatus
        {
            get => _clipboardMonitorStatus;
            set => this.RaiseAndSetIfChanged(ref _clipboardMonitorStatus, value);
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
                    
                    // Rozpocznij monitorowanie schowka po ręcznym otwarciu przeglądarki
                    StartClipboardMonitoring();
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
                StopClipboardMonitoring();
                Result = AuthorizationInput.Trim();
                _window.Close(true);
            }
        }

        private void Cancel()
        {
            StopClipboardMonitoring();
            Result = null;
            _window.Close(false);
        }
    }
}
