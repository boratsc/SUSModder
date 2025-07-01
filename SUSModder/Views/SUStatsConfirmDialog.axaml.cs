using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public partial class SUStatsConfirmDialog : Window
    {
        public bool? DialogResult { get; private set; }
        public bool UseStats { get; private set; }
        public SUStatsConfirmDialog()
        {
            InitializeComponent();
        }

        public SUStatsConfirmDialog(string serverName)
        {
            InitializeComponent();
            Title = "Statystyki SUStats";

            // Ustaw tekst z nazwą serwera
            if (this.FindControl<TextBlock>("ServerNameText") is TextBlock serverNameText)
            {
                serverNameText.Text = serverName;
            }
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            UseStats = true;
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            UseStats = false;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
