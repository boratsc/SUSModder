using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace SUSModder.Views
{
    public partial class SplashWindow : Window
    {
        private Border? _progressBar;
        private TextBlock? _loadingText;
        private NativeWebView? _splashVideo;

        public SplashWindow()
        {
            InitializeComponent();
            _progressBar = this.FindControl<Border>("ProgressBar");
            _loadingText = this.FindControl<TextBlock>("LoadingText");
        }

        /// <summary>
        /// Inicjalizuje odtwarzanie wideo splash przez NativeWebView.
        /// NativeWebView jest tworzony tylko gdy plik wideo istnieje – w przeciwnym razie
        /// assembly Avalonia.Controls.WebView nie jest ładowane (oszczędność ~50-100 MB RAM
        /// dla użytkowników Steam i Epic bez pliku wideo).
        /// </summary>
        public async Task InitializeVideoAsync()
        {
            try
            {
                var exeDir = AppContext.BaseDirectory;
                var videoPath = Path.Combine(exeDir, "Assets", "SplashAnimation.mp4");

                if (!File.Exists(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine("[SplashWindow] SplashAnimation.mp4 not found, using static image only");
                    return;
                }

                // Utwórz NativeWebView tylko gdy wideo istnieje – to wymusza load assembly
                // Avalonia.Controls.WebView dopiero w tym momencie, a nie przy starcie.
                var splashVideo = new NativeWebView
                {
                    Width = 640,
                    Height = 640,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                };

                // Dodaj do drzewa wizualnego (przed Image, żeby być na wierzchu)
                var panel = this.FindControl<Panel>("SplashPanel");
                if (panel != null)
                {
                    panel.Children.Insert(0, splashVideo);
                }

                _splashVideo = splashVideo;

                var splashJpgPath = Path.Combine(exeDir, "Assets", "splashscreen.jpg");

                // Wczytaj szablon HTML
                var html = LoadEmbeddedHtml();
                if (html == null) return;

                // Podmień placeholdery na file:// URI
                var videoUri = new Uri(videoPath).AbsoluteUri;
                var splashUri = new Uri(splashJpgPath).AbsoluteUri;
                html = html.Replace("VIDEO_SRC", videoUri)
                           .Replace("SPLASH_JPG_FILE", splashUri);

                // Zapisz tymczasowy HTML
                var htmlPath = Path.Combine(Path.GetTempPath(), "_susmodder_splash.html");
                await File.WriteAllTextAsync(htmlPath, html);

                // Ustaw źródło NativeWebView dopiero po AdapterCreated
                // (programowo tworzony WebView wymaga gotowego adaptera do nawigacji)
                splashVideo.AdapterCreated += async (sender, e) =>
                {
                    await Task.Delay(100);
                    splashVideo.Source = new Uri(htmlPath);
                    System.Diagnostics.Debug.WriteLine("[SplashWindow] NativeWebView lazily created and initialized with video");
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SplashWindow] Video init error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ładuje wbudowany szablon HTML.
        /// </summary>
        private static string? LoadEmbeddedHtml()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "SUSModder.Assets.splash-player.html";
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SplashWindow] Failed to load HTML: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Aktualizuje pasek postępu (0.0 - 1.0)
        /// </summary>
        public void UpdateProgress(double progress, string? statusText = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_progressBar != null)
                {
                    var targetWidth = this.Width * Math.Clamp(progress, 0, 1);
                    _progressBar.Width = targetWidth;
                }

                if (_loadingText != null && statusText != null)
                {
                    _loadingText.Text = statusText;
                }
            });
        }

        /// <summary>
        /// Animuje pasek postępu do określonej wartości
        /// </summary>
        public async Task AnimateProgressAsync(double targetProgress, int durationMs = 80)
        {
            if (_progressBar == null) return;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var currentWidth = _progressBar.Width;
                var targetWidth = this.Width * Math.Clamp(targetProgress, 0, 1);
                var steps = 8;
                var stepDuration = durationMs / steps;
                var increment = (targetWidth - currentWidth) / steps;

                for (int i = 0; i < steps; i++)
                {
                    _progressBar.Width += increment;
                    await Task.Delay(stepDuration);
                }

                _progressBar.Width = targetWidth;
            });
        }

        /// <summary>
        /// Zamyka splash window z fade out i sprzątaniem
        /// </summary>
        public async Task CloseWithFadeAsync()
        {
            // Wyczyść tymczasowy HTML
            try
            {
                var htmlPath = Path.Combine(Path.GetTempPath(), "_susmodder_splash.html");
                if (File.Exists(htmlPath)) File.Delete(htmlPath);
            }
            catch { /* ignore */ }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var steps = 6;
                for (int i = 0; i < steps; i++)
                {
                    this.Opacity = 1.0 - (i / (double)steps);
                    await Task.Delay(25);
                }
                this.Close();
            });
        }
    }
}