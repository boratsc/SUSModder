using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SUSModder.Platform.Examples;

/// <summary>
/// Przykładowa implementacja cross-platform path providera
/// </summary>
public interface IPathProvider
{
    /// <summary>
    /// Domyślna ścieżka instalacji modów
    /// </summary>
    string GetDefaultModsPath();

    /// <summary>
    /// Ścieżka konfiguracji Among Us
    /// </summary>
    string GetAmongUsConfigPath();

    /// <summary>
    /// Ścieżka pulpitu
    /// </summary>
    string GetDesktopPath();

    /// <summary>
    /// Ścieżka cache aplikacji
    /// </summary>
    string GetCachePath();

    /// <summary>
    /// Ścieżka logs aplikacji
    /// </summary>
    string GetLogsPath();
}

/// <summary>
/// Windows implementation
/// </summary>
public class WindowsPathProvider : IPathProvider
{
    public string GetDefaultModsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Among Us - Mody");
    }

    public string GetAmongUsConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Innersloth", "Among Us");
    }

    public string GetDesktopPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    public string GetCachePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SUSModder", "cache");
    }

    public string GetLogsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SUSModder", "logs");
    }
}

/// <summary>
/// Linux implementation
/// </summary>
public class LinuxPathProvider : IPathProvider
{
    public string GetDefaultModsPath()
    {
        // XDG Base Directory Specification
        string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".local/share");

        return Path.Combine(xdgDataHome, "among-us-mody");
    }

    public string GetAmongUsConfigPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Spróbuj znaleźć w Proton prefix (Steam)
        string protonPath = Path.Combine(home,
            ".steam/steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
            "AppData/LocalLow/Innersloth/Among Us");

        if (Directory.Exists(protonPath))
            return protonPath;

        // Szukaj w innych możliwych lokalizacjach Proton
        var alternativePaths = new[]
        {
            // Flatpak Steam
            Path.Combine(home,
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
                "AppData/LocalLow/Innersloth/Among Us"),

            // Snap Steam
            Path.Combine(home,
                "snap/steam/common/.local/share/Steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
                "AppData/LocalLow/Innersloth/Among Us")
        };

        foreach (var path in alternativePaths)
        {
            if (Directory.Exists(path))
                return path;
        }

        // Szukaj dynamicznie we wszystkich Proton prefixach
        return FindAmongUsConfigInProtonPrefixes() ?? string.Empty;
    }

    private string? FindAmongUsConfigInProtonPrefixes()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var steamPaths = new[]
        {
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam")
        };

        foreach (var steamPath in steamPaths)
        {
            string compatDataPath = Path.Combine(steamPath, "steamapps/compatdata");

            if (!Directory.Exists(compatDataPath))
                continue;

            // Przeszukaj wszystkie Proton prefixy
            try
            {
                foreach (var prefix in Directory.GetDirectories(compatDataPath))
                {
                    string configPath = Path.Combine(prefix,
                        "pfx/drive_c/users/steamuser/AppData/LocalLow/Innersloth/Among Us");

                    if (Directory.Exists(configPath))
                        return configPath;
                }
            }
            catch
            {
                // Ignore permission errors
            }
        }

        return null;
    }

    public string GetDesktopPath()
    {
        // Spróbuj użyć xdg-user-dir
        string? xdgDesktop = GetXdgUserDir("DESKTOP");
        if (xdgDesktop != null)
            return xdgDesktop;

        // Fallback
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop");
    }

    public string GetCachePath()
    {
        string xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".cache");

        return Path.Combine(xdgCacheHome, "susmodder");
    }

    public string GetLogsPath()
    {
        // Logs zwykle w ~/.local/share/susmodder/logs
        string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".local/share");

        return Path.Combine(xdgDataHome, "susmodder", "logs");
    }

    private string? GetXdgUserDir(string type)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-user-dir",
                Arguments = type,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                string path = process.StandardOutput.ReadToEnd().Trim();

                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch
        {
            // xdg-user-dir może nie być zainstalowany
        }

        return null;
    }
}

/// <summary>
/// Factory dla tworzenia odpowiedniego providera
/// </summary>
public static class PathProviderFactory
{
    public static IPathProvider Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPathProvider();
        }
        else if (OperatingSystem.IsLinux())
        {
            return new LinuxPathProvider();
        }
        else if (OperatingSystem.IsMacOS())
        {
            // TODO: Implementacja macOS
            throw new PlatformNotSupportedException("macOS not yet supported");
        }

        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}

/// <summary>
/// Przykład użycia
/// </summary>
public class PathProviderExample
{
    public static void Main()
    {
        // Dependency Injection approach
        IPathProvider pathProvider = PathProviderFactory.Create();

        Console.WriteLine("Platform: " + (OperatingSystem.IsWindows() ? "Windows" : "Linux"));
        Console.WriteLine("Default Mods Path: " + pathProvider.GetDefaultModsPath());
        Console.WriteLine("Among Us Config Path: " + pathProvider.GetAmongUsConfigPath());
        Console.WriteLine("Desktop Path: " + pathProvider.GetDesktopPath());
        Console.WriteLine("Cache Path: " + pathProvider.GetCachePath());
        Console.WriteLine("Logs Path: " + pathProvider.GetLogsPath());

        // Przykład użycia w service
        var modService = new ModService(pathProvider);
        modService.InstallMod();
    }
}

/// <summary>
/// Przykładowy service używający IPathProvider
/// </summary>
public class ModService
{
    private readonly IPathProvider _pathProvider;

    public ModService(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public void InstallMod()
    {
        string modsPath = _pathProvider.GetDefaultModsPath();

        // Upewnij się że katalog istnieje
        if (!Directory.Exists(modsPath))
        {
            Directory.CreateDirectory(modsPath);
        }

        Console.WriteLine($"Installing mod to: {modsPath}");

        // ... logika instalacji moda
    }
}
