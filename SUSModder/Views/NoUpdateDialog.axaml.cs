using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public partial class NoUpdateDialog : Window
    {
        public NoUpdateDialog()
        {
            InitializeComponent();
            CurrentVersionText.Text = "-";
        }

        public NoUpdateDialog(string currentVersion) : this()
        {
            CurrentVersionText.Text = currentVersion;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
