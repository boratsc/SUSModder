using System;
using Avalonia.Controls;
using Avalonia.Input;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    /// <summary>
    /// Okno podsumowania miejsca (3.1-08). Prezentuje wyłącznie pomiar i klasyfikację:
    /// w tym wydaniu nie ma tu i nie będzie „przycisku czyszczenia” — usuwanie dojdzie
    /// dopiero wraz z certyfikowanym API Core (allowlista + dowód braku aktywnej używania).
    /// </summary>
    public partial class StorageOverviewDialog : Window
    {
        private StorageOverviewViewModel? _viewModel;

        /// <summary>Konstruktor projektanta / fallback — bez danych.</summary>
        public StorageOverviewDialog()
        {
            InitializeComponent();
        }

        public StorageOverviewDialog(StorageOverviewViewModel viewModel)
            : this()
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            _viewModel.CloseRequested += OnCloseRequested;
            Closed += (_, _) =>
            {
                if (_viewModel is not null)
                {
                    _viewModel.CloseRequested -= OnCloseRequested;
                    _viewModel.CancelMeasurement();
                }
            };
        }

        private void OnCloseRequested(object? sender, EventArgs e)
        {
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // ESC: przerwany pomiar albo zamknięcie (ścieżka dostępności klawiaturowej).
            if (e.Key == Key.Escape)
            {
                if (_viewModel?.IsMeasuring == true)
                    _viewModel.CancelMeasurement();
                else
                    Close();

                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Focus na liście kategorii, jeśli jest — dalsza nawigacja Tabem.
            Focus();
        }
    }
}
