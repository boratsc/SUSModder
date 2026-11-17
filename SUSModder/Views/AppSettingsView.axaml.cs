using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class AppSettingsView : UserControl
    {
        private AppSettingsViewModel? _viewModel;

        public AppSettingsView()
        {
            InitializeComponent();
            _viewModel = new AppSettingsViewModel(this);
            DataContext = _viewModel;

            // Subskrybuj event zapisania ustawień
            _viewModel.SettingsSaved += OnSettingsSaved;
            _viewModel.UpdateChannelChanged += OnUpdateChannelChanged;
        }

        private void OnSettingsSaved()
        {
            // Powiadom MainWindowViewModel o zapisaniu ustawień
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel mainVM)
            {
                // Wywołaj publiczną metodę odświeżania
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await mainVM.RefreshAfterSettingsChangeAsync();
                });
            }

            // Zsynchronizuj widoczność ikony tray z nowym ustawieniem MinimizeToTray
            mainWindow?.ApplyTrayVisibility();

            // Zakotwiczenie czatu: zatwierdzona wartość trafia wprost do okna (bez ponownego odczytu serwisu).
            if (_viewModel != null)
                mainWindow?.ApplyChatAnchoredPreference(_viewModel.ChatAnchored);
        }
        
        private void OnUpdateChannelChanged(string newChannel)
        {
            // Powiadom MainWindowViewModel o zmianie kanału
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel mainVM)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainVM.HandleUpdateChannelChange(newChannel);
                });
            }
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

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            // Znajdź MainWindow i zamknij panel; fokus wraca do przycisku Ustawienia w sidebarze.
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel vm)
            {
                vm.IsAppSettingsVisible = false;
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => mainWindow.FindControl<Button>("SettingsSidebarButton")?.Focus());
            }
        }

        private void OpenSupport_Click(object? sender, RoutedEventArgs e)
            => Services.ProjectSupport.Open();

        private void CheckForUpdatesButton_Click(object? sender, RoutedEventArgs e)
        {
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel vm)
            {
                ((System.Windows.Input.ICommand)vm.CheckForAppUpdatesCommand).Execute(null);
            }
        }
    }
}
