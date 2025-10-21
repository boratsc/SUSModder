using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SUSModder.ViewModels;

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
    }
}
