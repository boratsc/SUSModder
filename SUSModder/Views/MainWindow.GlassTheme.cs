using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using SUSModder.Services;
using SUSModder.ViewModels;
using System.Reactive.Disposables;

namespace SUSModder.Views;

public partial class MainWindow
{
    private readonly GlassThemeFallbackService _glassFallbackService = new();
    private bool _refreshingGlassChrome;
    private bool _glassTransparencyRefreshScheduled;

    private void InitializeGlassThemeHooks()
    {
        Opened += (_, _) => RefreshGlassWindowChrome();

        Activated += (_, _) => RefreshGlassWindowChrome();
    }

    private void SubscribeGlassThemeChanges(MainWindowViewModel vm, CompositeDisposable disposables)
    {
        vm.WhenAnyValue(x => x.IsGlassTheme, x => x.GlassReduceTransparency)
          .Subscribe(_ => RefreshGlassWindowChrome())
          .DisposeWith(disposables);
    }

    private void RefreshGlassWindowChrome()
    {
        if (_refreshingGlassChrome)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        _refreshingGlassChrome = true;
        try
        {
            RefreshGlassWindowChromeCore(vm);
        }
        finally
        {
            _refreshingGlassChrome = false;
        }
    }

    private void RefreshGlassWindowChromeCore(MainWindowViewModel vm)
    {
        if (!vm.IsGlassTheme)
        {
            Classes.Remove("glass-theme");
            Classes.Remove("glass-opaque");
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            Background = this.FindResource("WindowBackgroundBrush") as IBrush ?? Brushes.Transparent;
            return;
        }

        Classes.Add("glass-theme");

        var useOpaque = _glassFallbackService.ShouldUseOpaqueFallback(
            vm.GlassReduceTransparency,
            ActualTransparencyLevel);

        if (useOpaque)
            Classes.Add("glass-opaque");
        else
            Classes.Remove("glass-opaque");

        if (useOpaque)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            Background = this.FindResource("GlassBlurFallbackBrush") as IBrush
                         ?? this.FindResource("WindowBackgroundGradientBrush") as IBrush
                         ?? Brushes.Transparent;
        }
        else
        {
            TransparencyLevelHint =
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            ];
            Background = Brushes.Transparent;
        }

        var reason = _glassFallbackService.GetFallbackReason(vm.GlassReduceTransparency, ActualTransparencyLevel);
        if (reason != GlassThemeFallbackService.FallbackReason.None)
            System.Diagnostics.Debug.WriteLine($"[GlassTheme] Opaque fallback: {reason}");
    }

    private void OnGlassTransparencyLevelChanged()
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsGlassTheme)
            return;

        if (_glassTransparencyRefreshScheduled)
            return;

        _glassTransparencyRefreshScheduled = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _glassTransparencyRefreshScheduled = false;
            RefreshGlassWindowChrome();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
}
