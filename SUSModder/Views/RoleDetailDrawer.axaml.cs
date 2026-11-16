using Avalonia.Controls;

namespace SUSModder.Views;

/// <summary>
/// E14: szczegóły roli (read-only) hostowane w prawej kolumnie MainWindow jako RoleDetailDrawerHost.
/// Cała logika i stan w MainWindowViewModel.Roles; widok tylko wiąże dane.
/// </summary>
public partial class RoleDetailDrawer : UserControl
{
    public RoleDetailDrawer()
    {
        InitializeComponent();
    }
}