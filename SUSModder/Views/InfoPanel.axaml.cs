using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class InfoPanel : UserControl
    {
        public InfoPanel()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.IsInfoPanelVisible = false;
            }
        }
    }
}