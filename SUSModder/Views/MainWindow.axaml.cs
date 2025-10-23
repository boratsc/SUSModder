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
using SUSModder.Services;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Input;

namespace SUSModder.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    // Dodaj komendy jako w�a�ciwo�ci
    public ReactiveCommand<Unit, Unit> RemoveSingleInstanceCommand { get; }
    public ReactiveCommand<Unit, Unit> LaunchMultipleInstancesCommand { get; }

    public MainWindow()
    {
        // DataContext będzie ustawiony z zewnątrz (przez App.axaml.cs)
        // Jeśli nie jest ustawiony (np. design mode), utwórz dummy
        if (DataContext == null)
        {
            DataContext = new MainWindowViewModel();
        }

        InitializeComponent();

        _fabButton = this.FindControl<Button>("FabButton");
        _fabMenuPanel = this.FindControl<Border>("FabMenuPanel");
        _fabHost = this.FindControl<Canvas>("FabHost");
        _statusBar = this.FindControl<Border>("StatusBar");

        // Inicjalizuj komendy
        RemoveSingleInstanceCommand = ReactiveCommand.CreateFromTask(RemoveSingleInstanceAsync);
        LaunchMultipleInstancesCommand = ReactiveCommand.CreateFromTask(LaunchMultipleInstancesAsync);

        // Ustaw początkową pozycję FAB po załadowaniu okna
        this.Opened += (_, _) =>
        {
            _fabInitialPlacementDone = false;
            InitializeFabPosition();
        };

        if (_fabMenuPanel != null)
        {
            _fabMenuPanel.GetObservable(Visual.BoundsProperty)
                         .Subscribe(_ => UpdateFabMenuPosition(_fabCurrentPosition.X, _fabCurrentPosition.Y));
        }

        if (_fabHost != null)
        {
            _fabHost.GetObservable(Visual.BoundsProperty)
                    .Subscribe(_ => EnsureFabWithinBounds());
        }

        this.GetObservable(Visual.BoundsProperty)
            .Subscribe(_ => EnsureFabWithinBounds());

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

                vm.WhenAnyValue(x => x.IsPaneOpen)
                  .Subscribe(_ => UpdateFabMenuPosition(_fabCurrentPosition.X, _fabCurrentPosition.Y))
                  .DisposeWith(disposables);
            }
        });
    }

    private void InitializeFabPosition()
    {
        if (_fabButton == null)
            return;

        var hostHeight = GetHostHeight();
        var hostWidth = GetHostWidth();
        var buttonHeight = _fabButton.Bounds.Height > 0 ? _fabButton.Bounds.Height : _fabButton.Height;

        if (hostHeight <= 0 || buttonHeight <= 0)
            return;

        var initialLeft = FabEdgePadding;
        var initialTop = CalculateMaxTop(hostHeight, buttonHeight);

        UpdateFabVisuals(initialLeft, initialTop);
        
        // Zapisz offset od dołu (początkowo FAB jest na maxTop, więc offset = 0)
        _fabOffsetFromBottom = 0;
        
        _fabInitialPlacementDone = true;
    }

    private void ModDeveloperMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Menu flyout otworzy si� automatycznie
        // Tutaj mamy dost�p do wybranego moda przez DataContext
        if (DataContext is MainWindowViewModel vm && vm.SelectedMod != null)
        {
            Debug.WriteLine($"Developer menu opened for mod: {vm.SelectedMod.Name}");
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
        var dialog = new SUSModder.Views.ConfirmDialog(title, message);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var dialog = new SUSModder.Views.MessageDialog(title, message);
        await dialog.ShowDialog(this);
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new SUSModder.Views.MessageDialog(title, message);
        await dialog.ShowDialog(this);
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
        if (this.FindControl<StackPanel>("DescriptionPanel") is not StackPanel panel)
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

    // FAB Button drag & drop functionality
    private const double FabEdgePadding = 24d;
    private const double FabPanelSpacing = 16d;

    private Button? _fabButton;
    private Border? _fabMenuPanel;
    private Canvas? _fabHost;
    private Border? _statusBar;

    private bool _isDraggingFab;
    private Point _fabDragStartPoint;
    private bool _wasDragged;
    private Point _fabOriginalPosition;
    private Point _fabCurrentPosition = new Point(FabEdgePadding, FabEdgePadding);
    private bool _fabInitialPlacementDone;
    private IPointer? _fabActivePointer;
    
    // Przechowujemy offset od dolnej krawędzi, aby FAB "trzymał się" dołu przy zmianie rozmiaru
    private double _fabOffsetFromBottom;

    private void OnFabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_fabButton == null)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _isDraggingFab = true;
            _wasDragged = false;
            _fabDragStartPoint = e.GetPosition(this); // Pozycja względem okna
            _fabOriginalPosition = _fabCurrentPosition;

            // Przechwyć wskaźnik, aby otrzymywać zdarzenia nawet poza przyciskiem
            _fabActivePointer = e.Pointer;
            _fabActivePointer.Capture(this); // Przechwytujemy na poziomie okna
            e.Handled = true;
        }
    }

    private void OnFabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingFab || !ReferenceEquals(e.Pointer, _fabActivePointer))
            return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _fabDragStartPoint;

        if (!_wasDragged && (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3))
        {
            _wasDragged = true;
        }

        if (_wasDragged)
        {
            var newLeft = _fabOriginalPosition.X + delta.X;
            var newTop = _fabOriginalPosition.Y + delta.Y;
            UpdateFabVisuals(newLeft, newTop);
        }
        e.Handled = true;
    }

    private void OnFabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingFab || !ReferenceEquals(e.Pointer, _fabActivePointer))
            return;

        if (_wasDragged && _fabButton != null)
        {
            // Zapisz ostatnią pozycję
            _fabCurrentPosition = new Point(Canvas.GetLeft(_fabButton), Canvas.GetTop(_fabButton));
            e.Handled = true;
        }
        else
        {
            // Jeśli nie było przeciągnięcia, potraktuj to jako kliknięcie
            if (DataContext is MainWindowViewModel vm)
            {
                vm.TogglePaneCommand.Execute(Unit.Default);
            }
        }

        // Zawsze zwalniaj zasoby przeciągania
        _isDraggingFab = false;
        _fabActivePointer?.Capture(null);
        _fabActivePointer = null;
    }

    private void UpdateFabVisuals(double desiredLeft, double desiredTop)
    {
        if (_fabButton == null)
            return;

        var hostWidth = GetHostWidth();
        var hostHeight = GetHostHeight();

        if (hostWidth <= 0)
        {
            hostWidth = _fabButton.Width + (FabEdgePadding * 2);
        }

        if (hostHeight <= 0)
        {
            hostHeight = _fabButton.Height + (FabEdgePadding * 2);
        }

        var buttonWidth = _fabButton.Bounds.Width > 0 ? _fabButton.Bounds.Width : _fabButton.Width;
    var buttonHeight = _fabButton.Bounds.Height > 0 ? _fabButton.Bounds.Height : _fabButton.Height;

    var maxLeft = hostWidth - buttonWidth - FabEdgePadding;
    var maxTop = CalculateMaxTop(hostHeight, buttonHeight);

        if (maxLeft < FabEdgePadding)
        {
            maxLeft = FabEdgePadding;
        }

        if (maxTop < FabEdgePadding)
        {
            maxTop = FabEdgePadding;
        }

        var clampedLeft = ClampToBounds(desiredLeft, FabEdgePadding, maxLeft);
        var clampedTop = ClampToBounds(desiredTop, FabEdgePadding, maxTop);

        Canvas.SetLeft(_fabButton, clampedLeft);
        Canvas.SetTop(_fabButton, clampedTop);

        _fabCurrentPosition = new Point(clampedLeft, clampedTop);
        
        // Zapisz offset od dolnej krawędzi (maxTop to pozycja przy samym dole)
        _fabOffsetFromBottom = maxTop - clampedTop;

        UpdateFabMenuPosition(clampedLeft, clampedTop);
    }

    private void UpdateFabMenuPosition(double buttonLeft, double buttonTop)
    {
        if (_fabMenuPanel == null || _fabButton == null)
            return;

    var hostWidth = GetHostWidth();
    var hostHeight = GetHostHeight();

        if (hostWidth <= 0)
        {
            hostWidth = _fabButton.Width + (FabEdgePadding * 2);
        }

        if (hostHeight <= 0)
        {
            hostHeight = _fabButton.Height + (FabEdgePadding * 2);
        }

        var panelWidth = _fabMenuPanel.Bounds.Width;
        if (panelWidth <= 0)
        {
            panelWidth = _fabMenuPanel.DesiredSize.Width;
        }
        if (panelWidth <= 0)
        {
            panelWidth = Math.Max(_fabMenuPanel.MinWidth, 200);
        }

        var panelHeight = _fabMenuPanel.Bounds.Height;
        if (panelHeight <= 0)
        {
            panelHeight = _fabMenuPanel.DesiredSize.Height;
        }
        if (panelHeight <= 0)
        {
            panelHeight = 320;
        }

        var buttonHeight = _fabButton.Bounds.Height > 0 ? _fabButton.Bounds.Height : _fabButton.Height;

        var left = buttonLeft;
        var top = buttonTop - panelHeight - FabPanelSpacing;

        if (left + panelWidth > hostWidth - FabEdgePadding)
        {
            left = hostWidth - panelWidth - FabEdgePadding;
        }
        if (left < FabEdgePadding)
        {
            left = FabEdgePadding;
        }

        if (top < FabEdgePadding)
        {
            top = buttonTop + buttonHeight + FabPanelSpacing;

            if (top + panelHeight > hostHeight - FabEdgePadding)
            {
                top = hostHeight - panelHeight - FabEdgePadding;
            }

            if (top < FabEdgePadding)
            {
                top = FabEdgePadding;
            }
        }

        Canvas.SetLeft(_fabMenuPanel, left);
        Canvas.SetTop(_fabMenuPanel, top);
    }

    private void EnsureFabWithinBounds()
    {
        if (!_fabInitialPlacementDone)
        {
            InitializeFabPosition();
            if (!_fabInitialPlacementDone)
                return;
        }

        // Oblicz nową pozycję Y na podstawie offsetu od dołu
        var hostHeight = GetHostHeight();
        var buttonHeight = _fabButton?.Bounds.Height > 0 ? _fabButton.Bounds.Height : (_fabButton?.Height ?? 56);
        var maxTop = CalculateMaxTop(hostHeight, buttonHeight);
        
        // FAB powinien pozostać w tej samej odległości od dolnej krawędzi
        var newTop = maxTop - _fabOffsetFromBottom;
        
        // Zachowaj poprzednią pozycję X
        UpdateFabVisuals(_fabCurrentPosition.X, newTop);
    }

    private static double ClampToBounds(double value, double min, double max)
    {
        if (!double.IsFinite(value))
        {
            return min;
        }

        var upper = max < min ? min : max;

        if (value < min)
            return min;

        if (value > upper)
            return upper;

        return value;
    }

    private double CalculateMaxTop(double hostHeight, double buttonHeight)
    {
        var statusBarHeight = GetStatusBarHeight();
        
        // FAB powinien być zawsze tuż nad status barem (niebieską linią)
        // hostHeight zawiera całą wysokość Canvas (z status barem)
        // Więc odejmujemy wysokość status bara i dodatkowy padding
        var maxTop = hostHeight - statusBarHeight - buttonHeight - FabEdgePadding;

        // Zabezpieczenie przed ujemnymi wartościami
        if (maxTop < FabEdgePadding)
        {
            maxTop = FabEdgePadding;
        }

        return maxTop;
    }

    private double GetStatusBarHeight()
    {
        if (_statusBar == null)
            return 0;

        var height = _statusBar.Bounds.Height;

        if (height <= 0 && _statusBar.DesiredSize.Height > 0)
        {
            height = _statusBar.DesiredSize.Height;
        }

        return height > 0 ? height : 0;
    }

    private double GetHostWidth()
    {
        double width = _fabHost?.Bounds.Width ?? 0;

        if (!(width > 0))
        {
            width = this.Bounds.Width;
        }

        if (!(width > 0))
        {
            width = this.ClientSize.Width;
        }

        if (!(width > 0) && _fabButton != null)
        {
            width = _fabButton.Width + (FabEdgePadding * 2);
        }

        return width > 0 ? width : (_fabButton?.Width ?? 0) + (FabEdgePadding * 2);
    }

    private double GetHostHeight()
    {
        double height = _fabHost?.Bounds.Height ?? 0;

        if (!(height > 0))
        {
            height = this.Bounds.Height;
        }

        if (!(height > 0))
        {
            height = this.ClientSize.Height;
        }

        if (!(height > 0) && _fabButton != null)
        {
            height = _fabButton.Height + (FabEdgePadding * 2);
        }

        return height > 0 ? height : (_fabButton?.Height ?? 0) + (FabEdgePadding * 2);
    }

    private void OnMenuPanelPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // Zapobiega zamykaniu menu podczas klikania w panel (ale nie w overlay)
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ConsoleLogger.Shutdown();
        base.OnClosing(e);
    }
}
