using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.ViewModels;
using System;
using System.Diagnostics;
using System.Web;


namespace SUSModder.Views
{
    public partial class EpicAuthDialog : Window
    {
        private EpicAuthDialogViewModel? _viewModel;

        public EpicAuthDialog()
        {
            InitializeComponent();
        }

        public EpicAuthDialog(string browserUrl) : this()
        {
            _viewModel = new EpicAuthDialogViewModel(this, browserUrl);
            DataContext = _viewModel;

            // Jeśli WebView2 jest dostępny, zainicjalizuj embedded przeglądarkę
            if (_viewModel.IsWebViewMode)
            {
                InitializeWebView(browserUrl);
            }
        }

        /// <summary>
        /// Inicjalizuje NativeWebView i ustawia event handlery do przechwycenia kodu autoryzacyjnego.
        /// </summary>
        private void InitializeWebView(string browserUrl)
        {
            try
            {
                var webView = this.FindControl<NativeWebView>("EpicWebView");
                if (webView == null)
                {
                    Debug.WriteLine("[EpicAuthDialog] Nie znaleziono kontrolki NativeWebView");
                    _viewModel?.FallbackToManualMode();
                    return;
                }

                // Konfiguracja środowiska przed inicjalizacją
                webView.AdapterCreated += (sender, e) =>
                {
                    Debug.WriteLine("[EpicAuthDialog] NativeWebView zainicjalizowany pomyślnie");
                    if (_viewModel != null)
                    {
                        _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";
                    }
                };

                // Event: przechwycenie nawigacji
                webView.NavigationStarted += OnWebViewNavigationStarted;

                // Event: nawigacja zakończona
                webView.NavigationCompleted += (sender, e) =>
                {
                    if (_viewModel != null && e.IsSuccess)
                    {
                        _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";
                    }
                };

                Debug.WriteLine($"[EpicAuthDialog] Inicjalizacja NativeWebView z URL: {browserUrl}");
                webView.Source = new Uri(browserUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Wyjątek podczas inicjalizacji NativeWebView: {ex.Message}");
                _viewModel?.FallbackToManualMode();
            }
        }


        /// <summary>
        /// Przechwytuje nawigację NativeWebView.
        /// Gdy Epic przekieruje na https://localhost/?code=XXXXX, wyciągamy kod i zamykamy dialog.
        /// </summary>
        private void OnWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            try
            {
                var uri = e.Request!;
                Debug.WriteLine($"[EpicAuthDialog] Nawigacja do: {uri?.Host}{uri?.PathAndQuery}");

                // Sprawdź czy to redirect na localhost z kodem autoryzacyjnym
                if (string.Equals(uri?.Host ?? "", "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    // Zatrzymaj nawigację - nie chcemy faktycznie nawigować na localhost
                    e.Cancel = true;

                    // Wyciągnij kod z query string
                    var queryParams = uri != null ? HttpUtility.ParseQueryString(uri.Query) : null;
                    var authCode = queryParams?["code"];

                    if (!string.IsNullOrWhiteSpace(authCode))
                    {
                        Debug.WriteLine($"[EpicAuthDialog] Przechwycono kod autoryzacyjny: {authCode.Substring(0, Math.Min(10, authCode.Length))}...");
                        _viewModel?.SetWebViewAuthCode(authCode);
                    }
                    else
                    {
                        Debug.WriteLine("[EpicAuthDialog] Redirect na localhost ale bez kodu - prawdopodobnie błąd");
                        if (_viewModel != null)
                        {
                            _viewModel.WebViewStatus = "Nie udało się uzyskać kodu. Spróbuj ponownie.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd przechwytywania nawigacji: {ex.Message}");
            }
        }

        /// <summary>
        /// Otwiera URL w zewnętrznej (systemowej) przeglądarce - jako alternatywa dla NativeWebView.
        /// </summary>
        private void OnOpenInExternalBrowser(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _viewModel.BrowserUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd otwierania przeglądarki: {ex.Message}");
            }
        }

        private void OnLegendaryLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/derrod/legendary",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore if browser fails to open
            }
        }
    }
}
