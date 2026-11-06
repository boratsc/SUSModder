using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using System.Globalization;
using System.Text;
using SUSModder.Core.Models;
using SUSModder.Core.Services;

namespace SUSModder.Core.GameIntegration
{
    public interface IEpicUserInteraction
    {
        bool Confirm(string message);
        void ShowError(string message);
        string? Prompt(string message, string title = "");
        /// <summary>
        /// Pokazuje dedykowany dialog authentication dla Epic Games (async).
        /// </summary>
        /// <param name="browserUrl">URL do otwarcia w przeglądarce (legendary OAuth URL)</param>
        /// <returns>User input (JSON, URL lub kod) lub null jeśli anulowano</returns>
        Task<string?> ShowEpicAuthDialogAsync(string browserUrl);
    }



    /// <summary>
    /// Wynik uruchomienia legendary.
    /// </summary>
    public sealed class LegendaryRunResult
    {
        public LegendaryRunResult(int exitCode, IReadOnlyList<string>? standardError = null, bool timedOut = false)
        {
            ExitCode = exitCode;
            StandardError = standardError ?? Array.Empty<string>();
            TimedOut = timedOut;
        }

        public int ExitCode { get; }

        /// <summary>Wynik zakończonego powodzeniem uruchomienia.</summary>
        public static LegendaryRunResult Success() => new(0);

        public IReadOnlyList<string> StandardError { get; }

        /// <summary>
        /// Proces przekroczył limit czasu. Drzewo procesów mogło nie zakończyć się,
        /// więc po takim timeoutcie nie wolno uruchamiać kolejnych operacji legendary.
        /// </summary>
        public bool TimedOut { get; }

