# Architektura: Port SUSModder na Linux

**Data:** 2025-10-30
**Status:** Proof of Concept
**Cel:** Zaprojektowanie architektury cross-platform dla SUSModder

---

## Spis treści

1. [Obecna architektura](#obecna-architektura)
2. [Problemy z obecną architekturą](#problemy-z-obecną-architekturą)
3. [Proponowana architektura cross-platform](#proponowana-architektura-cross-platform)
4. [Warstwa abstrakcji platformy](#warstwa-abstrakcji-platformy)
5. [Refaktor komponentów](#refaktor-komponentów)
6. [Struktura projektu](#struktura-projektu)
7. [Dependency Injection](#dependency-injection)

---

## Obecna architektura

### Diagram obecnej architektury

```
┌─────────────────────────────────────────────────┐
│           SUSModder (Avalonia UI)               │
│  ┌───────────────────────────────────────────┐  │
│  │  ViewModels (MainWindowViewModel, etc.)  │  │
│  │  - BEZPOŚREDNIE wywołania Windows API    │  │
│  │  - Hardcodowane ścieżki                  │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│         SUSModder.Core (Business Logic)         │
│  ┌───────────────────────────────────────────┐  │
│  │  Services                                 │  │
│  │  - GameLocator (Windows-specific paths)  │  │
│  │  - ModManager (7z.exe, hardcoded)        │  │
│  │  - EpicVersionManager (legendary.exe)    │  │
│  │  - ConfigManager (static class)          │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │  Utilities                                │  │
│  │  - PathSettings (static, %APPDATA%)      │  │
│  │  - HardwareIdProvider (WMI, Registry)    │  │
│  │  - FileSystemUtilities (PowerShell UAC)  │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│       Windows API / External Tools              │
│  - System.Management (WMI)                      │
│  - Microsoft.Win32.Registry                     │
│  - 7z.exe, legendary.exe, updater.exe           │
│  - PowerShell, CMD                              │
└─────────────────────────────────────────────────┘
```

### Główne problemy:

1. **Tight coupling** - Bezpośrednie wywołania Windows API w logice biznesowej
2. **Static classes** - `ConfigManager`, `PathSettings` - trudne do mockowania i testowania
3. **Hardcoded paths** - Ścieżki Windows wszędzie w kodzie
4. **No abstraction** - Brak warstwy abstrakcji dla operacji platformowych
5. **External tools embedded** - 7z.exe, legendary.exe w projekcie

---

## Problemy z obecną architekturą

### 1. PathSettings - Static class

**Obecna implementacja:**

```csharp
// SUSModder.Core/Utilities/PathSettings.cs
public static class PathSettings
{
    private static string _defaultModsPath;

    static PathSettings()
    {
        _defaultModsPath = Environment.ExpandEnvironmentVariables(
            "%APPDATA%\\Among Us - Mody"); // ❌ Windows-only
    }

    public static string GetActualModPath(string installPath)
    {
        // Static methods - nie można podmienić implementacji
    }
}
```

**Problemy:**
- ❌ Niemożliwe do mockowania w testach
- ❌ Hardcodowane zmienne Windows (%APPDATA%)
- ❌ Brak możliwości zmiany implementacji per platforma
- ❌ Global state

---

### 2. GameLocator - Hardcoded paths

**Obecna implementacja:**

```csharp
// SUSModder.Core/GameIntegration/GameLocator.cs
public static class GameLocator
{
    private static readonly string[] CommonSteamPaths =
    {
        Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Steam"),
        // ... więcej Windows paths
    };

    public static string? LocateAmongUs()
    {
        // Szuka tylko w Windows paths
    }
}
```

**Problemy:**
- ❌ Static class - brak abstrakcji
- ❌ Hardcoded Windows paths
- ❌ Brak wsparcia dla Linux Steam paths
- ❌ Niemożliwe testowanie z mock paths

---

### 3. ModManager - External tools dependency

**Obecna implementacja:**

```csharp
// SUSModder.Core/GameIntegration/ModManager.cs
public class ModManager
{
    private void Extract7zWithPassword(...)
    {
        string sevenZipPath = Path.Combine(appDirPath, "tools", "7z.exe");
        // ❌ Bezpośrednie wywołanie 7z.exe
    }
}
```

**Problemy:**
- ❌ Bezpośrednie wywołanie .exe
- ❌ Brak abstrakcji dla archiver
- ❌ Nie można użyć systemowego 7z na Linux

---

### 4. HardwareIdProvider - WMI dependency

**Obecna implementacja:**

```csharp
// SUSModder.Core/Utilities/HardwareIdProvider.cs
public class HardwareIdProvider
{
    private static string? GetWmiProperty(string wmiClass, string property)
    {
        using var searcher = new ManagementObjectSearcher(...);
        // ❌ System.Management - tylko Windows
    }

    private static string? GetMachineGuid()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine...
        // ❌ Registry - tylko Windows
    }
}
```

**Problemy:**
- ❌ Dependency na System.Management (WMI) - Windows-only
- ❌ Dependency na Microsoft.Win32.Registry - Windows-only
- ❌ Brak abstrakcji dla platform-specific hardware info

---

## Proponowana architektura cross-platform

### Diagram docelowej architektury

```
┌─────────────────────────────────────────────────────────────┐
│              SUSModder (Avalonia UI)                        │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ViewModels (MainWindowViewModel, etc.)              │  │
│  │  - Używa IPlatformServices (DI)                      │  │
│  │  - Platform-agnostic logic                           │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│          SUSModder.Core (Business Logic)                    │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Interfaces (Contracts)                               │  │
│  │  - IPathProvider                                      │  │
│  │  - IGameLocator                                       │  │
│  │  - IProcessManager                                    │  │
│  │  - IArchiveExtractor                                  │  │
│  │  - IPermissionManager                                 │  │
│  │  - IHardwareInfoProvider                              │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Platform-agnostic Services                           │  │
│  │  - ModService                                         │  │
│  │  - ConfigService                                      │  │
│  │  - GameService (uses IGameLocator, IProcessManager)  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│     SUSModder.Platform (Platform Abstraction Layer)         │
│  ┌────────────────────┬──────────────────┬────────────────┐ │
│  │  Windows           │  Linux           │  macOS         │ │
│  │  Implementation    │  Implementation  │  Implementation│ │
│  ├────────────────────┼──────────────────┼────────────────┤ │
│  │ WindowsPathProvider│ LinuxPathProvider│ macOSPath...   │ │
│  │ WindowsGameLocator │ LinuxGameLocator │ macOSGame...   │ │
│  │ WindowsProcess...  │ LinuxProcess...  │ macOSProc...   │ │
│  │ SevenZipExtractor  │ P7ZipExtractor   │ P7ZipExtr...   │ │
│  │ UACPermission...   │ PkexecPermission │ SudoPerm...    │ │
│  │ WmiHardwareInfo    │ ProcHardwareInfo │ SystemProf...  │ │
│  └────────────────────┴──────────────────┴────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│       OS APIs / External Tools (Platform-specific)          │
│  Windows: WMI, Registry, 7z.exe, PowerShell                 │
│  Linux: /proc, /sys, p7zip, pkexec, xdg-utils               │
│  macOS: system_profiler, 7z, osascript                      │
└─────────────────────────────────────────────────────────────┘
```

### Kluczowe zmiany:

1. ✅ **Warstwa abstrakcji** - Interfejsy dla wszystkich operacji platformowych
2. ✅ **Dependency Injection** - Wszystkie services używają DI zamiast static
3. ✅ **Platform-specific implementations** - Osobne klasy per platforma
4. ✅ **Testability** - Wszystkie interfejsy można mockować
5. ✅ **Separation of Concerns** - Business logic oddzielona od platform logic

---

## Warstwa abstrakcji platformy

### 1. IPlatformServices - Root interface

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Root interface dla wszystkich platform-specific services
/// </summary>
public interface IPlatformServices
{
    IPathProvider PathProvider { get; }
    IGameLocator GameLocator { get; }
    IProcessManager ProcessManager { get; }
    IArchiveExtractor ArchiveExtractor { get; }
    IPermissionManager PermissionManager { get; }
    IHardwareInfoProvider HardwareInfoProvider { get; }
}
```

---

### 2. IPathProvider - Zarządzanie ścieżkami

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Dostarcza ścieżki specyficzne dla platformy
/// </summary>
public interface IPathProvider
{
    /// <summary>
    /// Domyślna ścieżka instalacji modów
    /// Windows: %APPDATA%\Among Us - Mody
    /// Linux: ~/.local/share/among-us-mody
    /// </summary>
    string GetDefaultModsPath();

    /// <summary>
    /// Ścieżka konfiguracji Among Us
    /// Windows: %USERPROFILE%\AppData\LocalLow\Innersloth\Among Us
    /// Linux: ~/.steam/steam/steamapps/compatdata/945360/pfx/.../Among Us
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
```

**Implementacja Windows:**

```csharp
namespace SUSModder.Platform.Windows;

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

    // ... pozostałe metody
}
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

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

        // Proton prefix dla Steam Among Us (AppID 945360)
        string protonPath = Path.Combine(home,
            ".steam/steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
            "AppData/LocalLow/Innersloth/Among Us");

        if (Directory.Exists(protonPath))
            return protonPath;

        // Szukaj w innych lokalizacjach
        return FindAmongUsConfigInProtonPrefixes();
    }

    public string GetDesktopPath()
    {
        // Spróbuj xdg-user-dir
        string? xdgDesktop = GetXdgUserDir("DESKTOP");
        if (xdgDesktop != null)
            return xdgDesktop;

        // Fallback
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop");
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
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string path = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch { }

        return null;
    }

    // ... pozostałe metody
}
```

---

### 3. IGameLocator - Lokalizacja gry

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Lokalizuje instalację Among Us
/// </summary>
public interface IGameLocator
{
    /// <summary>
    /// Znajduje ścieżkę instalacji Among Us
    /// </summary>
    string? LocateAmongUs();

    /// <summary>
    /// Znajduje wszystkie instalacje Steam libraries
    /// </summary>
    IEnumerable<string> GetSteamLibraryPaths();

    /// <summary>
    /// Sprawdza czy dana ścieżka zawiera Among Us
    /// </summary>
    bool IsAmongUsInstallation(string path);
}
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

public class LinuxGameLocator : IGameLocator
{
    private readonly IPathProvider _pathProvider;

    public LinuxGameLocator(IPathProvider pathProvider)
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
                return amongUsPath;
        }

        // 2. Przeszukaj wszystkie Steam libraries
        foreach (var libraryPath in GetSteamLibraryPaths())
        {
            string amongUsPath = Path.Combine(libraryPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
                return amongUsPath;
        }

        // 3. Epic Games (Heroic Launcher)
        foreach (var epicPath in GetHeroicGamePaths())
        {
            if (IsAmongUsInstallation(epicPath))
                return epicPath;
        }

        return null;
    }

    public IEnumerable<string> GetSteamLibraryPaths()
    {
        var libraries = new List<string>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Główna lokalizacja Steam
        string mainSteam = Path.Combine(home, ".steam/steam");
        if (Directory.Exists(mainSteam))
            libraries.Add(mainSteam);

        // Flatpak Steam
        string flatpakSteam = Path.Combine(home,
            ".var/app/com.valvesoftware.Steam/.local/share/Steam");
        if (Directory.Exists(flatpakSteam))
            libraries.Add(flatpakSteam);

        // Parse libraryfolders.vdf dla dodatkowych bibliotek
        string vdfPath = Path.Combine(mainSteam, "steamapps/libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            var additionalLibraries = ParseSteamLibraryFolders(vdfPath);
            libraries.AddRange(additionalLibraries);
        }

        return libraries;
    }

    public bool IsAmongUsInstallation(string path)
    {
        if (!Directory.Exists(path))
            return false;

        // Sprawdź obecność Among Us.exe
        string exePath = Path.Combine(path, "Among Us.exe");
        return File.Exists(exePath);
    }

    private IEnumerable<string> GetCommonSteamPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new[]
        {
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),
            "/usr/share/steam",
            "/usr/local/share/steam",
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam")
        };
    }

    private IEnumerable<string> GetHeroicGamePaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new[]
        {
            Path.Combine(home, "Games/Heroic/Among Us"),
            Path.Combine(home, ".config/heroic/Among Us"),
            Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic/Among Us")
        };
    }

    private IEnumerable<string> ParseSteamLibraryFolders(string vdfPath)
    {
        // TODO: Implementować parser VDF
        // Na razie zwróć pustą listę
        return Array.Empty<string>();
    }
}
```

---

### 4. IProcessManager - Zarządzanie procesami

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Zarządza uruchamianiem procesów
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
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

public class LinuxProcessManager : IProcessManager
{
    public async Task<bool> LaunchAmongUsAsync(string installPath)
    {
        try
        {
            // Uruchom przez Steam URI (działa z Proton)
            var psi = new ProcessStartInfo
            {
                FileName = "steam",
                Arguments = "steam://rungameid/945360",
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            return process != null;
        }
        catch
        {
            // Fallback: xdg-open
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = "steam://rungameid/945360",
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                return process != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public void OpenFolder(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        };

        Process.Start(psi);
    }

    public void OpenUrl(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = url,
            UseShellExecute = false
        };

        Process.Start(psi);
    }

    public async Task CreateDesktopShortcutAsync(string name, string targetPath, string workingDirectory)
    {
        string desktopPath = GetDesktopPath();
        string desktopFile = Path.Combine(desktopPath, $"{name}.desktop");

        var content = new StringBuilder();
        content.AppendLine("[Desktop Entry]");
        content.AppendLine($"Name={name}");
        content.AppendLine("Type=Application");
        content.AppendLine($"Exec=steam steam://rungameid/945360");
        content.AppendLine($"Path={workingDirectory}");
        content.AppendLine("Terminal=false");
        content.AppendLine("Categories=Game;");

        // Opcjonalna ikona
        string iconPath = Path.Combine(workingDirectory, "icon.png");
        if (File.Exists(iconPath))
            content.AppendLine($"Icon={iconPath}");

        await File.WriteAllTextAsync(desktopFile, content.ToString());

        // Ustaw uprawnienia wykonywania
        Process.Start("chmod", $"+x \"{desktopFile}\"")?.WaitForExit();
    }

    private string GetDesktopPath()
    {
        // Implementacja jak w LinuxPathProvider
        // ... (można współdzielić przez IPathProvider)
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
    }
}
```

---

### 5. IArchiveExtractor - Ekstrakcja archiwów

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Ekstraktuje archiwa (zip, 7z)
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Ekstraktuje archiwum 7z z hasłem
    /// </summary>
    Task Extract7zAsync(string archivePath, string extractPath, string? password = null);

    /// <summary>
    /// Ekstraktuje archiwum zip
    /// </summary>
    Task ExtractZipAsync(string archivePath, string extractPath);
}
```

**Implementacja Windows:**

```csharp
namespace SUSModder.Platform.Windows;

public class WindowsArchiveExtractor : IArchiveExtractor
{
    public async Task Extract7zAsync(string archivePath, string extractPath, string? password = null)
    {
        // Użyj dołączonego 7z.exe
        string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        string sevenZipPath = Path.Combine(appDir, "tools", "7z.exe");

        if (!File.Exists(sevenZipPath))
            throw new FileNotFoundException($"7z.exe not found at {sevenZipPath}");

        string arguments = password != null
            ? $"x \"{archivePath}\" -o\"{extractPath}\" -p{password} -y"
            : $"x \"{archivePath}\" -o\"{extractPath}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start 7z.exe");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7z extraction failed with code {process.ExitCode}");
    }

    public async Task ExtractZipAsync(string archivePath, string extractPath)
    {
        // Użyj System.IO.Compression
        await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractPath));
    }
}
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

