using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using System;
using System.ComponentModel;

namespace SUSModder.Views
{
    public partial class PromptDialog : Window, INotifyPropertyChanged
    {
        private string _dialogTitle = "";
        private string _message = "";
        private string _inputText = "";

        public string DialogTitle
        {
            get => _dialogTitle;
            set
            {
                _dialogTitle = value;
                Title = value; // Ustaw tytuł okna
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DialogTitle)));
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText)));
            }
        }

        public bool DialogResult { get; private set; } = false;

        public new event PropertyChangedEventHandler? PropertyChanged;

        public PromptDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public PromptDialog(string title, string message, string defaultValue = "")
        {
            InitializeComponent();
            DialogTitle = title;
            Message = message;
            InputText = defaultValue;
            DataContext = this;
        }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            var textBox = this.FindControl<TextBox>("InputTextBox");
            if (textBox != null)
            {
                InputText = textBox.Text ?? "";
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            var textBox = this.FindControl<TextBox>("InputTextBox");
            textBox?.Focus();
        }
    }
}
