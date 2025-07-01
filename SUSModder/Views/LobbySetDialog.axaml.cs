using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Utilities;

namespace SUSModder.Views
{
    public partial class LobbySetDialog : Window
    {
        public bool DialogResult { get; private set; } = false;
        public int PlayerCount { get; private set; } = 10;

        public LobbySetDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            var value = PlayerCountNumeric?.Value ?? 10;
            PlayerCount = (int)value;

            // Walidacja
            if (PlayerCount < 4 || PlayerCount > 255)
            {
                ErrorMessage.Text = "Liczba graczy musi być w zakresie 4-255.";
                ErrorMessage.IsVisible = true;
                return;
            }

            // Wywołaj logikę ustawiania lobby
            if (LobbyUtils.SetLobbyPlayers(PlayerCount, out string errorMessage))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ErrorMessage.Text = errorMessage;
                ErrorMessage.IsVisible = true;
            }
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
