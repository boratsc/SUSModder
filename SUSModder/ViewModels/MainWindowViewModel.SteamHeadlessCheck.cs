using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SUSModder.Core.GameIntegration.Steam;
using SUSModder.ViewModels.Helpers;

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

            // Ścieżka ręczna: przy nieustalonej wersji gry pytamy o uruchomienie mimo to.
            // Auto-ścieżka (toast) nigdy nie override'uje — guard Core pozostaje twardy.
            if (result.Status == SteamHeadlessLoginStatus.BranchUnknown)
            {
                var runAnyway = await ShowConfirmDialogAsync(
                    _localizationService.Get("UI.Repair.SteamLoginCheck.BranchUnknownOverrideConfirm"),
                    title);

                if (!runAnyway)
                {
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Info.Title"),
                        _localizationService.Get("UI.Repair.Cancelled"));
                    return;
                }

                try
                {
                    result = await RunSteamLoginCheckOnceAsync(allowUnknownBranch: true);
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
                    await ShowMessageAsync(
                        _localizationService.Get("Dialogs.Info.Title"),
                        _localizationService.Get("UI.Repair.SteamLoginCheck.Running"));
                    return;
                }

                (type, title, message) = BuildSteamLoginCheckMessage(result);
            }

            // Wersja testowa (beta): zamiast samego OK dajemy jeden przycisk „Otwórz Steam".
            if (result.Status == SteamHeadlessLoginStatus.BetaBranchMismatch)
            {
                var openSteam = await ShowConfirmDialogAsync(
                    message,
                    title,
                    _localizationService.Get("UI.EnvironmentCheck.Action.OpenSteam"),
                    _localizationService.Get("UI.Buttons.Close"));
                if (openSteam)
                    TryOpenSteamForBeta();
                return;
            }

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
        /// <paramref name="allowUnknownBranch"/> wyłącznie po potwierdzeniu użytkownika
        /// w ścieżce ręcznej (BranchUnknown); auto-ścieżka zawsze bez override.
        /// </summary>
        private async Task<SteamHeadlessLoginResult?> RunSteamLoginCheckOnceAsync(bool allowUnknownBranch = false)
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
                var result = await verifier.RunAsync(_activeSteamLoginCheck.Token, allowUnknownBranch);
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
            Action? onClick = logPath == null ? null : () => TryOpenSteamLoginCheckLog(logPath);

            // Beta: toast klikalny otwiera Steam (instrukcja zmiany wersji jest w treści).
            if (result.Status == SteamHeadlessLoginStatus.BetaBranchMismatch)
            {
                message += "\n" + _localizationService.Get("UI.Repair.SteamLoginCheck.BetaToastHint");
                onClick = TryOpenSteamForBeta;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var toast = ToastNotification.Create(
                    type,
                    title,
                    message,
                    autoCloseMs: type == ToastType.Error ? 12000 : 8000,
                    onClick: onClick);
                ToastService.Enqueue(toast);
            });
        }

        /// <summary>Otwiera stronę Among Us w Steam (best-effort) — akcja przy wersji testowej (beta).</summary>
        private static void TryOpenSteamForBeta()
        {
            try
            {
                Process.Start(new ProcessStartInfo(SteamLinks.AmongUsGameProperties) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SteamLoginCheck] Nie udało się otworzyć Steam: {ex.Message}");
            }
        }

        /// <summary>
        /// Mapowanie wyniku na komunikat zlokalizowany (tytuł + treść) — logika w testowalnym
        /// helperze <see cref="SteamLoginCheckMessages"/>. Bez fałszywych sukcesów:
        /// „Confirmed" tylko po markerze w logu bieżącego uruchomienia.
        /// </summary>
        private (ToastType Type, string Title, string Message) BuildSteamLoginCheckMessage(SteamHeadlessLoginResult result)
            => SteamLoginCheckMessages.Build(_localizationService, result);

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
