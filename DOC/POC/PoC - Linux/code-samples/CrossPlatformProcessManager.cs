using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SUSModder.Platform.Examples;

/// <summary>
/// Interface dla zarządzania procesami cross-platform
/// </summary>
public interface IProcessManager
{
    /// <summary>
    /// Uruchamia grę Among Us
    /// </summary>
    Task<bool> LaunchAmongUsAsync(string installPath);

    /// <summary>
    /// Otwiera folder w system file manager
    /// </summary>
    void OpenFolder(string path);

    /// <summary>
    /// Otwiera URL w przeglądarce
    /// </summary>
    void OpenUrl(string url);

    /// <summary>
    /// Tworzy skrót na pulpicie
    /// </summary>
    Task CreateDesktopShortcutAsync(string name, string targetPath, string workingDirectory);
}

/// <summary>
/// Windows implementation
/// </summary>
public class WindowsProcessManager : IProcessManager
{
    public async Task<bool> LaunchAmongUsAsync(string installPath)
    {
        try
        {
            string exePath = Path.Combine(installPath, "Among Us.exe");

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"Among Us.exe not found at {exePath}");
            }

            // Utwórz steam_appid.txt (dla Steam DRM)
            string steamAppIdPath = Path.Combine(installPath, "steam_appid.txt");
            await File.WriteAllTextAsync(steamAppIdPath, "945360");

            // Opcja 1: Bezpośrednie uruchomienie .exe
            Process.Start(exePath);

            // Opcja 2: Przez Steam URI (alternatywnie)
            // Process.Start(new ProcessStartInfo("steam://rungameid/945360") { UseShellExecute = true });

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to launch game: {ex.Message}");
            return false;
        }
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public async Task CreateDesktopShortcutAsync(string name, string targetPath, string workingDirectory)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string shortcutPath = Path.Combine(desktopPath, $"{name}.lnk");

        // Użyj WScript.Shell COM object
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            throw new InvalidOperationException("WScript.Shell not available");
        }

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell == null)
        {
            throw new InvalidOperationException("Failed to create WScript.Shell instance");
        }

        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = $"Launch {name}";
            shortcut.Save();

            await Task.CompletedTask;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }
}

/// <summary>
/// Linux implementation
/// </summary>
public class LinuxProcessManager : IProcessManager
{
    private readonly IPathProvider? _pathProvider;

    public LinuxProcessManager(IPathProvider? pathProvider = null)
    {
        _pathProvider = pathProvider;
    }

    public async Task<bool> LaunchAmongUsAsync(string installPath)
    {
        try
        {
            // Na Linux, Among Us musi być uruchomiony przez Proton (Steam)
            // Nie możemy bezpośrednio uruchomić .exe

            // Utwórz steam_appid.txt (dla Steam DRM)
            string steamAppIdPath = Path.Combine(installPath, "steam_appid.txt");
            await File.WriteAllTextAsync(steamAppIdPath, "945360");

            // Opcja 1: Steam URI (recommended)
            var steamUriPsi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = "steam://rungameid/945360",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(steamUriPsi);
            if (process != null)
            {
                return true;
            }

            // Opcja 2: Fallback - bezpośrednio przez steam command
            var steamCmdPsi = new ProcessStartInfo
            {
                FileName = "steam",
                Arguments = "steam://rungameid/945360",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var fallbackProcess = Process.Start(steamCmdPsi);
            return fallbackProcess != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to launch game: {ex.Message}");
            return false;
        }
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        // xdg-open automatycznie wykryje odpowiedni file manager
        var psi = new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
    }

    public void OpenUrl(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = url,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
    }

    public async Task CreateDesktopShortcutAsync(string name, string targetPath, string workingDirectory)
    {
        // Pobierz desktop path
        string desktopPath = GetDesktopPath();
        string desktopFile = Path.Combine(desktopPath, $"{name}.desktop");

        // Twórz .desktop file (freedesktop.org standard)
        var content = new StringBuilder();
        content.AppendLine("[Desktop Entry]");
        content.AppendLine("Version=1.0");
        content.AppendLine("Type=Application");
        content.AppendLine($"Name={name}");
        content.AppendLine($"Comment=Launch {name} mod for Among Us");

        // Dla Among Us, zawsze uruchamiamy przez Steam
        content.AppendLine("Exec=steam steam://rungameid/945360");

        content.AppendLine($"Path={workingDirectory}");
        content.AppendLine("Terminal=false");
        content.AppendLine("Categories=Game;");

        // Dodaj ikonę jeśli istnieje
        string iconPath = Path.Combine(workingDirectory, "icon.png");
        if (File.Exists(iconPath))
        {
            content.AppendLine($"Icon={iconPath}");
        }
        else
        {
            // Fallback - użyj ikony Steam
            content.AppendLine("Icon=steam");
        }

        // Zapisz plik
        await File.WriteAllTextAsync(desktopFile, content.ToString());

        // Ustaw uprawnienia wykonywania
        var chmodPsi = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{desktopFile}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var chmodProcess = Process.Start(chmodPsi);
        if (chmodProcess != null)
        {
            await chmodProcess.WaitForExitAsync();
        }
    }

    private string GetDesktopPath()
    {
        // Spróbuj użyć PathProvider jeśli dostępny
        if (_pathProvider != null)
        {
            return _pathProvider.GetDesktopPath();
        }

        // Fallback - spróbuj xdg-user-dir
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-user-dir",
                Arguments = "DESKTOP",
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
            // Ignore errors
        }

        // Final fallback
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop");
    }
}

