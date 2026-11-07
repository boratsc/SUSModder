using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Services;

namespace SUSModder.Core.GameIntegration.Steam;

/// <summary>
/// Wynik headless-weryfikacji logowania Steam (EOS) dla vanilla Among Us.
/// </summary>
public enum SteamHeadlessLoginStatus
{
    /// <summary>Sygnał sukcesu potwierdzony w logu bieżącego uruchomienia.</summary>
    Confirmed,

    /// <summary>Brak sygnału sukcesu w oknie czasowym — konfiguracja NIEpotwierdzona.</summary>
    NotConfirmed,

    /// <summary>Użytkownik anulował nasłuch.</summary>
    Cancelled,

    /// <summary>Gra już działała przed startem testu — pominięto, gry nie zamknięto.</summary>
    GameAlreadyRunning,

    /// <summary>steam:// nie jest obsługiwany (Steam niezainstalowany / brak skojarzenia).</summary>
    SteamUnavailable,

    /// <summary>Steam installation of Among Us is not available locally.</summary>
    GameNotInstalled,

    /// <summary>Pozostałe błędy uruchomienia.</summary>
    Failed,
}

/// <summary>
/// Wynik testu logowania Steam headless.
/// </summary>
/// <param name="Status">Klasyfikacja wyniku.</param>
/// <param name="LogPath">Ścieżka prywatnego logu testu (null, gdy nie utworzono lub usunięto po sukcesie).</param>
/// <param name="OwnInstanceStopped">Czy potwierdzona instancja testu została zamknięta.</param>
/// <param name="OwnInstanceUnconfirmed">Czy NIE udało się potwierdzić własności procesu — nic nie zabito.</param>
/// <param name="Diagnostic">Techniczny kontekst błędu (do logów; UI lokalizuje po Status).</param>
public sealed record SteamHeadlessLoginResult(
    SteamHeadlessLoginStatus Status,
    string? LogPath = null,
    bool OwnInstanceStopped = false,
    bool OwnInstanceUnconfirmed = false,
    string? Diagnostic = null);

/// <summary>
/// Uruchamia vanilla Among Us przez <c>steam://run/&lt;appid&gt;</c> w trybie
/// batchmode/nographics i potwierdza logowanie EOS po prywatnym logu.
/// </summary>
/// <remarks>
/// <para>
/// Zasady bezpieczeństwa (potwierdzone testem manualnym 2026-11):
/// sukces tylko po potwierdzeniu pełnego logowania w logu bieżącego uruchomienia;
/// jeśli gra działa — test jest pomijany i gry nie dotykamy;
/// steam:// nie daje uchwytu do procesu gry, więc proces zamykamy TYLKO po jednoznacznym
/// potwierdzeniu własności (linia poleceń zawiera unikalną nazwę naszego logu);
/// przy braku potwierdzenia nie zabijamy niczego;
/// log testu zawiera potencjalne dane konta — usuwamy tylko własny plik i tylko po sukcesie;
/// diagnosticzne logi porażki zostają do wglądu.
/// </para>
/// </remarks>
public sealed partial class SteamHeadlessLoginVerifier
{
    /// <summary>Domyślny limit czasu na potwierdzenie w logu.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(180);

    /// <summary>Domyślny interwał odczytu logu.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _isGameRunning;
    private readonly Func<bool> _isGameInstalled;
    private readonly Action<string> _launchUri;
    private readonly Func<string, string?> _readLogFile;
    private readonly Func<string, int[]> _findConfirmedOwnPids;
    private readonly Action<int, string> _killConfirmedPid;
    private readonly Action<string> _deleteOwnLog;
    private readonly Func<string> _logPathFactory;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;

