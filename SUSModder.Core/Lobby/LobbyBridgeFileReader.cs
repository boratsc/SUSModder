using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Lobby
{
    /// <summary>
    /// Monitoruje plik %APPDATA%/SUSModder/lobby-bridge.json za pomocą FileSystemWatcher.
    /// Gdy SUSModder.Integration.dll (BepInEx plugin) zapisze kod lobby,
    /// LobbyBridgeFileReader odczytuje go i emituje zdarzenie LobbyCodeDetected.
    ///
    /// Użycie:
    ///   var reader = new LobbyBridgeFileReader(bridgePath, logger);
    ///   reader.LobbyCodeDetected += OnCodeDetected;
    ///   reader.Start();
    ///   // ...
    ///   reader.Dispose();
    /// </summary>
    public sealed class LobbyBridgeFileReader : IDisposable
    {
        private readonly string _bridgeFilePath;
        private readonly IDiagnosticsOutput _log;
        private FileSystemWatcher? _watcher;
        private bool _disposed;

        /// <summary>
        /// Czas ważności wpisu bridge (90 sekund).
        /// Starsze wpisy są ignorowane.
        /// </summary>
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Zdarzenie emitowane po wykryciu poprawnego kodu lobby z pliku bridge.
        /// Wywoływane na wątku FileSystemWatcher (NIE UI).
        /// Subskrybenci muszą marshallingować na UI thread jeśli potrzebują.
        /// </summary>
        public event EventHandler<LobbyCodeDetectedEventArgs>? LobbyCodeDetected;

        /// <summary>
        /// Tworzy reader dla domyślnej ścieżki %APPDATA%/SUSModder/lobby-bridge.json.
        /// </summary>
        public LobbyBridgeFileReader(IDiagnosticsOutput log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _bridgeFilePath = Path.Combine(appData, "SUSModder", "lobby-bridge.json");
        }

        /// <summary>
        /// Tworzy reader dla wskazanej ścieżki.
        /// </summary>
        public LobbyBridgeFileReader(string bridgeFilePath, IDiagnosticsOutput log)
        {
            _bridgeFilePath = bridgeFilePath ?? throw new ArgumentNullException(nameof(bridgeFilePath));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Uruchamia monitorowanie pliku bridge.
        /// Bezpieczne do wielokrotnego wywołania (idempotentne).
        /// </summary>
        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LobbyBridgeFileReader));

            if (_watcher != null)
                return; // Już uruchomiony

            var dir = Path.GetDirectoryName(_bridgeFilePath);
            var fileName = Path.GetFileName(_bridgeFilePath);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName))
            {
                _log.Write($"[LobbyBridge] Nieprawidłowa ścieżka: {_bridgeFilePath}");
                return;
            }

            // Upewnij się, że katalog istnieje
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _watcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = false // Włączymy po skonfigurowaniu
            };

            _watcher.Changed += OnBridgeFileChanged;
            _watcher.Created += OnBridgeFileChanged;
            _watcher.Renamed += OnBridgeFileChanged;
            _watcher.Error += OnWatcherError;

            _watcher.EnableRaisingEvents = true;
            _log.Write($"[LobbyBridge] Monitorowanie pliku: {_bridgeFilePath}");
        }

        /// <summary>
        /// Zatrzymuje monitorowanie (bez dispose).
        /// </summary>
        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnBridgeFileChanged;
                _watcher.Created -= OnBridgeFileChanged;
                _watcher.Renamed -= OnBridgeFileChanged;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
                _log.Write("[LobbyBridge] Monitorowanie zatrzymane.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
        }

        // ──────────────────────────────────────────────
        // Handlery FileSystemWatcher
        // ──────────────────────────────────────────────

        private void OnBridgeFileChanged(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher może wywołać zdarzenie wielokrotnie dla jednej zmiany.
            // Dodajemy krótkie opóźnienie aby poczekać na zakończenie atomic rename.
            Thread.Sleep(50);

            try
            {
                ProcessBridgeFile();
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBridge] Błąd przetwarzania: {ex.Message}");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _log.Write($"[LobbyBridge] Błąd FileSystemWatcher: {e.GetException()?.Message}");
        }

        // ──────────────────────────────────────────────
        // Logika odczytu
        // ──────────────────────────────────────────────

        private void ProcessBridgeFile()
        {
            if (!File.Exists(_bridgeFilePath))
            {
                _log.Write("[LobbyBridge] Plik bridge nie istnieje.");
                return;
            }

            string json;
            try
            {
                // Retry: plik mógł być jeszcze w trakcie zapisu (atomic rename już zakończony,
                // ale system plików może cache'ować. Dla pewności retry.
                json = RetryReadAllText(_bridgeFilePath, maxRetries: 2, delayMs: 30);
            }
            catch (Exception ex)
            {
                _log.Write($"[LobbyBridge] Nie można odczytać pliku: {ex.Message}");
                return;
            }

            LobbyBridgeFileData? data;
            try
            {
                data = JsonSerializer.Deserialize<LobbyBridgeFileData>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _log.Write($"[LobbyBridge] Błąd parsowania JSON: {ex.Message}");
                return;
            }

            if (data == null || string.IsNullOrWhiteSpace(data.Code))
            {
                _log.Write("[LobbyBridge] Brak kodu w danych bridge.");
                return;
            }

            // Walidacja TTL
            if (!TryParseTimestamp(data.Timestamp, out var timestamp))
            {
                _log.Write($"[LobbyBridge] Nieprawidłowy timestamp: {data.Timestamp}");
                return;
            }

            var age = DateTimeOffset.UtcNow - timestamp;
            if (age > Ttl)
            {
                _log.Write($"[LobbyBridge] Kod wygasł ({age.TotalSeconds:F0}s > {Ttl.TotalSeconds:F0}s), ignoruję.");
                return;
            }

            // Tylko publiczne lobby
            if (!data.IsPublic)
            {
                _log.Write("[LobbyBridge] Lobby jest prywatne, ignoruję.");
                return;
            }

            _log.Write($"[LobbyBridge] Wykryto kod: {data.Code} (modId={data.ModId}, region={data.Region}, {data.MaxPlayers} graczy, wiek {age.TotalSeconds:F0}s)");

            LobbyCodeDetected?.Invoke(this, new LobbyCodeDetectedEventArgs
            {
                Code = data.Code,
                ModId = data.ModId,
                Region = data.Region,
                MaxPlayers = data.MaxPlayers,
                Timestamp = timestamp
            });
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        private static string RetryReadAllText(string path, int maxRetries, int delayMs)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException) when (i < maxRetries)
                {
                    Thread.Sleep(delayMs);
                }
            }
            return File.ReadAllText(path); // Ostatnia próba — niech rzuci wyjątek
        }

        private static bool TryParseTimestamp(string timestamp, out DateTimeOffset result)
        {
            return DateTimeOffset.TryParse(timestamp, null, System.Globalization.DateTimeStyles.AssumeUniversal, out result);
        }
    }
}
