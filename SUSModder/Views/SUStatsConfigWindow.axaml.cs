using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class SUStatsConfigWindow : Window
    {
        public SUStatsConfigWindow()
        {
            InitializeComponent();
            DataContext = new SUStatsConfigViewModel();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