/// <summary>
/// Factory dla ProcessManager
/// </summary>
public static class ProcessManagerFactory
{
    public static IProcessManager Create(IPathProvider? pathProvider = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsProcessManager();
        }
        else if (OperatingSystem.IsLinux())
        {
            return new LinuxProcessManager(pathProvider);
        }

        throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}

/// <summary>
/// Przykład użycia
/// </summary>
public class ProcessManagerExample
{
    public static async Task Main()
    {
        IPathProvider pathProvider = PathProviderFactory.Create();
        IProcessManager processManager = ProcessManagerFactory.Create(pathProvider);

        Console.WriteLine("=== Process Manager Examples ===\n");

        // 1. Uruchomienie gry
        Console.WriteLine("1. Launching Among Us...");
        string gamePath = "/path/to/Among Us"; // Replace with actual path
        bool launched = await processManager.LaunchAmongUsAsync(gamePath);
        Console.WriteLine($"   Launch successful: {launched}\n");

        // 2. Otwieranie folderu
        Console.WriteLine("2. Opening folder...");
        string folderPath = pathProvider.GetDefaultModsPath();
        processManager.OpenFolder(folderPath);
        Console.WriteLine($"   Opened: {folderPath}\n");

        // 3. Otwieranie URL
        Console.WriteLine("3. Opening URL...");
        processManager.OpenUrl("https://github.com/SUSModder/SUSModder");
        Console.WriteLine("   URL opened in browser\n");

        // 4. Tworzenie skrótu
        Console.WriteLine("4. Creating desktop shortcut...");
        string modPath = Path.Combine(pathProvider.GetDefaultModsPath(), "TownOfUs");
        await processManager.CreateDesktopShortcutAsync(
            "Town of Us",
            Path.Combine(modPath, "Among Us.exe"),
            modPath);
        Console.WriteLine("   Desktop shortcut created\n");
    }
}

/// <summary>
/// Integration example w ViewModel
/// </summary>
public class GameLaunchViewModel
{
    private readonly IProcessManager _processManager;
    private readonly IPathProvider _pathProvider;

    public GameLaunchViewModel(IProcessManager processManager, IPathProvider pathProvider)
    {
        _processManager = processManager;
        _pathProvider = pathProvider;
    }

    public async Task LaunchGameAsync(string modName)
    {
        try
        {
            string modPath = Path.Combine(_pathProvider.GetDefaultModsPath(), modName);

            if (!Directory.Exists(modPath))
            {
                throw new DirectoryNotFoundException($"Mod not found: {modName}");
            }

            bool success = await _processManager.LaunchAmongUsAsync(modPath);

            if (!success)
            {
                throw new InvalidOperationException("Failed to launch game");
            }

            Console.WriteLine($"Successfully launched {modName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error launching game: {ex.Message}");
            throw;
        }
    }

    public void OpenModFolder(string modName)
    {
        string modPath = Path.Combine(_pathProvider.GetDefaultModsPath(), modName);
        _processManager.OpenFolder(modPath);
    }

    public async Task CreateShortcutForMod(string modName)
    {
        string modPath = Path.Combine(_pathProvider.GetDefaultModsPath(), modName);
        string exePath = Path.Combine(modPath, "Among Us.exe");

        await _processManager.CreateDesktopShortcutAsync(modName, exePath, modPath);
    }
}