public class LinuxArchiveExtractor : IArchiveExtractor
{
    public async Task Extract7zAsync(string archivePath, string extractPath, string? password = null)
    {
        // Znajdź systemowy 7z
        string? sevenZipPath = FindExecutableInPath("7z")
                            ?? FindExecutableInPath("7zz")
                            ?? FindExecutableInPath("7za");

        if (sevenZipPath == null)
        {
            throw new FileNotFoundException(
                "7z is not installed. Install it using:\n" +
                "Ubuntu/Debian: sudo apt install p7zip-full\n" +
                "Fedora: sudo dnf install p7zip\n" +
                "Arch: sudo pacman -S p7zip");
        }

        string arguments = password != null
            ? $"x \"{archivePath}\" -o\"{extractPath}\" -p{password} -y"
            : $"x \"{archivePath}\" -o\"{extractPath}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start 7z");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7z extraction failed with code {process.ExitCode}");
    }

    public async Task ExtractZipAsync(string archivePath, string extractPath)
    {
        // Użyj System.IO.Compression (działa cross-platform)
        await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractPath));
    }

    private static string? FindExecutableInPath(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = name,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string path = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
        }
        catch { }

        return null;
    }
}
```

---

### 6. IPermissionManager - Zarządzanie uprawnieniami

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Zarządza podwyższonymi uprawnieniami
/// </summary>
public interface IPermissionManager
{
    /// <summary>
    /// Usuwa katalog z podwyższonymi uprawnieniami (jeśli potrzebne)
    /// </summary>
    Task<bool> DeleteDirectoryElevatedAsync(string path);

    /// <summary>
    /// Sprawdza czy aplikacja ma uprawnienia administratora/root
    /// </summary>
    bool IsElevated();
}
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

public class LinuxPermissionManager : IPermissionManager
{
    public async Task<bool> DeleteDirectoryElevatedAsync(string path)
    {
        // Spróbuj najpierw normalnie
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            // Jeśli się nie udało, użyj pkexec
            return await DeleteWithPkexecAsync(path);
        }
    }

    public bool IsElevated()
    {
        // Sprawdź czy UID == 0 (root)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                return output == "0";
            }
        }
        catch { }

        return false;
    }

    private async Task<bool> DeleteWithPkexecAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = $"rm -rf \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0 && !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
```

