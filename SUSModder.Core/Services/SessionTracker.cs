using System;
using System.Diagnostics;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Śledzi czas trwania sesji użytkownika
    /// </summary>
    public class SessionTracker
    {
        private readonly Stopwatch _stopwatch;
        private DateTime _sessionStartTime;

        public SessionTracker()
        {
            _stopwatch = new Stopwatch();
            _sessionStartTime = DateTime.UtcNow;
            _stopwatch.Start();
        }

        /// <summary>
        /// Pobiera czas sesji w sekundach
        /// </summary>
        public int GetSessionTimeSeconds()
        {
            return (int)_stopwatch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Pobiera czas rozpoczęcia sesji
        /// </summary>
        public DateTime GetSessionStartTime()
        {
            return _sessionStartTime;
        }

        /// <summary>
        /// Resetuje licznik sesji (np. po wysłaniu heartbeat)
        /// </summary>
        public void Reset()
        {
            _stopwatch.Restart();
            _sessionStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Zatrzymuje tracking (przy zamykaniu aplikacji)
        /// </summary>
        public void Stop()
        {
            _stopwatch.Stop();
        }
    }
}
