using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SUSModder.ViewModels;
using System.Diagnostics;

namespace SUSModder.Views
{
    public partial class SUStatsConfigView : UserControl
    {
        public SUStatsConfigView()
        {
            InitializeComponent();
            DataContext = new SUStatsConfigViewModel();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            // Znajdź MainWindow i wywołaj command
            var mainWindow = this.FindLogicalAncestorOfType<MainWindow>();
            if (mainWindow?.DataContext is MainWindowViewModel vm)
            {
                // Bezpośrednie ustawienie właściwości
                vm.IsSUStatsConfigVisible = false;
            }
        }

        private void OpenClairbotLink_Click(object? sender, RoutedEventArgs e)
            => OpenUrl("https://clairbot.app");

        private void OpenClairHubLink_Click(object? sender, RoutedEventArgs e)
            => OpenUrl("https://hub.clairbot.app/among-us");

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