    /// <param name="isGameRunning">Probe czy jakikolwiek "Among Us" działa (domyślnie: lista procesów).</param>
    /// <param name="launchUri">Start steam:// URI (rzuca <see cref="Win32Exception"/> przy braku obsługi).</param>
    /// <param name="readLogFile">Odczyt logu (null, gdy plik jeszcze nie istnieje).</param>
    /// <param name="findConfirmedOwnPids">PID-y procesów z logiem gry w linii poleceń; rzut wyjątkiem = niepotwierdzone.</param>
    /// <param name="killConfirmedPid">Zabicie JEDNEGO wcześniej potwierdzonego PID-u.</param>
    /// <param name="deleteOwnLog">Usunięcie własnego pliku logu.</param>
    /// <param name="logPathFactory">Fabryka unikalnej ścieżki logu (nazwa pliku = token własności).</param>
    public SteamHeadlessLoginVerifier(
        Func<bool>? isGameRunning = null,
        Action<string>? launchUri = null,
        Func<string, string?>? readLogFile = null,
        Func<string, int[]>? findConfirmedOwnPids = null,
        Action<int, string>? killConfirmedPid = null,
        Action<string>? deleteOwnLog = null,
        Func<string>? logPathFactory = null,
        TimeSpan timeout = default,
        TimeSpan pollInterval = default,
        Func<bool>? isGameInstalled = null)
    {
        _isGameRunning = isGameRunning ?? DefaultIsGameRunning;
        _isGameInstalled = isGameInstalled ?? (() => GameLocator.IsValidAmongUsInstallDirectory(GameLocator.TryFindSteamPath()));
        _launchUri = launchUri ?? DefaultLaunchUri;
        _readLogFile = readLogFile ?? DefaultReadLogFile;
        _findConfirmedOwnPids = findConfirmedOwnPids ?? DefaultFindConfirmedOwnPids;
        _killConfirmedPid = killConfirmedPid ?? DefaultKillConfirmedPid;
        _deleteOwnLog = deleteOwnLog ?? DefaultDeleteOwnLog;
        _logPathFactory = logPathFactory ?? DefaultLogPath;
        _timeout = timeout == default ? DefaultTimeout : timeout;
        _pollInterval = pollInterval == default ? DefaultPollInterval : pollInterval;
    }

    /// <summary>
    /// Buduje URI startowe w zweryfikowanym formacie: steam://run/945360//&lt;args&gt;/
    /// Ścieżka logu z spacjami/unicode jest cytowana i percent-odkodowana.
    /// </summary>
    public static string BuildLaunchUri(string logPath)
    {
        var args = $"-batchmode -nographics -logFile \"{logPath}\"";
        return $"steam://run/{DepotDownloaderRunner.SteamAppId}//{Uri.EscapeDataString(args)}/";
    }

    /// <summary>
    /// Czy log zawiera potwierdzony sygnał zakończenia logowania z testu manualnego.
    /// </summary>
    public static bool LogConfirmsLogin(string? logContent)
    {
        if (string.IsNullOrEmpty(logContent))
            return false;

        return logContent.Contains("finishing flow, login status LoggedIn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Czy linia poleceń procesu zawiera token naszego logu (potwierdzenie własności).
    /// </summary>
    public static bool CommandLineMatchesOwnLog(string? commandLine, string logFileToken)
    {
        return !string.IsNullOrEmpty(commandLine)
            && !string.IsNullOrEmpty(logFileToken)
            && commandLine.Contains(logFileToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unikalna ścieżka logu w katalogu danych aplikacji (ASCII nazwa = bezpieczny token).
    /// </summary>
    public static string DefaultLogPath()
    {
        var dir = Path.Combine(UserSettingsService.GetAppDataFolder(), "Logs");
        Directory.CreateDirectory(dir);
        var token = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        return Path.Combine(dir, $"steam-headless-{token}.log");
    }

    /// <summary>
    /// Uruchamia test: start steam:// → monitor logu → warunkowe zatrzymanie potwierdzonej instancji.
    /// Nie blokuje innych operacji; wyjątki są klasyfikowane w wyniku, nie propagowane (poza cancel).
    /// </summary>
    public async Task<SteamHeadlessLoginResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Cancelled);

        if (_isGameRunning())
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.GameAlreadyRunning);

        if (!_isGameInstalled())
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.GameNotInstalled);

        string logPath;
        try
        {
            logPath = _logPathFactory();
        }
        catch (Exception ex)
        {
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Failed, Diagnostic: ex.Message);
        }

