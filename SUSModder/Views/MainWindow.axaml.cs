using Avalonia.ReactiveUI;
using SUSModder.ViewModels;

namespace SUSModder.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }
}