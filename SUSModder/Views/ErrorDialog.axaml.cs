using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public partial class ErrorDialog : Window
    {
        public ErrorDialog()
        {
            InitializeComponent();
        }

        public ErrorDialog(string title, string message) : this()
        {
            Title = title;
            ErrorMessage = message;
        }

        public string ErrorMessage
        {
            get => ErrorMessageTextBox.Text ?? string.Empty;
            set => ErrorMessageTextBox.Text = value;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyButton_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard != null)
            {
                _ = TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(ErrorMessage);
            }
        }
    }
}
