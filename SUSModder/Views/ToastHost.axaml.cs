using Avalonia;
using Avalonia.Controls;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    /// <summary>
    /// Kontener wyświetlający aktywne powiadomienia toast.
    /// Automatycznie pobiera ToastService z DI i ustawia jako DataContext.
    /// </summary>
    public partial class ToastHost : UserControl
    {
        public ToastHost()
        {
            InitializeComponent();

            // Pobierz singleton ToastService i ustaw jako DataContext
            // (to pozwala na binding ActiveToasts bez konieczności ustawiania z zewnątrz)
            if (!Design.IsDesignMode)
            {
                var toastService = App.GetService<ToastService>();
                DataContext = toastService;
            }
        }
    }
}
