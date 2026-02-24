using Avalonia.Controls;
using SUSModder.ViewModels;

namespace SUSModder.Views;

public partial class RecommendedDiscordsPanel : UserControl
{
    public RecommendedDiscordsPanel()
    {
        InitializeComponent();
        DataContext = new RecommendedDiscordsViewModel();
    }
}
