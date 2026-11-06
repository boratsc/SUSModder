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
        // CurrentTheme zmienia się przy każdym przełączeniu motywu (Dark/Light/Pink/Glass),
        // więc klasa okna i szkliste tło odświeżają się spójnie.
        vm.WhenAnyValue(x => x.CurrentTheme, x => x.GlassReduceTransparency)
          // CurrentTheme notifies before ApplyTheme swaps the resource dictionary.
          // Resolve chrome brushes after that synchronous swap has completed.
          .Subscribe(_ => Avalonia.Threading.Dispatcher.UIThread.Post(
              RefreshGlassWindowChrome,
              Avalonia.Threading.DispatcherPriority.Background))
          .DisposeWith(disposables);
    }

    /// <summary>
    /// Nadaje oknu dokładnie jedną klasę motywu: glass/dark/light/pink.
    /// Style powierzchni (GlassSurfaceStyles, DarkSurfaceStyles) selekcjonują po tej klasie.
    /// </summary>
    private void ApplyThemeWindowClass(MainWindowViewModel vm)
    {
        Classes.Remove("glass-theme");
        Classes.Remove("glass-opaque");
        Classes.Remove("dark-theme");
        Classes.Remove("light-theme");
        Classes.Remove("pink-theme");

        switch (vm.CurrentTheme)
        {
            case MainWindowViewModel.ThemeType.Glass:
                Classes.Add("glass-theme");
                break;
            case MainWindowViewModel.ThemeType.Light:
                Classes.Add("light-theme");
                break;
            case MainWindowViewModel.ThemeType.Pink:
                Classes.Add("pink-theme");
                break;
            default:
                Classes.Add("dark-theme");
                break;
        }
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
        ApplyThemeWindowClass(vm);

        if (!vm.IsGlassTheme)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            Background = this.FindResource("WindowBackgroundBrush") as IBrush ?? Brushes.Transparent;
            return;
        }

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
