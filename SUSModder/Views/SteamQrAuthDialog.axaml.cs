using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SUSModder.Core.Models;
using SUSModder.ViewModels;

namespace SUSModder.Views;

public partial class SteamQrAuthDialog : Window
{
    public SteamQrAuthDialog()
    {
        InitializeComponent();
    }

    public SteamQrAuthDialog(SteamQrDownloadContext context) : this()
    {
        DataContext = new SteamQrAuthDialogViewModel(this, context);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
