using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"),
            @"D:\SteamLibrary",
            @"D:\Steam",
            @"D:\",
            @"D:\Gry\Steam"
        };

        private static readonly string[] CommonEpicPaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games"),
            @"D:\Epic Games",
            @"D:\Gry\Epic Games",
            @"D:\Gry\Epic"
        };

        /// <summary>
        /// Wyszukuje ścieżkę do Among Us. Platforma (Steam/Epic) jest określana przez użytkownika w dialogu przy pierwszym uruchomieniu.
        /// </summary>
        public static string? TryFindAmongUsPath()
        {
            Console.WriteLine("Rozpoczęto wyszukiwanie Among Us.");

            // Sprawdź najpierw w ścieżkach Steam
            foreach (var basePath in GetSteamLibraryPaths())
            {
                var path = NormalizePath(Path.Combine(basePath, "steamapps", "common", "Among Us"));
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    Console.WriteLine($"Znaleziono grę: {path}");
                    return path;
                }
            }

            // Sprawdź w ścieżkach Epic (manifesty + fallback)
            foreach (var basePath in GetEpicInstallPaths())
            {
                var path = NormalizePath(basePath);
                Console.WriteLine($"Sprawdzam ścieżkę: {path}");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    Console.WriteLine($"Znaleziono grę: {path}");
                    return path;
                }
            }

            Console.WriteLine("Nie znaleziono gry Among Us.");
            return null;
        }

        private static IEnumerable<string> GetSteamLibraryPaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var basePath in CommonSteamPaths)
            {
                if (string.IsNullOrWhiteSpace(basePath))
                    continue;

                var normalizedBase = NormalizePath(basePath);
                if (Directory.Exists(normalizedBase))
                {
                    result.Add(normalizedBase);
                }

                var libraryFoldersPath = Path.Combine(normalizedBase, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFoldersPath))
                    continue;

                try
                {
                    var content = File.ReadAllText(libraryFoldersPath);
                    foreach (var libraryPath in ParseSteamLibraryFolders(content))
                    {
                        var normalizedLibrary = NormalizePath(libraryPath);
                        if (Directory.Exists(normalizedLibrary))
                        {
                            result.Add(normalizedLibrary);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Nie udało się odczytać libraryfolders.vdf: {ex.Message}");
                }
            }

            return result;
        }

        private static IEnumerable<string> ParseSteamLibraryFolders(string content)
        {
            // Obsługa formatu VDF: "path" "D:\\SteamLibrary"
            var matches = Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count < 2)
                    continue;

                var raw = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                yield return raw.Replace("\\\\", "\\");
            }
        }

        private static IEnumerable<string> GetEpicInstallPaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var basePath in CommonEpicPaths)
            {
                if (string.IsNullOrWhiteSpace(basePath))
                    continue;

                var candidate = NormalizePath(Path.Combine(basePath, "AmongUs"));
                if (Directory.Exists(candidate))
                {
                    result.Add(candidate);
                }
            }

            var manifestsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "EpicGamesLauncher",
                "Data",
                "Manifests"
            );

            if (!Directory.Exists(manifestsPath))
                return result;

            try
            {
                foreach (var itemFile in Directory.EnumerateFiles(manifestsPath, "*.item", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var json = File.ReadAllText(itemFile);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var installLocation = GetJsonString(root, "InstallLocation");
                        if (string.IsNullOrWhiteSpace(installLocation))
                            continue;

                        var isAmongUs = IsAmongUsEpicManifest(root);
                        if (!isAmongUs)
                            continue;

                        var normalized = NormalizePath(installLocation);
                        if (Directory.Exists(normalized))
                        {
                            result.Add(normalized);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Błąd parsowania manifestu Epic ({Path.GetFileName(itemFile)}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nie udało się odczytać manifestów Epic: {ex.Message}");
            }

            return result;
        }

        private static bool IsAmongUsEpicManifest(JsonElement root)
        {
            var displayName = GetJsonString(root, "DisplayName");
            if (!string.IsNullOrWhiteSpace(displayName) &&
                displayName.Contains("Among Us", StringComparison.OrdinalIgnoreCase))
                return true;

            var appName = GetJsonString(root, "AppName");
            if (!string.IsNullOrWhiteSpace(appName) &&
                (appName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase) ||
                 appName.Equals("Among Us", StringComparison.OrdinalIgnoreCase)))
                return true;

            var launchExe = GetJsonString(root, "LaunchExecutable");
            if (!string.IsNullOrWhiteSpace(launchExe) &&
                launchExe.EndsWith("Among Us.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string? GetJsonString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();

            return null;
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Replace('/', Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
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

                    if (string.IsNullOrWhiteSpace(userSelectedPath))
                    {
                        Console.WriteLine("Użytkownik anulował wybór pliku Among Us.exe.");
                        return null;
                    }

                    if (!File.Exists(userSelectedPath))
                    {
                        Console.WriteLine($"Wybrany plik nie istnieje: {userSelectedPath}");
                        return null;
                    }

                    if (!string.Equals(Path.GetFileName(userSelectedPath), "Among Us.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Wybrany plik nie jest Among Us.exe: {userSelectedPath}");
                        return null;
                    }

                    foundPath = Path.GetDirectoryName(userSelectedPath);
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

                // Zapisz ścieżkę Vanilla do user-settings (fallback po update)
                userSettingsService.UpdateUserSetting(settings => settings.VanillaInstallPath = foundPath);

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
