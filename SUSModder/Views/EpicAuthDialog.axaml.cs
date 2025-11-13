using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.ViewModels;
using System.Diagnostics;

namespace SUSModder.Views
{
    public partial class EpicAuthDialog : Window
    {
        public EpicAuthDialog()
        {
            InitializeComponent();
        }

        public EpicAuthDialog(string browserUrl) : this()
        {
            DataContext = new EpicAuthDialogViewModel(this, browserUrl);
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
