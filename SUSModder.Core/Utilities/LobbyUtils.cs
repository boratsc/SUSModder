using System;
using System.IO;
using Newtonsoft.Json;

namespace SUSModder.Core.Utilities
{
    public static class LobbyUtils
    {
        public static string GenerateCustomCode(int value)
        {
            byte[] bytes = new byte[] { 0x00, (byte)value, 0x01 };
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Ustawia liczbę graczy w pliku konfiguracyjnym ToU.
        /// </summary>
        /// <param name="numPlayers">Liczba graczy (4-255)</param>
        /// <param name="errorMessage">Zwraca komunikat błędu jeśli wystąpił</param>
        /// <returns>true jeśli sukces, false jeśli błąd</returns>
        public static bool SetLobbyPlayers(int numPlayers, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (numPlayers < 4 || numPlayers > 255)
            {
                errorMessage = "Liczba graczy musi być w zakresie 4-255.";
                return false;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string filePath = Path.Combine(userProfile, @"AppData\LocalLow\Innersloth\Among Us\settings.amogus_TOU");

            if (!File.Exists(filePath))
            {
                errorMessage = "Brak pliku konfiguracyjnego ToU - uruchom grę z modem, zamknij i spróbuj ponownie.";
                return false;
            }

            string jsonContent = File.ReadAllText(filePath);
            dynamic? settings = JsonConvert.DeserializeObject(jsonContent);
            if (settings == null)
            {
                errorMessage = "Błąd podczas odczytu pliku konfiguracyjnego.";
                return false;
            }

            string customCode = GenerateCustomCode(numPlayers);

            string normalHostOptions = settings.multiplayer.normalHostOptions;
            if (normalHostOptions.Length < 12)
            {
                errorMessage = "Plik konfiguracyjny ma nieprawidłowy format.";
                return false;
            }

            string updatedNormalHostOptions = $"{normalHostOptions.Substring(0, 8)}{customCode}{normalHostOptions.Substring(12)}";
            settings.multiplayer.normalHostOptions = updatedNormalHostOptions;

            string updatedJsonContent = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(filePath, updatedJsonContent);

            return true;
        }
    }
}
