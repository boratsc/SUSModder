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
        }

        ApplyTrayVisibility();
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

                // Prawa kolumna: szczegóły zwijają się bez zaznaczenia, czat przełącza dymek na belce.
                vm.WhenAnyValue(x => x.IsBrowserDetailPanelVisible, x => x.IsChatColumnVisible)
                  .Subscribe(_ => ApplyRightColumnLayout())
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

        if (change.Property == WindowStateProperty)
        {
            var icon = this.FindControl<SymbolIcon>("MaximizeIcon");
            if (icon != null)
                icon.Symbol = WindowState == WindowState.Maximized ? Symbol.SquareMultiple : Symbol.Maximize;
        }
    }

    // ── Czat ogólny: lifecycle + podział prawej kolumny ──────

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

    /// <summary>Zapamiętana wysokość wiersza szczegółów (po ewentualnym dragu GridSplittera).</summary>
    private GridLength? _detailsRowRemembered;

    /// <summary>
    /// Układ prawej kolumny (szczegóły + czat). Szerokie okno: kolumna obok listy;
    /// wąskie (&lt; 1100 px): overlay nad listą z przyciskiem powrotu. Bez ściskania tekstu.
    /// </summary>
    private void ApplyRightColumnLayout()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var content = this.FindControl<Grid>("ContentGrid");
        var host = this.FindControl<Border>("RightColumnHost");
        var grid = this.FindControl<Grid>("RightColumnGrid");
        if (content == null || host == null || grid == null || grid.RowDefinitions.Count < 4)
            return;

        var details = vm.IsBrowserDetailPanelVisible;
        var chat = vm.IsChatColumnVisible;
        var visible = details || chat;
        var narrow = Bounds.Width > 0 && Bounds.Width < 1100;

        content.ColumnDefinitions[1].Width = narrow
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(440);
        content.ColumnDefinitions[2].Width = !narrow && visible
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        host.SetValue(Grid.ColumnProperty, narrow ? 1 : 2);
        host.SetValue(Grid.ColumnSpanProperty, narrow ? 2 : 1);
        host.ZIndex = narrow ? 50 : 0;
        host.IsVisible = visible;

        var back = this.FindControl<Border>("DetailBackRow");
        if (back != null)
            back.IsVisible = narrow && visible;

        var rows = grid.RowDefinitions;
        if (details)
        {
            rows[1].Height = _detailsRowRemembered ?? new GridLength(3, GridUnitType.Star);
        }
        else
        {
            // Nie zapamiętujemy stanu startowego (0px) — tylko realny rozmiar użytkownika.
            if (rows[1].Height.IsStar || rows[1].Height.Value > 0)
                _detailsRowRemembered = rows[1].Height;
            rows[1].Height = new GridLength(0);
        }

        rows[3].Height = chat ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        var splitter = this.FindControl<GridSplitter>("DetailChatSplitter");
        if (splitter != null)
            splitter.IsVisible = details && chat;
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

    /// <summary>Powrót z overlayu szczegółów/czatu w wąskim oknie.</summary>
    private void OnDetailBackClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.IsChatColumnVisible = false;
        if (vm.IsDllPanelVisible)
            vm.CloseDllDetailCommand.Execute().Subscribe();
        else if (vm.IsPackInstancePanelVisible)
            vm.ClosePackInstanceDetailCommand.Execute().Subscribe();
        else if (vm.IsModPanelVisible)
            vm.CloseModDetailCommand.Execute().Subscribe();
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
            (!vm.IsModPanelVisible && !vm.IsPackInstancePanelVisible && !vm.IsDllPanelVisible))
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

        if (vm.IsAnyToolModalOpen)
            return;

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

        // Wąskie okno: Escape zamyka overlay samego czatu.
        if (Bounds.Width < 1100 && vm.IsChatColumnVisible)
        {
            vm.IsChatColumnVisible = false;
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
