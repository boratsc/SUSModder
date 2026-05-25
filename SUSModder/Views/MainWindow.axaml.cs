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
using FluentIcons.Common;
using FluentIcons.Avalonia;

namespace SUSModder.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    // Dodaj komendy jako w�a�ciwo�ci
    public ReactiveCommand<Unit, Unit> RemoveSingleInstanceCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LaunchMultipleInstancesCommand { get; private set; } = null!;

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

    private void InitializeWindow()
    {
        InitializeComponent();

        _fabDiscordPromoContent = this.FindControl<Grid>("FabDiscordPromoContent");

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
                  .Subscribe(_ => UpdateFabIcon(vm))
                  .DisposeWith(disposables);
            }
        });
    }

    /// <summary>
    /// Aktualizuje ikonę FAB na podstawie stanu aktualizacji i instalacji.
    /// Ustawiana w code-behind, bo compiled binding nie aktualizuje ikony SymbolIcon.
    /// </summary>
    private void UpdateFabIcon(MainWindowViewModel vm)
    {
        var icon = this.FindControl<SymbolIcon>("FabIcon");
        if (icon == null) return;

        if (vm.IsAnyModInstalling)
            icon.Symbol = Symbol.ArrowSync;
        else if (vm.FabHasBadge)
            icon.Symbol = Symbol.ArrowDownload;
        else
            icon.Symbol = Symbol.Navigation;
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
        ConsoleLogger.Shutdown();
        base.OnClosing(e);
    }
}