        public bool ContainsError(string marker)
            => StandardError.Any(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Globalna blokada po niepewnym zakończeniu procesu legendary (timeout).
    /// </summary>
    /// <remarks>
    /// Blokada trwa do restartu aplikacji: wyjście procesu rodzica nie dowodzi, że zakończyły się
    /// jego potomki (legendary odpala procesy robocze), więc nie zwalniamy jej po zniknięciu PID-u.
    /// Użytkownik musi ręcznie upewnić się, że legendary nie działa, i zrestartować aplikację.
    /// </remarks>
    internal static class LegendaryProcessGuard
    {
        private static bool _blocked;

        internal static void MarkUnfinished() => _blocked = true;

        internal static bool IsBlocked() => _blocked;

        /// <summary>Wyłącznie dla testów.</summary>
        internal static void Reset() => _blocked = false;
    }

    /// <summary>
    /// Uruchomienie komendy legendary - punkt podmiany w testach (symulowany runner).
    /// </summary>
    public delegate Task<LegendaryRunResult> LegendaryCommandRunner(
        string arguments,
        TimeSpan? timeout,
        Action<string, bool>? onOutput);

    public class EpicVersionManager
    {
        private readonly string legendaryPath;
        private readonly string manifestDirectory;
        private readonly string installDirectory;
        private const string EpicAppId = "963137e4c29d4c79a81323b8fab03a40";
        private const string NoBaseUrlsErrorMarker = "No base URLs found";
        private readonly string appSettingsFilePath;
        private readonly string logFilePath;
        private readonly string legendaryLogFilePath;
        public event Action<string>? LegendaryOutput;
        private readonly object _fileLock = new object();
        public event Action<int, string>? ProgressChanged;
        public event Action<string>? SpeedChanged;
        private double _lastProgressPercentage = 0;
        private string _lastCurrentFiles = "";
        private string _lastTotalFiles = "";
        private string _lastEta = "";
        private string _lastDownloaded = "";
        private string _lastDownloadSpeed = "";
        public event Action<ModConfiguration>? InstallationCompleted;
        private StringBuilder _errorLog = new StringBuilder();
        public event Action<string, string>? EpicLaunchError; // ModName, LogContent

        /// <summary>
        /// Last stable failure code from install/download flows (e.g. download_hash_mismatch).
        /// </summary>
        public string? LastFailureCode { get; private set; }

        /// <summary>
        /// Wywoływane tuż przed <c>legendary launch</c> — sygnał do startu obserwacji procesu gry.
        /// </summary>
        public event Action? GameLaunchStarting;
        private bool _hasLaunchError = false;
        private bool _isRetryingInstallation = false;
        private int _retryCount = 0;
        private const int MaxRetryAttempts = 2;

        public void ClearLegendaryLog()
        {
            try
            {
                if (File.Exists(legendaryLogFilePath))
                {
                    File.Delete(legendaryLogFilePath);
                    LogToFile("Legendary log file cleared");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to clear legendary log: {ex.Message}");
            }
        }
        public string GetErrorLog() => _errorLog.ToString();

        private readonly IDiagnosticsOutput _output;
        private readonly IEpicUserInteraction _userInteraction;
        private readonly LegendaryCommandRunner _commandRunner;
        private readonly string legendaryConfigDirectory;
        private ILegendaryVersionProvider? _versionProvider;

        public EpicVersionManager(
            IDiagnosticsOutput output,
            IEpicUserInteraction userInteraction,
            LegendaryCommandRunner? commandRunner = null,
            string? legendaryConfigDirectory = null,
            string? manifestDirectory = null,
            ILegendaryVersionProvider? versionProvider = null)
        {
            // BaseDirectory = katalog aplikacji (modów/tooli). ProcessPath przy `dotnet run`
            // wskazuje na dotnet.exe → C:\Program Files\dotnet\ (brak zapisu).
            var appDir = ResolveWritableAppDirectory();
            var logsDir = ResolveWritableLogsDirectory(appDir);

            legendaryPath = LegendaryToolPaths.ExecutablePath;
            this.manifestDirectory = manifestDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            installDirectory = PathSettings.ModsInstallPath;
            appSettingsFilePath = Path.Combine(appDir, "appsettings.json");

            logFilePath = Path.Combine(logsDir, "epic.log.txt");
            legendaryLogFilePath = Path.Combine(logsDir, "legendary.log.txt");

            _output = output;
            _userInteraction = userInteraction;
            _commandRunner = commandRunner ?? RunLegendaryProcessAsync;
            this.legendaryConfigDirectory = legendaryConfigDirectory ?? LegendaryBaseUrlState.ResolveConfigDirectory();
            _versionProvider = versionProvider;
        }

        /// <summary>
        /// Katalog z appsettings / artefaktami aplikacji — nigdy Program Files\dotnet.
        /// </summary>
        private static string ResolveWritableAppDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (IsUsableAppDirectory(baseDir))
                return baseDir;

            var appContextDir = AppContext.BaseDirectory;
            if (IsUsableAppDirectory(appContextDir))
                return appContextDir;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SUSModder");
        }

        private static string ResolveWritableLogsDirectory(string appDir)
        {
            // Preferuj %APPDATA%\SUSModder\logs — zawsze zapisywalne (także przy instalacji w Program Files).
            var appDataLogs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SUSModder",
                "logs");

            try
            {
                Directory.CreateDirectory(appDataLogs);
                return appDataLogs;
            }
            catch
            {
                // Fallback: obok aplikacji, jeśli się da
                try
                {
                    Directory.CreateDirectory(appDir);
                    return appDir;
                }
                catch
                {
                    return Path.GetTempPath();
                }
            }
        }

        private static bool IsUsableAppDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            try
            {
                var full = Path.GetFullPath(directory);
                // `dotnet run` → ProcessPath/BaseDir potrafi wskazać instalację SDK
                if (full.Contains($"{Path.DirectorySeparatorChar}dotnet{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || full.EndsWith($"{Path.DirectorySeparatorChar}dotnet", StringComparison.OrdinalIgnoreCase))
                    return false;

                return Directory.Exists(full);
            }
            catch
            {
                return false;
            }
        }

        private void AppendLegendaryLog(string line)
        {
            try
            {
                lock (_fileLock)
                    File.AppendAllText(legendaryLogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Log never blocks / crashes launch
            }
        }

        private void LogToFile(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(logFilePath, logEntry);
            }
            catch
            {
                // don't throw on logging failure
            }
        }

        private void Write(string line)
        {
            _output?.Write(line);
            LogToFile(line);
        }

        private void ShowError(string msg) => _userInteraction?.ShowError(msg);

        private bool Confirm(string msg) => _userInteraction?.Confirm(msg) ?? false;

        /// <summary>
        /// Czy dalsze polecenia legendary są zablokowane po niepewnym zakończeniu procesu (timeout).
        /// </summary>
        private bool IsLegendaryBlocked()
        {
            if (!LegendaryProcessGuard.IsBlocked())
                return false;

            Write("Poprzednia operacja legendary zakończyła się niepewnie (timeout) - dalsze polecenia legendary " +
                  "są zablokowane do restartu aplikacji (upewnij się, że legendary nie działa).");
            return true;
        }

        private string? Prompt(string msg, string title = "") => _userInteraction?.Prompt(msg, title);

        private async Task<string?> ShowEpicAuthDialogAsync(string browserUrl)
        {
            if (_userInteraction == null)
                return null;

            return await _userInteraction.ShowEpicAuthDialogAsync(browserUrl);
        }

        private async Task<string?> CheckInstalledAppsAsync()
        {
            try
            {
                if (IsLegendaryBlocked())
                    return null;

                Write("Sprawdzanie zainstalowanych aplikacji (legendary.exe list-installed --json)");

                string tempFile = Path.Combine(Path.GetTempPath(), "tempepic.json");

                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "list-installed --json",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var tracked = await StartTrackedLegendaryAsync(psi);
                var proc = tracked?.Process;
                if (proc == null)
                {
                    Write("Nie udało się uruchomić procesu legendary.exe");
                    return null;
                }

                string stdout = await proc.StandardOutput.ReadToEndAsync();
                string stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    Write("Otrzymany JSON jest pusty → brak zainstalowanych aplikacji");
                    // fallback na ścieżkę vanilla
                    var configs = ConfigManager.LoadConfig();
                    var vanilla = configs.FirstOrDefault(c => c.Id == 0);
                    if (vanilla != null && !string.IsNullOrEmpty(vanilla.InstallPath))
                    {
                        Write($"Fallback ścieżki vanilla: {vanilla.InstallPath}");
                        return vanilla.InstallPath;
                    }
                    return null;
                }

                File.WriteAllText(tempFile, stdout);

                List<InstalledApp> apps;
                try
                {
                    apps = JsonSerializer.Deserialize<List<InstalledApp>>(stdout)
                           ?? new List<InstalledApp>();
                }
                catch (JsonException jsonEx)
                {
                    Write($"Błąd parsowania JSON z legendary list-installed: {jsonEx.Message}");
                    return null;
                }

                // wczytaj configy tylko raz
                var configsAll = ConfigManager.LoadConfig();
                var vanillaCfg = configsAll.FirstOrDefault(c => c.Id == 0);

                // 1) wyszukaj wpis
                var entry = apps.FirstOrDefault(a => a.app_name == EpicAppId);
                if (entry == null)
                {
                    Write($"Brak pozycji {EpicAppId} w tempepic.json");
                    // spróbuj naprawić manifest używając ścieżki vanilla
                    if (vanillaCfg != null && !string.IsNullOrEmpty(vanillaCfg.InstallPath))
                    {
                        Write($"Naprawiam brakujący wpis, override ścieżki na: {vanillaCfg.InstallPath}");
                        await RunLegendaryCommandAsync(
                            $"repair {EpicAppId} --override-install-path \"{vanillaCfg.InstallPath}\" -y");
                        return vanillaCfg.InstallPath;
                    }
                    return null;
                }

                // 2) wpis znaleziony
                string foundPath = entry.install_path;
                Write($"Legendary zwraca ścieżkę: {foundPath}");

                // 3) porównaj z config.json (vanilla) i naprawiaj jeżeli różne
                if (vanillaCfg != null &&
                    !string.Equals(vanillaCfg.InstallPath, foundPath, StringComparison.OrdinalIgnoreCase))
                {
                    Write($"Różnica ścieżek: config.json='{vanillaCfg.InstallPath}', legendary='{foundPath}'. Naprawiam...");
                    await RunLegendaryCommandAsync(
                        $"repair {EpicAppId} --override-install-path \"{vanillaCfg.InstallPath}\" -y");
                }

                return foundPath;
            }
            catch (Exception ex)
            {
                Write($"ERROR w CheckInstalledAppsAsync: {ex}");
                return null;
            }
        }

        private class InstalledApp
        {
            public string app_name { get; set; } = string.Empty;
            public string install_path { get; set; } = string.Empty;
        }

        private class OwnedGame
        {
            public string app_name { get; set; } = string.Empty;
            public string app_title { get; set; } = string.Empty;
        }

        /// <summary>
        /// Sprawdza czy użytkownik posiada Among Us na koncie Epic Games przez legendary list-games.
        /// Wymaga zalogowania w legendary. Zwraca true jeśli gra jest na koncie, false w przeciwnym razie.
        /// </summary>
        public async Task<bool> CheckIsGameOwnedAsync()
        {
            try
            {
                if (IsLegendaryBlocked())
                    return false;

                if (!File.Exists(legendaryPath))
                {
                    Write("[GameOwned] legendary.exe nie istnieje - nie można sprawdzić posiadania gry");
                    return false;
                }

                Write("[GameOwned] Sprawdzanie posiadania Among Us przez legendary list-games --json...");

                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "list-games --json",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var tracked = await StartTrackedLegendaryAsync(psi);
                var proc = tracked?.Process;
                if (proc == null)
                {
                    Write("[GameOwned] Nie udało się uruchomić legendary.exe");
                    return false;
                }

                // Timeout 30 sekund - serwery Epic mogą być wolne
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                string stdout = string.Empty;
                string stderr = string.Empty;

                try
                {
                    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync(cts.Token);
                    stdout = await stdoutTask;
                    stderr = await stderrTask;
                }
                catch (OperationCanceledException)
                {
                    Write("[GameOwned] Timeout podczas oczekiwania na legendary list-games (30s)");
                    LegendaryProcessGuard.MarkUnfinished();
                    try { proc.Kill(); } catch { }
                    return false;
                }

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    Write("[GameOwned] legendary list-games zwrócił pustą odpowiedź - prawdopodobnie użytkownik nie jest zalogowany");
                    return false;
                }

                List<OwnedGame> games;
                try
                {
                    games = JsonSerializer.Deserialize<List<OwnedGame>>(stdout) ?? new List<OwnedGame>();
                }
                catch (JsonException jsonEx)
                {
                    Write($"[GameOwned] Błąd parsowania JSON z legendary list-games: {jsonEx.Message}");
                    return false;
                }

                var amongUs = games.FirstOrDefault(g =>
                    g.app_name.Equals(EpicAppId, StringComparison.OrdinalIgnoreCase) ||
                    g.app_title.Contains("Among Us", StringComparison.OrdinalIgnoreCase));

                if (amongUs != null)
                {
                    Write($"[GameOwned] Among Us znaleziony na koncie Epic: app_name={amongUs.app_name}, title={amongUs.app_title}");
                    return true;
                }

                Write($"[GameOwned] Among Us NIE znaleziony na koncie Epic (sprawdzono {games.Count} gier)");
                return false;
            }
            catch (Exception ex)
            {
                Write($"[GameOwned] ERROR w CheckIsGameOwnedAsync: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ModifyEpicAsync(ModConfiguration modConfig, object? progressBar, object? progressLabel)
        {
            LastFailureCode = null;

            if (modConfig == null)
            {
                ShowError("Konfiguracja moda jest nieprawidłowa.");
                return false;
            }

            var downloadResolution = await ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "epic");
            string downloadUrl = downloadResolution.Url;

            Write($"[CDN] URL pobierania moda Epic '{modConfig.ModName}': {downloadUrl}");
            if (!string.IsNullOrWhiteSpace(downloadResolution.ExpectedSha256))
                Write($"[CDN] Expected SHA256: {downloadResolution.ExpectedSha256}");
            else
                Write("[CDN] WARNING: brak SHA256 w metadanych — kontynuuję bez twardej weryfikacji (download_hash_missing).");

            string baseDirectory = installDirectory;
            
            // Unikalna nazwa katalogu temp dla każdej instalacji, aby uniknąć konfliktów przy równoczesnych instalacjach
            string uniqueTempId = Guid.NewGuid().ToString("N");
            string tempDirectory = Path.Combine(baseDirectory, "temp", uniqueTempId);
            Directory.CreateDirectory(tempDirectory);
            string modFile = Path.Combine(tempDirectory, "mod.zip");

            try
            {
            ProgressChanged?.Invoke(10, "Rozpoczynam pobieranie moda...");

            Write($"Rozpoczynam pobieranie moda '{modConfig.ModName}' z: {downloadUrl}");
            if (!await DownloadFileAsync(downloadUrl, modFile))
            {
                Write($"ERROR: Nie udało się pobrać moda z {downloadUrl}");
                ShowError($"Nie udało się pobrać moda z {downloadUrl}.");
                return false;
            }

            Write($"Pomyślnie pobrano mod do: {modFile}");

            if (Sha256Verifier.IsWellFormedHash(downloadResolution.ExpectedSha256))
            {
                ProgressChanged?.Invoke(38, "Weryfikacja sumy kontrolnej...");
                var actualHash = await Sha256Verifier.ComputeFileHexAsync(modFile);
                if (!string.Equals(actualHash, downloadResolution.ExpectedSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Write($"ERROR: SHA256 mismatch for Epic mod: expected {downloadResolution.ExpectedSha256}, got {actualHash}");
                    try { File.Delete(modFile); } catch { /* best effort */ }
                    LastFailureCode = DownloadVerificationCodes.HashMismatch;
                    ShowError(DownloadVerificationCodes.HashMismatch);
                    return false;
                }

                Write($"SHA256 OK for Epic mod '{modConfig.ModName}'");
            }
            else if (!string.IsNullOrWhiteSpace(downloadResolution.ExpectedSha256))
            {
                Write($"[CDN] WARNING: malformed expected SHA256 '{downloadResolution.ExpectedSha256}' — treating as missing.");
            }

            ProgressChanged?.Invoke(40, "Pobieranie zakończone, przygotowywanie instalacji...");

            // Verify-before-replace: only delete target AmongUs after successful download (+ hash when available).
            string gameBasePath = Path.Combine(baseDirectory, modConfig.ModName, "AmongUs");
            if (Directory.Exists(gameBasePath))
            {
                Write($"Usuwam istniejący katalog: {gameBasePath}");
                Directory.Delete(gameBasePath, true);
            }
            Directory.CreateDirectory(gameBasePath);
            string tempExtractPath = Path.Combine(tempDirectory, "extractMod");

            ProgressChanged?.Invoke(50, "Rozpakowywanie archiwum...");

            try
            {
                await InstallPathWriteHelper.PrepareExtractionDirectoryAsync(tempExtractPath, _output);
            }
            catch (Exception ex) when (InstallPathWriteHelper.IsAccessDeniedException(ex))
            {
                Write($"ERROR podczas przygotowania katalogu rozpakowania: {ex.Message}");
                ShowError(InstallPathWriteHelper.FormatExtractionAccessDeniedMessage(tempExtractPath, baseDirectory));
                return false;
            }

            try
            {
                Write($"Rozpakowuję archiwum moda: {modFile} do {tempExtractPath}");

                var extractionProgress = new Progress<ExtractionProgress>(p =>
                {
                    int pct;
                    if (p.PercentComplete.HasValue)
                    {
                        pct = (int)p.PercentComplete.Value;
                    }
                    else if (p.TotalBytes > 0)
                    {
                        pct = (int)(p.BytesExtracted * 100 / p.TotalBytes);
                    }
                    else
                    {
                        ProgressChanged?.Invoke(60,
                            $"Rozpakowywanie... ({p.BytesExtracted} B)");
                        return;
                    }

                    string currentFile = !string.IsNullOrEmpty(p.CurrentFile)
                        ? $" ({Path.GetFileName(p.CurrentFile)})"
                        : "";
                    int mapped = 50 + (pct * 20 / 100); // 50-70% overall
                    ProgressChanged?.Invoke(mapped,
                        $"Rozpakowywanie: {pct}%{currentFile}");
                });

                var extractor = new SharpCompressExtractor();
                await extractor.ExtractAsync(modFile, tempExtractPath, progress: extractionProgress);

                Write("Pomyślnie rozpakowano archiwum moda");
            }
            catch (Exception ex)
            {
                Write($"ERROR podczas rozpakowywania: {ex.Message}");
                if (InstallPathWriteHelper.IsAccessDeniedException(ex))
                {
                    var blockedPath = InstallPathWriteHelper.TryGetBlockedPathFromException(ex);
                    ShowError(InstallPathWriteHelper.FormatExtractionAccessDeniedMessage(blockedPath, baseDirectory));
                }
                else
                {
                    ShowError($"Błąd podczas rozpakowywania archiwum: {ex.Message}");
                }

                return false;
            }

            ProgressChanged?.Invoke(70, "Kopiowanie plików...");

            string sourcePath = Directory.Exists(Path.Combine(tempExtractPath, "BepInEx"))
                ? tempExtractPath
                : Directory.GetDirectories(tempExtractPath).FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(sourcePath))
            {
                Write("ERROR: Nie znaleziono plików do skopiowania");
                ShowError("Nie znaleziono plików do skopiowania.");
                return false;
            }

            Write($"Kopiuję pliki z {sourcePath} do {gameBasePath}");
            CopyContent(sourcePath, gameBasePath);

            ProgressChanged?.Invoke(90, "Aktualizowanie konfiguracji...");

            var existingConfigs = ConfigManager.LoadConfig();
            var existingConfig = existingConfigs.FirstOrDefault(c => c.Id == modConfig.Id);

            if (existingConfig != null)
            {
                existingConfig.InstallPath = gameBasePath;
                existingConfig.LastUpdated = DateTime.Now;
                Write($"Zaktualizowano konfigurację dla istniejącego moda: {modConfig.ModName}");
            }
            else
            {
                modConfig.InstallPath = gameBasePath;
                existingConfigs.Add(modConfig);
                Write($"Dodano nową konfigurację dla moda: {modConfig.ModName}");
            }

            ConfigManager.SaveConfig(existingConfigs);

            // === NOWY KOD: Stwórz Installation Map ===
            try
            {
                var installationMap = new InstallationMap
                {
                    InstalledAt = DateTime.Now,
                    InstalledBy = $"SUSModder v{GetAppVersion()}",
                    Platform = "epic",
                    FullMod = new FullModInstallation
                    {
                        ModId = modConfig.Id,
                        ModName = modConfig.ModName,
                        ModVersion = modConfig.ModVersion ?? "unknown",
                        AmongVersion = modConfig.AmongVersion ?? "unknown",
                        InstallPath = gameBasePath,
                        InstalledFrom = downloadUrl,
                        LastUpdated = DateTime.Now
                    },
                    InstalledDlls = new List<DllModInstallation>()
                };

                await InstallationMapManager.SaveInstallationMapAsync(gameBasePath, installationMap);
                Write($"[InstallationMap] Zapisano mapę instalacji w: {gameBasePath}");
            }
            catch (Exception ex)
            {
                Write($"[WARNING] Nie udało się zapisać Installation Map: {ex.Message}");
                // Nie przerywamy instalacji jeśli zapis mapy się nie powiódł
            }
            // === KONIEC NOWEGO KODU ===

            ProgressChanged?.Invoke(100, "Instalacja zakończona!");

            // DODAJ DEBUGOWANIE
            Write($"🔍 DEBUG: ModifyEpicAsync kończy się - przed GC.Collect()");
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync COMPLETED for mod: {modConfig.ModName}");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync FULLY COMPLETED for mod: {modConfig.ModName}");
            InstallationCompleted?.Invoke(modConfig);
            return true;
            }
            finally
            {
                ModsStorageCleanupService.TryDeleteInstallTempDirectory(tempDirectory, Write);
            }
        }

        /// <summary>
        /// Finalizuje uwierzytelnienie używając authorization code.
        /// Wywołuje legendary auth --code {authCode}
        /// </summary>
        /// <param name="authCode">Authorization code z Epic OAuth</param>
        /// <returns>True jeśli uwierzytelnienie się powiodło, false w przeciwnym razie</returns>
        private async Task<bool> AuthenticateWithCodeAsync(string authCode)
        {
            try
            {
                Write("Finalizowanie uwierzytelnienia z kodem autoryzacji...");

                // Kod autoryzacji jest sekretem - nie logujemy ani komendy (zawiera kod),
                // ani wyjścia procesu (legendary może je powtórzyć). Dlatego wołamy runnera
                // bezpośrednio, zamiast przez RunLegendaryCommandCoreAsync.
                var result = await _commandRunner($"auth --code {authCode}", null, null);

                if (result.ExitCode == 0)
                {
                    Write("✓ Pomyślnie uwierzytelniono z kodem autoryzacji!");
                    return true;
                }

                Write($"✗ Błąd uwierzytelnienia (exit code: {result.ExitCode})");
                return false;
            }
            catch (Exception)
            {
                Write("Błąd podczas finalizacji uwierzytelnienia.");
                return false;
            }
        }

        /// <summary>
        /// Wyciąga authorization code z różnych formatów wejściowych:
        /// - JSON response od Epic Games
        /// - Redirect URL (https://localhost/launcher/authorized?code=...)
        /// - Bezpośredni kod autoryzacji
        /// </summary>
        private string? ExtractAuthorizationCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim();

            // 1. Spróbuj JSON
            try
            {
                using var jsonDoc = JsonDocument.Parse(input);
                if (jsonDoc.RootElement.TryGetProperty("authorizationCode", out var codeElement))
                {
                    var code = codeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        Write("Wyciągnięto kod autoryzacji z JSON.");
                        return code;
                    }
                }
            }
            catch
            {
                // Nie jest JSON, kontynuuj
            }

