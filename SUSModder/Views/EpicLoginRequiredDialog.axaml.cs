using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SUSModder.Views
{
    /// <summary>
    /// Dialog wyświetlany gdy weryfikacja logowania do Epic Games nie powiodła się.
    /// Daje użytkownikowi trzy opcje: zaloguj się, zmień na Steam, lub zamknij aplikację.
    /// </summary>
    public partial class EpicLoginRequiredDialog : Window
    {
        public EpicLoginRequiredDialog()
        {
            InitializeComponent();

            var loginButton = this.FindControl<Button>("LoginButton");
            var switchToSteamButton = this.FindControl<Button>("SwitchToSteamButton");
            var closeButton = this.FindControl<Button>("CloseButton");

            if (loginButton != null)
                loginButton.Click += (_, _) => Close("login");

            if (switchToSteamButton != null)
                switchToSteamButton.Click += (_, _) => Close("steam");

            if (closeButton != null)
                closeButton.Click += (_, _) => Close("close");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