---

### 7. IHardwareInfoProvider - Informacje o sprzęcie

```csharp
namespace SUSModder.Platform;

/// <summary>
/// Dostarcza informacje o sprzęcie dla telemetrii
/// </summary>
public interface IHardwareInfoProvider
{
    /// <summary>
    /// Unikalny identyfikator maszyny
    /// </summary>
    string GetMachineId();

    /// <summary>
    /// Informacje o CPU
    /// </summary>
    string? GetCpuInfo();

    /// <summary>
    /// Informacje o systemie operacyjnym
    /// </summary>
    string GetOSInfo();
}
```

**Implementacja Linux:**

```csharp
namespace SUSModder.Platform.Linux;

public class LinuxHardwareInfoProvider : IHardwareInfoProvider
{
    public string GetMachineId()
    {
        // Użyj /etc/machine-id
        try
        {
            if (File.Exists("/etc/machine-id"))
                return File.ReadAllText("/etc/machine-id").Trim();

            if (File.Exists("/var/lib/dbus/machine-id"))
                return File.ReadAllText("/var/lib/dbus/machine-id").Trim();
        }
        catch { }

        return Guid.NewGuid().ToString();
    }

    public string? GetCpuInfo()
    {
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var modelLine = lines.FirstOrDefault(l => l.StartsWith("model name"));
                if (modelLine != null)
                {
                    return modelLine.Split(':').LastOrDefault()?.Trim();
                }
            }
        }
        catch { }

        return null;
    }

    public string GetOSInfo()
    {
        try
        {
            // Spróbuj /etc/os-release
            if (File.Exists("/etc/os-release"))
            {
                var lines = File.ReadAllLines("/etc/os-release");
                var prettyName = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                if (prettyName != null)
                {
                    return prettyName.Split('=')[1].Trim('"');
                }
            }
        }
        catch { }

        return RuntimeInformation.OSDescription;
    }
}
```

