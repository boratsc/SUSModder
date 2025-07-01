using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views
{
    public partial class ConfirmDialog : Window
    {
        public bool Result { get; private set; }

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        public ConfirmDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
        }
        public string OkButtonText
{
            get => YesButton.Content?.ToString() ?? "";
            set => YesButton.Content = value;
        }

        public string CancelButtonText
        {
            get => NoButton.Content?.ToString() ?? "";
            set => NoButton.Content = value;
        }

        private void YesButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void NoButton_Click(object? sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
