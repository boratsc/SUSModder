using System;
using System.IO;
using System.Text.Json;

namespace SUSModder.Services
{
    /// <summary>
    /// Zarządza zapisywaniem i wczytywaniem sesji SUStats (logowanie).
    /// Dane są przechowywane w katalogu TEMP jako plain text (kod nie jest tajny).
    /// </summary>
    public class SUStatsSessionManager
    {
        private static readonly string SessionFilePath = Path.Combine(
            Path.GetTempPath(),
            "SUSModder",
            "sustats_session.json"
        );

        /// <summary>
        /// Model danych sesji SUStats
        /// </summary>
        public class SessionData
        {
            public int ServerId { get; set; }
            public string Password { get; set; } = string.Empty;
            public bool IsStatsEnabled { get; set; }
        }

        /// <summary>
        /// Zapisuje dane sesji do pliku w TEMP
        /// </summary>
        public static void SaveSession(int serverId, string password, bool isStatsEnabled)
        {
            try
            {
                var sessionData = new SessionData
                {
                    ServerId = serverId,
                    Password = password,
                    IsStatsEnabled = isStatsEnabled
                };

                // Utwórz katalog jeśli nie istnieje
                var directory = Path.GetDirectoryName(SessionFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Zapisz do pliku JSON
                var json = JsonSerializer.Serialize(sessionData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SessionFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[SUStats Session] ✅ Zapisano sesję dla serwera ID: {serverId}, Stats: {isStatsEnabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Session] ❌ Błąd podczas zapisywania sesji: {ex.Message}");
            }
        }

        /// <summary>
        /// Wczytuje zapisaną sesję z pliku w TEMP
        /// </summary>
        public static SessionData? LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("[SUStats Session] Brak zapisanej sesji");
                    return null;
                }

                var json = File.ReadAllText(SessionFilePath);
                var sessionData = JsonSerializer.Deserialize<SessionData>(json);

                if (sessionData != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SUStats Session] ✅ Wczytano sesję dla serwera ID: {sessionData.ServerId}");
                }

                return sessionData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Session] ❌ Błąd podczas wczytywania sesji: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Usuwa zapisaną sesję (wylogowanie)
        /// </summary>
        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                    System.Diagnostics.Debug.WriteLine("[SUStats Session] ✅ Usunięto zapisaną sesję");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SUStats Session] ❌ Błąd podczas usuwania sesji: {ex.Message}");
            }
        }

        /// <summary>
        /// Sprawdza czy istnieje zapisana sesja
        /// </summary>
        public static bool HasSavedSession()
        {
            return File.Exists(SessionFilePath);
        }
    }
}
