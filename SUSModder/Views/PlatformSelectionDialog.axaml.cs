using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class PlatformSelectionDialog : Window
    {
        public PlatformSelectionDialog()
        {
            InitializeComponent();
            DataContext = new PlatformSelectionViewModel(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
