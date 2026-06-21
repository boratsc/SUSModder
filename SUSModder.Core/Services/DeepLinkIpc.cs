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
            return SendMessage(PackCommand, packCode, maxAttempts, autoInstall ? "1" : "0");
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
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(ct);
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
    }
}