            // 2. Spróbuj URL z query parameter ?code=
            if (input.Contains("?code=") || input.Contains("&code="))
            {
                try
                {
                    var codeIndex = input.IndexOf("?code=");
                    if (codeIndex < 0)
                        codeIndex = input.IndexOf("&code=");

                    if (codeIndex >= 0)
                    {
                        var startIndex = input.IndexOf('=', codeIndex) + 1;
                        var endIndex = input.IndexOf('&', startIndex);
                        var code = endIndex > startIndex
                            ? input.Substring(startIndex, endIndex - startIndex)
                            : input.Substring(startIndex);

                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            Write("Wyciągnięto kod autoryzacji z redirect URL.");
                            return code.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Write($"Błąd parsowania URL: {ex.Message}");
                }
            }

            // 3. Użyj bezpośrednio jako kod (usuń whitespace i cudzysłowy)
            var cleanCode = input.Trim().Trim('"', '\'').Trim();
            // Epic authorization codes są zazwyczaj 32-znakowe hex stringi, ale akceptujmy 20-64 znaki alfanumeryczne
            if (cleanCode.Length >= 20 && cleanCode.Length <= 64 &&
                !cleanCode.Contains(" ") && !cleanCode.Contains("\n") && !cleanCode.Contains("\r"))
            {
                // Sprawdź czy to wygląda jak authorization code (alfanumeryczny)
                if (System.Text.RegularExpressions.Regex.IsMatch(cleanCode, @"^[a-zA-Z0-9]+$"))
                {
                    Write("Użyto bezpośredniego kodu autoryzacji.");
                    return cleanCode;
                }
            }

            Write("Nie udało się wyciągnąć kodu autoryzacji.");
            return null;
        }

