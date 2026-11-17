using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using SUSModder.ViewModels;

namespace SUSModder.Views;

public partial class RecommendedDiscordsPanel : UserControl
{
    private readonly RecommendedDiscordsViewModel _viewModel = new();
    private readonly CompositeDisposable _hostSubscriptions = new();
    private MainWindowViewModel? _mainVM;

    public RecommendedDiscordsPanel()
    {
        InitializeComponent();
        // F3d: VM na wewnętrznej siatce — sam UserControl dziedziczy DataContext okna,
        // dzięki czemu IsVisible="{Binding IsDiscordsTab}" w MainWindow.axaml działa.
        Root.DataContext = _viewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // F3b: dostęp do MainWindowViewModel (wzorzec AppSettingsViewModel.TryGetMainWindowViewModel)
        // — akcje zestawu reuse istniejące flow instalacji/aktualizacji.
        if (_viewModel.Host == null &&
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                is MainWindow { DataContext: MainWindowViewModel mainVM })
        {
            _viewModel.Host = mainVM;
            _mainVM = mainVM;
            // F3d: panel jest stale w drzewie (zakładka jak Role); otwarcie = IsDiscordsTab.
            // F3g: otwarcie odpala cichy check paczek (onlyNew) PRZED odświeżeniem wierszy.
            mainVM.WhenAnyValue(x => x.IsDiscordsTab)
                .Subscribe(visible =>
                {
                    if (visible)
                        _ = _viewModel.RefreshPackStateOnTabOpenedAsync();
                })
                .DisposeWith(_hostSubscriptions);
            // F3g: live badge — check timera/startup odświeża wiersze otwartej zakładki.
            mainVM.PackUpdateStateRefreshed += OnPackUpdateStateRefreshed;
        }

        _viewModel.RefreshPackInstanceDetection();
    }

    private void OnPackUpdateStateRefreshed()
    {
        if (_mainVM?.IsDiscordsTab == true)
            _viewModel.RefreshPackInstanceDetection();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_mainVM != null)
        {
            _mainVM.PackUpdateStateRefreshed -= OnPackUpdateStateRefreshed;
            _mainVM = null;
        }
        _hostSubscriptions.Clear();
        _viewModel.Host = null;
        base.OnDetachedFromVisualTree(e);
    }
}
