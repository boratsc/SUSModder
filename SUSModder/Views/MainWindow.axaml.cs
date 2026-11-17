using Avalonia;
using Avalonia.Controls;
using ReactiveUI.Avalonia.Reactive;
using ReactiveUI;
using SUSModder.ViewModels;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Reactive;
using System.Linq;
using Avalonia.Media;
using SUSModder.Core.Services;
using SUSModder.Services;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using FluentIcons.Common;
using FluentIcons.Avalonia;

namespace SUSModder.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    // Dodaj komendy jako w�a�ciwo�ci
    public ReactiveCommand<Unit, Unit> RemoveSingleInstanceCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LaunchMultipleInstancesCommand { get; private set; } = null!;

    // System tray
    private SystemTrayService? _systemTrayService;
    private bool _forceClose;

    public MainWindow()
    {
        // Konstruktor wymagany przez designer oraz XAML loader.
        // W runtime preferowany jest konstruktor z ViewModel.
        if (Design.IsDesignMode)
            DataContext = new MainWindowViewModel();

        InitializeWindow();
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeWindow();
    }

    /// <summary>
    /// Inicjalizuje SystemTrayService. Wywoływana z App.axaml.cs po pokazaniu MainWindow.
    /// </summary>
    public void InitializeSystemTray()
    {
        if (_systemTrayService != null)
            return;

        _systemTrayService = new SystemTrayService();
        _systemTrayService.Initialize(this);
        _systemTrayService.RestoreRequested += OnTrayRestoreRequested;

        // Subskrybuj zmiany modów w ViewModel (aktualizacja menu tray)
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.InstalledModsCount))
                {
                    UpdateTrayModsList();
                }
            };
            vm.CommunityChat.PropertyChanged += OnCommunityChatPropertyChanged;
            _systemTrayService.SetUnreadCount(vm.CommunityChat.NewMessageCount);
        }

        ApplyTrayVisibility();
    }

    private void OnCommunityChatPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommunityChatPanelViewModel.NewMessageCount)) return;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            _systemTrayService?.SetUnreadCount(((CommunityChatPanelViewModel)sender!).NewMessageCount);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _systemTrayService?.SetUnreadCount(((CommunityChatPanelViewModel)sender!).NewMessageCount));
    }

    /// <summary>
    /// Aktualizuje widoczność ikony tray na podstawie bieżącego ustawienia MinimizeToTray.
    /// Gdy opcja jest włączona, ikona jest zawsze widoczna; gdy wyłączona — ukryta.
    /// </summary>
    public void ApplyTrayVisibility()
    {
        if (_systemTrayService == null)
            return;

        var settings = new UserSettingsService().LoadUserSettings();
        if (settings.MinimizeToTray)
        {
            _systemTrayService.Show();
        }
        else
        {
            _systemTrayService.Hide();
            // Jeśli okno było ukryte w tray, a użytkownik wyłączył tę opcję,
            // przywróć okno, aby nie pozostawić aplikacji niedostępnej.
            if (!IsVisible)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();
            }
        }
    }

    /// <summary>
    /// Odświeża listę modów w menu tray na podstawie bieżących danych z ViewModel.
    /// </summary>
    internal void UpdateTrayModsList()
    {
        if (_systemTrayService == null || DataContext is not MainWindowViewModel vm)
            return;

        _systemTrayService.UpdateRecentMods(vm.GetTrayQuickLaunchMods());
    }

    private void OnTrayRestoreRequested()
    {
        // Gdy użytkownik kliknie "Przywróć" w menu tray,
        // SystemTrayService.RestoreWindow() już obsługuje przywrócenie okna.
        // To zdarzenie jest rejestrowane dla ewentualnych dodatkowych działań.
    }

    /// <summary>
    /// Przywraca okno z tray, taskbar lub tła i ustawia je na pierwszym planie.
    /// Wywoływana gdy druga instancja aplikacji zażąda aktywacji.
    /// </summary>
    public void RestoreAndActivate()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_systemTrayService?.IsVisible == true)
            {
                _systemTrayService.RestoreWindow();
                return;
            }

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Show();
            Activate();
            Focus();
        });
    }

    private void InitializeWindow()
    {
        InitializeComponent();

        var modsList = this.FindControl<ListBox>("ModsListBox");
        if (modsList != null)
            modsList.SelectionChanged += ModsListBox_SelectionChanged;

        var packsList = this.FindControl<ListBox>("PackInstancesListBox");
        if (packsList != null)
            packsList.SelectionChanged += PackInstancesListBox_SelectionChanged;

        var dllList = this.FindControl<ListBox>("DllModsListBox");
        if (dllList != null)
            dllList.SelectionChanged += DllModsListBox_SelectionChanged;

        // Responsywność: < 1100 px prawa kolumna staje się overlayem nad listą.
        SizeChanged += (_, _) => ApplyRightColumnLayout();

        // Inicjalizuj komendy
        RemoveSingleInstanceCommand = ReactiveCommand.CreateFromTask(RemoveSingleInstanceAsync);
        LaunchMultipleInstancesCommand = ReactiveCommand.CreateFromTask(LaunchMultipleInstancesAsync);

        // Nasłuchuj zmiany wybranego moda i aktualizuj opis
        this.WhenActivated(disposables =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.WhenAnyValue(x => x.SelectedMod)
                  .Subscribe(mod =>
                  {
                      if (mod != null)
                          SetDescriptionWithLinks(mod.Description ?? "");
                      else
                          SetDescriptionWithLinks("");
                  })
                  .DisposeWith(disposables);

                vm.WhenAnyValue(x => x.IsBulkSelectionMode)
                  .Subscribe(active =>
                  {
                      if (active)
                      {
                          _bulkListSelectionAnchor = vm.SelectedMod;
                          _bulkPackSelectionAnchor = vm.SelectedPackInstance;
                      }
                  })
                  .DisposeWith(disposables);

                // E5: stan początkowy fokusu okna (IsActive mógł już zajść przed podpięciem VM).
                vm.CommunityChat.WindowFocused = IsActive;

                // E5: prawa kolumna = tylko szczegóły (czat jest nakładką pod dymkiem).
                vm.WhenAnyValue(x => x.IsBrowserDetailPanelVisible)
                  .Subscribe(_ => ApplyRightColumnLayout())
                  .DisposeWith(disposables);

                // E5: po zamknięciu czatu fokus wraca do dymka na belce.
                vm.WhenAnyValue(x => x.IsChatOpen)
                  .Subscribe(open =>
                  {
                      if (!open)
                          Avalonia.Threading.Dispatcher.UIThread.Post(
                              () => this.FindControl<Button>("ChatToggleButton")?.Focus());
                  })
                  .DisposeWith(disposables);

                // Ustawienia: po otwarciu fokus wchodzi do panelu (Tab przechodzi do pierwszej kontrolki).
                // Powrót fokusu po zamknięciu obsługują Escape i przycisk „Zamknij" — nie tutaj,
                // bo zamknięcie bywa skutkiem kliknięcia innej zakładki (fokus zostaje w niej).
                vm.WhenAnyValue(x => x.IsAppSettingsVisible)
                  .Skip(1)
                  .Subscribe(open =>
                  {
                      if (open)
                          Avalonia.Threading.Dispatcher.UIThread.Post(
                              () => this.FindControl<AppSettingsView>("AppSettingsViewControl")?.Focus());
                  })
                  .DisposeWith(disposables);
            }
        });

        ApplyRightColumnLayout();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty || change.Property == WindowStateProperty)
            UpdateCommunityChatLifecycle();

        // E5: fokus okna dla polityki toastów czatu (brak toastów gdy czat otwarty I okno ma focus).
        if (change.Property == IsActiveProperty && DataContext is MainWindowViewModel vm)
            vm.CommunityChat.WindowFocused = change.NewValue is true;

        if (change.Property == WindowStateProperty)
        {
            var icon = this.FindControl<SymbolIcon>("MaximizeIcon");
            if (icon != null)
                icon.Symbol = WindowState == WindowState.Maximized ? Symbol.SquareMultiple : Symbol.Maximize;

            // Po maximize/restore ponownie oceń dokowanie czatu (szerokość mogła się zmienić).
            ApplyRightColumnLayout();
        }
    }

    // ── Czat ogólny: lifecycle pollingu (pauza tylko dla okna, nie dla zamkniętego czatu) ──

    /// <summary>
    /// Pauzuje polling czatu, gdy okno jest ukryte (tray) lub zminimalizowane;
    /// po powrocie wznawia z kontrolowanym odświeżeniem. Brak agresywnego pollingu w tle.
    /// </summary>
    private void UpdateCommunityChatLifecycle()
    {
        var vm = ViewModel ?? DataContext as MainWindowViewModel;
        if (vm?.CommunityChat is null)
            return;

        var hidden = !IsVisible || WindowState == WindowState.Minimized;
        if (hidden)
            vm.CommunityChat.Pause();
        else
            vm.CommunityChat.Resume();
    }

    /// <summary>
    /// E5: układ regionów — lista 440 + szczegóły na reszcie (pełna wysokość);
    /// bez zaznaczenia lista pełnej szerokości. Wąska część aplikacji (&lt; 1100 DIP):
    /// szczegóły jako overlay nad listą z przyciskiem powrotu.
    /// Czat: dokowany = stała kolumna 520; inaczej nakładka pod dymkiem,
    /// rozmiar ograniczony dostępną przestrzenią (min(520, szer) × min(690, wys)).
    /// </summary>
    private void ApplyRightColumnLayout()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var content = this.FindControl<Grid>("ContentGrid");
        var host = this.FindControl<Border>("RightColumnHost");
        if (content == null || host == null)
            return;

        // Dokowanie efektywne oceniamy z rzeczywistej szerokości okna (także po resize/maximize).
        var docked = Bounds.Width > 0 && ChatDockingLayout.IsDocked(vm.ChatAnchored, Bounds.Width);
        vm.IsChatDocked = docked;

        var details = vm.IsBrowserDetailPanelVisible;
        // Wąskość liczona od części aplikacji bez kolumny czatu (dokowanie + side-by-side >= 1620).
        var appWidth = ChatDockingLayout.AppPartWidth(docked, Bounds.Width);
        var narrow = appWidth > 0 && ChatDockingLayout.IsDetailsNarrow(docked, Bounds.Width);
        var sideBySide = details && !narrow;

        content.ColumnDefinitions[1].Width = sideBySide
            ? new GridLength(440)
            : new GridLength(1, GridUnitType.Star);
        content.ColumnDefinitions[2].Width = sideBySide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        content.ColumnDefinitions[3].Width = docked
            ? new GridLength(ChatDockingLayout.ChatColumnWidth)
            : new GridLength(0);
        host.SetValue(Grid.ColumnProperty, narrow ? 1 : 2);
        host.SetValue(Grid.ColumnSpanProperty, narrow ? 2 : 1);
        host.ZIndex = narrow ? 50 : 0;
        host.IsVisible = details;

        var back = this.FindControl<Border>("DetailBackRow");
        if (back != null)
            back.IsVisible = narrow && details;

        // Środkowy przycisk wsparcia: przy wąskim oknie chowamy, by nie nachodził na breadcrumb-y.
        var support = this.FindControl<Button>("SupportButton");
        if (support != null)
            support.IsVisible = !narrow;

        var chat = this.FindControl<Border>("ChatOverlayHost");
        if (chat == null)
            return;

        if (docked)
        {
            // Kolumna: pełna wysokość, bez cienia/zaokrągleń/marginesu; pod szczegółami-overlay nie jest.
            chat.SetValue(Grid.ColumnProperty, 3);
            chat.SetValue(Grid.ColumnSpanProperty, 1);
            chat.HorizontalAlignment = HorizontalAlignment.Stretch;
            chat.VerticalAlignment = VerticalAlignment.Stretch;
            chat.Margin = new Thickness(0);
            chat.Width = double.NaN;
            chat.MaxHeight = double.PositiveInfinity;
            chat.CornerRadius = new CornerRadius(0);
            chat.BoxShadow = default;
            chat.BorderThickness = new Thickness(1, 0, 0, 0);
            chat.ZIndex = 0;
        }
        else
        {
            // Nakładka: prawa/góra krawędź jak dawniej (nad obszarem treści, nie nad sidebarem);
            // ZIndex 60 > overlay szczegółów (50), ale modale są później w drzewie i zostają nad czatem.
            chat.SetValue(Grid.ColumnProperty, 1);
            chat.SetValue(Grid.ColumnSpanProperty, 3);
            chat.HorizontalAlignment = HorizontalAlignment.Right;
            chat.VerticalAlignment = VerticalAlignment.Top;
            chat.Margin = new Thickness(0, 4, 12, 0);
            chat.CornerRadius = new CornerRadius(14);
            chat.BoxShadow = BoxShadows.Parse("0 8 24 0 #40000000");
            chat.BorderThickness = new Thickness(1);
            chat.ZIndex = 60;
            // Rozmiar jak dawniej; fallbacki = wartości z XAML, gdy okno jeszcze niezmierzone.
            chat.Width = Bounds.Width > 0 ? Math.Min(520, Bounds.Width - 24) : 520;
            chat.MaxHeight = content.Bounds.Height > 0 ? Math.Min(690, content.Bounds.Height - 8) : 690;
        }
    }

    // ── Zakotwiczenie czatu: geometria okna po zapisie ustawień ──

    // Szerokość nadana automatycznie przy włączeniu (warunek cofnięcia) i szerokość sprzed poszerzenia.
    private double? _chatAutoGrantedWidth;
    private double _chatPreAnchorWidth;

    /// <summary>
    /// Woła widok ustawień po udanym zapisie (ścieżka SettingsSaved) z zatwierdzoną wartością.
    /// Poszerzenie/cofnięcie tylko przy rzeczywistej zmianie preferencji i tylko w stanie normalnym.
    /// </summary>
    public void ApplyChatAnchoredPreference(bool anchored)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var changed = vm.ChatAnchored != anchored;
        vm.ChatAnchored = anchored;
        if (changed)
            AdjustChatAnchorWidth(anchored);
        ApplyRightColumnLayout();
    }

    private void AdjustChatAnchorWidth(bool anchored)
    {
        // Zmaksymalizowanego/minimized nie poszerzamy ani nie przywracamy programowo;
        // po zmianie WindowState układ jest oceniany ponownie (OnPropertyChanged).
        if (WindowState != WindowState.Normal)
            return;

        if (anchored)
        {
            // DIP-aware: WorkingArea jest w px fizycznych, Width okna w DIP.
            var screen = Screens.ScreenFromWindow(this);
            var workAreaDip = screen is null ? double.PositiveInfinity : screen.WorkingArea.Width / RenderScaling;
            var target = ChatDockingLayout.WidenedWidth(Width, workAreaDip);
            if (target <= Width)
                return;

            _chatPreAnchorWidth = Width;
            Width = target;
            _chatAutoGrantedWidth = target;
        }
        else
        {
            // Cofnij tylko gdy użytkownik nie zmienił szerokości ręcznie po poszerzeniu.
            if (ChatDockingLayout.ShouldRevertAutoWidth(Width, _chatAutoGrantedWidth))
                Width = _chatPreAnchorWidth;
            _chatAutoGrantedWidth = null;
        }
    }

    // Hook startowy: Opened = okno ma uchwyt, więc Screens.ScreenFromWindow działa.
    // Flaga jednorazowości ustawiana PRZED resize: poszerzenie dodaje 520 bezwarunkowo,
    // a Opened może zajść ponownie (np. Show() po ukryciu w tray).
    private bool _chatAnchorStartupApplied;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Design.IsDesignMode || _chatAnchorStartupApplied)
            return;
        _chatAnchorStartupApplied = true;

        // Po restarcie preferencja jest już w VM, ale okno startuje wąskie — przywróć dokowanie.
        if (DataContext is MainWindowViewModel vm && vm.ChatAnchored)
        {
            AdjustChatAnchorWidth(true);
            ApplyRightColumnLayout();
        }
    }



    // ── Górna belka: chrome okna (przeciąganie, min/max/close) ──

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Przyciski obsługują własne kliknięcia (e.Handled) — przeciągamy tylko wolny obszar belki.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        for (Control? c = e.Source as Control; c != null && c != sender; c = c.Parent as Control)
            if (c is Button)
                return;
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void OnSupportClicked(object? sender, RoutedEventArgs e)
        => ProjectSupport.Open();

    // ── Sidebar: nawigacja klawiaturą (strzałki góra/dół; Enter aktywuje fokusowany przycisk) ──

    private void SidebarNav_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || sender is not Control root)
            return;

        var buttons = root.GetLogicalDescendants().OfType<Button>().ToList();
        if (buttons.Count == 0)
            return;

        var index = buttons.FindIndex(b => b.IsFocused);
        var next = e.Key == Key.Down
            ? buttons[(index + 1) % buttons.Count]
            : buttons[(index - 1 + buttons.Count) % buttons.Count];
        next.Focus();
        e.Handled = true;
    }

    /// <summary>Powrót z overlayu szczegółów w wąskim oknie.</summary>
    private void OnDetailBackClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (vm.IsDllPanelVisible)
            vm.CloseDllDetailCommand.Execute().Subscribe();
        else if (vm.IsPackInstancePanelVisible)
            vm.ClosePackInstanceDetailCommand.Execute().Subscribe();
        else if (vm.IsModPanelVisible)
            vm.CloseModDetailCommand.Execute().Subscribe();
        else if (vm.IsRolePanelVisible)
            vm.CloseRoleDetailCommand.Execute().Subscribe();
    }

    /// <summary>Konsola deweloperska z menu „Więcej" (ta sama ścieżka co przełącznik w Ustawieniach).</summary>
    private void OnShowConsoleClicked(object? sender, RoutedEventArgs e)
        => ConsoleWindow.ShowConsole();

    /// <summary>Akcje menu „Więcej" zamykają menu (toggle grupy zestawów zostaje otwarty).</summary>
    private void OnMoreMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn && btn.Name == "PackGroupToggle")
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.IsPaneOpen = false;
    }

    private ModItem? _bulkListSelectionAnchor;
    private ModInstanceItem? _bulkPackSelectionAnchor;
    private bool _revertingBulkListSelection;
    private bool _revertingBulkPackSelection;
    private bool _revertingBulkDllSelection;
    private ModItem? _bulkDllSelectionAnchor;

    private void ModsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_revertingBulkListSelection || sender is not ListBox listBox)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!vm.IsBulkSelectionMode)
        {
            _bulkListSelectionAnchor = vm.SelectedMod;
            return;
        }

        if (listBox.SelectedItem is not ModItem clicked)
            return;

        _revertingBulkListSelection = true;
        try
        {
            listBox.SelectedItem = _bulkListSelectionAnchor;
        }
        finally
        {
            _revertingBulkListSelection = false;
        }

        vm.ToggleBulkModCheckCommand.Execute(clicked).Subscribe();
    }

    /// <summary>
    /// E3: Enter aktywuje zaznaczony mod (ta sama ścieżka co dwuklik);
    /// w trybie bulk przełącza zaznaczenie checkboxa. Strzałki obsługuje ListBox.
    /// </summary>
    private void ModsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel vm || vm.SelectedMod is not { } mod)
            return;

        if (vm.IsBulkSelectionMode)
            vm.ToggleBulkModCheckCommand.Execute(mod).Subscribe();
        else
            vm.ModDoubleClickCommand.Execute(mod).Subscribe();
        e.Handled = true;
    }

    /// <summary>
    /// E4: Enter aktywuje zaznaczony zestaw (ta sama ścieżka co dwuklik);
    /// w trybie bulk przełącza zaznaczenie checkboxa. Strzałki obsługuje ListBox.
    /// </summary>
    private void PackInstancesListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel vm || vm.SelectedPackInstance is not { } pack)
            return;

        if (vm.IsBulkSelectionMode)
            vm.ToggleBulkPackCheckCommand.Execute(pack).Subscribe();
        else
            vm.PackInstanceDoubleClickCommand.Execute(pack).Subscribe();
        e.Handled = true;
    }

    /// <summary>
    /// E4: Enter otwiera inspektor zaznaczonego DLL (ta sama ścieżka co dwuklik);
    /// w trybie bulk przełącza zaznaczenie checkboxa. Strzałki obsługuje ListBox.
    /// </summary>
    private void DllModsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel vm || vm.SelectedDllMod is not { } dll)
            return;

        if (vm.IsBulkSelectionMode)
            vm.ToggleBulkModCheckCommand.Execute(dll).Subscribe();
        else
            vm.SelectDllModCommand.Execute(dll).Subscribe();
        e.Handled = true;
    }

    private void PackInstancesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_revertingBulkPackSelection || sender is not ListBox listBox)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!vm.IsBulkSelectionMode)
        {
            _bulkPackSelectionAnchor = vm.SelectedPackInstance;
            return;
        }

        if (listBox.SelectedItem is not ModInstanceItem clicked)
            return;

        _revertingBulkPackSelection = true;
        try
        {
            listBox.SelectedItem = _bulkPackSelectionAnchor;
        }
        finally
        {
            _revertingBulkPackSelection = false;
        }

        vm.ToggleBulkPackCheckCommand.Execute(clicked).Subscribe();
    }

    private void DllModsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_revertingBulkDllSelection || sender is not ListBox listBox)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (!vm.IsBulkSelectionMode)
        {
            _bulkDllSelectionAnchor = vm.SelectedDllMod;
            return;
        }

        if (listBox.SelectedItem is not ModItem clicked)
            return;

        _revertingBulkDllSelection = true;
        try
        {
            listBox.SelectedItem = _bulkDllSelectionAnchor;
        }
        finally
        {
            _revertingBulkDllSelection = false;
        }

        vm.ToggleBulkModCheckCommand.Execute(clicked).Subscribe();
    }

    private void ModGridBackground_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            (!vm.IsModPanelVisible && !vm.IsPackInstancePanelVisible && !vm.IsDllPanelVisible && !vm.IsRolePanelVisible))
            return;

        for (Control? c = e.Source as Control; c != null; c = c.Parent as Control)
        {
            if (c is ListBoxItem or CheckBox or BulkModeChip)
                return;
            if (c is Button btn && btn.Classes.Contains("bulk-mode-chip"))
                return;
        }

        if (vm.IsDllPanelVisible)
            vm.CloseDllDetailCommand.Execute().Subscribe();
        else if (vm.IsPackInstancePanelVisible)
            vm.ClosePackInstanceDetailCommand.Execute().Subscribe();
        else if (vm.IsRolePanelVisible)
            vm.CloseRoleDetailCommand.Execute().Subscribe();
        else
            vm.CloseModDetailCommand.Execute().Subscribe();
        e.Handled = true;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (vm.IsPaneOpen)
        {
            vm.IsPaneOpen = false;
            e.Handled = true;
            return;
        }

        // E5: warstwy Escape — Więcej → modale → czat → ustawienia → szczegóły.
        // Modale zachowują własną obsługę; czat i ustawienia zamykamy tylko gdy brak modala.
        if (vm.IsAnyToolModalOpen || vm.IsInlineDialogVisible || vm.IsAntivirusWarningVisible)
            return;

        // Escape zamyka wyłącznie nakładkę czatu — nigdy zadokowaną kolumnę.
        if (vm.IsChatOpen && !vm.IsChatDocked)
        {
            vm.IsChatOpen = false;
            e.Handled = true;
            return;
        }

        // Ustawienia: panel treści — Escape zamyka go i wraca do poprzedniej zakładki
        // (zakładka i zaznaczenie bez zmian; fokus wraca do przycisku Ustawienia).
        if (vm.IsAppSettingsVisible)
        {
            vm.IsAppSettingsVisible = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => this.FindControl<Button>("SettingsSidebarButton")?.Focus());
            e.Handled = true;
            return;
        }

        if (vm.IsDllPanelVisible)
        {
            vm.CloseDllDetailCommand.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        if (vm.IsPackInstancePanelVisible)
        {
            vm.ClosePackInstanceDetailCommand.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        if (vm.IsModPanelVisible)
        {
            vm.CloseModDetailCommand.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        if (vm.IsRolePanelVisible)
        {
            vm.CloseRoleDetailCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private async Task RemoveSingleInstanceAsync()
    {
        try
        {
            // Sprawd� czy mamy wybrany mod
            if (DataContext is not MainWindowViewModel vm || vm.SelectedMod == null)
            {
                await ShowErrorDialogAsync("B��d", "Nie wybrano moda.");
                return;
            }

            var selectedMod = vm.SelectedMod;

            // Sprawd� czy mod jest zainstalowany
            if (string.IsNullOrEmpty(selectedMod.InstallPath) || !Directory.Exists(selectedMod.InstallPath))
            {
                await ShowErrorDialogAsync("B��d", "Wybrany mod nie jest zainstalowany lub �cie�ka instalacji nie istnieje.");
                return;
            }

            // Poka� dialog potwierdzenia
            var confirmResult = await ShowConfirmDialogAsync(
                $"Czy na pewno chcesz usun�� ograniczenie SingleInstance z moda '{selectedMod.Name}'?\n\n" +
                "Ta operacja pozwoli na uruchomienie wielu kopii tego moda jednocze�nie.",
                "Potwierdzenie");

            if (!confirmResult)
                return;

            // �cie�ka do pliku boot.config
            string bootConfigPath = Path.Combine(selectedMod.InstallPath, "Among Us_Data", "boot.config");

            if (!File.Exists(bootConfigPath))
            {
                await ShowErrorDialogAsync("B��d", $"Nie znaleziono pliku boot.config w �cie�ce:\n{bootConfigPath}");
                return;
            }

            // Wczytaj zawarto�� pliku
            string[] lines = await File.ReadAllLinesAsync(bootConfigPath);

            // Usu� linijki zawieraj�ce single-instance
            var filteredLines = lines.Where(line =>
                !line.Trim().StartsWith("single-instance=", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // Sprawd� czy co� zosta�o usuni�te
            if (lines.Length == filteredLines.Length)
            {
                await ShowInfoDialogAsync("Informacja",
                    "Nie znaleziono linijki 'single-instance=' w pliku boot.config.\n" +
                    "Mod prawdopodobnie ju� nie ma ograniczenia SingleInstance.");
                return;
            }

            // Zapisz zmodyfikowany plik
            await File.WriteAllLinesAsync(bootConfigPath, filteredLines);

            await ShowInfoDialogAsync("Sukces",
                $"Pomy�lnie usuni�to ograniczenie SingleInstance z moda '{selectedMod.Name}'.\n\n" +
                "Teraz mo�esz uruchomi� wiele kopii tego moda jednocze�nie.");

            Debug.WriteLine($"Successfully removed SingleInstance from mod: {selectedMod.Name}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing SingleInstance: {ex.Message}");
            await ShowErrorDialogAsync("B��d", $"Nie uda�o si� usun�� SingleInstance:\n{ex.Message}");
        }
    }

    // Dodaj pomocnicze metody dla dialog�w
    private async Task<bool> ShowConfirmDialogAsync(string message, string title)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            return await vm.ShowInlineConfirmAsync(title, message);
        }

        return false;
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ShowInlineMessageAsync(title, message);
        }
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ShowInlineErrorAsync(title, message);
        }
    }


    private async Task LaunchMultipleInstancesAsync()
    {
        try
        {
            // Sprawd� czy mamy wybrany mod
            if (DataContext is not MainWindowViewModel vm || vm.SelectedMod == null)
            {
                await ShowErrorDialogAsync("B��d", "Nie wybrano moda.");
                return;
            }

            var selectedMod = vm.SelectedMod;

            // Sprawd� czy mod jest zainstalowany
            if (string.IsNullOrEmpty(selectedMod.InstallPath) || !Directory.Exists(selectedMod.InstallPath))
            {
                await ShowErrorDialogAsync("B��d", "Wybrany mod nie jest zainstalowany lub �cie�ka instalacji nie istnieje.");
                return;
            }

            // Poka� dialog z wyborem ilo�ci instancji
            var instanceCount = await ShowInstanceCountDialogAsync();
            if (instanceCount <= 0)
                return;

            // Poka� ostrze�enie dla Epic Games (je�li potrzebne)
            var platform = vm.DeterminePlatform(); // U�yj metody z ViewModel je�li jest publiczna
            if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase))
            {
                var confirmEpic = await ShowConfirmDialogAsync(
                    $"Uruchamianie wielu instancji dla Epic Games mo�e by� niestabilne.\n\n" +
                    $"Czy na pewno chcesz uruchomi� {instanceCount} instancji moda '{selectedMod.Name}'?",
                    "Ostrze�enie - Epic Games");

                if (!confirmEpic)
                    return;
            }

            // Uruchom wybrane ilo�ci instancji
            int successfulLaunches = 0;
            var errors = new List<string>();

            for (int i = 0; i < instanceCount; i++)
            {
                try
                {
                    Debug.WriteLine($"Launching instance {i + 1} of {instanceCount} for mod: {selectedMod.Name}");

                    // Wywo�aj bezpo�rednio metod� Launch z ViewModel
                    await vm.LaunchAsync();

                    successfulLaunches++;

                    // Pauza mi�dzy uruchomieniami (opr�cz ostatniej instancji)
                    if (i < instanceCount - 1)
                    {
                        await Task.Delay(2000); // Pauza dla stabilno�ci
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to launch instance {i + 1} of {selectedMod.Name}: {ex.Message}");
                    errors.Add($"Instancja {i + 1}: {ex.Message}");
                }
            }

            // Poka� wyniki
            if (successfulLaunches == instanceCount)
            {
                await ShowInfoDialogAsync("Sukces",
                    $"Pomy�lnie uruchomiono wszystkie {instanceCount} instancji moda '{selectedMod.Name}'.");
            }
            else if (successfulLaunches > 0)
            {
                var errorMessage = $"Uruchomiono {successfulLaunches} z {instanceCount} instancji moda '{selectedMod.Name}'.";
                if (errors.Any())
                {
                    errorMessage += $"\n\nB��dy:\n{string.Join("\n", errors)}";
                }
                await ShowInfoDialogAsync("Cz�ciowy sukces", errorMessage);
            }
            else
            {
                var errorMessage = $"Nie uda�o si� uruchomi� �adnej instancji moda '{selectedMod.Name}'.";
                if (errors.Any())
                {
                    errorMessage += $"\n\nB��dy:\n{string.Join("\n", errors)}";
                }
                await ShowErrorDialogAsync("B��d", errorMessage);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error launching multiple instances: {ex.Message}");
            await ShowErrorDialogAsync("B��d", $"Nie uda�o si� uruchomi� wielu instancji:\n{ex.Message}");
        }
    }


    private async Task<int> ShowInstanceCountDialogAsync()
    {
        // Dialog z motywami aplikacji - zwi�kszone wymiary
        var dialog = new Window
        {
            Title = "Ilo�� instancji",
            Width = 400,  // Zwi�kszone z 350
            Height = 280, // Zwi�kszone z 220
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.FindResource("WindowBackgroundBrush") as IBrush
        };

        var mainBorder = new Border
        {
            Background = this.FindResource("SecondaryBackgroundBrush") as IBrush,
            BorderBrush = this.FindResource("BorderBrush") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(20), // Zwi�kszone z 15
            Padding = new Thickness(25)  // Zwi�kszone z 20
        };

        var stackPanel = new StackPanel { Spacing = 25 };

        // Nag��wek
        var headerText = new TextBlock
        {
            Text = "Uruchom wiele instancji",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        stackPanel.Children.Add(headerText);

        // Opis
        var descriptionText = new TextBlock
        {
            Text = "Ile instancji chcesz uruchomi�?",
            FontSize = 12,
            Foreground = this.FindResource("TextSecondaryBrush") as IBrush,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        stackPanel.Children.Add(descriptionText);

        // NumericUpDown z motywami
        var numericUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 255,
            Value = 2,
            Increment = 1,  // Dodaj to - zwi�ksza/zmniejsza o 1
            FormatString = "F0",  // Dodaj to - format bez miejsc po przecinku
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Width = 120,
            Height = 32,
            Background = this.FindResource("TertiaryBackgroundBrush") as IBrush,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
            BorderBrush = this.FindResource("BorderBrush") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        stackPanel.Children.Add(numericUpDown);

        // Panel przycisk�w
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 15,
            Margin = new Thickness(0, 10, 0, 0)
        };

        bool dialogResult = false;

        // Przycisk OK z motywami
        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 32,
            Background = this.FindResource("AccentBrush") as IBrush,
            Foreground = Avalonia.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        okButton.Click += (s, e) => { dialogResult = true; dialog.Close(); };

        // Przycisk Anuluj z motywami
        var cancelButton = new Button
        {
            Content = "Anuluj",
            Width = 90,
            Height = 32,
            Background = this.FindResource("SecondaryBackgroundBrush") as IBrush,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
            BorderBrush = this.FindResource("BorderBrush") as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        cancelButton.Click += (s, e) => { dialogResult = false; dialog.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stackPanel.Children.Add(buttonPanel);

        mainBorder.Child = stackPanel;
        dialog.Content = mainBorder;

        await dialog.ShowDialog(this);

        return dialogResult ? (int)numericUpDown.Value : 0;
    }



    // Reszta istniej�cych metod...
    /// <summary>
    /// Ustawia opis z klikalnymi linkami w DescriptionPanel (StackPanel).
    /// </summary>
    public void SetDescriptionWithLinks(string description)
    {
        var panel = this.FindControl<ModDetailDrawer>("ModDetailDrawerControl")?.FindControl<StackPanel>("DescriptionPanel")
                    ?? this.FindControl<StackPanel>("DescriptionPanel");
        if (panel is null)
            return;

        panel.Children.Clear();

        if (string.IsNullOrEmpty(description))
            return;

        var regex = new Regex(@"(https?://[^\s]+)", RegexOptions.IgnoreCase);
        int lastIndex = 0;
        foreach (Match match in regex.Matches(description))
        {
            // Dodaj tekst przed linkiem
            if (match.Index > lastIndex)
            {
                var text = description.Substring(lastIndex, match.Index - lastIndex);
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }

            // Dodaj klikalny link jako Button
            var link = match.Value;
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = link,
                    TextDecorations = Avalonia.Media.TextDecorations.Underline
                },
                Padding = new Thickness(0),
                Background = null,
                BorderThickness = new Thickness(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            btn.Classes.Add("link");
            btn.Click += (_, __) =>
            {
                try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch { }
            };
            panel.Children.Add(btn);

            lastIndex = match.Index + match.Length;
        }

        // Dodaj tekst po ostatnim linku
        if (lastIndex < description.Length)
        {
            var text = description.Substring(lastIndex);
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    // FAB Button overlay click handler - zamyka menu po kliknięciu w tło
    private void OnOverlayPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsPaneOpen = false;
        }
    }

    private void OnToolModalOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseToolModalCommand.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void OnToolModalPanelPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnInlineDialogOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.IsInlineDialogDismissible)
        {
            vm.ResolveInlineDialog(false);
        }

        e.Handled = true;
    }

    private void OnInlineDialogCardPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnInlineDialogPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ResolveInlineDialog(true);
        }
    }

    private void OnInlineDialogSecondaryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ResolveInlineDialog(false);
        }
    }

    private void OnAntivirusWarningCardPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnAntivirusWarningConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ResolveAntivirusWarning();
        }
    }

    private void OnMenuPanelPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Zapobiega zamykaniu menu podczas klikania w panel (ale nie w overlay)
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Jeśli wymuszone zamknięcie (np. z menu tray "Zamknij") – omiń minimize-to-tray
        if (_forceClose)
        {
            _forceClose = false;
            goto cleanup;
        }

        // Sprawdź czy minimalizować do zasobnika zamiast zamykać
        var userSettingsService = new UserSettingsService();
        var settings = userSettingsService.LoadUserSettings();

        if (settings.MinimizeToTray && _systemTrayService != null)
        {
            // Anuluj zamknięcie, schowaj do tray
            e.Cancel = true;

            _systemTrayService.Show();

            // Pokaż dymek przy pierwszym minimalizowaniu
            _systemTrayService.ShowFirstMinimizeNotificationIfNeeded();

            this.Hide();
            return;
        }

    cleanup:
        if (DataContext is MainWindowViewModel vm && vm.IsModPackCreatorVisible &&
            vm.ModPackCreatorContent is ModPackCreatorView creator && !creator.TryBeginHostClose())
        {
            e.Cancel = true;
            return;
        }
        // Zwalnianie zasobów ViewModel (timery, bitmapy, background taski)
        if (DataContext is MainWindowViewModel mainVm)
            mainVm.CommunityChat.PropertyChanged -= OnCommunityChatPropertyChanged;
        if (DataContext is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }

        _systemTrayService?.Dispose();
        _systemTrayService = null;

        ConsoleLogger.Shutdown();
        base.OnClosing(e);
    }

    /// <summary>
    /// Wymusza zamknięcie aplikacji z pominięciem minimize-to-tray.
    /// Wywoływane z SystemTrayService przy wyborze "Zamknij" w menu tray.
    /// </summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>
    /// Zwraca instancję SystemTrayService (jeśli został zainicjalizowany).
    /// Używane przez App.axaml.cs do przekazania serwisu do ViewModel.
    /// </summary>
    public SystemTrayService? SystemTrayService => _systemTrayService;

    // ── Launch diagnostics actions ────────────────────────────

    private void OnOpenModFolderClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.OpenModFolder();

    private void OnOpenBepInExLogsClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.OpenBepInExLogs();

    private async void OnGenerateSupportBundleClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            await ViewModel.GenerateSupportBundleAsync();
    }

    private void OnOpenAiSupportClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsLaunchDiagnosticsVisible = false;
            ViewModel.ShowAiSupport();
        }
    }

    private void OnCloseLaunchDiagnosticsClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.HideLaunchDiagnostics();

    // ── AI Support actions ────────────────────────────────────

    private void OnAiSupportAcceptPrivacyClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.AiSupportPrivacyAccepted = true;
    }

    private async void OnAnalyzeAiSupportClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            await ViewModel.AnalyzeProblemAsync();
    }

    private async void OnAiSupportHelpedClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            await ViewModel.SendAiSupportFeedbackAsync(helped: true);
    }

    private async void OnAiSupportNotHelpedClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            await ViewModel.SendAiSupportFeedbackAsync(helped: false);
    }

    private void OnCloseAiSupportClicked(object? sender, RoutedEventArgs e)
        => ViewModel?.HideAiSupport();
}
