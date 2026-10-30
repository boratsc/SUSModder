using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SUSModder.ViewModels;

namespace SUSModder.Views;

/// <summary>
/// Stały panel globalnego komunikatora w prawej kolumnie głównego okna.
/// Widok tylko prezentuje stan i przekazuje intencje użytkownika do VM:
/// - śledzenie pozycji przewijania (auto-scroll vs. przycisk „nowe wiadomości"),
///   z odłożeniem przewinięcia do priorytetu Loaded (po layoucie nowych pozycji),
/// - Enter = wyślij (guard podwójnego wysłania leży po stronie VM),
/// - zaznaczanie/kopiowanie tekstu przez SelectableTextBlock (Avalonia 12).
/// </summary>
public partial class CommunityChatPanel : UserControl
{
    private CommunityChatPanelViewModel? _boundViewModel;
    private ScrollViewer? _messagesScroll;

    /// <summary>
    /// Intencja „trzymaj dół”: ustawiana przy żądaniu przewinięcia, kasowana po
    /// zastosowaniu lub świadomej rezygnacji. Gdy aktywna, wzrost Extent/Viewport
    /// (dopisanie wiadomości przed layoutem) NIE odwołuje śledzenia.
    /// </summary>
    private bool _followBottomPending;

    public CommunityChatPanel()
    {
        InitializeComponent();
        DataContextChanged += OnPanelDataContextChanged;
        Loaded += OnPanelLoaded;
    }

    private void OnPanelLoaded(object? sender, RoutedEventArgs e)
    {
        // Dociągnij stan przewijania po pierwszym layoucie.
        UpdateUserAtBottom();
        if (_boundViewModel is { Messages.Count: > 0 } vm && vm.UserAtBottom)
            OnScrollToBottomRequested();
    }

    private void OnPanelDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        _boundViewModel = DataContext as CommunityChatPanelViewModel;
        AttachViewModel();
    }

    private void AttachViewModel()
    {
        if (_boundViewModel is null)
            return;

        _boundViewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is null)
            return;

        _boundViewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _boundViewModel = null;
    }

    private void OnScrollToBottomRequested()
    {
        _followBottomPending = true;

        // Przewinięcie musi czekać na layout dopisanych pozycji — wykonane natychmiast
        // liczyłoby na starym Extent i nie docierało do prawdziwego końca.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.InvokeAsync(ApplyFollowBottomIfPending, DispatcherPriority.Loaded);
            return;
        }

        Dispatcher.UIThread.Post(() =>
            Dispatcher.UIThread.InvokeAsync(ApplyFollowBottomIfPending, DispatcherPriority.Loaded));
    }

    private void ApplyFollowBottomIfPending()
    {
        if (!_followBottomPending)
            return;

        _followBottomPending = false;

        var scroll = EnsureScroll();
        if (scroll is null)
            return;

        // W międzyczasie użytkownik świadomie odszedł od końca — nie walczmy o pasek.
        if (_boundViewModel is { UserAtBottom: false })
            return;

        scroll.ScrollToEnd();
        UpdateUserAtBottom();
    }

    private ScrollViewer? EnsureScroll()
    {
        if (_messagesScroll is not null)
            return _messagesScroll;

        _messagesScroll = this.FindControl<ScrollViewer>("MessagesScroll");
        if (_messagesScroll is not null)
            _messagesScroll.PropertyChanged += OnScrollPropertyChanged;

        return _messagesScroll;
    }

    private void OnScrollPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty)
        {
            // Zmiana offsetu to realne przewijanie (koło/użytkownik lub ScrollToEnd).
            UpdateUserAtBottom();
            return;
        }

        if (e.Property == ScrollViewer.ExtentProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            // Sam wzrost zakresu (nowe pozycje przed odłożonym przewinięciem)
            // nie może zdjąć śledzenia dołu.
            if (_followBottomPending)
                return;

            UpdateUserAtBottom();
        }
    }

    private void UpdateUserAtBottom()
    {
        var scroll = EnsureScroll();
        if (scroll is null || _boundViewModel is null)
            return;

        var extent = scroll.Extent.Height;
        var viewport = scroll.Viewport.Height;
        var offset = scroll.Offset.Y;

        var atBottom = extent <= viewport || offset >= extent - viewport - 2.0;
        _boundViewModel.NotifyUserAtBottom(atBottom);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
            return;

        // Enter wysyła; zjadamy klawisz, by nie odpalać nawigacji fokusem.
        // Strzeżnik podwójnego wysłania leży po stronie VM (CanExecute + flaga in-flight).
        e.Handled = true;
        _boundViewModel?.SendCommand.Execute().Subscribe();
    }

    private void OnNewMessagesClick(object? sender, RoutedEventArgs e)
    {
        // VM kasuje licznik i zgłasza żądanie przewinięcia — ścieżka odłożona (Loaded)
        // odczeka na layout i dopiero wtedy przewinie do prawdziwego końca.
        _boundViewModel?.OnNewMessagesButton();
    }
}