---

## Refaktor komponentów

### Przed (Static):

```csharp
// ❌ Static class - tight coupling
public static class PathSettings
{
    private static string _defaultModsPath;

    static PathSettings()
    {
        _defaultModsPath = Environment.ExpandEnvironmentVariables("%APPDATA%\\Among Us - Mody");
    }

    public static string GetActualModPath(string installPath)
    {
        // ...
    }
}

// Użycie
string path = PathSettings.GetActualModPath(modPath);
```

### Po (DI):

```csharp
// ✅ Interface + DI
public class ConfigService
{
    private readonly IPathProvider _pathProvider;

    public ConfigService(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public string GetActualModPath(string installPath)
    {
        string defaultPath = _pathProvider.GetDefaultModsPath();
        // ...
    }
}

// Użycie
public class MainWindowViewModel
{
    private readonly ConfigService _configService;

    public MainWindowViewModel(ConfigService configService)
    {
        _configService = configService;
    }

    public void DoSomething()
    {
        string path = _configService.GetActualModPath(modPath);
    }
}
```

---

## Struktura projektu

### Nowa struktura solution:

```
SUSModder.sln
├── SUSModder/                      (Avalonia UI)
│   ├── ViewModels/
│   ├── Views/
│   ├── Services/
│   └── SUSModder.csproj
│
├── SUSModder.Core/                 (Platform-agnostic business logic)
│   ├── Services/
│   │   ├── ModService.cs
│   │   ├── ConfigService.cs
│   │   └── GameService.cs
│   ├── Models/
│   ├── Configuration/
│   └── SUSModder.Core.csproj
│
├── SUSModder.Platform/             (Platform abstraction layer) ⭐ NOWY
│   ├── Interfaces/
│   │   ├── IPlatformServices.cs
│   │   ├── IPathProvider.cs
│   │   ├── IGameLocator.cs
│   │   ├── IProcessManager.cs
│   │   ├── IArchiveExtractor.cs
│   │   ├── IPermissionManager.cs
│   │   └── IHardwareInfoProvider.cs
│   └── SUSModder.Platform.csproj
│
├── SUSModder.Platform.Windows/     (Windows implementations) ⭐ NOWY
│   ├── WindowsPlatformServices.cs
│   ├── WindowsPathProvider.cs
│   ├── WindowsGameLocator.cs
│   ├── WindowsProcessManager.cs
│   ├── WindowsArchiveExtractor.cs
│   ├── WindowsPermissionManager.cs
│   ├── WindowsHardwareInfoProvider.cs
│   └── SUSModder.Platform.Windows.csproj
│
├── SUSModder.Platform.Linux/       (Linux implementations) ⭐ NOWY
│   ├── LinuxPlatformServices.cs
│   ├── LinuxPathProvider.cs
│   ├── LinuxGameLocator.cs
│   ├── LinuxProcessManager.cs
│   ├── LinuxArchiveExtractor.cs
│   ├── LinuxPermissionManager.cs
│   ├── LinuxHardwareInfoProvider.cs
│   └── SUSModder.Platform.Linux.csproj
│
└── Updater/                        (Updater executable)
    ├── Program.cs
    └── Updater.csproj
```