        /// <summary>
        /// Próbuje uwierzytelnić użytkownika w Epic Games Store.
        /// Kolejność metod:
        /// 1. Import sesji z Epic Games Launcher (automatic) - legendary auth --import
        /// 2. Manual code entry z smart parser (fallback) - legendary auth --disable-webview + user input
        ///    Smart parser automatycznie wyciąga kod z JSON/URL/direct code.
        /// </summary>
        /// <returns>True jeśli uwierzytelnienie się powiodło, false w przeciwnym razie</returns>
        internal async Task<bool> AuthenticateAsync()
        {
            try
            {
                if (IsLegendaryBlocked())
                    return false;

                // === METODA 1: Próba importu sesji z Epic Games Launcher ===
                Write("Próba importu sesji z Epic Games Launcher...");

                // Wyjście procesu nie jest logowane - może zawierać dane sesji.
                var importResult = await _commandRunner("auth --import", null, null);
                string importStderr = string.Join(Environment.NewLine, importResult.StandardError);

                // Jeśli import się powiódł, zakończ
                if (importResult.ExitCode == 0
                    && !importStderr.Contains("ERROR")
                    && !importStderr.Contains("No EGS login session found"))
                {
                    Write("✓ Pomyślnie zaimportowano sesję z Epic Games Launcher");
                    return true;
                }

                // === METODA 2: Dedykowany dialog authentication (UX-friendly) ===
                Write("Import sesji nie powiódł się. Pokazuję dialog logowania...");

                // Używamy tego samego flow co legendary: /id/login?redirectUrl=<encoded>/id/api/redirect
                // (zob. legendary/api/egs.py:get_auth_url() oraz legendary issue #468).
                // Po zalogowaniu NativeWebView ładuje /id/api/redirect który zwraca JSON.
                // Wyciągamy authorizationCode z body przez InvokeScript (JavaScript injection).
                var redirectUrl = Uri.EscapeDataString(
                    "https://www.epicgames.com/id/api/redirect?clientId=34a02cf8f4414e29b15921876da36f9a&responseType=code");
                string oauthUrl = $"https://www.epicgames.com/id/login?redirectUrl={redirectUrl}";

                Write($"OAuth URL: {oauthUrl}");

                // Pokaż dedykowany dialog (async - nie blokuje UI thread)
                string? userInput = await ShowEpicAuthDialogAsync(oauthUrl);

                if (string.IsNullOrWhiteSpace(userInput))
                {
                    Write("Użytkownik anulował wprowadzanie kodu autoryzacji");
                    ShowError("Logowanie do Epic Games zostało anulowane.");
                    return false;
                }

                // Wyciągnij kod z różnych formatów (JSON, URL, bezpośredni kod)
                string? manualCode = ExtractAuthorizationCode(userInput);

                if (string.IsNullOrWhiteSpace(manualCode))
                {
                    Write("Nie udało się wyciągnąć kodu autoryzacji z podanych danych.");
                    ShowError("Nie można wyciągnąć kodu autoryzacji z podanych danych.\n\n" +
                             "Upewnij się, że wkleiłeś:\n" +
                             "- Cały JSON z przeglądarki\n" +
                             "- Lub redirect URL\n" +
                             "- Lub sam kod autoryzacji (20-64 znaki alfanumeryczne)");
                    return false;
                }

                // Użyj kodu do uwierzytelnienia
                Write("Użyto kodu autoryzacji z manual entry.");
                var authResult = await AuthenticateWithCodeAsync(manualCode);

                if (authResult)
                {
                    Write("✓ Uwierzytelnienie ręczne zakończone sukcesem!");
                    return true;
                }
                else
                {
                    ShowError("Nie udało się uwierzytelnić z podanym kodem.\n\n" +
                             "Spróbuj ponownie lub sprawdź czy kod jest poprawny.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Write($"Błąd podczas uwierzytelniania: {ex.Message}");
                ShowError($"Wystąpił błąd podczas logowania do Epic Games:\n{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wylogowuje użytkownika z Epic Games Store (legendary auth --delete).
        /// Usuwa zapisane dane uwierzytelniania.
        /// </summary>
        /// <returns>True jeśli wylogowanie się powiodło, false w przeciwnym razie</returns>
        public async Task<bool> LogoutAsync()
        {
            try
            {
                if (IsLegendaryBlocked())
                    return false;

                if (!File.Exists(legendaryPath))
                {
                    Write("Legendary.exe nie znaleziony.");
                    ShowError("Legendary.exe nie został znaleziony. Nie można wylogować.");
                    return false;
                }

                Write("Wylogowywanie z Epic Games Store...");

                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "auth --delete",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var tracked = await StartTrackedLegendaryAsync(psi);
                var process = tracked?.Process;
                if (process == null)
                {
                    Write("Nie udało się uruchomić legendary.exe");
                    ShowError("Nie udało się uruchomić procesu wylogowywania.");
                    return false;
                }

                var stderr = await process.StandardError.ReadToEndAsync();
                var stdout = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    Write("✓ Pomyślnie wylogowano z Epic Games Store");
                    return true;
                }
                else
                {
                    Write($"✗ Błąd wylogowywania: {stderr}");
                    // Nawet jeśli zwróci błąd, często działa (np. gdy już byliśmy wylogowani)
                    if (stderr.Contains("No saved credentials") || stderr.Contains("not logged in"))
                    {
                        Write("Użytkownik nie był zalogowany - traktowane jako sukces");
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Write($"Błąd podczas wylogowywania: {ex.Message}");
                ShowError($"Wystąpił błąd podczas wylogowywania z Epic Games:\n{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loguje użytkownika do Epic Games Store.
        /// Publiczna wersja AuthenticateAsync do wywołania z UI.
        /// </summary>
        /// <returns>True jeśli logowanie się powiodło, false w przeciwnym razie</returns>
        public async Task<bool> LoginAsync()
        {
            if (!File.Exists(legendaryPath))
            {
                Write("Legendary.exe nie znaleziony.");
                ShowError("Legendary.exe nie został znaleziony. Nie można zalogować.");
                return false;
            }

            return await AuthenticateAsync();
        }

        /// <summary>
        /// Upewnia się, że legendary.exe istnieje – pobiera go jeśli nie ma.
        /// </summary>
        /// <remarks>
        /// Gdy pliku brak, pobranie jest blokujące (potrzebne przed sprawdzeniem sesji Epic).
        /// Gdy plik istnieje, weryfikacja SHA-256 i ewentualna podmiana idą w tle —
        /// nie blokują startu aplikacji. Serwis respektuje ręczny wybór wydania:
        /// przypięta wersja nigdy nie nadpisuje ręcznie wybranego.
        /// </remarks>
        public async Task EnsureLegendaryExistsAsync()
        {
            var provider = _versionProvider ??= CreateDefaultVersionProvider();
            if (provider == null)
            {
                if (!File.Exists(legendaryPath))
                {
                    Write("Pobieranie legendary.exe...");
                    await DownloadLegendaryAsync();
                }

                return;
            }

            if (File.Exists(legendaryPath))
            {
                // Plik istnieje — wymiana w tle jest zadaniem aplikacji (po potwierdzeniu trybu Epic),
                // więc tutaj niczego nie planujemy. Dzięki temu nie ma podwójnych harmonogramów.
                return;
            }

            // Bez binarki nie ma sesji — pobranie musi zakończyć się przed startem.
            var missing = await provider.EnsureInstalledAsync();
            if (missing.Success)
            {
                Write($"[Legendary] legendary.exe pobrany ({missing.Installed?.DisplayName}).");
                return;
            }

            LastFailureCode = DownloadVerificationCodes.ToolDownloadFailed;
            Write($"[Legendary] Nie udało się pobrać legendary.exe ({missing.ErrorCode}).");
        }
        /// <summary>
        /// Ścieżka narzędzia legendary.exe używanego przez aplikację.
        /// </summary>
        public string LegendaryToolPath => legendaryPath;

        /// <summary>
        /// Czy ścieżka wskazuje na właściwe narzędzie legendary.exe.
        /// </summary>
        private bool IsLegendaryToolPath(string executablePath) =>
            string.Equals(executablePath, legendaryPath, StringComparison.OrdinalIgnoreCase);

        private async Task<TrackedLegendaryProcess?> StartTrackedLegendaryAsync(ProcessStartInfo startInfo)
        {
            using var gate = await LegendaryOperationGate.AcquireAsync(CancellationToken.None);
            if (IsLegendaryBlocked() || !await EnsureToolIsRunnableAsync())
                return null;

            var process = Process.Start(startInfo);
            if (process == null)
                return null;

            LegendaryOperationGate.RegisterOperationStart();
            return new TrackedLegendaryProcess(process);
        }

        private sealed class TrackedLegendaryProcess : IDisposable
        {
            public Process Process { get; }
            public TrackedLegendaryProcess(Process process)
            {
                Process = process;
            }

            public void Dispose() => _ = FinishAsync();

            private async Task FinishAsync()
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        LegendaryProcessGuard.MarkUnfinished();
                        await Process.WaitForExitAsync();
                    }

                    LegendaryOperationGate.RegisterOperationEnd();
                    Process.Dispose();
                }
                catch
                {
                    // Bez potwierdzenia końca procesu licznik pozostaje aktywny (fail-closed).
                    LegendaryProcessGuard.MarkUnfinished();
                }
            }
        }

        /// <summary>Weryfikuje zaufanie przed startem; naprawę planuje aplikacja po wyborze trybu Epic.</summary>
        private async Task<bool> EnsureToolIsRunnableAsync(CancellationToken ct = default)
        {
            if (!File.Exists(legendaryPath))
            {
                Write("[Legendary] Brak legendary.exe — nie uruchamiam.");
                return false;
            }

            var provider = _versionProvider ??= CreateDefaultVersionProvider();
            if (provider == null)
            {
                // Brak serwisa wersji — nie mamy jak ocenić zaufania, więc nie uruchamiamy.
                Write("[Legendary] Brak serwisa wersji narzędzia — nie uruchamiam legendary.exe.");
                return false;
            }

            var inspection = await provider.InspectInstalledAsync(ct);

            switch (inspection.Trust)
            {
                case LegendaryToolTrust.Trusted:
                    return true;

                case LegendaryToolTrust.TrustedButNotSelected:
                    // Binarka SUSModder, ale inna niż wybrana — uruchomić można,
                    // wymiana na właściwą wersję nastąpi w tle (planowana przez aplikację).
                    Write("[Legendary] Zainstalowana wersja to inna niż wybrana — uruchamiam.");
                    return true;

                default:
                    // Nieznana suma albo brak pliku — nie uruchamiamy obcego pliku.
                    Write("[Legendary] Zainstalowany plik nie jest zaufany — nie uruchamiam go.");
                    return false;
            }
        }

        /// <summary>
        /// Domyślny serwis wersji narzędzia (z ustawieniami aplikacji).
        /// </summary>
        private static ILegendaryVersionProvider? CreateDefaultVersionProvider()
        {
            try
            {
                return new LegendaryVersionService();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Legendary] Nie udało się utworzyć serwisa wersji: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sprawdza czy użytkownik jest aktualnie zalogowany do Epic Games Store
        /// bez otwierania żadnych dialogów.
        /// Używa legendary status --json do weryfikacji sesji.
        /// </summary>
        /// <returns>True jeśli użytkownik jest zalogowany, false w przeciwnym razie</returns>
        public async Task<bool> CheckAuthStatusAsync()
        {
            try
            {
                if (IsLegendaryBlocked())
                    return false;

                if (!File.Exists(legendaryPath))
                {
                    Write("Legendary.exe nie znaleziony – nie można sprawdzić statusu auth.");
                    return false;
                }

                Write("Sprawdzanie statusu logowania do Epic Games...");

                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "status --json",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var tracked = await StartTrackedLegendaryAsync(psi);
                var process = tracked?.Process;
                if (process == null)
                {
                    Write("Nie udało się uruchomić legendary.exe do sprawdzenia statusu.");
                    return false;
                }

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Próba parsowania JSON z legendaryStatus
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(stdout);
                        var root = jsonDoc.RootElement;

                        // legendary status --json zwraca pole "account" gdy zalogowany
                        if (root.TryGetProperty("account", out var accountElem))
                        {
                            var account = accountElem.GetString();
                            if (!string.IsNullOrWhiteSpace(account))
                            {
                                var normalized = account.Trim().ToLowerInvariant();

                                // legendary bywa niespójne i potrafi zwrócić np. "<not logged in>".
                                // Traktuj takie wartości jako BRAK sesji.
                                if (normalized is "none" or "<not logged in>" or "not logged in" or "null" or "n/a")
                                {
                                    Write("✗ Użytkownik niezalogowany (account placeholder).");
                                    return false;
                                }

                                Write($"✓ Zalogowany jako: {account}");
                                return true;
                            }
                        }

                        // Pole "logged_in" (alternatywny format)
                        if (root.TryGetProperty("logged_in", out var loggedInElem))
                        {
                            bool loggedIn = loggedInElem.GetBoolean();
                            Write(loggedIn ? "✓ Użytkownik zalogowany (logged_in=true)" : "✗ Użytkownik niezalogowany (logged_in=false)");
                            return loggedIn;
                        }
                    }
                    catch
                    {
                        // JSON parse failed – sprawdź przez tekst
                    }
                }

                // Fallback: sprawdź przez stderr i exit code
                // legendary status wypisuje "Epic account: <name>" lub "Epic account: None"
                var combined = stdout + stderr;
                if (combined.Contains("Epic account: None") ||
                    combined.Contains("<not logged in>", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Not logged in") ||
                    combined.Contains("No saved credentials") ||
                    combined.Contains("not logged"))
                {
                    Write("✗ Użytkownik nie jest zalogowany do Epic Games.");
                    return false;
                }

                if (process.ExitCode == 0 && (combined.Contains("Epic account:") || combined.Contains("account_id:")))
                {
                    Write("✓ Użytkownik jest zalogowany do Epic Games.");
                    return true;
                }

                // Dodatkowy fallback: próba legendary auth --import (cicha weryfikacja)
                Write("Fallback: próba weryfikacji przez auth --import...");

                var importPsi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "auth --import",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var trackedImport = await StartTrackedLegendaryAsync(importPsi);
                var importProcess = trackedImport?.Process;
                if (importProcess != null)
                {
                    var importStderr = await importProcess.StandardError.ReadToEndAsync();
                    var importStdout = await importProcess.StandardOutput.ReadToEndAsync();
                    await importProcess.WaitForExitAsync();

                    if (importProcess.ExitCode == 0 &&
                        !importStderr.Contains("ERROR") &&
                        !importStderr.Contains("No EGS login session found"))
                    {
                        Write("✓ Sesja zaimportowana pomyślnie.");
                        return true;
                    }

                    // Jeśli import mówi "already logged in" to też OK
                    if (importStderr.Contains("already") || importStdout.Contains("already"))
                    {
                        Write("✓ Już zalogowany (import: already logged in).");
                        return true;
                    }
                }

                Write("✗ Nie można zweryfikować statusu logowania.");
                return false;
            }
            catch (Exception ex)
            {
                Write($"Błąd podczas sprawdzania statusu auth: {ex.Message}");
                return false;
            }
        }

        public async Task HandleEpicGameAsync(ModConfiguration modConfig)
        {
            if (modConfig == null || string.IsNullOrEmpty(modConfig.AmongVersion))
            {
                Write("Konfiguracja gry jest nieprawidłowa.");
                return;
            }

            // Blokada po niepewnym timeoutcie - sprawdzamy na samym wejściu, przed uwierzytelnieniem
            // i przed jakimikolwiek operacjami na plikach gry.
            if (IsLegendaryBlocked())
            {
                ReportEpicFailure(modConfig,
                    "BŁĄD: poprzedni proces legendary mógł nie zakończyć się - restart aplikacji wymagany.");
                return;
            }

            if (!File.Exists(legendaryPath))
            {
                // Ręcznie wybrane wydanie ma pierwszeństwo — przypięta wersja go nie nadpisuje.
                var provider = _versionProvider ??= CreateDefaultVersionProvider();
                if (provider != null)
                {
                    var prepared = await provider.EnsureInstalledAsync();
                    if (!prepared.Success)
                    {
                        LastFailureCode = DownloadVerificationCodes.ToolDownloadFailed;
                        Write($"[Legendary] Nie udało się pobrać legendary.exe ({prepared.ErrorCode}).");
                        ReportEpicFailure(modConfig, "Nie udało się pobrać legendary.exe.");
                        return;
                    }
                }
                else
                {
                    await DownloadLegendaryAsync();
                }
            }

            // Sprawdź czy użytkownik jest już zalogowany (sesja persisted przez legendary).
            // Jeśli tak – pomijamy AuthenticateAsync, unikając zbędnego auth --import
            // który może pokazywać WebView przy każdym odpaleniu moda.
            bool isLoggedIn = await CheckAuthStatusAsync();
            if (!isLoggedIn)
            {
                Write("Brak aktywnej sesji Epic Games – próba uwierzytelnienia...");

                // Uwierzytelnij użytkownika (spróbuj import, jeśli nie uda się - WebView dialog)
                bool authenticated = await AuthenticateAsync();
                if (!authenticated)
                {
                    Write("Nie udało się uwierzytelnić użytkownika. Przerywam operację.");
                    return;
                }
            }

            string installDirectory;
            int lastLaunchId = GetLastLaunchId();
            Write($"Epic launch: modId={modConfig.Id}, lastLaunchId={lastLaunchId}, installPath={modConfig.InstallPath}");

            if (modConfig.Id == lastLaunchId)
            {
                // Sprawdź czy InstallPath nie jest null
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    ShowError("Ścieżka instalacji moda jest nieprawidłowa.");
                    return;
                }

                installDirectory = ResolveEpicBaseInstallDirectory(modConfig);

                try
                {
                    // Resetuj licznik retry na początku nowej sesji uruchomienia
                    _retryCount = 0;
                    _hasLaunchError = false;

                    // Weryfikuj czy pliki gry faktycznie istnieją przed importem
                    string gameExePath = ResolveEpicGameExePath(installDirectory);
                    if (!File.Exists(gameExePath))
                    {
                        Write($"OSTRZEŻENIE: Plik gry nie istnieje w {installDirectory}");
                        Write("Wymuszanie pełnej reinstalacji zamiast importu...");
                        
                        // Wymuś pełną reinstalację zamiast importu
                        _retryCount++;
                        await PerformReinstallationSequence(modConfig);
                        return;
                    }

                    EnsureEpicPayloadInsideGameDirectory(installDirectory);

                    await RunLegendaryCommandAsync($"import 963137e4c29d4c79a81323b8fab03a40 \"{installDirectory}\" -y");
                    await LaunchGameAsync();

                    // Jeśli wystąpił błąd uruchamiania i nie przekroczyliśmy limitu prób
                    if (_hasLaunchError && !_isRetryingInstallation && _retryCount < MaxRetryAttempts)
                    {
                        Write($"Wykryto błąd uruchamiania - próba automatycznej reinstalacji ({_retryCount + 1}/{MaxRetryAttempts})...");
                        _isRetryingInstallation = true;
                        _retryCount++;

                        // Wykonaj sekwencję reinstalacji
                        await PerformReinstallationSequence(modConfig);

                        _isRetryingInstallation = false;
                    }
                    else if (_hasLaunchError && _retryCount >= MaxRetryAttempts)
                    {
                        Write($"Przekroczono limit prób reinstalacji ({MaxRetryAttempts}). Zgłaszanie błędu...");
                        string logContent = GetLegendaryLogContent();
                        EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                    }
                }
                catch (Exception ex)
                {
                    Write($"Błąd podczas uruchamiania: {ex.Message}");

                    // Jeśli nie przekroczyliśmy limitu prób reinstalacji
                    if (!_isRetryingInstallation && _retryCount < MaxRetryAttempts)
                    {
                        Write($"Próba automatycznej reinstalacji po błędzie ({_retryCount + 1}/{MaxRetryAttempts})...");
                        _isRetryingInstallation = true;
                        _retryCount++;

                        try
                        {
                            await PerformReinstallationSequence(modConfig);
                        }
                        catch (Exception reinstallEx)
                        {
                            Write($"Błąd podczas reinstalacji: {reinstallEx.Message}");
                            
                            // Jeśli przekroczyliśmy limit, zgłoś błąd
                            if (_retryCount >= MaxRetryAttempts)
                            {
                                Write($"Przekroczono limit prób reinstalacji ({MaxRetryAttempts}). Zgłaszanie błędu...");
                                string logContent = GetLegendaryLogContent();
                                EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                            }
                        }
                        finally
                        {
                            _isRetryingInstallation = false;
                        }
                    }
                    else if (_retryCount >= MaxRetryAttempts)
                    {
                        Write($"Przekroczono limit prób ({MaxRetryAttempts}). Zgłaszanie błędu...");
                        string logContent = GetLegendaryLogContent();
                        EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                    }
                }

                return;
            }

            bool installed = await PerformEpicInstallFlowAsync(modConfig);
            if (installed)
                SaveLastLaunchId(modConfig.Id);
        }

        /// <summary>
        /// Instalacja gry (vanilla lub moda) przez legendary: import katalogu, instalacja
        /// z przypiętym manifestem, destrukcyjny uninstall i start gry.
        /// </summary>
        /// <remarks>
        /// Wydzielone z <see cref="HandleEpicGameAsync"/>, bo ten etap wymaga zalogowania
        /// do Epic Games i realnego legendary - dzięki wydzieleniu da się go przetestować
        /// na symulowanym runnerze.
        /// </remarks>
        /// <returns>true gdy instalacja i start przeszły pomyślnie.</returns>
        internal async Task<bool> PerformEpicInstallFlowAsync(ModConfiguration modConfig)
        {
            if (modConfig.Id == 0 && string.IsNullOrEmpty(modConfig.InstallPath))
            {
                Write("Ścieżka instalacji dla Vanilla Among Us jest nieprawidłowa.");
                return false;
            }

            string installDirectory = ResolveEpicBaseInstallDirectory(modConfig);

            // Uninstall usuwa wpis z installed.json wraz z zapisanymi tam adresami CDN.
            // Gdy adresy są tylko w installed.json (w metadanych ich nie ma), przerywamy przed usunięciem.
            // Dotyczy ścieżki moda - vanilla instaluje się z CDN i nie potrzebuje base_urls.
            if (modConfig.Id != 0
                && LegendaryBaseUrlState.HasBaseUrlsOnlyInInstalledJson(legendaryConfigDirectory, EpicAppId))
            {
                Write("[BaseUrls] Adresy CDN istnieją tylko w installed.json, który uninstall usunie - operacja przerwana.");
                ReportEpicFailure(modConfig,
                    "BŁĄD: adresy CDN istnieją tylko w installed.json - operacja przerwana przed usunięciem.");
                return false;
            }

            await RunLegendaryCommandAsync("uninstall 963137e4c29d4c79a81323b8fab03a40 --keep-files -y");

            // uninstall --keep-files musi usunąć wpis z installed.json - inaczej destrukcyjny
            // uninstall poniżej mógłby skasować pliki nieznanej instalacji.
            if (!IsRegistrationRemoved())
            {
                ReportEpicFailure(modConfig,
                    "BŁĄD: wpis instalacji w legendary nie zniknął po uninstall --keep-files - operacja przerwana.");
                return false;
            }

            // Odświeżenie base_urls wykonujemy po odinstalowaniu (bez usuwania plików), gdy gra nie jest
            // już zarejestrowana w legendary - wtedy import nie dotyka istniejącej instalacji użytkownika.
            // Gdy proces odświeżania może jeszcze żyć, przerywamy przed dalszymi krokami.
            if (modConfig.Id != 0 && await EnsureLegendaryBaseUrlsAsync() == BaseUrlState.Uncertain)
            {
                ReportEpicFailure(modConfig,
                    "BŁĄD: odświeżanie base_urls przekroczyło limit czasu - instalacja moda przerwana.");
                return false;
            }

            await RunLegendaryCommandAsync($"import 963137e4c29d4c79a81323b8fab03a40 \"{installDirectory}\" -y");

            // Import rejestruje instalację tylko wtedy, gdy katalog zawiera pliki gry.
            // Świeża instalacja (katalog pusty) zostawia więc brak wpisu - to nie jest błąd,
            // przechodzimy do instalacji. Obca ścieżka i niepewny stan blokują operację.
            var importStatus = CheckRegisteredInstallPath(installDirectory);
            if (importStatus == RegisteredInstallPathStatus.Unknown)
            {
                Write("[Preflight] Nie udało się ustalić stanu instalacji w legendary - operacja przerwana.");
                ReportEpicFailure(modConfig,
                    "BŁĄD: nie udało się ustalić stanu instalacji w legendary - operacja przerwana.");
                return false;
            }

            if (importStatus == RegisteredInstallPathStatus.Mismatch)
            {
                Write("[Preflight] Zarejestrowana ścieżka instalacji wskazuje na inny katalog - operacja przerwana.");
                ReportEpicFailure(modConfig,
                    "BŁĄD: legendary zarejestrował inną ścieżkę instalacji - operacja przerwana.");
                return false;
            }

            if (importStatus == RegisteredInstallPathStatus.Missing)
            {
                Write("[Preflight] Brak wpisu instalacji w legendary (świeża instalacja) - przechodzę do instalacji.");
            }

            string amongVersionFormatted = modConfig.AmongVersion?.Replace("-", ".") ?? string.Empty;

            // Manifest musi istnieć przed pierwszą próbą instalacji z --manifest (fallback poniżej).
            // Bez niego nie wolno odinstalowywać gry - pobranie musi zakończyć się sukcesem.
            if (modConfig.Id != 0 && !await DownloadManifestAsync(amongVersionFormatted))
            {
                ReportEpicFailure(modConfig,
                    "BŁĄD: nie udało się pobrać manifestu gry - operacja przerwana.");
                return false;
            }

            if (importStatus == RegisteredInstallPathStatus.Missing)
            {
                if (!await InstallGameAsync(modConfig, amongVersionFormatted))
                {
                    ReportEpicFailure(modConfig, "BŁĄD: instalacja gry nie powiodła się - operacja przerwana.");
                    return false;
                }
                if (CheckRegisteredInstallPath(installDirectory) != RegisteredInstallPathStatus.Matches)
                {
                    ReportEpicFailure(modConfig, "BŁĄD: nie udało się potwierdzić ścieżki instalacji gry.");
                    return false;
                }
                await LaunchGameAsync();
                return true;
            }

            // Preflight przed destrukcyjnym uninstall: upewniamy się, że zarejestrowana
            // w legendary ścieżka instalacji to nasz katalog gry.
            var preflightStatus = CheckRegisteredInstallPath(installDirectory);
            if (preflightStatus == RegisteredInstallPathStatus.Mismatch
                || preflightStatus == RegisteredInstallPathStatus.Unknown)
            {
                Write("[Preflight] Zarejestrowana ścieżka instalacji nie zgadza się z katalogiem gry - operacja przerwana.");
                ReportEpicFailure(modConfig,
                    "BŁĄD: ścieżka instalacji w legendary nie zgadza się z katalogiem gry - operacja przerwana.");
                return false;
            }

            // Destrukcyjny uninstall kasuje pliki gry i wpis z installed.json wraz z adresami CDN.
            // Bez pewnych adresów w metadanych gra nie da się odtworzyć - przerywamy (fail closed).
            if (preflightStatus == RegisteredInstallPathStatus.Matches && modConfig.Id != 0 && !HasRecoverableBaseUrls())
            {
                Write("[BaseUrls] Brak adresów CDN w metadanych legendary - destrukcyjny uninstall przerwany.");
                ReportEpicFailure(modConfig,
                    "BŁĄD: brak adresów CDN w metadanych legendary - destrukcyjna instalacja przerwana.");
                return false;
            }

            if (preflightStatus == RegisteredInstallPathStatus.Matches)
            {
                await UninstallGameAsync();
            }
            else
            {
                // Brak wpisu w legendary - nie ma czego kasować, przechodzimy do instalacji.
                Write("[Preflight] Brak wpisu instalacji w legendary - pomijam uninstall i przechodzę do instalacji.");
            }

            if (modConfig.Id == 0)
            {
                bool vanillaInstalled = await InstallGameAsync(modConfig, amongVersionFormatted);
                if (!vanillaInstalled)
                    Write("OSTRZEŻENIE: instalacja vanilla zakończyła się błędem - uruchamiam grę zgodnie z dotychczasowym przebiegiem.");
                await LaunchGameAsync();
            }
            else
            {
                if (!await InstallGameAsync(modConfig, amongVersionFormatted))
                {
                    ReportEpicFailure(modConfig, "BŁĄD: instalacja gry nie powiodła się - operacja przerwana.");
                    return false;
                }
                await LaunchGameAsync();
            }

            return true;
        }

        internal async Task<bool> PerformReinstallationSequence(ModConfiguration modConfig)
        {
            try
            {
                Write($"Rozpoczynam automatyczną reinstalację dla moda: {modConfig.ModName} (próba {_retryCount}/{MaxRetryAttempts})");

                string amongVersionFormatted = modConfig.AmongVersion?.Replace("-", ".") ?? string.Empty;

                // Wykonaj sekwencję reinstalacji
                if (modConfig.Id != 0) // Dla modów (nie vanilla)
                {
                    Write("Pobieranie manifestu...");

                    // Bez manifestu nie wolno odinstalowywać gry - pobranie musi zakończyć się sukcesem.
                    if (!await DownloadManifestAsync(amongVersionFormatted))
                    {
                        Write("BŁĄD: nie udało się pobrać manifestu - reinstalacja przerwana.");
                        ReportEpicFailure(modConfig,
                            "BŁĄD: nie udało się pobrać manifestu - reinstalacja przerwana.");
                        return false;
                    }

                    // Odświeżenie base_urls przed destrukcyjnym uninstallem.
                    if (await EnsureLegendaryBaseUrlsAsync() == BaseUrlState.Uncertain)
                    {
                        Write("BŁĄD: odświeżanie base_urls przekroczyło limit czasu - reinstalacja przerwana.");
                        return false;
                    }
                }

                Write("Odinstalowywanie gry...");

                // Preflight przed destrukcyjnym uninstall: upewniamy się, że zarejestrowana
                // w legendary ścieżka instalacji to nasz katalog gry. Brak wpisu (świeża
                // instalacja) nie jest błędem - wtedy pomijamy uninstall i instalujemy.
                var registrationStatus = CheckRegisteredInstallPath(ResolveEpicBaseInstallDirectory(modConfig));
                if (registrationStatus == RegisteredInstallPathStatus.Unknown)
                {
                    Write("[Preflight] Nie udało się ustalić stanu instalacji w legendary - reinstalacja przerwana.");
                    ReportEpicFailure(modConfig,
                        "BŁĄD: nie udało się ustalić stanu instalacji w legendary - reinstalacja przerwana.");
                    return false;
                }

                if (registrationStatus == RegisteredInstallPathStatus.Mismatch)
                {
                    Write("[Preflight] Zarejestrowana ścieżka instalacji wskazuje na inny katalog - reinstalacja przerwana.");
                    ReportEpicFailure(modConfig,
                        "BŁĄD: ścieżka instalacji w legendary nie zgadza się z katalogiem gry - reinstalacja przerwana.");
                    return false;
                }

                // Destrukcyjny uninstall kasuje pliki gry i wpis z installed.json wraz z adresami CDN.
                // Bez pewnych adresów w metadanych gra nie da się odtworzyć - przerywamy (fail closed).
                if (registrationStatus == RegisteredInstallPathStatus.Matches && modConfig.Id != 0 && !HasRecoverableBaseUrls())
                {
                    Write("[BaseUrls] Brak adresów CDN w metadanych legendary - destrukcyjny uninstall przerwany.");
                    ReportEpicFailure(modConfig,
                        "BŁĄD: brak adresów CDN w metadanych legendary - reinstalacja przerwana.");
                    return false;
                }

                if (registrationStatus == RegisteredInstallPathStatus.Matches)
                {
                    await UninstallGameAsync();
                }
                else
                {
                    // Brak wpisu w legendary - nie ma czego kasować, przechodzimy do instalacji.
                    Write("[Preflight] Brak wpisu instalacji w legendary - pomijam uninstall i przechodzę do instalacji.");
                }

                Write("Instalowanie gry...");
                bool gameInstalled = await InstallGameAsync(modConfig, amongVersionFormatted);
                if (!gameInstalled && modConfig.Id != 0)
                {
                    Write("BŁĄD: instalacja nie powiodła się - reinstalacja przerwana.");
                    ReportEpicFailure(modConfig, "BŁĄD: instalacja gry nie powiodła się - reinstalacja przerwana.");
                    return false;
                }

                // Weryfikuj czy instalacja się powiodła przed launchem
                string installDirectory;
                if (modConfig.Id == 0)
                {
                    installDirectory = ResolveEpicBaseInstallDirectory(modConfig);
                }
                else
                {
                    installDirectory = ResolveEpicBaseInstallDirectory(modConfig);
                }
                
                string gameExePath = ResolveEpicGameExePath(installDirectory);
                if (!File.Exists(gameExePath))
                {
                    Write($"BŁĄD: Instalacja nie powiodła się - brak pliku gry: {gameExePath}");
                    _hasLaunchError = true;
                    
                    // Jeśli przekroczyliśmy limit, zgłoś błąd
                    if (_retryCount >= MaxRetryAttempts)
                    {
                        string logContent = GetLegendaryLogContent();
                        EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                    }
                    return false;
                }

                Write("Uruchamianie gry po reinstalacji...");
                // Resetuj flagę błędu przed ponowną próbą uruchomienia
                _hasLaunchError = false;
                await LaunchGameAsync();

                // Sprawdź czy tym razem uruchomienie się powiodło
                if (!_hasLaunchError)
                {
                    Write("Automatyczna reinstalacja zakończona pomyślnie!");
                    return true;
                }
                else
                {
                    Write("Automatyczna reinstalacja nie rozwiązała problemu.");
                    
                    // Jeśli to nie była ostatnia próba, spróbuj jeszcze raz
                    if (_retryCount < MaxRetryAttempts)
                    {
                        Write($"Ponawiam próbę... ({_retryCount + 1}/{MaxRetryAttempts})");
                        _retryCount++;
                        return await PerformReinstallationSequence(modConfig);
                    }
                    else
                    {
                        // Zgłoś błąd do frontu
                        string logContent = GetLegendaryLogContent();
                        EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Write($"Błąd podczas automatycznej reinstalacji: {ex.Message}");
                
                // Jeśli przekroczyliśmy limit prób, zgłoś błąd
                if (_retryCount >= MaxRetryAttempts)
                {
                    string logContent = GetLegendaryLogContent();
                    EpicLaunchError?.Invoke(modConfig.ModName, logContent);
                }
                
                throw; // Przekaż wyjątek wyżej
            }
        }

        /// <summary>
        /// Pobiera manifest gry do katalogu manifestów. Manifest trafia do unikalnego pliku
        /// tymczasowego w tym samym katalogu i jest przenoszony na ścieżkę docelową dopiero
        /// po pełnym, niepustym pobraniu. Przy niepowodzeniu nie zostaje ani plik częściowy,
        /// ani stary manifest (mógłby pochodzić z innej wersji gry).
        /// </summary>
        /// <returns>true gdy manifest został pobrany.</returns>
        protected internal virtual async Task<bool> DownloadManifestAsync(string amongVersionFormatted)
        {
            if (string.IsNullOrWhiteSpace(amongVersionFormatted))
            {
                Write("Niepoprawna wersja Among Us.");
                return false;
            }

            string manifestUrl = $"https://github.com/whichtwix/Data/raw/master/epic/manifests/{EpicAppId}_{amongVersionFormatted}.manifest";
            string manifestPath = Path.Combine(manifestDirectory, $"{EpicAppId}_{amongVersionFormatted}.manifest");
            string tempPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            bool saved = false;
            try
            {
                if (!await DownloadFileAsync(manifestUrl, tempPath))
                {
                    Write($"Nie udało się pobrać manifestu: {manifestUrl}");
                    return false;
                }

                var info = new FileInfo(tempPath);
                if (!info.Exists || info.Length == 0)
                {
                    Write("Pobrany manifest jest pusty - odrzucam go.");
                    return false;
                }

                File.Move(tempPath, manifestPath, overwrite: true);
                saved = true;
                return true;
            }
            catch (Exception ex)
            {
                Write($"Wystąpił błąd podczas pobierania manifestu: {ex.Message}.");
                return false;
            }
            finally
            {
                TryDeleteFile(tempPath);

                if (!saved)
                {
                    // Stary manifest może pochodzić z innej wersji gry - nie wolno go użyć
                    // przy instalacji, bo doprowadziłoby do błędnej wersji plików.
                    TryDeleteFile(manifestPath);
                }
            }
        }

        public async Task UninstallGameAsync()
        {
            // --skip-uninstaller: odinstalownik Epic Games może kasować pliki poza katalogiem
            // instalacji albo wymagać interakcji - usuwamy wyłącznie to, co zarejestrował legendary.
            string commandArguments = $"uninstall {EpicAppId} -y --skip-uninstaller";
            await RunLegendaryCommandAsync(commandArguments);
        }

        /// <summary>
        /// Czy wpis instalacji zniknął z <c>installed.json</c> (po uninstall --keep-files).
        /// </summary>
        private bool IsRegistrationRemoved()
        {
            var state = LegendaryBaseUrlState.GetRegisteredInstallPath(legendaryConfigDirectory, EpicAppId, out _);
            if (state == LegendaryInstallPathState.Missing)
                return true;

            Write($"[Preflight] Wpis instalacji w legendary nadal istnieje (stan: {state}) - operacja przerwana.");
            return false;
        }

        /// <summary>
        /// Stan zarejestrowanej w legendary ścieżki instalacji względem oczekiwanego katalogu gry.
        /// </summary>
        private enum RegisteredInstallPathStatus
        {
            /// <summary>Zarejestrowana ścieżka wskazuje na oczekiwany katalog gry.</summary>
            Matches,

            /// <summary>Brak wpisu instalacji w legendary (świeża instalacja).</summary>
            Missing,

            /// <summary>Zarejestrowana ścieżka wskazuje na inny katalog.</summary>
            Mismatch,

            /// <summary>Stan nieznany - pliku nie da się odczytać albo JSON jest uszkodzony.</summary>
            Unknown
        }

        /// <summary>
        /// Porównuje zarejestrowaną w legendary ścieżkę instalacji z oczekiwanym katalogiem gry.
        /// Katalogi porównujemy po normalizacji (pełna ścieżka, bez rozróżniania wielkości liter).
        /// Dopuszczamy też ścieżkę wskazującą podkatalog "AmongUs".
        /// </summary>
        /// <remarks>
        /// Fail-closed: nieczytelny plik i uszkodzony JSON zwracają <see cref="RegisteredInstallPathStatus.Unknown"/>,
        /// a obca ścieżka <see cref="RegisteredInstallPathStatus.Mismatch"/> - destrukcyjny uninstall
        /// kasuje pliki w katalogu rejestracji, więc w tych stanach nie wolno kasować.
        /// </remarks>
        private RegisteredInstallPathStatus CheckRegisteredInstallPath(string expectedDirectory)
        {
            var state = LegendaryBaseUrlState.GetRegisteredInstallPath(
                legendaryConfigDirectory,
                EpicAppId,
                out string? registeredPath);

            switch (state)
            {
                case LegendaryInstallPathState.Missing:
                    return RegisteredInstallPathStatus.Missing;

                case LegendaryInstallPathState.Unknown:
                    return RegisteredInstallPathStatus.Unknown;

                default:
                    return AreSameGameDirectory(registeredPath, expectedDirectory)
                        ? RegisteredInstallPathStatus.Matches
                        : RegisteredInstallPathStatus.Mismatch;
            }
        }

        /// <summary>
        /// Czy adresy CDN przetrwają destrukcyjny uninstall - uninstall usuwa wpis z
        /// <c>installed.json</c> wraz z zapisanymi tam adresami, więc odtworzyć instalację
        /// można tylko wtedy, gdy adresy są w metadanych gry.
        /// </summary>
        private bool HasRecoverableBaseUrls()
            => LegendaryBaseUrlState.HasBaseUrlsInMetadata(legendaryConfigDirectory, EpicAppId);

        private static bool AreSameGameDirectory(string? registeredPath, string? expectedDirectory)
        {
            if (string.IsNullOrWhiteSpace(registeredPath) || string.IsNullOrWhiteSpace(expectedDirectory))
                return false;

            string left = NormalizeDirectoryPath(registeredPath);
            string right = NormalizeDirectoryPath(expectedDirectory);

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            // legendary rejestruje katalog gry - dopuszczamy ścieżkę wskazującą podkatalog "AmongUs".
            return string.Equals(left, Path.Combine(right, "AmongUs"), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.Combine(left, "AmongUs"), right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                full = path;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ResolveEpicBaseInstallDirectory(ModConfiguration modConfig)
        {
            if (!string.IsNullOrWhiteSpace(modConfig.InstallPath))
            {
                return TrimEpicGameSubdirectory(modConfig.InstallPath);
            }

            return Path.Combine(PathSettings.ModsInstallPath, modConfig.ModName ?? string.Empty);
        }

        private static string TrimEpicGameSubdirectory(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directoryName = Path.GetFileName(trimmed);

            return string.Equals(directoryName, "AmongUs", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(trimmed) ?? trimmed
                : trimmed;
        }

        private static string ResolveEpicGameExePath(string baseInstallDirectory)
        {
            var epicExePath = Path.Combine(baseInstallDirectory, "AmongUs", "Among Us.exe");
            if (File.Exists(epicExePath))
                return epicExePath;

            return Path.Combine(baseInstallDirectory, "Among Us.exe");
        }

        private void EnsureEpicPayloadInsideGameDirectory(string baseInstallDirectory)
        {
            var gameDirectory = Path.Combine(baseInstallDirectory, "AmongUs");
            if (!Directory.Exists(gameDirectory))
                return;

            foreach (var directory in Directory.GetDirectories(baseInstallDirectory))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, "AmongUs", StringComparison.OrdinalIgnoreCase))
                    continue;

                MoveDirectoryContent(directory, Path.Combine(gameDirectory, name));
                TryDeleteEmptyDirectory(directory);
            }

            foreach (var file in Directory.GetFiles(baseInstallDirectory))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, ".susmodder-install.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = Path.Combine(gameDirectory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
                TryDeleteFile(file);
            }
        }

        private static void MoveDirectoryContent(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in Directory.GetFiles(sourceDirectory))
            {
                var destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
                TryDeleteFile(file);
            }

            foreach (var directory in Directory.GetDirectories(sourceDirectory))
            {
                MoveDirectoryContent(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
                TryDeleteEmptyDirectory(directory);
            }
        }

        private static void TryDeleteFile(string file)
        {
            try { File.Delete(file); }
            catch { /* best effort cleanup */ }
        }

        private static void TryDeleteEmptyDirectory(string directory)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch { /* best effort cleanup */ }
        }

        /// <summary>
        /// Instaluje grę (z przypiętym manifestem dla modów).
        /// </summary>
        /// <returns>
        /// true gdy instalacja zakończyła się powodzeniem. Ścieżka vanilla zachowuje dotychczasowe
        /// zachowanie - start gry następuje także po nieudanej instalacji, bo mechanizm automatycznej
        /// reinstalacji opiera się na nieudanym starcie.
        /// </returns>
        public async Task<bool> InstallGameAsync(ModConfiguration modConfig, string amongVersionFormatted)
        {
            // Blokada po niepewnym timeoutcie - nie wykonujemy operacji na plikach gry.
            if (IsLegendaryBlocked())
                return false;

            string installDirectory;
            if (modConfig.Id == 0)
            {
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    Write("Ścieżka instalacji dla Vanilla Among Us jest nieprawidłowa.");
                    return false;
                }

                installDirectory = ResolveEpicBaseInstallDirectory(modConfig);
            }
            else
            {
                installDirectory = ResolveEpicBaseInstallDirectory(modConfig);
            }
            Directory.CreateDirectory(installDirectory);
            string commandArguments;
            bool usesPinnedManifest = false;

            if (modConfig.Id == 0)
            {
                commandArguments = $"install {EpicAppId} --base-path \"{installDirectory}\" -y";
            }
            else
            {
                string manifestFilePath = Path.Combine(manifestDirectory, $"{EpicAppId}_{amongVersionFormatted}.manifest");
                if (!File.Exists(manifestFilePath))
                {
                    Write($"Nie znaleziono manifestu: {manifestFilePath}.");
                    return false;
                }
                commandArguments = $"install {EpicAppId} -y --manifest \"{manifestFilePath}\" --base-path \"{installDirectory}\"";
                usesPinnedManifest = true;
            }

            bool installed = true;
            if (usesPinnedManifest)
            {
                installed = await RunPinnedManifestInstallAsync(commandArguments);

                // Nieudana instalacja z przypiętym manifestu - nie przenosimy payloadu i nie kontynuujemy.
                if (!installed)
                    return false;
            }
            else
            {
                // Vanilla: zachowujemy dotychczasowe zachowanie (start gry następuje także po nieudanej
                // instalacji, bo mechanizm automatycznej reinstalacji opiera się na nieudanym starcie).
                var result = await RunLegendaryCommandCoreAsync(commandArguments);
                HandleFailedLaunch(commandArguments, result);
                installed = result.ExitCode == 0;
            }

            if (modConfig.Id != 0)
                EnsureEpicPayloadInsideGameDirectory(installDirectory);
             
            // Weryfikacja czy instalacja się powiodła
            string gameExePath = ResolveEpicGameExePath(installDirectory);
            if (!File.Exists(gameExePath))
            {
                Write($"OSTRZEŻENIE: Plik gry nie został znaleziony po instalacji: {gameExePath}");
                Write($"Instalacja mogła się nie powieść. Sprawdź logi legendary.");
            }
            else
            {
                Write($"Weryfikacja instalacji OK: {gameExePath}");
            }

            return installed;
        }

        public async Task LaunchGameAsync()
        {
            GameLaunchStarting?.Invoke();
            string commandArguments = $"launch {EpicAppId} --skip-version-check";
            await RunLegendaryCommandAsync(commandArguments);
        }

        public async Task RunLegendaryCommandAsync(string commandArguments)
        {
            var result = await RunLegendaryCommandCoreAsync(commandArguments);
            HandleFailedLaunch(commandArguments, result);
        }

        private async Task<LegendaryRunResult> RunLegendaryCommandCoreAsync(string commandArguments, TimeSpan? timeout = null)
        {
            try
            {
                Write($"Launching legendary.exe {commandArguments}");

                // Poprzedni proces legendary mógł przeżyć timeout - kolejnych poleceń nie uruchamiamy,
                // dopóki nie potwierdzimy jego zakończenia.
                if (LegendaryProcessGuard.IsBlocked())
                {
                    Write("Poprzedni proces legendary nadal działa - kolejne polecenia legendary są wstrzymane.");
                    return new LegendaryRunResult(
                        -1,
                        new[] { "Poprzedni proces legendary nadal działa - operacja wstrzymana." },
                        timedOut: true);
                }

                var result = await _commandRunner(commandArguments, timeout, ForwardLegendaryOutput);

                if (result.TimedOut)
                {
                    Write($"legendary.exe przekroczył limit czasu - operacja przerwana.");
                    return result;
                }

                var exitMsg = $"Process exited with code {result.ExitCode}";
                LegendaryOutput?.Invoke(exitMsg);
                Write(exitMsg);

                return result;
            }
            catch (Exception ex)
            {
                Write($"ERROR running legendary.exe {commandArguments}: {ex}");
                Write($"Wystąpił błąd podczas uruchamiania legendary.exe: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Uruchamia legendary.exe jako proces. Limit czasu dotyczy wyłącznie operacji odświeżania
        /// metadanych - pełnej instalacji (pobieranie gry) nie ograniczamy arbitralnym timeoutem.
        /// </summary>
        private Task<LegendaryRunResult> RunLegendaryProcessAsync(
            string commandArguments,
            TimeSpan? timeout,
            Action<string, bool>? onOutput)
            => ExecuteLegendaryProcessAsync(legendaryPath, commandArguments, timeout, onOutput);

        /// <summary>
        /// Wykonuje komendę legendary w procesie potomnym.
        /// </summary>
        /// <remarks>
        /// Każde niepotwierdzone zakończenie procesu, który wystartował, oznacza stan niepewny
        /// (drzewo procesów mogło działać dalej) - wtedy blokujemy kolejne komendy legendary.
        /// Proces, który nie wystartował, niczego nie blokuje.
        /// </remarks>
        internal async Task<LegendaryRunResult> ExecuteLegendaryProcessAsync(
            string executablePath,
            string commandArguments,
            TimeSpan? timeout,
            Action<string, bool>? onOutput)
        {
            // Synchroniczna kontrola zaufania do pliku — obcego EXE nie uruchamiamy.
            // Kontrola dotyczy wyłącznie właściwego narzędzia; inne ścieżki (np. testy) są nietknięte.
            bool isTool = IsLegendaryToolPath(executablePath);

            // Bramka chroni moment startu procesu przed równoległą podmianą pliku.
            // Trzymamy ją tylko do chwili rejestracji operacji — długi proces nie blokuje podmiany
            // (podmiana rozpoznaje trwającą operację i odracza się, nie ubijając procesu).
            using var gate = isTool
                ? await LegendaryOperationGate.AcquireAsync(CancellationToken.None)
                : null;

            if (isTool && !await EnsureToolIsRunnableAsync())
            {
                return new LegendaryRunResult(
                    -1,
                    new[] { "legendary.exe nie jest zaufany — proces nie został uruchomiony." });
            }

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = commandArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            // Zbieraj stderr do analizy błędów
            var stderrLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                onOutput?.Invoke(e.Data, false);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // Dodaj do kolekcji stderr
                stderrLines.Add(e.Data);

                onOutput?.Invoke(e.Data, true);
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                // Proces nie wystartował - nic nie działa w tle, więc blokada nie jest potrzebna.
                Write($"Nie udało się uruchomić legendary.exe: {ex.Message}");
                return new LegendaryRunResult(-1, stderrLines);
            }

            // Rejestrujemy operację — podmiana pliku w trakcie pracy procesu jest odraczana
            // (ToolBusy), a nie realizowana siłą. Procesu nie ubijamy.
            LegendaryOperationGate.RegisterOperationStart();

            // Proces wystartował i jest zarejestrowany — bramka podmiany może się już zwolnić.
            gate?.Dispose();

            try
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (timeout.HasValue)
                {
                    using var cts = new System.Threading.CancellationTokenSource(timeout.Value);
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Write($"legendary.exe przekroczył limit {timeout.Value.TotalSeconds:0} s - kończę proces");
                        LegendaryProcessGuard.MarkUnfinished();
                        try
                        {
                            await TerminateLegendaryProcessAsync(process);
                        }
                        catch (Exception ex)
                        {
                            Write($"Nie udało się zakończyć procesu legendary: {ex.Message}");
                        }

                        // Kill nie potwierdza zakończenia potomków - całą operację traktujemy jako niepewną.
                        return new LegendaryRunResult(-1, stderrLines, timedOut: true);
                    }
                }
                else
                {
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception)
            {
                // Proces wystartował, a jego zakończenie jest niepotwierdzone - blokujemy kolejne komendy.
                LegendaryProcessGuard.MarkUnfinished();
                throw;
            }
            finally
            {
                // Operacja zakończona (lub przerwana) — podmiana pliku znów jest możliwa.
                LegendaryOperationGate.RegisterOperationEnd();
            }

            return new LegendaryRunResult(process.ExitCode, stderrLines);
        }

        /// <summary>
        /// Kończy proces legendary po przekroczeniu limitu czasu. Nie czeka bezterminowo -
        /// po nieudanym kill wraca, bo drzewo procesów mogło dalej działać.
        /// </summary>
        internal static async Task TerminateLegendaryProcessAsync(
            Func<bool> hasExited,
            Action kill,
            Func<CancellationToken, Task> waitForExitAsync,
            TimeSpan grace)
        {
            try
            {
                if (!hasExited())
                    kill();
            }
            catch
            {
                // proces mógł zakończyć się sam albo nie da się go ubić
            }

            using var graceTimeout = new CancellationTokenSource(grace);
            try
            {
                await waitForExitAsync(graceTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // Po nieudanym kill nie czekamy bezterminowo.
            }
            catch (Exception)
            {
                // Wyjątek z oczekiwania na koniec procesu nie może przerwać obsługi timeoutu -
                // i tak nie potwierdza, że drzewo procesów zakończyło pracę.
            }
        }

        private static Task TerminateLegendaryProcessAsync(Process process)
            => TerminateLegendaryProcessAsync(
                () => process.HasExited,
                () => process.Kill(entireProcessTree: true),
                token => process.WaitForExitAsync(token),
                TimeSpan.FromSeconds(5));

        private void HandleFailedLaunch(string commandArguments, LegendaryRunResult result)
        {
            // Sprawdź czy wystąpił błąd uruchamiania
            if (result.ExitCode == 1 && commandArguments.Contains("launch"))
            {
                // Sprawdź czy to konkretnie błąd FileNotFoundError
                bool isFileNotFoundError = result.ContainsError("FileNotFoundError") ||
                                           result.ContainsError("Nie można odnaleźć określonego pliku") ||
                                           result.ContainsError("[WinError 2]");

                if (isFileNotFoundError)
                {
                    Write("Wykryto błąd FileNotFoundError - pliki gry nie zostały znalezione");
                }

                _hasLaunchError = true;

                // Jeśli nie jesteśmy w trakcie retry, nie zwracaj jeszcze błędu do frontu
                if (!_isRetryingInstallation)
                {
                    Write("Błąd uruchamiania zostanie obsłużony przez automatyczną reinstalację");
                }
                else
                {
                    // Jeśli to już druga próba (po reinstalacji), wywala błąd
                    string logContent = GetLegendaryLogContent();
                    string modName = ExtractModNameFromLaunchCommand(commandArguments);
                    Write($"Błąd uruchamiania po reinstalacji dla moda: {modName}");
                    EpicLaunchError?.Invoke(modName, logContent);
                }
            }
        }

        /// <summary>
        /// Stan base_urls w konfiguracji legendary.
        /// </summary>
        private enum BaseUrlState
        {
            /// <summary>Adresy CDN są dostępne.</summary>
            Ready,

            /// <summary>
            /// Odświeżenie się nie powiodło, ale proces zakończył się czysto - próbujemy install
            /// z przypiętym manifestem tak jak wcześniej (HasBaseUrls może nie umieć odczytać
            /// konfiguracji, a legendary umie).
            /// </summary>
            Unavailable,

            /// <summary>
            /// Proces mógł przeżyć (timeout) - nie uruchamiamy kolejnych komend legendary.
            /// </summary>
            Uncertain
        }

        /// <summary>
        /// Instalacja z przypiętym manifestem (<c>--manifest &lt;plik lokalny&gt;</c>).
        /// legendary nie odpytuje wtedy API CDN, więc korzysta wyłącznie z <c>base_urls</c> zapisanych
        /// w metadanych gry - a te są czyszczone przy każdym odświeżeniu metadanych Epic
        /// (np. po wydaniu nowej wersji Among Us, także przez <c>legendary list-games</c>).
        /// Bez tego zabezpieczenia instalacja kończy się błędem "No base URLs found, please try again.".
        /// </summary>
        /// <returns>true gdy instalacja zakończyła się powodzeniem.</returns>
        internal async Task<bool> RunPinnedManifestInstallAsync(string commandArguments)
        {
            if (await EnsureLegendaryBaseUrlsAsync() == BaseUrlState.Uncertain)
                return false;

            var result = await RunLegendaryCommandCoreAsync(commandArguments);
            HandleFailedLaunch(commandArguments, result);

            // Timeout: drzewo procesów mogło przeżyć - nie wolno kontynuować ani powtarzać instalacji.
            if (result.TimedOut)
            {
                Write("[BaseUrls] Proces legendary przekroczył limit czasu - przerywam operację.");
                return false;
            }

            if (result.ExitCode == 0 || !result.ContainsError(NoBaseUrlsErrorMarker))
                return result.ExitCode == 0;

            Write("legendary zgłosił brak base_urls w metadanych - uzupełniam je i ponawiam instalację...");

            if (await EnsureLegendaryBaseUrlsAsync(forceRefresh: true) != BaseUrlState.Ready)
            {
                Write("BŁĄD: brak base_urls - nie można pobrać plików gry z przypiętego manifestu.");
                return false;
            }

            var retryResult = await RunLegendaryCommandCoreAsync(commandArguments);
            HandleFailedLaunch(commandArguments, retryResult);

            if (retryResult.TimedOut)
            {
                Write("[BaseUrls] Proces legendary przekroczył limit czasu - przerywam operację.");
                return false;
            }

            if (retryResult.ExitCode != 0 && retryResult.ContainsError(NoBaseUrlsErrorMarker))
                Write("Ponowna próba instalacji również zgłosiła brak base_urls.");

            return retryResult.ExitCode == 0;
        }

        /// <summary>
        /// Uzupełnia <c>base_urls</c> w metadanych legendary.
        /// Odświeżenie wykonuje <c>legendary import</c> do PUSTEGO katalogu: legendary pobiera wtedy
        /// manifest z CDN i zapisuje świeże adresy, ale nie znajduje plików gry, więc nie rejestruje
        /// instalacji ani nie zmienia plików użytkownika. Sam import blokowany jest lockiem
        /// <c>installed.json</c>, więc nie ma wyścigu z inną instancją legendary.
        /// </summary>
        /// <remarks>
        /// Ograniczenie: dla zarejestrowanej gry z <c>egl_guid</c> legendary może wykonać synchronizację
        /// EGL już w <c>is_installed</c> - dlatego przy istniejącym (lub nieznanym) wpisie wstępu odświeżania
        /// w ogóle nie uruchamiamy. Sprawdzenie nie gwarantuje bezpieczeństwa (między nim a startem procesu
        /// może dojść do zmiany stanu), dlatego kod wyjścia traktujemy tylko wskazówką - decyduje
        /// faktyczny stan metadanych odczytany po wyjściu procesu.
        /// </remarks>
        private async Task<BaseUrlState> EnsureLegendaryBaseUrlsAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && HasLegendaryBaseUrls())
                return BaseUrlState.Ready;

            if (LegendaryBaseUrlState.IsRegistered(legendaryConfigDirectory, EpicAppId))
            {
                Write("[BaseUrls] Pomijam odświeżanie base_urls - gra jest zarejestrowana w legendary " +
                      "(import nie odświeży metadanych, a odświeżanie mogłoby zmodyfikować istniejącą instalację).");
                return HasLegendaryBaseUrls() ? BaseUrlState.Ready : BaseUrlState.Unavailable;
            }

            Write("[BaseUrls] Uzupełnianie brakujących base_urls w metadanych legendary...");

            string warmupDirectory;
            try
            {
                // Pusty, jednorazowy katalog - legendary wymaga istniejącej ścieżki dla importu.
                warmupDirectory = Directory.CreateTempSubdirectory("susmodder-legendary-").FullName;
            }
            catch (Exception ex)
            {
                Write($"[BaseUrls] Nie udało się przygotować katalogu pomocniczego: {ex.Message}");
                return BaseUrlState.Unavailable;
            }

            bool timedOut = false;
            try
            {
                string warmupArguments =
                    $"import {EpicAppId} \"{warmupDirectory}\" -y --skip-dlcs";

                // Kod wyjścia bywa 0 mimo błędu (np. "Game is already installed!") - decyduje stan metadanych.
                var result = await RunLegendaryCommandCoreAsync(warmupArguments, TimeSpan.FromMinutes(2));
                timedOut = result.TimedOut;
                LogToFile($"[BaseUrls] Odświeżenie metadanych z CDN zakończone kodem {result.ExitCode}");
            }
            catch (Exception ex)
            {
                // Wyjątek oznacza niepewny stan procesu (mógł przeżyć) - nie kontynuujemy i nie usuwamy katalogu.
                timedOut = true;
                LogToFile($"[BaseUrls] Nie udało się odświeżyć metadanych: {ex.Message}");
            }
            finally
            {
                // Po timeoutcie drzewo procesów mogło działać dalej - katalogu nie usuwamy.
                if (timedOut)
                    Write($"[BaseUrls] Katalog pomocniczy pozostawiam (proces mógł przeżyć): {warmupDirectory}");
                else
                    TryDeleteEmptyDirectory(warmupDirectory);
            }

            if (timedOut)
            {
                Write("[BaseUrls] Przekroczony limit czasu odświeżania - przerywam operację.");
                return BaseUrlState.Uncertain;
            }

            bool hasBaseUrls = HasLegendaryBaseUrls();
            if (hasBaseUrls)
                Write("[BaseUrls] base_urls w metadanych legendary są dostępne.");
            else
                Write("OSTRZEŻENIE: Nie udało się uzupełnić base_urls - próbuję instalacji z przypiętym manifestem.");

            return hasBaseUrls ? BaseUrlState.Ready : BaseUrlState.Unavailable;
        }

        private bool HasLegendaryBaseUrls()
            => LegendaryBaseUrlState.HasBaseUrls(legendaryConfigDirectory, EpicAppId);

        /// <summary>Przekazuje wyjście procesu legendary do logu, UI i parsera postępu.</summary>
        private void ForwardLegendaryOutput(string line, bool isError)
        {
            ParseAndReportProgress(line);

            if (isError)
            {
                var errorLine = "[ERR] " + line;
                LegendaryOutput?.Invoke(errorLine);
                AppendLegendaryLog(errorLine);
            }
            else
            {
                LegendaryOutput?.Invoke(line);
                AppendLegendaryLog(line);
            }
        }

        /// <summary>Zgłasza niepowodzenie operacji Epic do frontu (dialog błędu z logami legendary).</summary>
        private void ReportEpicFailure(ModConfiguration? modConfig, string message)
        {
            Write(message);
            EpicLaunchError?.Invoke(modConfig?.ModName ?? "Among Us", GetLegendaryLogContent());
        }

        private void ParseAndReportProgress(string logLine)
        {
            if (string.IsNullOrEmpty(logLine))
                return;

            try
            {
                bool shouldReport = false;

                // 1. Parse główny progress z lepszą obsługą błędów
                var progressMatch = System.Text.RegularExpressions.Regex.Match(
                    logLine, @"= Progress: (\d+\.?\d*)% \((\d+)/(\d+)\).*?ETA: (\d{2}:\d{2}:\d{2})");

                if (progressMatch.Success)
                {
                    var percentageStr = progressMatch.Groups[1].Value;

                    // Próbuj różne sposoby parsowania
                    double percentage = 0;
                    bool parsed = false;

                    // 1. Spróbuj InvariantCulture
                    if (double.TryParse(percentageStr, NumberStyles.Float, CultureInfo.InvariantCulture, out percentage))
                    {
                        parsed = true;
                    }
                    // 2. Spróbuj CurrentCulture
                    else if (double.TryParse(percentageStr, NumberStyles.Float, CultureInfo.CurrentCulture, out percentage))
                    {
                        parsed = true;
                    }
                    // 3. Spróbuj zamienić kropkę na przecinek
                    else if (double.TryParse(percentageStr.Replace('.', ','), out percentage))
                    {
                        parsed = true;
                    }

                    if (parsed)
                    {
                        _lastProgressPercentage = percentage;
                        _lastCurrentFiles = progressMatch.Groups[2].Value;
                        _lastTotalFiles = progressMatch.Groups[3].Value;
                        _lastEta = progressMatch.Groups[4].Value;
                        shouldReport = true;
                    }
                    else
                    {
                        Write($"Failed to parse percentage: '{percentageStr}'");
                    }
                }

                // Reszta kodu bez zmian...
                var downloadedMatch = System.Text.RegularExpressions.Regex.Match(
                    logLine, @"Downloaded: ([\d.]+\s+\w+)");

                if (downloadedMatch.Success)
                {
                    _lastDownloaded = downloadedMatch.Groups[1].Value;
                }

                var speedMatch = System.Text.RegularExpressions.Regex.Match(
                    logLine, @"\+ Download\s+-\s+([\d.]+\s+\w+/s)");

                if (speedMatch.Success)
                {
                    _lastDownloadSpeed = speedMatch.Groups[1].Value;
                    SpeedChanged?.Invoke(_lastDownloadSpeed);
                    if (_lastProgressPercentage > 0)
                        shouldReport = true;
                }

                if (shouldReport && _lastProgressPercentage > 0)
                {
                    var messageParts = new List<string>
            {
                $"{_lastProgressPercentage:F1}%"
            };

                    if (!string.IsNullOrEmpty(_lastDownloadSpeed))
                        messageParts.Add($"Prędkość: {_lastDownloadSpeed}");


                    if (!string.IsNullOrEmpty(_lastDownloaded))
                        messageParts.Add($"Pobrano: {_lastDownloaded}");


                    var message = $"Pobieranie: {string.Join(" | ", messageParts)}";

                    ProgressChanged?.Invoke((int)_lastProgressPercentage, message);
                    return;
                }

                if (_lastProgressPercentage == 0)
                {
                    var phase = GetInstallationPhase(logLine);
                    if (!string.IsNullOrEmpty(phase))
                    {
                        var estimatedProgress = GetEstimatedProgress(phase);
                        ProgressChanged?.Invoke(estimatedProgress, phase);
                    }
                }
            }
            catch (Exception ex)
            {
                Write($"Error parsing progress: {ex.Message}");
                Write($"Problematic line: {logLine}");
            }
        }

        private string GetInstallationPhase(string logLine)
        {
            if (logLine.Contains("Preparing download"))
                return "Przygotowywanie pobierania...";
            if (logLine.Contains("Parsing game manifest"))
                return "Analizowanie manifestu gry...";
            if (logLine.Contains("Starting download workers"))
                return "Rozpoczynanie pobierania...";
            if (logLine.Contains("Starting file writing worker"))
                return "Przygotowywanie zapisu plików...";
            if (logLine.Contains("Waiting for installation to finish"))
                return "Finalizowanie instalacji...";
            if (logLine.Contains("Finished installation process"))
                return "Instalacja zakończona!";
            if (logLine.Contains("Launching") && !logLine.Contains("legendary.exe"))
                return "Uruchamianie gry...";
            if (logLine.Contains("Logging in"))
                return "Logowanie do Epic Games...";

            return string.Empty;
        }

        private int GetEstimatedProgress(string phase)
        {
            return phase switch
            {
                "Przygotowywanie pobierania..." => 5,
                "Analizowanie manifestu gry..." => 10,
                "Rozpoczynanie pobierania..." => 15,
                "Przygotowywanie zapisu plików..." => 20,
                "Finalizowanie instalacji..." => 95,
                "Instalacja zakończona!" => 100,
                "Logowanie do Epic Games..." => 5,
                "Uruchamianie gry..." => 100,
                _ => 0
            };
        }

        // Pinned legendary — wydanie zdefiniowane w LegendaryVersionService (upstream 0.21.1).
        // URL i SHA-256 trzymamy w jednym miejscu, żeby pobieranie i podmiana nie rozjechały się.
        private async Task DownloadLegendaryAsync()
        {
            string url = LegendaryVersionService.PinnedDownloadUrl;
            string expectedSha256 = LegendaryVersionService.PinnedSha256;
            string tmpPath = $"{legendaryPath}.tmp.{Guid.NewGuid():N}";

            try
            {
                Write($"[Legendary] Pobieram {url}...");
                if (!await DownloadFileAsync(url, tmpPath))
                {
                    LastFailureCode = DownloadVerificationCodes.ToolDownloadFailed;
                    throw new InvalidOperationException(DownloadVerificationCodes.ToolDownloadFailed);
                }

                if (!File.Exists(tmpPath))
                {
                    LastFailureCode = DownloadVerificationCodes.ToolDownloadFailed;
                    throw new InvalidOperationException(DownloadVerificationCodes.ToolDownloadFailed);
                }

                var actualHash = await Sha256Verifier.ComputeFileHexAsync(tmpPath);
                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmpPath); } catch { /* best effort */ }
                    LastFailureCode = DownloadVerificationCodes.ToolHashMismatch;
                    Write($"[Legendary] SHA256 mismatch: expected {expectedSha256}, got {actualHash}");
                    throw new InvalidOperationException(DownloadVerificationCodes.ToolHashMismatch);
                }

                File.Move(tmpPath, legendaryPath, overwrite: true);
                Write($"[Legendary] Downloaded and verified ({LegendaryVersionService.PinnedTag}).");
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { /* best effort */ }
                }
            }
        }

