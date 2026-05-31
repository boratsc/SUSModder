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
    /// Przekazuje deep link z drugiej instancji (susmodder://pack/...) do już działającej aplikacji.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class DeepLinkIpc
    {
        public const string MutexName = "Global\\SUSModder_SingleInstance_v1";
        public const string PipeName = "SUSModder_DeepLink_v1";

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
        /// Jeśli inna instancja działa — wysyła kod paczki i zwraca true (ta instancja powinna się zamknąć).
        /// </summary>
        public static bool TryForwardToRunningInstance(string[] args, int maxAttempts = 15)
        {
            var (packCode, autoInstall) = ExtractPackFromArgs(args);
            if (string.IsNullOrEmpty(packCode))
                return false;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(400);
                    using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                    writer.WriteLine(packCode);
                    writer.WriteLine(autoInstall ? "1" : "0");
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

        public static void StartServer(Action<string, bool> onDeepLinkReceived, CancellationToken ct = default)
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
                        var packCode = await reader.ReadLineAsync(ct);
                        var autoLine = await reader.ReadLineAsync(ct);
                        server.Disconnect();

                        if (!string.IsNullOrWhiteSpace(packCode) && ModPackCodeValidator.IsValid(packCode))
                        {
                            var auto = autoLine == "1";
                            onDeepLinkReceived(ModPackCodeValidator.Normalize(packCode), auto);
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
