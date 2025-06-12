using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public partial class MessageDialog : Window
    {
        public MessageDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
