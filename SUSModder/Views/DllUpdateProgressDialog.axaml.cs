using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SUSModder.Core.Services.Localization;
using System;

namespace SUSModder.Views
{
    public partial class DllUpdateProgressDialog : Window
    {
        private TextBlock? _dllModNameText;
        private TextBlock? _currentLocationText;
        private TextBlock? _statusText;
        private TextBlock? _percentText;
        private TextBlock? _bigPercentText;
        private Border? _progressFill;
        private readonly ILocalizationService _localizationService;

        public DllUpdateProgressDialog()
        {
            _localizationService = App.GetService<ILocalizationService>();
            
            InitializeComponent();
            _dllModNameText = this.FindControl<TextBlock>("DllModNameText");
            _currentLocationText = this.FindControl<TextBlock>("CurrentLocationText");
            _statusText = this.FindControl<TextBlock>("StatusText");
            _percentText = this.FindControl<TextBlock>("PercentText");
            _bigPercentText = this.FindControl<TextBlock>("BigPercentText");
            _progressFill = this.FindControl<Border>("ProgressFill");
        }

        public DllUpdateProgressDialog(string dllModName) : this()
        {
            if (_dllModNameText != null)
            {
                _dllModNameText.Text = dllModName;
            }
            this.Title = _localizationService.GetFormatted("DllManager.UpdateProgress.WindowTitle", dllModName);
        }

        public void UpdateProgress(int current, int total, string locationName, string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Oblicz procent
                int percent = total > 0 ? (int)((current / (double)total) * 100) : 0;

                // Aktualizuj teksty procentu
                if (_percentText != null)
                {
                    _percentText.Text = $"{percent}%";
                }
                if (_bigPercentText != null)
                {
                    _bigPercentText.Text = $"{percent}%";
                }

                // Aktualizuj pasek postępu
                if (_progressFill != null && this.Bounds.Width > 0)
                {
                    double maxWidth = 440; // Szerokość paska (500 - 2*30 margin - padding)
                    _progressFill.Width = (percent / 100.0) * maxWidth;
                }

                // Aktualizuj lokalizację
                if (_currentLocationText != null)
                {
                    _currentLocationText.Text = _localizationService.Get("DllManager.UpdateProgress.LocationPrefix") + locationName;
                }

                // Aktualizuj status
                if (_statusText != null)
                {
                    _statusText.Text = status;
                }
            });
        }

        public void SetCompleted()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_statusText != null)
                {
                    _statusText.Text = _localizationService.Get("DllManager.UpdateProgress.UpdateCompleted");
                }
            });
        }

        public void SetError(string errorMessage)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_statusText != null)
                {
                    _statusText.Text = _localizationService.GetFormatted("DllManager.UpdateProgress.UpdateError", errorMessage);
                }
            });
        }
    }
}
