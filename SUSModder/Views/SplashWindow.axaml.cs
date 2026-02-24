using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace SUSModder.Views
{
    public partial class SplashWindow : Window
    {
        private Border? _progressBar;
        private TextBlock? _loadingText;

        public SplashWindow()
        {
            InitializeComponent();
            _progressBar = this.FindControl<Border>("ProgressBar");
            _loadingText = this.FindControl<TextBlock>("LoadingText");
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
        /// Zamyka splash window z fade out effect
        /// </summary>
        public async Task CloseWithFadeAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Fade out animation (zoptymalizowana - 6 kroków × 25ms zamiast 10 × 30ms)
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
