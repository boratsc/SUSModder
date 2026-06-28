using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
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

            // Jeśli NativeWebView jest dostępny, zainicjalizuj embedded przeglądarkę
            if (_viewModel.IsWebViewMode)
            {
                InitializeWebView(browserUrl);
            }
        }

        /// <summary>
        /// Inicjalizuje NativeWebView programowo (lazy) i ustawia event handlery.
        /// Assembly Avalonia.Controls.WebView jest ładowane dopiero w tym momencie.
        /// Nawigacja do URL następuje dopiero po AdapterCreated – inaczej WebView nie ładuje strony.
        /// </summary>
        private void InitializeWebView(string browserUrl)
        {
            try
            {
                // Znajdź Grid dla WebView (wiersz 0 sekcji WebViewMode)
                var webViewGrid = this.FindControl<Grid>("WebViewContainer");
                if (webViewGrid == null)
                {
                    Debug.WriteLine("[EpicAuthDialog] Nie znaleziono kontenera WebViewContainer");
                    _viewModel?.FallbackToManualMode();
                    return;
                }

                // Utwórz NativeWebView programowo – to ładuje assembly Avalonia.Controls.WebView
                var webView = new NativeWebView
                {
                    Margin = new Avalonia.Thickness(0)
                };

                // Ustaw w wierszu 0 gridu (cała dostępna przestrzeń)
                Grid.SetRow(webView, 0);
                webViewGrid.Children.Add(webView);

                // Event: przechwycenie nawigacji (localhost redirect z kodem)
                webView.NavigationStarted += OnWebViewNavigationStarted;

                // Event: nawigacja zakończona – wyciągamy authorizationCode z JSON body
                webView.NavigationCompleted += async (sender, e) =>
                {
                    if (!e.IsSuccess) return;

                    if (_viewModel != null)
                        _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";

                    try
                    {
                        var currentUrl = webView.Source?.ToString() ?? "";
                        if (!currentUrl.Contains("/id/api/redirect", StringComparison.OrdinalIgnoreCase))
                            return;

                        Debug.WriteLine("[EpicAuthDialog] NavigationCompleted na /id/api/redirect – próbuję InvokeScript");

                        // Najpierw test czy InvokeScript w ogóle działa
                        var testResult = await webView.InvokeScript("document.title");
                        Debug.WriteLine($"[EpicAuthDialog] InvokeScript test (title): '{testResult}'");

                        // Dla JSON response (application/json) WebView może renderować w <pre> lub jako text/plain.
                        // Próbujemy różne selektory: <pre>, body.textContent, documentElement.textContent
                        var bodyText = await webView.InvokeScript(
                            "(document.querySelector('pre')?.textContent || document.body?.textContent || document.documentElement?.textContent || '').trim()");

                        Debug.WriteLine($"[EpicAuthDialog] InvokeScript body (pierwsze 200 znaków): '{bodyText?.Substring(0, Math.Min(200, bodyText?.Length ?? 0))}'");

                        if (string.IsNullOrWhiteSpace(bodyText))
                        {
                            Debug.WriteLine("[EpicAuthDialog] Body puste – InvokeScript nie działa dla tego content-type");
                            return;
                        }

                        // InvokeScript zwraca wynik jako JSON-string, czyli wartość jest dodatkowo
                        // zakodowana (z escape'owanymi cudzysłowami). Np. dla JS stringa '{"a":1}'
                        // dostajemy C# string '"{\\"a\\":1}"'.
                        // Trzeba najpierw zdeserializować zewnętrzny JSON-string.
                        try
                        {
                            var rawJson = System.Text.Json.JsonSerializer.Deserialize<string>(bodyText.Trim());
                            if (!string.IsNullOrWhiteSpace(rawJson) && rawJson.StartsWith("{"))
                            {
                                bodyText = rawJson;
                            }
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            Debug.WriteLine("[EpicAuthDialog] Body nie jest JSON-stringiem, próbuję użyć surowego");
                        }

                        if (!bodyText.StartsWith("{"))
                        {
                            Debug.WriteLine($"[EpicAuthDialog] Body nie zaczyna się od '{{' – to nie JSON: {bodyText.Substring(0, Math.Min(100, bodyText.Length))}");
                            return;
                        }

                        Debug.WriteLine($"[EpicAuthDialog] Body: {bodyText.Substring(0, Math.Min(300, bodyText.Length))}");

                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(bodyText);

                        if (jsonDoc.RootElement.TryGetProperty("authorizationCode", out var codeProp))
                        {
                            var authCode = codeProp.GetString();
                            if (!string.IsNullOrWhiteSpace(authCode))
                            {
                                Debug.WriteLine("[EpicAuthDialog] Znaleziono authorizationCode!");
                                _viewModel?.SetWebViewAuthCode(authCode);
                                return;
                            }
                        }

                        if (jsonDoc.RootElement.TryGetProperty("sid", out var sidProp))
                        {
                            var sid = sidProp.GetString();
                            if (!string.IsNullOrWhiteSpace(sid))
                            {
                                Debug.WriteLine("[EpicAuthDialog] Znaleziono sid!");
                                _viewModel?.SetWebViewAuthCode(sid);
                                return;
                            }
                        }

                        Debug.WriteLine("[EpicAuthDialog] JSON nie zawiera authorizationCode ani sid");
                    }
                    catch (Exception invokeEx)
                    {
                        Debug.WriteLine($"[EpicAuthDialog] Błąd InvokeScript: {invokeEx.Message}");
                    }
                };

                // Czekamy aż adapter WebView będzie gotowy, potem ładujemy URL
                webView.AdapterCreated += async (sender, e) =>
                {
                    Debug.WriteLine("[EpicAuthDialog] NativeWebView zainicjalizowany pomyślnie – ładuję URL");

                    if (_viewModel != null)
                        _viewModel.WebViewStatus = "Zaloguj się na swoje konto Epic Games";

                    await Task.Delay(100);
                    Debug.WriteLine($"[EpicAuthDialog] Ładowanie URL: {browserUrl}");
                    webView.Source = new Uri(browserUrl);
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Wyjątek: {ex.Message}");
                _viewModel?.FallbackToManualMode();
            }
        }

        /// <summary>
        /// Otwiera link do legendary GitHub w systemowej przeglądarce.
        /// </summary>
        private void OnLegendaryLinkClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/derrod/legendary",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd otwierania linku: {ex.Message}");
            }
        }

        /// <summary>
        /// Otwiera URL w zewnętrznej (systemowej) przeglądarce - jako alternatywa dla NativeWebView.
        /// </summary>
        private void OnOpenInExternalBrowser(object? sender, Avalonia.Input.PointerPressedEventArgs e)
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

        /// <summary>
        /// Przechwytuje nawigację NativeWebView.
        /// Łapie redirect na localhost (z redirectUrl w JSON /id/api/redirect) i wyciąga code.
        /// </summary>
        private void OnWebViewNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
        {
            try
            {
                var uri = e.Request!;
                Debug.WriteLine($"[EpicAuthDialog] Nawigacja do: {uri?.Host}{uri?.PathAndQuery}");

                if (uri == null) return;

                // Sprawdź czy to redirect na localhost z kodem autoryzacyjnym
                // (z pola "redirectUrl" w JSON response /id/api/redirect)
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    var queryParams = HttpUtility.ParseQueryString(uri.Query);
                    var authCode = queryParams["code"];

                    if (!string.IsNullOrWhiteSpace(authCode))
                    {
                        Debug.WriteLine($"[EpicAuthDialog] Przechwycono kod z localhost: {authCode.Substring(0, Math.Min(10, authCode.Length))}...");
                        _viewModel?.SetWebViewAuthCode(authCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EpicAuthDialog] Błąd nawigacji: {ex.Message}");
            }
        }
    }
}
