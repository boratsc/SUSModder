using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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



    public class EpicVersionManager
    {
        private readonly string legendaryPath;
        private readonly string manifestDirectory;
        private readonly string installDirectory;
        private const string EpicAppId = "963137e4c29d4c79a81323b8fab03a40";
        private readonly string appSettingsFilePath;
        private readonly string logFilePath;
        private readonly string legendaryLogFilePath;
        public event Action<string>? LegendaryOutput;
        private readonly object _fileLock = new object();
        public event Action<int, string>? ProgressChanged;
        private double _lastProgressPercentage = 0;
        private string _lastCurrentFiles = "";
        private string _lastTotalFiles = "";
        private string _lastEta = "";
        private string _lastDownloaded = "";
        private string _lastDownloadSpeed = "";
        public event Action<ModConfiguration>? InstallationCompleted;
        private StringBuilder _errorLog = new StringBuilder();
        public event Action<string, string>? EpicLaunchError; // ModName, LogContent
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

        public EpicVersionManager(IDiagnosticsOutput output, IEpicUserInteraction userInteraction)
        {
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;

            legendaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legendary.exe");
            manifestDirectory = AppDomain.CurrentDomain.BaseDirectory;
            installDirectory = PathSettings.ModsInstallPath;
            appSettingsFilePath = Path.Combine(exeDir, "appsettings.json");

            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "epic.log.txt");
            legendaryLogFilePath = Path.Combine(exeDir, "legendary.log.txt");

            _output = output;
            _userInteraction = userInteraction;
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

        private void Write(string line) => _output?.Write(line);

        private void ShowError(string msg) => _userInteraction?.ShowError(msg);

        private bool Confirm(string msg) => _userInteraction?.Confirm(msg) ?? false;

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

                using var proc = Process.Start(psi);
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

        public async Task ModifyEpicAsync(ModConfiguration modConfig, object? progressBar, object? progressLabel)
        {
            if (modConfig == null)
            {
                ShowError("Konfiguracja moda jest nieprawidłowa.");
                return;
            }

            string downloadUrl = ModDownloadUrlBuilder.Build(modConfig, "epic");

            Write($"[CDN] URL pobierania moda Epic '{modConfig.ModName}': {downloadUrl}");

            string baseDirectory = installDirectory;
            
            // Unikalna nazwa katalogu temp dla każdej instalacji, aby uniknąć konfliktów przy równoczesnych instalacjach
            string uniqueTempId = Guid.NewGuid().ToString("N");
            string tempDirectory = Path.Combine(baseDirectory, "temp", uniqueTempId);
            Directory.CreateDirectory(tempDirectory);
            string modFile = Path.Combine(tempDirectory, "mod.zip");
            ProgressChanged?.Invoke(10, "Rozpoczynam pobieranie moda...");

            Write($"Rozpoczynam pobieranie moda '{modConfig.ModName}' z: {downloadUrl}");
            await DownloadFileAsync(downloadUrl, modFile);

            if (!File.Exists(modFile))
            {
                Write($"ERROR: Nie udało się pobrać moda z {downloadUrl}");
                ShowError($"Nie udało się pobrać moda z {downloadUrl}.");
                return;
            }

            Write($"Pomyślnie pobrano mod do: {modFile}");
            ProgressChanged?.Invoke(40, "Pobieranie zakończone, przygotowywanie instalacji...");

            string gameBasePath = Path.Combine(baseDirectory, modConfig.ModName, "AmongUs");
            if (Directory.Exists(gameBasePath))
            {
                Write($"Usuwam istniejący katalog: {gameBasePath}");
                Directory.Delete(gameBasePath, true);
            }
            Directory.CreateDirectory(gameBasePath);
            string tempExtractPath = Path.Combine(tempDirectory, "extractMod");
            Directory.CreateDirectory(tempExtractPath);

            ProgressChanged?.Invoke(50, "Rozpakowywanie archiwum...");

            try
            {
                Write($"Rozpakowuję archiwum moda: {modFile} do {tempExtractPath}");
                ZipFile.ExtractToDirectory(modFile, tempExtractPath, overwriteFiles: true);
                Write("Pomyślnie rozpakowano archiwum moda");
            }
            catch (Exception ex)
            {
                Write($"ERROR podczas rozpakowywania: {ex.Message}");
                ShowError($"Błąd podczas rozpakowywania archiwum: {ex.Message}");
                return;
            }

            ProgressChanged?.Invoke(70, "Kopiowanie plików...");

            string sourcePath = Directory.Exists(Path.Combine(tempExtractPath, "BepInEx"))
                ? tempExtractPath
                : Directory.GetDirectories(tempExtractPath).FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(sourcePath))
            {
                Write("ERROR: Nie znaleziono plików do skopiowania");
                ShowError("Nie znaleziono plików do skopiowania.");
                return;
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

            // Usuń unikalny katalog temp dla tej instalacji
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                    Write($"Usunięto katalog tymczasowy: {tempDirectory}");
                }
            }
            catch (Exception ex)
            {
                Write($"[WARNING] Nie udało się usunąć katalogu tymczasowego: {ex.Message}");
            }

            ProgressChanged?.Invoke(100, "Instalacja zakończona!");

            // DODAJ DEBUGOWANIE
            Write($"🔍 DEBUG: ModifyEpicAsync kończy się - przed GC.Collect()");
            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync COMPLETED for mod: {modConfig.ModName}");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            System.Diagnostics.Debug.WriteLine($"🔍 DEBUG: ModifyEpicAsync FULLY COMPLETED for mod: {modConfig.ModName}");
            InstallationCompleted?.Invoke(modConfig);
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
                Write($"Finalizowanie uwierzytelnienia z kodem...");

                var codePsi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = $"auth --code {authCode}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var codeProcess = Process.Start(codePsi);
                if (codeProcess == null)
                {
                    Write("Nie udało się uruchomić legendary.exe z kodem");
                    return false;
                }

                var codeStderr = await codeProcess.StandardError.ReadToEndAsync();
                var codeStdout = await codeProcess.StandardOutput.ReadToEndAsync();
                await codeProcess.WaitForExitAsync();

                if (codeProcess.ExitCode == 0)
                {
                    Write("✓ Pomyślnie uwierzytelniono z kodem autoryzacji!");
                    Write(codeStdout);
                    return true;
                }
                else
                {
                    Write($"✗ Błąd uwierzytelnienia: {codeStderr}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Write($"Błąd podczas finalizacji uwierzytelnienia: {ex.Message}");
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
                        Write($"Wyciągnięto kod z JSON: {code}");
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
                            Write($"Wyciągnięto kod z URL: {code}");
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
                    Write($"Użyto bezpośredniego kodu: {cleanCode}");
                    return cleanCode;
                }
            }

            Write($"Nie można wyciągnąć kodu z: {input.Substring(0, Math.Min(50, input.Length))}...");
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
        private async Task<bool> AuthenticateAsync()
        {
            try
            {
                // === METODA 1: Próba importu sesji z Epic Games Launcher ===
                Write("Próba importu sesji z Epic Games Launcher...");

                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
                    Arguments = "auth --import",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    Write("Nie udało się uruchomić legendary.exe");
                    return false;
                }

                var stderr = await process.StandardError.ReadToEndAsync();
                var stdout = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Jeśli import się powiódł, zakończ
                if (process.ExitCode == 0 && !stderr.Contains("ERROR") && !stderr.Contains("No EGS login session found"))
                {
                    Write("✓ Pomyślnie zaimportowano sesję z Epic Games Launcher");
                    return true;
                }

                // === METODA 2: Dedykowany dialog authentication (UX-friendly) ===
                Write("Import sesji nie powiódł się. Pokazuję dialog logowania...");
                Write(stderr);

                // Przygotuj OAuth URL (używamy standardowego legendary URL)
                // WAŻNE: redirectUrl musi być URL-encoded! (jak w legendary Python: urllib.parse.quote)
                const string clientId = "34a02cf8f4414e29b15921876da36f9a";
                string redirectUrl = $"https://www.epicgames.com/id/api/redirect?clientId={clientId}&responseType=code";
                string encodedRedirectUrl = Uri.EscapeDataString(redirectUrl);
                string oauthUrl = $"https://www.epicgames.com/id/login?redirectUrl={encodedRedirectUrl}";

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
                    Write($"Nie udało się wyciągnąć kodu autoryzacji z: {userInput.Substring(0, Math.Min(100, userInput.Length))}");
                    ShowError("Nie można wyciągnąć kodu autoryzacji z podanych danych.\n\n" +
                             "Upewnij się, że wkleiłeś:\n" +
                             "- Cały JSON z przeglądarki\n" +
                             "- Lub redirect URL\n" +
                             "- Lub sam kod autoryzacji (20-64 znaki alfanumeryczne)");
                    return false;
                }

                // Użyj kodu do uwierzytelnienia
                Write($"Użyto kodu z manual entry: {manualCode}");
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

                using var process = Process.Start(psi);
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

        public async Task HandleEpicGameAsync(ModConfiguration modConfig)
        {
            if (modConfig == null || string.IsNullOrEmpty(modConfig.AmongVersion))
            {
                Write("Konfiguracja gry jest nieprawidłowa.");
                return;
            }

            if (!File.Exists(legendaryPath))
            {
                await DownloadLegendaryAsync();
            }

            // Uwierzytelnij użytkownika (spróbuj import, jeśli nie uda się - manual code entry)
            bool authenticated = await AuthenticateAsync();
            if (!authenticated)
            {
                Write("Nie udało się uwierzytelnić użytkownika. Przerywam operację.");
                return;
            }

            string installDirectory;
            int lastLaunchId = GetLastLaunchId();

            if (modConfig.Id == lastLaunchId)
            {
                // Sprawdź czy InstallPath nie jest null
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    ShowError("Ścieżka instalacji moda jest nieprawidłowa.");
                    return;
                }

                installDirectory = modConfig.InstallPath;

                try
                {
                    // Resetuj licznik retry na początku nowej sesji uruchomienia
                    _retryCount = 0;
                    _hasLaunchError = false;

                    // Weryfikuj czy pliki gry faktycznie istnieją przed importem
                    string gameExePath = Path.Combine(installDirectory, "Among Us.exe");
                    if (!File.Exists(gameExePath))
                    {
                        Write($"OSTRZEŻENIE: Plik gry nie istnieje w {installDirectory}");
                        Write("Wymuszanie pełnej reinstalacji zamiast importu...");
                        
                        // Wymuś pełną reinstalację zamiast importu
                        _retryCount++;
                        await PerformReinstallationSequence(modConfig);
                        return;
                    }

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

            if (modConfig.Id == 0)
            {
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    Write("Ścieżka instalacji dla Vanilla Among Us jest nieprawidłowa.");
                    return;
                }

                installDirectory = modConfig.InstallPath.Replace("AmongUs", "").TrimEnd(Path.DirectorySeparatorChar);
            }
            else
            {
                installDirectory = Path.Combine(PathSettings.ModsInstallPath, modConfig.ModName);
            }
            await RunLegendaryCommandAsync("uninstall 963137e4c29d4c79a81323b8fab03a40 --keep-files -y");
            await RunLegendaryCommandAsync($"import 963137e4c29d4c79a81323b8fab03a40 \"{installDirectory}\" -y");

            string? foundPath = await CheckInstalledAppsAsync();

            if (string.IsNullOrEmpty(foundPath))
            {
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    Write("Ścieżka instalacji moda jest nieprawidłowa.");
                    return;
                }

                string basePath = modConfig.InstallPath;
                string manifestFile = Path.Combine(
                    manifestDirectory,
                    $"{EpicAppId}_{modConfig.AmongVersion.Replace("-", ".")}.manifest");

                string installArgs;
                if (modConfig.Id == 0)
                {
                    installArgs =
                      $"install {EpicAppId} --base-path \"{basePath}\" -y";
                }
                else
                {
                    installArgs =
                      $"install {EpicAppId} -y " +
                      $"--manifest \"{manifestFile}\" " +
                      $"--base-path \"{basePath}\"";
                }

                LogToFile($"Repair nie powiódł się → fallback install: {installArgs}");
                await RunLegendaryCommandAsync(installArgs);

                foundPath = basePath;
            }

            string amongVersionFormatted = modConfig.AmongVersion?.Replace("-", ".") ?? string.Empty;

            if (modConfig.Id == 0)
            {
                await UninstallGameAsync();
                await InstallGameAsync(modConfig, amongVersionFormatted);
                await LaunchGameAsync();
            }
            else
            {
                await DownloadManifestAsync(amongVersionFormatted);
                await UninstallGameAsync();
                await InstallGameAsync(modConfig, amongVersionFormatted);
                await LaunchGameAsync();
            }

            SaveLastLaunchId(modConfig.Id);
        }

        private async Task PerformReinstallationSequence(ModConfiguration modConfig)
        {
            try
            {
                Write($"Rozpoczynam automatyczną reinstalację dla moda: {modConfig.ModName} (próba {_retryCount}/{MaxRetryAttempts})");

                string amongVersionFormatted = modConfig.AmongVersion?.Replace("-", ".") ?? string.Empty;

                // Wykonaj sekwencję reinstalacji
                if (modConfig.Id != 0) // Dla modów (nie vanilla)
                {
                    Write("Pobieranie manifestu...");
                    await DownloadManifestAsync(amongVersionFormatted);
                }

                Write("Odinstalowywanie gry...");
                await UninstallGameAsync();

                Write("Instalowanie gry...");
                await InstallGameAsync(modConfig, amongVersionFormatted);

                // Weryfikuj czy instalacja się powiodła przed launchem
                string installDirectory;
                if (modConfig.Id == 0)
                {
                    installDirectory = modConfig.InstallPath?.Replace("AmongUs", "").TrimEnd(Path.DirectorySeparatorChar) ?? "";
                }
                else
                {
                    installDirectory = Path.Combine(PathSettings.ModsInstallPath, modConfig.ModName);
                }
                
                string gameExePath = Path.Combine(installDirectory, "AmongUs", "Among Us.exe");
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
                    return;
                }

                Write("Uruchamianie gry po reinstalacji...");
                // Resetuj flagę błędu przed ponowną próbą uruchomienia
                _hasLaunchError = false;
                await LaunchGameAsync();

                // Sprawdź czy tym razem uruchomienie się powiodło
                if (!_hasLaunchError)
                {
                    Write("Automatyczna reinstalacja zakończona pomyślnie!");
                }
                else
                {
                    Write("Automatyczna reinstalacja nie rozwiązała problemu.");
                    
                    // Jeśli to nie była ostatnia próba, spróbuj jeszcze raz
                    if (_retryCount < MaxRetryAttempts)
                    {
                        Write($"Ponawiam próbę... ({_retryCount + 1}/{MaxRetryAttempts})");
                        _retryCount++;
                        await PerformReinstallationSequence(modConfig);
                    }
                    else
                    {
                        // Zgłoś błąd do frontu
                        string logContent = GetLegendaryLogContent();
                        EpicLaunchError?.Invoke(modConfig.ModName, logContent);
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

        private async Task DownloadManifestAsync(string amongVersionFormatted)
        {
            if (string.IsNullOrWhiteSpace(amongVersionFormatted))
            {
                Write("Niepoprawna wersja Among Us.");
                return;
            }

            string manifestUrl = $"https://github.com/whichtwix/Data/raw/master/epic/manifests/{EpicAppId}_{amongVersionFormatted}.manifest";
            string manifestPath = Path.Combine(manifestDirectory, $"{EpicAppId}_{amongVersionFormatted}.manifest");
            await DownloadFileAsync(manifestUrl, manifestPath);
        }

        public async Task UninstallGameAsync()
        {
            string commandArguments = $"uninstall {EpicAppId} -y";
            await RunLegendaryCommandAsync(commandArguments);
        }

        public async Task InstallGameAsync(ModConfiguration modConfig, string amongVersionFormatted)
        {
            string installDirectory;
            if (modConfig.Id == 0)
            {
                if (string.IsNullOrEmpty(modConfig.InstallPath))
                {
                    Write("Ścieżka instalacji dla Vanilla Among Us jest nieprawidłowa.");
                    return;
                }

                installDirectory = modConfig.InstallPath.Replace("AmongUs", "").TrimEnd(Path.DirectorySeparatorChar);
            }
            else
            {
                installDirectory = Path.Combine(PathSettings.ModsInstallPath, modConfig.ModName);
            }
            Directory.CreateDirectory(installDirectory);
            string commandArguments;

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
                    return;
                }
                commandArguments = $"install {EpicAppId} -y --manifest \"{manifestFilePath}\" --base-path \"{installDirectory}\"";
            }
            await RunLegendaryCommandAsync(commandArguments);
            
            // Weryfikacja czy instalacja się powiodła
            string gameExePath = Path.Combine(installDirectory, "AmongUs", "Among Us.exe");
            if (!File.Exists(gameExePath))
            {
                Write($"OSTRZEŻENIE: Plik gry nie został znaleziony po instalacji: {gameExePath}");
                Write($"Instalacja mogła się nie powieść. Sprawdź logi legendary.");
            }
            else
            {
                Write($"Weryfikacja instalacji OK: {gameExePath}");
            }
        }

        public async Task LaunchGameAsync()
        {
            string commandArguments = $"launch {EpicAppId} --skip-version-check";
            await RunLegendaryCommandAsync(commandArguments);
        }

        public async Task RunLegendaryCommandAsync(string commandArguments)
        {
            try
            {
                Write($"Launching legendary.exe {commandArguments}");
                var psi = new ProcessStartInfo
                {
                    FileName = legendaryPath,
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

                    ParseAndReportProgress(e.Data);
                    LegendaryOutput?.Invoke(e.Data);
                    lock (_fileLock)
                        File.AppendAllText(legendaryLogFilePath, e.Data + Environment.NewLine);
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    // Dodaj do kolekcji stderr
                    stderrLines.Add(e.Data);

                    ParseAndReportProgress(e.Data);

                    var line = "[ERR] " + e.Data;
                    LegendaryOutput?.Invoke(line);
                    lock (_fileLock)
                        File.AppendAllText(legendaryLogFilePath, line + Environment.NewLine);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                var exitMsg = $"Process exited with code {process.ExitCode}";
                LegendaryOutput?.Invoke(exitMsg);
                Write(exitMsg);

                // Sprawdź czy wystąpił błąd uruchamiania
                if (process.ExitCode == 1 && commandArguments.Contains("launch"))
                {
                    // Sprawdź czy to konkretnie błąd FileNotFoundError
                    bool isFileNotFoundError = stderrLines.Any(line =>
                        line.Contains("FileNotFoundError") ||
                        line.Contains("Nie można odnaleźć określonego pliku") ||
                        line.Contains("[WinError 2]"));

                    if (isFileNotFoundError)
                    {
                        Write("Wykryto błąd FileNotFoundError - pliki gry nie zostały znalezione");
                    }

                    _hasLaunchError = true;

                    // Jeśli nie jesteśmy w trakcie retry, nie zgłaszaj jeszcze błędu do frontu
                    if (!_isRetryingInstallation)
                    {
                        Write("Błąd uruchamiania zostanie obsłużony przez automatyczną reinstalację");
                    }
                    else
                    {
                        // Jeśli to już druga próba (po reinstalacji), zgłoś błąd
                        string logContent = GetLegendaryLogContent();
                        string modName = ExtractModNameFromLaunchCommand(commandArguments);
                        Write($"Błąd uruchamiania po reinstalacji dla moda: {modName}");
                        EpicLaunchError?.Invoke(modName, logContent);
                    }
                }
            }
            catch (Exception ex)
            {
                Write($"ERROR running legendary.exe {commandArguments}: {ex}");
                Write($"Wystąpił błąd podczas uruchamiania legendary.exe: {ex.Message}");
                throw; // Przekaż wyjątek wyżej
            }
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

        private async Task DownloadLegendaryAsync()
        {
            string url = "https://github.com/Heroic-Games-Launcher/legendary/releases/download/0.20.38/legendary_windows_x86_64.exe";
            await DownloadFileAsync(url, legendaryPath);
            Write("Legendary downloaded.");
        }

        private async Task DownloadFileAsync(string url, string filePath)
        {
            using var client = new HttpClient();
            try
            {
                ProgressChanged?.Invoke(15, "Łączenie z serwerem...");

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Write($"Nie znaleziono zasobu dla URL: {url}.");
                    return;
                }
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

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

                if (canReportProgress)
                {
                    ProgressChanged?.Invoke(35, "Pobieranie zakończone");
                }
            }
            catch (HttpRequestException ex)
            {
                Write($"Błąd HTTP: {ex.Message} dla URL: {url}.");
                throw;
            }
            catch (Exception ex)
            {
                Write($"Wystąpił błąd podczas pobierania pliku: {ex.Message}.");
                throw;
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
            var json = File.ReadAllText(appSettingsFilePath);
            var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);
            if (jsonObj != null && jsonObj.ContainsKey("Configuration") && jsonObj["Configuration"].ContainsKey("lastLaunchId"))
            {
                return int.Parse(jsonObj["Configuration"]["lastLaunchId"]?.ToString() ?? "-1");
            }
            return -1;
        }

        private void SaveLastLaunchId(int id)
        {
            var json = File.ReadAllText(appSettingsFilePath);
            var jsonObj = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(json);
            if (jsonObj != null && jsonObj.ContainsKey("Configuration"))
            {
                var config = jsonObj["Configuration"];
                config["lastLaunchId"] = id;
                jsonObj["Configuration"] = config;
                var updatedJson = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(appSettingsFilePath, updatedJson);
            }
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
