using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.Core.Models;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class VersionSelectionPanel : UserControl
    {
        public VersionSelectionPanel()
        {
            InitializeComponent();
        }

        private void Version_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control control && control.DataContext is ModVersionHistory version)
            {
                if (DataContext is VersionSelectionViewModel viewModel)
                {
                    viewModel.SelectedVersion = version;
                }
            }
        }
    }
}
