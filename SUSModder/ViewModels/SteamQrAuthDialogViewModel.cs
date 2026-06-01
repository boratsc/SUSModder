using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using SUSModder.Core.GameIntegration.Steam;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels;

public class SteamQrAuthDialogViewModel : ViewModelBase
{
    private readonly Window _window;
    private readonly SteamQrDownloadContext _context;
    private readonly DepotDownloaderRunner _runner = new();
    private CancellationTokenSource? _cts;

    private string _statusText = "Przygotowywanie logowania Steam...";
    private string _qrDisplay = string.Empty;
    private bool _isBusy = true;
    private bool _hasError;
    private string _errorText = string.Empty;

    public SteamQrAuthDialogViewModel(Window window, SteamQrDownloadContext context)
    {
        _window = window;
        _context = context;

        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
        FallbackCommand = ReactiveCommand.CreateFromTask(UseFallbackAsync);

        _ = StartDownloadAsync();
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string QrDisplay
    {
        get => _qrDisplay;
        set => this.RaiseAndSetIfChanged(ref _qrDisplay, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    public string ErrorText
    {
        get => _errorText;
        set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> FallbackCommand { get; }

    public bool DialogResult { get; private set; }

    private async Task StartDownloadAsync()
    {
        _cts = new CancellationTokenSource();
        var recentLines = new List<string>();

        try
        {
            _runner.OnProgress = p =>
            {
                _context.OnDepotProgress?.Invoke(p);
                if (p.Percent.HasValue)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusText = $"Pobieranie: {p.Percent.Value:0.#}% {p.LastFileName}";
                    });
                }
            };

            _runner.OnLogLine = line =>
            {
                recentLines.Add(line);
                if (recentLines.Count > 200)
                    recentLines.RemoveAt(0);

                if (DepotDownloaderRunner.TryExtractQrBlock(recentLines, out var qrBlock))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        QrDisplay = qrBlock;
                        StatusText = "Zeskanuj kod w aplikacji Steam na telefonie.";
                    });
                }
            };

            StatusText = "Oczekiwanie na kod QR ze Steam...";
            _context.Log.Write("[Steam QR] Uruchamiam DepotDownloader z logowaniem QR.");

            await _runner.RunDownloadAsync(
                _context.ExtractedCachePath,
                _context.ManifestId,
                useQrAuth: true,
                _context.Log,
                _cts.Token);

            DialogResult = true;
            await CloseAsync(true);
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
            await CloseAsync(false);
        }
        catch (Exception ex)
        {
            _context.Log.Write($"[Steam QR] Błąd: {ex.Message}");
            HasError = true;
            ErrorText = ex.Message;
            StatusText = "Logowanie nie powiodło się.";
            IsBusy = false;
        }
    }

    private async Task CancelAsync()
    {
        _cts?.Cancel();
        DialogResult = false;
        await CloseAsync(false);
    }

    private async Task UseFallbackAsync()
    {
        _cts?.Cancel();
        DialogResult = false;
        await CloseAsync(false);
    }

    private async Task CloseAsync(bool result)
    {
        IsBusy = false;
        await Dispatcher.UIThread.InvokeAsync(() => _window.Close(result));
    }
}
