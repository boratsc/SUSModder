using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SUSModder.Platform.Examples;

/// <summary>
/// Interface dla lokalizacji gry
/// </summary>
public interface IGameLocator
{
    /// <summary>
    /// Znajduje ścieżkę instalacji Among Us
    /// </summary>
    string? LocateAmongUs();

    /// <summary>
    /// Znajduje wszystkie Steam library paths
    /// </summary>
    IEnumerable<string> GetSteamLibraryPaths();

    /// <summary>
    /// Sprawdza czy dana ścieżka zawiera Among Us
    /// </summary>
    bool IsAmongUsInstallation(string path);
}

/// <summary>
/// Linux implementation - Steam library detection
/// </summary>
public class LinuxGameLocator : IGameLocator
{
    private readonly IPathProvider? _pathProvider;

    public LinuxGameLocator(IPathProvider? pathProvider = null)
    {
        _pathProvider = pathProvider;
    }

    public string? LocateAmongUs()
    {
        // 1. Sprawdź standardowe lokalizacje Steam
        foreach (var steamPath in GetCommonSteamPaths())
        {
            string amongUsPath = Path.Combine(steamPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
            {
                Console.WriteLine($"Found Among Us in Steam common path: {amongUsPath}");
                return amongUsPath;
            }
        }

        // 2. Przeszukaj wszystkie Steam library folders
        foreach (var libraryPath in GetSteamLibraryPaths())
        {
            string amongUsPath = Path.Combine(libraryPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
            {
                Console.WriteLine($"Found Among Us in Steam library: {amongUsPath}");
                return amongUsPath;
            }
        }

        // 3. Szukaj w Epic Games (Heroic Launcher)
        string? epicPath = LocateInEpicGames();
        if (epicPath != null)
        {
            Console.WriteLine($"Found Among Us in Epic Games: {epicPath}");
            return epicPath;
        }

        Console.WriteLine("Among Us not found in any known location");
        return null;
    }

    public IEnumerable<string> GetSteamLibraryPaths()
    {
        var libraries = new List<string>();

        foreach (var steamPath in GetCommonSteamPaths())
        {
            if (Directory.Exists(steamPath))
            {
                libraries.Add(steamPath);

                // Parse libraryfolders.vdf dla dodatkowych bibliotek
                string vdfPath = Path.Combine(steamPath, "steamapps/libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    var additionalLibraries = ParseSteamLibraryFolders(vdfPath);
                    libraries.AddRange(additionalLibraries);
                }
            }
        }

        return libraries.Distinct();
    }

    public bool IsAmongUsInstallation(string path)
    {
        if (!Directory.Exists(path))
            return false;

        // Sprawdź obecność Among Us.exe (działa nawet na Linux przez Proton)
        string exePath = Path.Combine(path, "Among Us.exe");
        if (!File.Exists(exePath))
            return false;

        // Opcjonalnie: Sprawdź inne charakterystyczne pliki
        string unityDataPath = Path.Combine(path, "Among Us_Data");
        return Directory.Exists(unityDataPath);
    }

    private IEnumerable<string> GetCommonSteamPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var paths = new List<string>
        {
            // Native Steam
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),

            // Flatpak Steam
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),

            // Snap Steam
            Path.Combine(home, "snap/steam/common/.local/share/Steam"),

            // System-wide Steam (rzadkie)
            "/usr/share/steam",
            "/usr/local/share/steam"
        };

