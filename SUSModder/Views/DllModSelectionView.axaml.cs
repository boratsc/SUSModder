using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree; // Dla FindAncestorOfType
using SUSModder.ViewModels;
using System.Threading.Tasks;
using System;

namespace SUSModder.Views
{
    public partial class DllModSelectionView : UserControl
    {
        public DllModSelectionView()
        {
            InitializeComponent();
            
            // Podpięcie się pod event DataContextChanged - poprawna sygnatura
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Odłącz event od starego ViewModel (jeśli istnieje)
            if (DataContext is DllModSelectionViewModel viewModel)
            {
                viewModel.CloseRequested += ViewModel_CloseRequested;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Odłącz event przy zamykaniu widoku
            if (DataContext is DllModSelectionViewModel viewModel)
            {
                viewModel.CloseRequested -= ViewModel_CloseRequested;
            }
            
            base.OnDetachedFromVisualTree(e);
        }

        private void ViewModel_CloseRequested(object? sender, System.EventArgs e)
        {
            // Znajdź okno-rodzica i zamknij je
            var window = this.FindAncestorOfType<Window>();
            window?.Close();
        }
    }
}