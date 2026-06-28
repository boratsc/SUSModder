using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SUSModder.Services
{
    /// <summary>
    /// Nasłuchuje na localhost:{port} na callback z Discord OAuth2.
    /// Używa TcpListener (nie HttpListener) — brak zależności od HTTP.SYS, port zawsze zwalniany.
    /// Obsługuje tylko jeden callback, potem kończy nasłuchiwanie.
    /// </summary>
    public class OAuthLoopbackListener : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        /// <summary>Wywoływany po otrzymaniu kodu autoryzacyjnego (code, state).</summary>
        public event Action<string, string?>? CodeReceived;

        /// <summary>Wywoływany w przypadku błędu.</summary>
        public event Action<string>? ErrorOccurred;

    /// <summary>
    /// Startuje nasłuchiwanie na http://127.0.0.1:{port}/
    /// Może być wywołane wielokrotnie — tworzy nowy TcpListener za każdym razem.
    /// </summary>
    public Task StartAsync(int port)
    {
        // Zatrzymaj poprzedni listener jeśli istnieje
        StopInternal();

        _cts = new CancellationTokenSource();

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            System.Diagnostics.Debug.WriteLine($"[OAuthLoopbackListener] TcpListener started on 127.0.0.1:{port}");
        }
        catch (Exception ex)
        {
            _listener = null;
            System.Diagnostics.Debug.WriteLine($"[OAuthLoopbackListener] Failed to start: {ex.Message}");
            ErrorOccurred?.Invoke($"Nie można uruchomić nasłuchiwania na porcie {port}: {ex.Message}");
            return Task.CompletedTask;
        }

        _ = ListenLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener!.AcceptTcpClientAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    await HandleClientAsync(client, ct);
                    break; // Obsługujemy tylko jeden callback
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OAuthLoopbackListener] Listen loop error: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Parsuj pierwszą linię: GET /susmodder/callback?code=xxx HTTP/1.1
                var firstLine = request.Split('\n')[0].Trim();
                var parts = firstLine.Split(' ');
                var path = parts.Length > 1 ? parts[1] : "/";

                // Wyciągnij code i state z query string
                string? code = null;
                string? state = null;
                string? error = null;

                if (path.StartsWith("/susmodder/callback"))
                {
                    var queryIndex = path.IndexOf('?');
                    if (queryIndex >= 0)
                    {
                        var query = path[(queryIndex + 1)..];
                        var qparts = query.Split('&');
                        foreach (var part in qparts)
                        {
                            var kv = part.Split('=', 2);
                            if (kv.Length == 2)
                            {
                                var key = Uri.UnescapeDataString(kv[0]);
                                var val = Uri.UnescapeDataString(kv[1]);
                                if (key == "code") code = val;
                                if (key == "state") state = val;
                                if (key == "error") error = val;
                            }
                        }
                    }
                }

                // Zwróć response HTML
                var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>SUSModder</title>" +
                    "<style>body{font-family:Arial;text-align:center;padding:40px;background:#1a1a2e;color:#fff;}" +
                    "h1{color:#5865F2;}.card{background:#16213e;border-radius:8px;padding:20px;max-width:420px;margin:auto;}" +
                    "</style></head><body><div class='card'>" +
                    "<h1>&#x2705; Autoryzacja zakończona</h1>" +
                    "<p>Możesz zamknąć to okno i wrócić do aplikacji SUSModder.</p>" +
                    "<p lang='en'>You can close this window and return to SUSModder.</p>" +
                    "</div></body></html>";

                var responseBytes = Encoding.UTF8.GetBytes(html);
                var responseHeader = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.UTF8.GetBytes(responseHeader);

                await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                await stream.FlushAsync(ct);

                // Fire eventy
                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine($"[OAuthLoopbackListener] Error received: {error}");
                    ErrorOccurred?.Invoke(error);
                }
                else if (!string.IsNullOrEmpty(code))
                {
                    System.Diagnostics.Debug.WriteLine("[OAuthLoopbackListener] Authorization code received.");
                    CodeReceived?.Invoke(code, state);
                }
                else
                {
                    ErrorOccurred?.Invoke("Callback nie zawiera kodu autoryzacyjnego.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OAuthLoopbackListener] HandleClient error: {ex.Message}");
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            StopInternal();
            System.Diagnostics.Debug.WriteLine("[OAuthLoopbackListener] Disposed.");
        }
    }
}
