using Avalonia;
using Avalonia.Controls;
using SUSModder.ViewModels;
using System;

namespace SUSModder.Views
{
    public partial class DllModSelectionView : UserControl
    {
        private DllModSelectionViewModel? _subscribedViewModel;

        public DllModSelectionView()
        {
            InitializeComponent();
            
            // Podpięcie się pod event DataContextChanged - poprawna sygnatura
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.CloseRequested -= ViewModel_CloseRequested;
            }

            if (DataContext is DllModSelectionViewModel viewModel)
            {
                viewModel.CloseRequested += ViewModel_CloseRequested;
                _subscribedViewModel = viewModel;
            }
            else
            {
                _subscribedViewModel = null;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Odłącz event przy zamykaniu widoku
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.CloseRequested -= ViewModel_CloseRequested;
                _subscribedViewModel = null;
            }
            
            base.OnDetachedFromVisualTree(e);
        }

        private void ViewModel_CloseRequested(object? sender, System.EventArgs e)
        {
            // Zamknięcie jest obsługiwane przez właściciela widoku (MainWindowViewModel).
        }
    }
}
