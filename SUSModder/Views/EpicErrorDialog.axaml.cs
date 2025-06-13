using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class EpicErrorDialog : Window
    {
        public EpicErrorDialog()
        {
            InitializeComponent();
        }

        public EpicErrorDialog(string modName, string logContent) : this()
        {
            DataContext = new EpicErrorDialogViewModel(modName, logContent, this);
        }
    }
}