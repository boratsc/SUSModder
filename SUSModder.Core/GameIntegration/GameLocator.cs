using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Services;

namespace SUSModder.Core.GameIntegration
{
    public static class GameLocator
    {
        private static readonly string[] CommonSteamPaths =
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Steam"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Steam"),
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Steam"),
            "D:/Steam",
            "D:/",
            "D:/Gry/Steam"
        };

        private static readonly string[] CommonEpicPaths =
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Epic Games"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Epic Games"),
            "D:/Epic Games",
            "D:/Gry/Epic Games",
            "D:/Gry/Epic"
        };

        /// <summary>
        /// Wyszukuje ścieżkę do Among Us. Platforma (Steam/Epic) jest określana przez użytkownika w dialogu przy pierwszym uruchomieniu.
        /// </summary>
        public static string? TryFindAmongUsPath()
        {
            Console.WriteLine("Rozpoczęto wyszukiwanie Among Us.");

            // Sprawdź najpierw w ścieżkach Steam
            foreach (var basePath in CommonSteamPaths)
            {
                var path = Path.Combine(basePath, "steamapps", "common", "Among Us");
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    Console.WriteLine($"Znaleziono grę: {path}");
                    return path.Replace("\\\\", "\\").Replace("/", "\\");
                }
            }

            // Sprawdź w ścieżkach Epic
            foreach (var basePath in CommonEpicPaths)
            {
                var path = Path.Combine(basePath, "AmongUs");
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    Console.WriteLine($"Znaleziono grę: {path}");
                    return path.Replace("\\\\", "\\").Replace("/", "\\");
                }
            }

            Console.WriteLine("Nie znaleziono gry Among Us.");
            return null;
        }

        /// <summary>
        /// Sprawdza i konfiguruje Vanilla mod (podstawowa gra Among Us).
        /// Platforma (Steam/Epic) jest pobierana z UserSettings - użytkownik wybiera ją przy pierwszym uruchomieniu.
        /// </summary>
        public static async Task<ModConfiguration?> CheckAndSetupVanillaModAsync(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            IUserInteraction? userInteraction = null)
        {
            var existingConfig = modConfigs.FirstOrDefault(x => x.ModName == "AmongUs" &&
                                                                x.Id == 0 &&
                                                                !string.IsNullOrEmpty(x.InstallPath));

            if (existingConfig != null)
            {
                Console.WriteLine("Among Us już zainstalowano z wersją Vanilla.");
                return null; // już istnieje
            }

            // Pobierz platformę z UserSettings (wybraną przez użytkownika przy pierwszym uruchomieniu)
            var userSettingsService = new UserSettingsService();
            var userMode = userSettingsService.LoadUserSettings().Mode;
            
            if (string.IsNullOrEmpty(userMode))
            {
                Console.WriteLine("Platforma nie została wybrana przez użytkownika.");
                return null;
            }

            string? foundPath = TryFindAmongUsPath();

            if (foundPath == null)
            {
                // Jeśli nie znaleziono automatycznie, poproś użytkownika o wskazanie
                if (userInteraction != null)
                {
                    var userSelectedPath = await userInteraction.ShowSelectFileDialogAsync(
                        "Among Us executable|*.exe",
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    );

                    if (!string.IsNullOrEmpty(userSelectedPath) && File.Exists(userSelectedPath))
                    {
                        foundPath = Path.GetDirectoryName(userSelectedPath);
                    }
                    else
                    {
                        await userInteraction.ShowErrorAsync(
                            "Nie wybrano prawidłowego pliku Among Us.exe. Aplikacja będzie działać bez podstawowej wersji gry.",
                            "Ostrzeżenie"
                        );
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine("Nie znaleziono gry i brak interfejsu użytkownika do wyboru ścieżki.");
                    return null;
                }
            }

            if (foundPath != null)
            {
                var vanillaMod = new ModConfiguration
                {
                    ModName = "AmongUs",
                    PngFileName = "Vanilla.png",
                    InstallPath = foundPath,
                    GitHubRepoOrLink = string.Empty,
                    EpicGitHubRepoOrLink = string.Empty,
                    ModType = "Vanilla",
                    DllInstallPath = null,
                    ModVersion = "",
                    LastUpdated = DateTime.Now,
                    AmongVersion = GetGameVersion(foundPath),
                    Description = $"Platform: {userMode}"
                };

                // Dodaj do listy i zapisz
                modConfigs.Add(vanillaMod);
                ConfigManager.SaveConfig(modConfigs);

                // Synchronizuj Mode z UserSettings do appsettings.json (dla kompatybilności)
                configuration["Configuration:Mode"] = userMode;
                ConfigManager.SaveConfigurationSetting("Mode", userMode);

                Console.WriteLine($"Among Us ({userMode}) został dodany do listy modów.");

                return vanillaMod;
            }

            return null;
        }

        private static string GetGameVersion(string path)
        {
            try
            {
                var exePath = Path.Combine(path, "Among Us.exe");
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? "Nieznana";
            }
            catch
            {
                Console.WriteLine($"Nie udało się odczytać wersji gry.");
                return "Nieznana";
            }
        }
    }
}
