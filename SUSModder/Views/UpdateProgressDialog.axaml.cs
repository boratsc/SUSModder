using Avalonia.Controls;
using Avalonia.Threading;
using System;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Views
{
    public partial class UpdateProgressDialog : Window
    {
        private readonly ILocalizationService _localizationService;
        private string _modName = string.Empty;
        private string _status = string.Empty;
        private int _progress = 0;

        public UpdateProgressDialog()
        {
            InitializeComponent();
            DataContext = this;
            _localizationService = App.GetService<ILocalizationService>();
        }

        public UpdateProgressDialog(string modName) : this()
        {
            ModName = modName;
            Title = _localizationService.Get("Dialogs.Progress.WindowTitle");
        }

        public string ModName
        {
            get => _modName;
            set
            {
                _modName = value;
                Dispatcher.UIThread.Post(() =>
                {
                    ModNameText.Text = _localizationService.GetFormatted("Dialogs.Progress.UpdateLabel", value);
                });
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText.Text = value;
                });
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                Dispatcher.UIThread.Post(() =>
                {
                    BigPercentText.Text = $"{value}%";
                    PercentText.Text = $"{value}%";
                    // Zakładamy, że szerokość kontenera to ~410px (440 - 30 padding)
                    var maxWidth = 410.0;
                    ProgressFill.Width = (maxWidth * value) / 100.0;
                });
            }
        }

        public void UpdateProgress(int percentage, string status)
        {
            Progress = percentage;
            Status = status;
        }
    }
}
