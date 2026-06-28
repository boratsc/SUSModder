using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Przekazuje deep link z drugiej instancji (susmodder://pack/...) lub żądanie aktywacji okna
    /// do już działającej aplikacji.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DeepLinkIpc
    {
        public const string MutexName = "Global\\SUSModder_SingleInstance_v1";
        public const string PipeName = "SUSModder_DeepLink_v1";

        private const string ActivateCommand = "activate";
        private const string PackCommand = "pack";

        /// <summary>
        /// Plik fallback: jeśli IPC pipe nie odpowiada, druga instancja zapisuje tutaj kod paczki.
        /// Pierwsza instancja sprawdza ten plik po uruchomieniu pipe servera.
        /// </summary>
        internal static string FallbackFilePath =>
            Path.Combine(Path.GetTempPath(), "susmodder_pending_deeplink.txt");

        public static Mutex? TryAcquirePrimaryInstanceMutex()
        {
            try
            {
                var mutex = new Mutex(true, MutexName, out var createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    return null;
                }

                return mutex;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Jeśli inna instancja działa — wysyła kod paczki (jeśli został podany jako argument)
        /// lub żądanie aktywacji okna i zwraca true (ta instancja powinna się zamknąć).
        /// </summary>
        public static bool TryForwardToRunningInstance(string[] args, int maxAttempts = 15)
        {
            var (packCode, autoInstall) = ExtractPackFromArgs(args);
            if (string.IsNullOrEmpty(packCode))
                return TryForwardActivation(maxAttempts);

            return TryForwardPack(packCode, autoInstall, maxAttempts);
        }

        /// <summary>
        /// Wysyła do pierwszej instancji żądanie aktywacji okna (przywrócenia z tray / focusu).
        /// Zwraca true, jeśli udało się połączyć z działającą instancją.
        /// </summary>
        public static bool TryForwardActivation(int maxAttempts = 15)
        {
            return SendMessage(ActivateCommand, string.Empty, maxAttempts);
        }

        private static bool TryForwardPack(string packCode, bool autoInstall, int maxAttempts)
        {
            var forwarded = SendMessage(PackCommand, packCode, maxAttempts, autoInstall ? "1" : "0");
            if (forwarded)
                return true;

            // Fallback: zapisz do pliku gdy IPC pipe nie odpowiada
            WriteFallbackFile(packCode, autoInstall);
            return false;
        }

        private static void WriteFallbackFile(string packCode, bool autoInstall)
        {
            try
            {
                var normalized = ModPackCodeValidator.Normalize(packCode);
                var line = autoInstall ? $"{normalized}|1" : normalized;
                File.WriteAllText(FallbackFilePath, line, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Zapisano fallback: {line} -> {FallbackFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Błąd zapisu fallback: {ex.Message}");
            }
        }

        /// <summary>
        /// Odczytuje i czyści plik fallback. Zwraca (packCode, autoInstall) lub (null, false).
        /// </summary>
        public static (string? PackCode, bool AutoInstall) ReadAndClearFallbackFile()
        {
            try
            {
                if (!File.Exists(FallbackFilePath))
                    return (null, false);

                var content = File.ReadAllText(FallbackFilePath, Encoding.UTF8).Trim();
                File.Delete(FallbackFilePath);

                if (string.IsNullOrEmpty(content))
                    return (null, false);

                var parts = content.Split('|');
                var code = parts[0];
                var auto = parts.Length > 1 && parts[1] == "1";

                if (ModPackCodeValidator.IsValid(code))
                {
                    System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Odczytano fallback: {code} (auto={auto})");
                    return (ModPackCodeValidator.Normalize(code), auto);
                }

                System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Fallback zawiera nieprawidłowy kod: {code}");
                return (null, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Błąd odczytu fallback: {ex.Message}");
                return (null, false);
            }
        }

        private static bool SendMessage(string command, string payload, int maxAttempts, params string[] extraLines)
        {
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(400);
                    using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                    writer.WriteLine(command);
                    writer.WriteLine(payload);
                    foreach (var line in extraLines)
                        writer.WriteLine(line);
                    return true;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            return false;
        }

        public static (string? PackCode, bool AutoInstall) ExtractPackFromArgs(string[]? args)
        {
            if (args == null)
                return (null, false);

            foreach (var arg in args)
            {
                var parsed = DeepLinkService.ParseDeepLink(arg);
                if (parsed.IsValid && !string.IsNullOrEmpty(parsed.PackCode))
                    return (parsed.PackCode, parsed.AutoInstall);
            }

            return (null, false);
        }

        public static void StartServer(Action<string, bool> onDeepLinkReceived, Action? onActivateRequested = null, CancellationToken ct = default)
        {
            // Sprawdź plik fallback zanim pipe server wystartuje (druga instancja mogła zapisać zanim serwer był gotowy)
            CheckAndProcessFallback(onDeepLinkReceived);

            _ = Task.Run(async () =>
            {
                var lastFallbackCheck = Environment.TickCount64;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Co 2 sekundy sprawdzaj plik fallback (na wypadek gdyby pipe nie był gotowy)
                        var now = Environment.TickCount64;
                        if (now - lastFallbackCheck > 2000)
                        {
                            lastFallbackCheck = now;
                            CheckAndProcessFallback(onDeepLinkReceived);
                        }

                        await using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        // Czekaj na połączenie z timeoutem 2s (żeby nie blokować pollingu fallback)
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectCts.CancelAfter(2000);
                        try
                        {
                            await server.WaitForConnectionAsync(connectCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            continue; // timeout — sprawdź fallback w następnej iteracji
                        }

                        using var reader = new StreamReader(server, Encoding.UTF8);
                        var command = await reader.ReadLineAsync(ct);
                        var payload = await reader.ReadLineAsync(ct);
                        var extraLine = await reader.ReadLineAsync(ct);
                        server.Disconnect();

                        if (string.IsNullOrWhiteSpace(command))
                            continue;

                        if (command == ActivateCommand)
                        {
                            onActivateRequested?.Invoke();
                            continue;
                        }

                        if (command == PackCommand && !string.IsNullOrWhiteSpace(payload) && ModPackCodeValidator.IsValid(payload))
                        {
                            var auto = extraLine == "1";
                            onDeepLinkReceived(ModPackCodeValidator.Normalize(payload), auto);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(200, ct);
                    }
                }
            }, ct);
        }

        private static void CheckAndProcessFallback(Action<string, bool> onDeepLinkReceived)
        {
            var (fallbackCode, fallbackAuto) = ReadAndClearFallbackFile();
            if (!string.IsNullOrEmpty(fallbackCode))
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLinkIpc] Przetwarzam fallback: {fallbackCode} (auto={fallbackAuto})");
                onDeepLinkReceived(fallbackCode, fallbackAuto);
            }
        }
    }
}