        try
        {
            _launchUri(BuildLaunchUri(logPath));
        }
        catch (Win32Exception ex)
        {
            // Brak skojarzenia steam:// lub odrzucenie przez shell — Steam niedostępny.
            return new SteamHeadlessLoginResult(
                SteamHeadlessLoginStatus.SteamUnavailable,
                LogPath: null,
                Diagnostic: ex.Message);
        }
        catch (Exception ex)
        {
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Failed, Diagnostic: ex.Message);
        }

        var status = await MonitorLogAsync(logPath, cancellationToken);

        // steam:// nie daje uchwytu do procesu gry — zamykamy tylko jednoznacznie
        // potwierdzone instancje (nasz unikalny token logu w linii poleceń).
        var (stopped, unconfirmed) = TryStopConfirmedOwnInstances(Path.GetFileName(logPath));

        if (status == SteamHeadlessLoginStatus.Confirmed)
        {
            // Sukces: log z potencjalnymi danymi konta usuwamy (tylko własny plik).
            // shortcut: logi nieskonfirmowane zostawiamy do diagnostyki bez automatycznej retencji —
            // dodać czyszczenie starszych plików steam-headless-*.log przy starcie, gdy urosną.
            TryDeleteOwnLog(logPath);
            return new SteamHeadlessLoginResult(SteamHeadlessLoginStatus.Confirmed, OwnInstanceStopped: stopped, OwnInstanceUnconfirmed: unconfirmed);
        }

        return new SteamHeadlessLoginResult(status, LogPath: logPath, OwnInstanceStopped: stopped, OwnInstanceUnconfirmed: unconfirmed);
    }

    private async Task<SteamHeadlessLoginStatus> MonitorLogAsync(string logPath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _timeout;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return SteamHeadlessLoginStatus.Cancelled;

            string? content;
            try
            {
                content = _readLogFile(logPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Chwilowa niedostępność pliku (tworzenie, blokada zapisu) — nie jest sygnałem.
                content = null;
            }

            if (LogConfirmsLogin(content))
                return SteamHeadlessLoginStatus.Confirmed;

            if (DateTime.UtcNow >= deadline)
                return SteamHeadlessLoginStatus.NotConfirmed;

            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return SteamHeadlessLoginStatus.Cancelled;
            }
        }
    }

    private (bool Stopped, bool Unconfirmed) TryStopConfirmedOwnInstances(string logFileToken)
    {
        int[] pids;
        try
        {
            pids = _findConfirmedOwnPids(logFileToken);
        }
        catch
        {
            // Brak wiarygodnego potwierdzenia własności — nigdy nie zabijamy niczego.
            return (false, true);
        }

        if (pids.Length == 0)
            return (false, _isGameRunning());

        var stopped = 0;
        foreach (var pid in pids)
        {
            try
            {
                _killConfirmedPid(pid, logFileToken);
                stopped++;
            }
            catch
            {
                // Process mógł już zakończyć się sam — brak fałszywego sukcesu w liczniku.
            }
        }

        return (stopped > 0, stopped < pids.Length);
    }

    private void TryDeleteOwnLog(string logPath)
    {
        try
        {
            _deleteOwnLog(logPath);
        }
        catch
        {
            // Plik zajęty przez zamykaną grę — pomijamy, to TYLKO nasz plik.
        }
    }

    #region Domyślne operacje środowiskowe (Windows)

    private static bool DefaultIsGameRunning()
    {
        var processes = Process.GetProcessesByName("Among Us");
        var running = processes.Length > 0;
        foreach (var process in processes)
            process.Dispose();
        return running;
    }

    private static void DefaultLaunchUri(string uri)
    {
        var started = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        started?.Dispose();
    }

    private static string? DefaultReadLogFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static int[] DefaultFindConfirmedOwnPids(string logFileToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Weryfikacja własności procesu dostępna tylko na Windows.");

        // Wypytujemy linię poleceń i zwracamy PID-y zawierające unikalną nazwę naszego logu.
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'Among Us.exe'");

        var matches = new List<int>();
        foreach (ManagementObject row in searcher.Get())
        {
            try
            {
                var commandLine = row["CommandLine"] as string;
                if (CommandLineMatchesOwnLog(commandLine, logFileToken))
                {
                    var pid = Convert.ToInt32(row["ProcessId"]);
                    matches.Add(pid);
                }
            }
            finally
            {
                row.Dispose();
            }
        }

        return matches.ToArray();
    }

    private static void DefaultKillConfirmedPid(int pid, string logFileToken)
    {
        var process = Process.GetProcessById(pid);
        using (process)
        {
            _ = process.Handle;
            if (!DefaultFindConfirmedOwnPids(logFileToken).Contains(pid) || process.HasExited)
                throw new InvalidOperationException("Process ownership could not be confirmed.");
            process.Kill();
            process.WaitForExit(5000);
        }
    }

    private static void DefaultDeleteOwnLog(string path) => File.Delete(path);

    #endregion
}
