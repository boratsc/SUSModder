using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SUSModder.Core.GameIntegration.Steam;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial: headless-weryfikacja logowania Steam (EOS) dla vanilla po pierwszej konfiguracji
    /// oraz jako opcja naprawy tylko dla Steam.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Jednorazowość automatycznego testu na proces (wzór LegendaryBackgroundUpdater).</summary>
        private static int _steamLoginCheckAutoScheduled;

        /// <summary>Aktywne CTS bieżącego testu (jeden test naraz; anulowanie kliknięciem w toast).</summary>
        private CancellationTokenSource? _activeSteamLoginCheck;

        private readonly object _steamLoginCheckLock = new();

        /// <summary>Pozwala testom zresetować jednorazowość automatu.</summary>
        internal static void ResetSteamLoginCheckAutoScheduleForTests() =>
            Interlocked.Exchange(ref _steamLoginCheckAutoScheduled, 0);

        /// <summary>
        /// Planuje automatyczny test po załadowaniu aplikacji — wywoływane wyłącznie po pierwszej
        /// konfiguracji z wybranym Steam. Nigdy nie blokuje startu ani innych operacji.
        /// </summary>
        public void ScheduleSteamLoginCheckAfterFirstSetup()
        {
            if (!OperatingSystem.IsWindows())
                return;

            if (Interlocked.Exchange(ref _steamLoginCheckAutoScheduled, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await RunSteamLoginCheckOnceAsync();
                    if (result != null)
                        await ShowSteamLoginCheckToastAsync(result);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SteamLoginCheck] Auto: wyjątek ({ex.Message}).");
                }
            });
        }

        /// <summary>
        /// Ścieżka ręczna z ekranu napraw (Steam-only): potwierdzenie → test → czytelny wynik.
        /// </summary>
        private async Task ExecuteSteamLoginCheckRepairAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Info.Title"),
                    _localizationService.Get("UI.Repair.SteamLoginCheck.WindowsOnly"));
                return;
            }

            var confirmResult = await ShowConfirmDialogAsync(
                _localizationService.Get("UI.Repair.SteamLoginCheck.ConfirmMessage"),
                _localizationService.Get("Dialogs.Confirm.Title"));

            if (!confirmResult)
                return;

            SteamHeadlessLoginResult? result;
            try
            {
                result = await RunSteamLoginCheckOnceAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync(
                    string.Format(_localizationService.Get("UI.Repair.Error"), ex.Message),
                    _localizationService.Get("Dialogs.Error.Title"));
                return;
            }

            if (result == null)
            {
                // Test już się wykonywał (automat po starcie) — brak drugiego równoległego nasłuchu.
                await ShowMessageAsync(
                    _localizationService.Get("Dialogs.Info.Title"),
                    _localizationService.Get("UI.Repair.SteamLoginCheck.Running"));
                return;
            }

            var (type, title, message) = BuildSteamLoginCheckMessage(result);
            if (type == ToastType.Error)
            {
                await ShowErrorDialogAsync(message, title);
            }
            else
            {
                await ShowMessageAsync(title, message);
            }
        }

        /// <summary>
        /// Wspólny przebieg testu z toastem „w trakcie — kliknij, aby anulować".
        /// Zwraca null, gdy inny test już trwa (brak równoległych nasłuchów).
        /// </summary>
        private async Task<SteamHeadlessLoginResult?> RunSteamLoginCheckOnceAsync()
        {
            lock (_steamLoginCheckLock)
            {
                if (_activeSteamLoginCheck != null)
                    return null;
                _activeSteamLoginCheck = new CancellationTokenSource();
            }

            ToastNotification? runningToast = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                runningToast = ToastNotification.Create(
                    ToastType.Info,
                    _localizationService.Get("UI.Repair.SteamLoginCheck.Running"),
                    _localizationService.Get("UI.Repair.SteamLoginCheck.RunningHint"),
                    autoCloseMs: 0,
                    onClick: () =>
                    {
                        try { _activeSteamLoginCheck?.Cancel(); } catch { /* już zakończony */ }
                    });
                ToastService.Enqueue(runningToast);
            });

            try
            {
                var verifier = new SteamHeadlessLoginVerifier();
                var result = await verifier.RunAsync(_activeSteamLoginCheck.Token);
                System.Diagnostics.Debug.WriteLine(
                    $"[SteamLoginCheck] Wynik: {result.Status} (log: {result.LogPath ?? "-"}, " +
                    $"zamknięto: {result.OwnInstanceStopped}, niepotwierdzony: {result.OwnInstanceUnconfirmed}).");
                return result;
            }
            finally
            {
                CancellationTokenSource? toDispose;
                lock (_steamLoginCheckLock)
                {
                    toDispose = _activeSteamLoginCheck;
                    _activeSteamLoginCheck = null;
                }
                toDispose?.Dispose();

                if (runningToast != null)
                {
                    var toastId = runningToast.Id;
                    await Dispatcher.UIThread.InvokeAsync(() => ToastService.Dismiss(toastId));
                }
            }
        }

        /// <summary>
        /// Czytelny PL/EN status wyniku (toasty ścieżki automatycznej).
        /// </summary>
        private async Task ShowSteamLoginCheckToastAsync(SteamHeadlessLoginResult result)
        {
            var (type, title, message) = BuildSteamLoginCheckMessage(result);
            var logPath = result.LogPath;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var toast = ToastNotification.Create(
                    type,
                    title,
                    message,
                    autoCloseMs: type == ToastType.Error ? 12000 : 8000,
                    onClick: logPath == null ? null : () => TryOpenSteamLoginCheckLog(logPath));
                ToastService.Enqueue(toast);
            });
        }

        /// <summary>
        /// Mapowanie wyniku na komunikat zlokalizowany (tytuł + treść). Bez fałszywych sukcesów:
        /// „Confirmed" tylko po markerze w logu bieżącego uruchomienia.
        /// </summary>
        private (ToastType Type, string Title, string Message) BuildSteamLoginCheckMessage(SteamHeadlessLoginResult result)
        {
            var loc = _localizationService;
            string? extra = result.OwnInstanceUnconfirmed
                ? loc.Get("UI.Repair.SteamLoginCheck.ProcessLeftOpen")
                : null;

            return result.Status switch
            {
                SteamHeadlessLoginStatus.Confirmed => (
                    ToastType.Success,
                    loc.Get("Dialogs.Success.Title"),
                    Append(loc.Get("UI.Repair.SteamLoginCheck.Success"), extra)),

                SteamHeadlessLoginStatus.NotConfirmed => (
                    ToastType.Warning,
                    loc.Get("UI.Repair.SteamLoginCheck.NotConfirmedTitle"),
                    Append(loc.GetFormatted("UI.Repair.SteamLoginCheck.NotConfirmed", result.LogPath ?? "-"), extra)),

                SteamHeadlessLoginStatus.Cancelled => (
                    ToastType.Info,
                    loc.Get("Dialogs.Info.Title"),
                    Append(loc.Get("UI.Repair.Cancelled"), extra)),

                SteamHeadlessLoginStatus.GameAlreadyRunning => (
                    ToastType.Info,
                    loc.Get("Dialogs.Info.Title"),
                    loc.Get("UI.Repair.SteamLoginCheck.GameRunning")),

                SteamHeadlessLoginStatus.SteamUnavailable => (
                    ToastType.Error,
                    loc.Get("Dialogs.Error.Title"),
                    loc.Get("UI.Repair.SteamLoginCheck.SteamUnavailable")),

                SteamHeadlessLoginStatus.GameNotInstalled => (
                    ToastType.Warning,
                    loc.Get("UI.Repair.SteamLoginCheck.NotConfirmedTitle"),
                    loc.Get("UI.Repair.SteamLoginCheck.GameNotInstalled")),

                _ => (
                    ToastType.Error,
                    loc.Get("Dialogs.Error.Title"),
                    Append(loc.GetFormatted("UI.Repair.SteamLoginCheck.Failed", result.Diagnostic ?? "-"), extra)),
            };
        }

        private static string Append(string message, string? extra) =>
            extra == null ? message : message + "\n\n" + extra;

        /// <summary>
        /// Otwiera folder z logiem diagnostycznym testu (explorer /select). Best-effort.
        /// </summary>
        private void TryOpenSteamLoginCheckLog(string logPath)
        {
            try
            {
                if (!System.IO.File.Exists(logPath))
                {
                    // Plik mógł zniknąć — otwórz przynajmniej folder Logs.
                    var dir = System.IO.Path.GetDirectoryName(logPath);
                    if (dir != null && System.IO.Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                        return;
                    }
                }

                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SteamLoginCheck] Nie udało się otworzyć logu: {ex.Message}");
            }
        }
    }
}
