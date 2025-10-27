using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SUSModder.Core.Models;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class VersionSelectionDialog : Window
    {
        public VersionSelectionDialog()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is VersionSelectionViewModel viewModel)
            {
                viewModel.VersionSelected += (sender, version) =>
                {
                    Close(version); // Zwróć wybraną wersję
                };

                viewModel.Cancelled += (sender, args) =>
                {
                    Close(null); // Zwróć null jeśli anulowano
                };
            }
        }

        private void Version_Tapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border border && border.DataContext is ModVersionHistory version)
            {
                if (DataContext is VersionSelectionViewModel viewModel)
                {
                    viewModel.SelectedVersion = version;
                }
            }
        }
    }
}
