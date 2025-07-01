using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using System.Threading.Tasks;
using System;

namespace SUSModder.Views
{
    public partial class HashDisplayDialog : Window, INotifyPropertyChanged
    {
        private string _hash = "";
        private string _message = "";

        public string Hash
        {
            get => _hash;
            set
            {
                _hash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hash)));
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

        public new event PropertyChangedEventHandler? PropertyChanged;

        // Konstruktor bezparametrowy dla AXAML
        public HashDisplayDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        // Konstruktor z hashem
        public HashDisplayDialog(string hash) : this()
        {
            Hash = hash;
            Message = "Konfiguracja została zapisana na serwerze!";
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(Hash);

                // Zmień tekst przycisku na chwilę
                if (sender is Button button)
                {
                    var originalContent = button.Content;
                    button.Content = "Skopiowano!";
                    button.IsEnabled = false;

                    await Task.Delay(1500);

                    button.Content = originalContent;
                    button.IsEnabled = true;
                }
            }
        }


        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            // Zaznacz hash w TextBox
            var hashTextBox = this.FindControl<TextBox>("HashTextBox");
            hashTextBox?.SelectAll();
            hashTextBox?.Focus();
        }
    }
}
