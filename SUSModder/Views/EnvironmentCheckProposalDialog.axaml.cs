using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SUSModder.Views;

/// <summary>
/// Jednorazowa propozycja sprawdzenia środowiska (wariant C).
/// ShowDialog&lt;bool&gt;: true = Sprawdź; false (Nie teraz, Esc lub zamknięcie okna) = Nie teraz.
/// </summary>
public partial class EnvironmentCheckProposalDialog : Window
{
    public EnvironmentCheckProposalDialog()
    {
        InitializeComponent();
    }

    private void OnCheckClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnSkipClick(object? sender, RoutedEventArgs e) => Close(false);
}
