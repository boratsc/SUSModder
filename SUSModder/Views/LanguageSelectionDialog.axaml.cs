using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class LanguageSelectionDialog : Window
    {
        public LanguageSelectionDialog()
        {
            InitializeComponent();
            DataContext = new LanguageSelectionViewModel(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
