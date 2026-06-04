using Avalonia;
using Avalonia.Controls;

namespace SUSModder.Controls;

public partial class PackInstancesHintPanel : UserControl
{
    public static readonly StyledProperty<bool> ShowEmptyStateMessageProperty =
        AvaloniaProperty.Register<PackInstancesHintPanel, bool>(nameof(ShowEmptyStateMessage), true);

    public bool ShowEmptyStateMessage
    {
        get => GetValue(ShowEmptyStateMessageProperty);
        set => SetValue(ShowEmptyStateMessageProperty, value);
    }

    public PackInstancesHintPanel()
    {
        InitializeComponent();
    }
}
