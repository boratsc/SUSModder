using System;
using System.IO;
using System.Threading.Tasks;

namespace SUSModder.Core.Utilities
{
    public static class FixBlackScreen
    {
        // Synchroniczna wersja tylko do operacji na plikach
        public static void ExecuteFixCore()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string targetDirectory = Path.Combine(userProfile, @"AppData\LocalLow\Innersloth\Among Us");

            if (!Directory.Exists(targetDirectory))
            {
                throw new DirectoryNotFoundException("Katalog Among Us nie został znaleziony.");
            }

            // Usunięcie zawartości poza plikami .txt i regionInfo.json
            foreach (var filePath in Directory.GetFiles(targetDirectory))
            {
                string fileName = Path.GetFileName(filePath);

                bool isTxtFile = fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
                bool isRegionInfo = string.Equals(fileName, "regionInfo.json", StringComparison.OrdinalIgnoreCase);

                if (!isTxtFile && !isRegionInfo)
                {
                    File.Delete(filePath);
                }
            }

            foreach (var directoryPath in Directory.GetDirectories(targetDirectory))
            {
                Directory.Delete(directoryPath, true);
            }
        }

        // Stara wersja z UI - zachowaj dla kompatybilności
        public static void ExecuteFix(IUserInteraction userInteraction)
        {
            bool confirmed = userInteraction.Confirm(
                "Czy jesteś pewny, że chcesz zrestartować ustawienia gry?",
                "Potwierdzenie"
            );
            if (!confirmed)
                return;

            try
            {
                ExecuteFixCore();
                userInteraction.ShowInfo("Ustawienia gry zostały zresetowane.", "Sukces");
            }
            catch (Exception ex)
            {
                userInteraction.ShowError($"Wystąpił błąd podczas resetowania ustawień: {ex.Message}", "Błąd");
            }
        }
    }
}