---

## Dependency Injection

### Bootstrap DI w Program.cs

```csharp
// SUSModder/Program.cs
public static void Main(string[] args)
{
    var services = new ServiceCollection();

    // Zarejestruj platform services
    if (OperatingSystem.IsWindows())
    {
        services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
    }
    else if (OperatingSystem.IsLinux())
    {
        services.AddSingleton<IPlatformServices, LinuxPlatformServices>();
    }
    else
    {
        throw new PlatformNotSupportedException();
    }

    // Zarejestruj core services
    services.AddSingleton<ConfigService>();
    services.AddSingleton<ModService>();
    services.AddSingleton<GameService>();

    // Zarejestruj ViewModels
    services.AddTransient<MainWindowViewModel>();

    var serviceProvider = services.BuildServiceProvider();

    // Uruchom aplikację
    BuildAvaloniaApp()
        .AfterSetup(_ =>
        {
            // Przekaż service provider do ViewModels
            var mainViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
            // ...
        })
        .StartWithClassicDesktopLifetime(args);
}
```

### PlatformServices factory

```csharp
// SUSModder.Platform.Windows/WindowsPlatformServices.cs
public class WindowsPlatformServices : IPlatformServices
{
    private readonly Lazy<IPathProvider> _pathProvider;
    private readonly Lazy<IGameLocator> _gameLocator;
    // ... inne lazy

    public WindowsPlatformServices()
    {
        _pathProvider = new Lazy<IPathProvider>(() => new WindowsPathProvider());
        _gameLocator = new Lazy<IGameLocator>(() => new WindowsGameLocator(_pathProvider.Value));
        // ... inicjalizuj inne
    }

    public IPathProvider PathProvider => _pathProvider.Value;
    public IGameLocator GameLocator => _gameLocator.Value;
    // ... inne properties
}
```

---

## Podsumowanie zmian architektury

| Aspekt | Przed | Po |
|--------|-------|-----|
| **Coupling** | Tight - bezpośrednie wywołania Windows API | Loose - przez interfaces |
| **Static classes** | PathSettings, GameLocator, ConfigManager | Instance classes z DI |
| **Testability** | Trudne - nie można mockować static | Łatwe - wszystko przez interfaces |
| **Platform support** | Tylko Windows | Windows + Linux + macOS (extensible) |
| **Separation** | Business logic + platform logic razem | Oddzielone w platform layer |
| **External tools** | Hardcodowane .exe w projekcie | Conditional - system lub bundled |

---

**Następny dokument:** [03-MIGRATION-STRATEGY.md](./03-MIGRATION-STRATEGY.md) - Szczegółowa strategia migracji kodu
