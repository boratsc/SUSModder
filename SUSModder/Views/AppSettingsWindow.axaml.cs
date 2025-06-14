using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;
using SUSModder.Views;

namespace SUSModder.Views
{
    public partial class AppSettingsWindow : Window
    {
        public AppSettingsWindow()
        {
            InitializeComponent();
            DataContext = new AppSettingsViewModel(this);
        }
        private void OnShowConsoleToggled(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                if (toggle.IsChecked == true)
                {
                    ConsoleWindow.ShowConsole();
                }
                else
                {
                    ConsoleWindow.Instance?.Close();
                }
            }
        }
    }
}