        return paths.Where(Directory.Exists);
    }

    private IEnumerable<string> ParseSteamLibraryFolders(string vdfPath)
    {
        var libraries = new List<string>();

        try
        {
            string content = File.ReadAllText(vdfPath);

            // VDF format:
            // "libraryfolders"
            // {
            //     "0"
            //     {
            //         "path"    "/home/user/.steam/steam"
            //     }
            //     "1"
            //     {
            //         "path"    "/mnt/games/SteamLibrary"
            //     }
            // }

            // Regex do wyciągnięcia ścieżek
            var pathRegex = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var matches = pathRegex.Matches(content);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string libraryPath = match.Groups[1].Value;

                    // Expand home directory ~ jeśli występuje
                    if (libraryPath.StartsWith("~"))
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        libraryPath = libraryPath.Replace("~", home);
                    }

                    if (Directory.Exists(libraryPath))
                    {
                        libraries.Add(libraryPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse libraryfolders.vdf: {ex.Message}");
        }

        return libraries;
    }

    private string? LocateInEpicGames()
    {
        // Heroic Launcher locations
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var heroicPaths = new[]
        {
            // Native Heroic
            Path.Combine(home, "Games/Heroic/Among Us"),
            Path.Combine(home, "Games/Among Us"),

            // Flatpak Heroic
            Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/Games/Among Us"),

            // Custom Heroic install paths (check config)
            GetHeroicCustomInstallPath()
        };

        foreach (var path in heroicPaths)
        {
            if (!string.IsNullOrEmpty(path) && IsAmongUsInstallation(path))
            {
                return path;
            }
        }

        return null;
    }

    private string? GetHeroicCustomInstallPath()
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Heroic config locations
            var configPaths = new[]
            {
                Path.Combine(home, ".config/heroic/GamesConfig/Among Us.json"),
                Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic/GamesConfig/Among Us.json")
            };

            foreach (var configPath in configPaths)
            {
                if (File.Exists(configPath))
                {
                    // Parse JSON config dla install path
                    string config = File.ReadAllText(configPath);

                    // Simple regex extraction (proper JSON parsing recommended)
                    var installRegex = new Regex(@"""install"":\s*\{\s*""install_path"":\s*""([^""]+)""");
                    var match = installRegex.Match(config);

                    if (match.Success && match.Groups.Count > 1)
                    {
                        string installPath = match.Groups[1].Value;

                        // Unescape JSON paths
                        installPath = installPath.Replace("\\/", "/");

                        if (Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse Heroic config: {ex.Message}");
        }

        return null;
    }
}

/// <summary>
/// Windows implementation dla porównania
/// </summary>
public class WindowsGameLocator : IGameLocator
{
    public string? LocateAmongUs()
    {
        // 1. Sprawdź Steam
        foreach (var steamPath in GetCommonSteamPaths())
        {
            string amongUsPath = Path.Combine(steamPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
            {
                return amongUsPath;
            }
        }

        // 2. Sprawdź Epic
        foreach (var epicPath in GetCommonEpicPaths())
        {
            string amongUsPath = Path.Combine(epicPath, "Among Us");
            if (IsAmongUsInstallation(amongUsPath))
            {
                return amongUsPath;
            }
        }

        return null;
    }

    public IEnumerable<string> GetSteamLibraryPaths()
    {
        var libraries = new List<string>();

        foreach (var steamPath in GetCommonSteamPaths())
        {
            if (Directory.Exists(steamPath))
            {
                libraries.Add(steamPath);

                // Parse libraryfolders.vdf
                string vdfPath = Path.Combine(steamPath, "steamapps/libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    // Parse logic (simplified)
                    // W rzeczywistości użyj parsera VDF
                }
            }
        }

        return libraries;
    }

    public bool IsAmongUsInstallation(string path)
    {
        if (!Directory.Exists(path))
            return false;

        string exePath = Path.Combine(path, "Among Us.exe");
        return File.Exists(exePath);
    }

    private IEnumerable<string> GetCommonSteamPaths()
    {
        return new[]
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%\\Steam"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\Steam"),
            "C:\\Steam",
            "D:\\Steam",
            "D:\\Gry\\Steam"
        };
    }

    private IEnumerable<string> GetCommonEpicPaths()
    {
        return new[]
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%\\Epic Games"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\Epic Games"),
            "D:\\Epic Games"
        };
    }
}

/// <summary>
/// Factory
/// </summary>
public static class GameLocatorFactory
{
    public static IGameLocator Create(IPathProvider? pathProvider = null)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return new WindowsGameLocator();
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Linux))
        {
            return new LinuxGameLocator(pathProvider);
        }

        throw new PlatformNotSupportedException();
    }
}

/// <summary>
/// Przykład użycia
/// </summary>
public class GameLocatorExample
{
    public static void Main()
    {
        Console.WriteLine("=== Game Locator Example ===\n");

        IGameLocator locator = GameLocatorFactory.Create();

        // 1. Znajdź Among Us
        Console.WriteLine("1. Locating Among Us...");
        string? gamePath = locator.LocateAmongUs();

        if (gamePath != null)
        {
            Console.WriteLine($"   Found: {gamePath}");

            // Sprawdź czy to valid installation
            bool isValid = locator.IsAmongUsInstallation(gamePath);
            Console.WriteLine($"   Valid installation: {isValid}\n");
        }
        else
        {
            Console.WriteLine("   Among Us not found\n");
        }

        // 2. Wylistuj wszystkie Steam libraries
        Console.WriteLine("2. Steam Library Paths:");
        foreach (var library in locator.GetSteamLibraryPaths())
        {
            Console.WriteLine($"   - {library}");
        }
    }
}

/// <summary>
/// Integration w service
/// </summary>
public class GameService
{
    private readonly IGameLocator _gameLocator;
    private readonly IPathProvider _pathProvider;

    public GameService(IGameLocator gameLocator, IPathProvider pathProvider)
    {
        _gameLocator = gameLocator;
        _pathProvider = pathProvider;
    }

    public string? GetAmongUsPath()
    {
        // Spróbuj auto-detect
        string? path = _gameLocator.LocateAmongUs();

        if (path != null)
        {
            Console.WriteLine($"Auto-detected Among Us at: {path}");
            return path;
        }

        // Jeśli nie znaleziono, poproś użytkownika o manual path
        Console.WriteLine("Among Us not found. Please select installation manually.");
        return null;
    }

    public bool ValidateGamePath(string path)
    {
        return _gameLocator.IsAmongUsInstallation(path);
    }

    public IEnumerable<string> GetAllPossibleGameLocations()
    {
        var locations = new List<string>();

        // Wszystkie Steam libraries
        foreach (var library in _gameLocator.GetSteamLibraryPaths())
        {
            string amongUsPath = Path.Combine(library, "steamapps/common/Among Us");
            locations.Add(amongUsPath);
        }

        return locations;
    }
}
