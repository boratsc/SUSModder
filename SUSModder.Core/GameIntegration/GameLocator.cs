using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;

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

        public static string? TryFindAmongUsPath(out string? mode)
        {
            Console.WriteLine("Rozpoczęto wyszukiwanie Among Us.");
            mode = null;

            foreach (var basePath in CommonSteamPaths)
            {
                var path = Path.Combine(basePath, "steamapps", "common", "Among Us");
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    Console.WriteLine($"Znaleziono grę w Steam: {path}");
                    mode = "steam";
                    return path.Replace("\\\\", "\\").Replace("/", "\\");
                }
            }

            foreach (var basePath in CommonEpicPaths)
            {
                var path = Path.Combine(basePath, "AmongUs");
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")) && Directory.Exists(Path.Combine(path, ".egstore")))
                {
                    Console.WriteLine($"Znaleziono grę w Epic: {path}");
                    mode = "epic";
                    return path.Replace("\\\\", "\\").Replace("/", "\\");
                }
            }

            Console.WriteLine("Nie znaleziono gry Among Us.");
            return null;
        }

        // Dodaj asynchroniczną metodę
        public static async Task<bool> CheckAndSetupVanillaModAsync(
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
                return true;
            }

            string? foundPath = TryFindAmongUsPath(out string? detectedMode);

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

                        // Sprawdź czy to Steam czy Epic na podstawie struktury folderów
                        if (foundPath != null && (
                            Directory.Exists(Path.Combine(foundPath, ".egstore")) ||
                            Directory.Exists(Path.Combine(foundPath, "Among Us_Data", "StreamingAssets", "aa", "EGS"))
                        ))
                        {
                            detectedMode = "epic";
                        }
                        else if (foundPath != null)
                        {
                            detectedMode = "steam";
                        }
                    }
                    else
                    {
                        await userInteraction.ShowErrorAsync(
                            "Nie wybrano prawidłowego pliku Among Us.exe. Aplikacja będzie działać bez podstawowej wersji gry.",
                            "Ostrzeżenie"
                        );
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("Nie znaleziono gry i brak interfejsu użytkownika do wyboru ścieżki.");
                    return false;
                }
            }

            if (detectedMode != null && foundPath != null)
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
                    Description = $"Detected as {detectedMode}"
                };

                modConfigs.Add(vanillaMod);
                ConfigManager.SaveConfig(modConfigs);

                configuration["Configuration:Mode"] = detectedMode;
                ConfigManager.SaveConfigurationSetting("Mode", detectedMode);

                if (userInteraction != null)
                {
                    await userInteraction.ShowInfoAsync(
                        $"Pomyślnie dodano Among Us ({detectedMode}) do listy modów.",
                        "Sukces"
                    );
                }

                return true;
            }

            return false;
        }


        // Zachowaj starą metodę dla kompatybilności wstecznej
        public static void CheckAndSetupVanillaMod(
            System.Collections.Generic.List<ModConfiguration> modConfigs,
            IConfiguration configuration,
            IUserInteraction? userInteraction = null)
        {
            // Wywołaj asynchroniczną wersję synchronicznie (nie zalecane, ale dla kompatybilności)
            var task = CheckAndSetupVanillaModAsync(modConfigs, configuration, userInteraction);
            task.Wait();
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
