using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
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

        _fabDiscordPromoContent = this.FindControl<Grid>("FabDiscordPromoContent");

        var statusBar = this.FindControl<Border>("StatusBar");
        if (statusBar != null)
            statusBar.LayoutUpdated += (_, _) => UpdateFabMenuLayout();

        LayoutUpdated += (_, _) => UpdateFabMenuLayout();

        var modsList = this.FindControl<ListBox>("ModsListBox");
        if (modsList != null)
            modsList.SelectionChanged += ModsListBox_SelectionChanged;

        var packsList = this.FindControl<ListBox>("PackInstancesListBox");
        if (packsList != null)
            packsList.SelectionChanged += PackInstancesListBox_SelectionChanged;

        var dllList = this.FindControl<ListBox>("DllModsListBox");
        if (dllList != null)
            dllList.SelectionChanged += DllModsListBox_SelectionChanged;

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

                vm.WhenAnyValue(x => x.CurrentPromotedDiscord)
                  .Subscribe(_ => TriggerDiscordPromoScrollAnimation())
                  .DisposeWith(disposables);

                // Aktualizuj ikonę FAB na podstawie stanu aktualizacji
                vm.WhenAnyValue(x => x.FabHasBadge, x => x.IsAnyModInstalling)
                  .Subscribe(_ =>
                  {
                      System.Diagnostics.Debug.WriteLine($"[FAB-DEBUG] WhenAnyValue fired: FabHasBadge={vm.FabHasBadge}, IsAnyModInstalling={vm.IsAnyModInstalling}");
                      UpdateFabIcon(vm);
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

                vm.WhenAnyValue(x => x.IsPaneOpen)
                  .Subscribe(_ => UpdateFabMenuLayout())
                  .DisposeWith(disposables);

                vm.WhenAnyValue(x => x.IsDiscordPromoStatusBarMode, x => x.IsSystemStatusBarMode)
                  .Subscribe(_ => UpdateFabMenuLayout())
                  .DisposeWith(disposables);

                SubscribeGlassThemeChanges(vm, disposables);
            }
        });

        InitializeGlassThemeHooks();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ActualTransparencyLevelProperty)
            OnGlassTransparencyLevelChanged();
    }

    /// <summary>
    /// Kotwiczy menu FAB tuż nad przyciskiem (wysokość paska statusu + przewijanie przy długiej liście).
    /// </summary>
    private void UpdateFabMenuLayout()
    {
        var panel = this.FindControl<Border>("FabMenuPanel");
        var scroll = this.FindControl<ScrollViewer>("FabMenuScroll");
        var statusBar = this.FindControl<Border>("StatusBar");
        if (panel == null)
            return;

        const double gapAboveStatusBar = 10;
        var statusHeight = statusBar?.Bounds.Height ?? 0;
        if (statusHeight < 1)
            statusHeight = 76;

        panel.Margin = new Thickness(16, 0, 0, statusHeight + gapAboveStatusBar);

        if (scroll == null || Bounds.Height < 1)
            return;

        var topReserve = 24;
        var maxMenuHeight = Math.Max(200, Bounds.Height - statusHeight - gapAboveStatusBar - topReserve);
        scroll.MaxHeight = maxMenuHeight;
    }

    /// <summary>
    /// Aktualizuje ikonę FAB na podstawie stanu aktualizacji i instalacji.
    /// Ustawiana w code-behind, bo compiled binding nie aktualizuje ikony SymbolIcon.
    /// </summary>
    private void UpdateFabIcon(MainWindowViewModel vm)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var icon = this.FindControl<SymbolIcon>("FabIcon");
            System.Diagnostics.Debug.WriteLine($"[FAB-DEBUG] UpdateFabIcon: icon={(icon != null ? "found" : "NULL")}, FabHasBadge={vm.FabHasBadge}, IsAnyModInstalling={vm.IsAnyModInstalling}");
            if (icon == null) return;

            var newSymbol = vm.IsAnyModInstalling ? Symbol.ArrowSync
                          : vm.FabHasBadge ? Symbol.ArrowDownload
                          : Symbol.Navigation;
            System.Diagnostics.Debug.WriteLine($"[FAB-DEBUG] Setting icon.Symbol = {newSymbol} (was {icon.Symbol})");
            icon.Symbol = newSymbol;
        });
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
            if (c is ListBoxItem or Controls.ModCard or Controls.PackInstanceCard or Controls.DllAddonCard or CheckBox or BulkModeChip or BrowserTabBar or BrowserToolbar)
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

        if (!vm.IsModPanelVisible)
            return;

        vm.CloseModDetailCommand.Execute().Subscribe();
        e.Handled = true;
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

    private Grid? _fabDiscordPromoContent;
    private int _discordPromoAnimationVersion;

    private async void TriggerDiscordPromoScrollAnimation()
    {
        if (_fabDiscordPromoContent == null)
            return;

        var animationVersion = ++_discordPromoAnimationVersion;
        _fabDiscordPromoContent.Classes.Remove("visible");

        await Task.Delay(45);

        if (animationVersion != _discordPromoAnimationVersion || _fabDiscordPromoContent == null)
            return;

        _fabDiscordPromoContent.Classes.Add("visible");
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

    private void OnLobbyBoardHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsInfoPanelVisible = false;
            ViewModel.IsAdditionalActionsVisible = false;
            ViewModel.ShowLobbyBoard();
        }
    }

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
