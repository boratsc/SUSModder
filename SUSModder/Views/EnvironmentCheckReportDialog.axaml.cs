using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SUSModder.Core.Diagnostics.Environment;
using SUSModder.Core.Services.Localization;
using SUSModder.ViewModels.Helpers;

namespace SUSModder.Views;

/// <summary>
/// Raport sprawdzenia środowiska (tylko odczyt). Uruchamia probe'y Core w tle po otwarciu,
/// pokazuje postęp, wiersze z akcjami i pozwala ponowić bez nakładania się przebiegów.
/// </summary>
public partial class EnvironmentCheckReportDialog : Window
{
    private readonly SelfCheckContext _context;
    private readonly ILocalizationService _loc;
    private CancellationTokenSource? _cts;
    private bool _running;

    /// <summary>Ostatni raport (null, gdy zamknięto przed wynikiem).</summary>
    public SelfCheckReport? Report { get; private set; }

    /// <summary>Konstruktor wymagany przez loader XAML (design-time / AVLN3001).</summary>
    public EnvironmentCheckReportDialog()
    {
        _context = null!;
        _loc = null!;
        InitializeComponent();
        Opened += async (_, _) => await RunChecksAsync();
        Closing += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { /* już zakończony */ }
        };
    }

    public EnvironmentCheckReportDialog(SelfCheckContext context, ILocalizationService loc) : this()
    {
        _context = context;
        _loc = loc;
    }

    private async void OnRecheckClick(object? sender, RoutedEventArgs e) => await RunChecksAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRowActionClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not EnvironmentCheckRow row)
            return;

        switch (row.Action)
        {
            case EnvironmentCheckAction.OpenWindowsSecurity:
                TryOpen("ms-settings:windowsdefender");
                break;
            case EnvironmentCheckAction.OpenSteam:
                // Instrukcja tekstowa jest zawsze widoczna, więc best-effort wystarczy.
                TryOpen(SteamLinks.AmongUsGameProperties);
                break;
        }
    }

    private static void TryOpen(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EnvironmentCheck] Nie udało się otworzyć {uri}: {ex.Message}");
        }
    }

    private async Task RunChecksAsync()
    {
        if (_running || _context is null)
            return;

        _running = true;
        RecheckButton.IsEnabled = false;
        ErrorText.IsVisible = false;
        ProgressPanel.IsVisible = true;
        _cts = new CancellationTokenSource();

        try
        {
            var report = await new EnvironmentSelfCheckService().RunAsync(_context, _cts.Token);
            if (_cts.Token.IsCancellationRequested)
                return; // okno zamknięte w trakcie — nie aktualizujemy martwego UI

            Report = report;
            var rows = EnvironmentCheckPresentation.BuildRows(report, _loc, _context.ModsInstallPath);
            ResultsList.ItemsSource = rows.Where(EnvironmentCheckPresentation.NeedsAttention).ToList();
            var rest = rows.Where(r => !EnvironmentCheckPresentation.NeedsAttention(r)).ToList();
            RestList.ItemsSource = rest;
            RestExpander.IsVisible = rest.Count > 0;
            SummaryText.Text = EnvironmentCheckPresentation.BuildSummary(rows, _loc);
            SummaryText.IsVisible = true;
            TimestampText.Text = string.Format(
                _loc.Get("UI.EnvironmentCheck.Timestamp"),
                report.CheckedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
            TimestampText.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // zamknięcie okna w trakcie sprawdzania
        }
        catch (Exception ex)
        {
            // Neutralny komunikat bez straszenia; techniczny wyjątek tylko do diagnostyki.
            Debug.WriteLine($"[EnvironmentCheck] Nie udało się dokończyć sprawdzenia: {ex}");
            ErrorText.Text = _loc.Get("UI.EnvironmentCheck.Error");
            ErrorText.IsVisible = true;
        }
        finally
        {
            _running = false;
            ProgressPanel.IsVisible = false;
            RecheckButton.IsEnabled = true;
            var cts = _cts;
            _cts = null;
            cts?.Dispose();
        }
    }
}
