using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using SUSModder.ViewModels;

namespace SUSModder.Behaviors;

/// <summary>Captures actual list scroll owners; restores after layout, without persisting UI state.</summary>
public sealed class BrowserSessionScrollBehavior : Behavior<Window>
{
    private MainWindowViewModel? _viewModel;
    private int _restoreVersion;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { } window)
        {
            window.DataContextChanged += OnDataContextChanged;
            window.Closed += OnClosed;
            Bind();
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is { } window)
        {
            window.DataContextChanged -= OnDataContextChanged;
            window.Closed -= OnClosed;
        }
        Unbind();
        base.OnDetaching();
    }

    private void OnClosed(object? sender, EventArgs e) => Unbind();
    private void OnDataContextChanged(object? sender, EventArgs e) => Bind();

    private void Bind()
    {
        Unbind();
        _viewModel = AssociatedObject?.DataContext as MainWindowViewModel;
        if (_viewModel == null) return;
        _viewModel.BrowserScrollCaptureRequested += Capture;
        _viewModel.BrowserScrollRestoreRequested += Restore;
    }

    private void Unbind()
    {
        _restoreVersion++;
        if (_viewModel == null) return;
        _viewModel.BrowserScrollCaptureRequested -= Capture;
        _viewModel.BrowserScrollRestoreRequested -= Restore;
        _viewModel = null;
    }

    private ScrollViewer? FindScroll(MainWindowViewModel.ModBrowserTab tab)
    {
        var name = tab switch
        {
            MainWindowViewModel.ModBrowserTab.Catalog => "ModsListBox",
            MainWindowViewModel.ModBrowserTab.DllAddons => "DllModsListBox",
            _ => "PackInstancesListBox"
        };
        var list = AssociatedObject?.FindControl<ListBox>(name);
        if (list == null) return null;
        // Pack instances intentionally have an external scroll owner (hint below the list).
        return tab == MainWindowViewModel.ModBrowserTab.MyPacks
            ? list.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
            : list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private void Capture(MainWindowViewModel.ModBrowserTab tab)
    {
        _restoreVersion++;
        if (FindScroll(tab) is { } scroll)
            _viewModel?.CaptureBrowserScroll(tab, scroll.Offset.Y);
    }

    private void Restore(MainWindowViewModel.ModBrowserTab tab, double offset)
    {
        var version = ++_restoreVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _restoreVersion || _viewModel?.ActiveBrowserTab != tab) return;
            if (FindScroll(tab) is not { } scroll) return;
            var maximum = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maximum));
        }, DispatcherPriority.Loaded);
    }
}
