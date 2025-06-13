using Avalonia.Controls;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class AppSettingsWindow : Window
    {
        public AppSettingsWindow()
        {
            InitializeComponent();
            DataContext = new AppSettingsViewModel(this);
        }
    }
}