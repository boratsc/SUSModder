using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class RecommendedDiscordsWindow : Window
    {
        public RecommendedDiscordsWindow()
        {
            InitializeComponent();
            DataContext = new RecommendedDiscordsViewModel();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
