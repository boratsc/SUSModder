using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.ViewModels;
using System;
using System.Diagnostics;
using System.Web;
using Microsoft.Web.WebView2.Core;

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
        /// Inicjalizuje WebView2 i ustawia event handlery do przechwycenia kodu autoryzacyjnego.
        /// </summary>
        private async void InitializeWebView(string browserUrl)
        {
            try
            {
                var webView = this.FindControl<Avalonia.Controls.WebView2>("EpicWebView");
                if (webView == null)
                {
                    Debug.WriteLine("[EpicAuthDialog] Nie znaleziono kontrolki WebView2");
                    _viewModel?.FallbackToManualMode();
                    return;
                }

                // Event: inicjalizacja WebView2 zakończona
                webView.CoreWebView2InitializationCompleted += (sender, e) =>
                {
                    if (e.IsSuccess && webView.CoreWebView2 != null)
                    {
                        Debug.WriteLine("[EpicAuthDialog] WebView2 zainicjalizowany pomyślnie");

                        // Ustaw User-Agent na EpicGamesLauncher (jak robi Heroic Games Launcher)
                        webView.CoreWebView2.Settings.UserAgent =
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EpicGamesLauncher";

                        // Wyłącz niepotrzebne funkcje
                        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                        if (_viewModel != null)
                        {
                            _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[EpicAuthDialog] Błąd inicjalizacji WebView2: {e.InitializationException?.Message}");
                        _viewModel?.FallbackToManualMode();
                    }
                };

                // Event: przechwycenie nawigacji - tutaj łapiemy redirect z kodem
                webView.NavigationStarting += OnWebViewNavigationStarting;

                // Event: nawigacja zakończona - aktualizacja statusu
                webView.NavigationCompleted += (sender, e) =>
                {
                    if (_viewModel != null && e.IsSuccess)
                    {
                        _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";
                    }
                };

                // Inicjalizuj WebView2 - użyj browserExecutableFolder jeśli runtime wykryty na dysku
                var executableFolder = _viewModel?.WebView2BrowserExecutableFolder;
                Debug.WriteLine($"[EpicAuthDialog] Inicjalizacja WebView2 z URL: {browserUrl}, executableFolder: {executableFolder ?? "(auto)"}");

                if (executableFolder != null)
                {
                    // Runtime znaleziony na dysku ale nie w rejestrze - musimy wskazać ścieżkę
                    var environment = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: executableFolder);
                    await webView.EnsureCoreWebView2Async(environment);
                }
                else
                {
                    // Standardowa inicjalizacja (runtime wykryty przez rejestr)
                    await webView.EnsureCoreWebView2Async();
                }

                webView.Source = new Uri(browserUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Wyjątek podczas inicjalizacji WebView2: {ex.Message}");
                _viewModel?.FallbackToManualMode();
            }
        }

        /// <summary>
        /// Przechwytuje nawigację WebView2.
        /// Gdy Epic przekieruje na https://localhost/?code=XXXXX, wyciągamy kod i zamykamy dialog.
        /// </summary>
        private void OnWebViewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Uri);
                Debug.WriteLine($"[EpicAuthDialog] Nawigacja do: {uri.Host}{uri.PathAndQuery}");

                // Sprawdź czy to redirect na localhost z kodem autoryzacyjnym
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    // Zatrzymaj nawigację - nie chcemy faktycznie nawigować na localhost
                    e.Cancel = true;

                    // Wyciągnij kod z query string
                    var queryParams = HttpUtility.ParseQueryString(uri.Query);
                    var authCode = queryParams["code"];

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
        /// Otwiera URL w zewnętrznej (systemowej) przeglądarce - jako alternatywa dla WebView2.
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