        /// <summary>
        /// Pobiera plik do podanej ścieżki. Plik jest najpierw zapisywany w unikalnym pliku
        /// tymczasowym obok ścieżki docelowej i przenoszony dopiero po pełnym pobraniu -
        /// na ścieżce docelowej nigdy nie zostaje plik częściowy.
        /// </summary>
        /// <returns>true gdy plik został pobrany, false przy 404 lub błędzie pobierania.</returns>
        private async Task<bool> DownloadFileAsync(string url, string filePath)
        {
            using var client = new HttpClient();
            string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                ProgressChanged?.Invoke(15, "Łączenie z serwerem...");

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Write($"Nie znaleziono zasobu dla URL: {url}.");
                    return false;
                }
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalBytesRead = 0L;
                    int bytesRead;
                    var lastProgressReport = DateTime.Now;

                    ProgressChanged?.Invoke(20, "Pobieranie pliku...");

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        // Reportuj progress co 500ms
                        if (canReportProgress && DateTime.Now - lastProgressReport > TimeSpan.FromMilliseconds(500))
                        {
                            var progressPercentage = (int)((totalBytesRead * 100L) / totalBytes);
                            // Mapuj na zakres 20-35% (pobieranie to część całego procesu)
                            var mappedProgress = 20 + (progressPercentage * 15 / 100);

                            var downloadedMB = totalBytesRead / (1024.0 * 1024.0);
                            var totalMB = totalBytes / (1024.0 * 1024.0);

                            ProgressChanged?.Invoke(mappedProgress, $"Pobieranie: {downloadedMB:F1} MB / {totalMB:F1} MB");
                            lastProgressReport = DateTime.Now;
                        }
                    }
                }

                if (canReportProgress)
                {
                    ProgressChanged?.Invoke(35, "Pobieranie zakończone");
                }

                File.Move(tempPath, filePath, overwrite: true);
                return true;
            }
            catch (HttpRequestException ex)
            {
                Write($"Błąd HTTP: {ex.Message} dla URL: {url}.");
                return false;
            }
            catch (Exception ex)
            {
                Write($"Wystąpił błąd podczas pobierania pliku: {ex.Message}.");
                return false;
            }
            finally
            {
                // Plik tymczasowy nigdy nie zostaje - w przypadku błędu na ścieżce docelowej
                // nie ma ani pliku częściowego, ani starej zawartości.
                TryDeleteFile(tempPath);
            }
        }

        private void CopyContent(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destinationDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destinationDir);
            }
        }

        private void DirectoryCopy(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destinationDir = Path.Combine(destDir, Path.GetFileName(dir));
                DirectoryCopy(dir, destinationDir);
            }
        }

        private int GetLastLaunchId()
        {
            try
            {
                var settings = new UserSettingsService().LoadUserSettings();
                return settings.LastLaunchId;
            }
            catch (Exception ex)
            {
                Write($"Nie udało się odczytać LastLaunchId z user settings: {ex.Message}");
            }

            // Legacy fallback: appsettings.json obok exe / BaseDirectory
            foreach (var path in GetLegacyAppSettingsCandidates())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json);
                    if (jsonObj != null
                        && jsonObj.TryGetValue("Configuration", out var config)
                        && config.TryGetValue("lastLaunchId", out var idElement))
                    {
                        if (idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var id))
                            return id;
                        if (idElement.ValueKind == JsonValueKind.String
                            && int.TryParse(idElement.GetString(), out var parsed))
                            return parsed;
                    }
                }
                catch (Exception ex)
                {
                    Write($"Legacy GetLastLaunchId ({path}) nieudane: {ex.Message}");
                }
            }

            return -1;
        }

        private void SaveLastLaunchId(int id)
        {
            try
            {
                var userSettings = new UserSettingsService();
                userSettings.UpdateUserSetting(s => s.LastLaunchId = id);
            }
            catch (Exception ex)
            {
                Write($"Nie udało się zapisać LastLaunchId do user settings: {ex.Message}");
            }

            // Legacy: utrzymuj też appsettings jeśli plik istnieje (stare narzędzia/logi)
            foreach (var path in GetLegacyAppSettingsCandidates())
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    var json = File.ReadAllText(path);
                    var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);
                    if (jsonObj == null || !jsonObj.ContainsKey("Configuration"))
                        continue;

                    var config = jsonObj["Configuration"];
                    config["lastLaunchId"] = id;
                    jsonObj["Configuration"] = config;
                    var updatedJson = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, updatedJson);
                    return;
                }
                catch (Exception ex)
                {
                    Write($"Legacy SaveLastLaunchId ({path}) nieudane: {ex.Message}");
                }
            }
        }

        private IEnumerable<string> GetLegacyAppSettingsCandidates()
        {
            var list = new List<string>();
            void TryAdd(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try { path = Path.GetFullPath(path); }
                catch { return; }
                if (list.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                    return;
                list.Add(path);
            }

            TryAdd(appSettingsFilePath);
            TryAdd(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"));
            TryAdd(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            // ProcessPath pomijamy gdy to dotnet.exe (Program Files\dotnet)

            return list;
        }

        private string GetLegendaryLogContent()
        {
            try
            {
                if (File.Exists(legendaryLogFilePath))
                {
                    return File.ReadAllText(legendaryLogFilePath);
                }
                return "Brak dostępnych logów.";
            }
            catch (Exception ex)
            {
                return $"Błąd odczytu logów: {ex.Message}";
            }
        }

        private string ExtractModNameFromLaunchCommand(string commandArguments)
        {
            // Dla launch command argumenty wyglądają jak: "launch 963137e4c29d4c79a81323b8fab03a40 --skip-version-check"
            // Możemy spróbować wyciągnąć nazwę z kontekstu lub użyć domyślnej
            return "wybranego moda"; // Można to ulepszyć jeśli potrzebujemy dokładnej nazwy
        }

        // Dodaj właściwość do sprawdzania czy wystąpił błąd
        public bool HasLaunchError => _hasLaunchError;

        // Dodaj metodę do resetowania flagi błędu
        public void ResetErrorState()
        {
            _hasLaunchError = false;
        }

        /// <summary>
        /// Pobiera wersję aplikacji
        /// </summary>
        private static string GetAppVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
