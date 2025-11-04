# Analiza Wykonalności: Port SUSModder na Linux

**Data:** 2025-10-30
**Status:** Analiza wstępna
**Wniosek:** ✅ **WYKONALNE z znaczącymi modyfikacjami**

---

## Spis treści

1. [Podsumowanie](#podsumowanie)
2. [Metodologia analizy](#metodologia-analizy)
3. [Zależności platformowe](#zależności-platformowe)
4. [Kompatybilność UI (Avalonia)](#kompatybilność-ui-avalonia)
5. [Among Us + BepInEx na Linux](#among-us--bepinex-na-linux)
6. [Ocena komponentów](#ocena-komponentów)
7. [Wnioski końcowe](#wnioski-końcowe)

---

## Podsumowanie

Po szczegółowej analizie kodu źródłowego SUSModder (wersja 2.0.1), **port na Linux jest możliwy**, ale wymaga przeprojektowania kluczowych komponentów aplikacji.

### Główne przeszkody:

1. ❌ **Zależność od System.Management (WMI)** - Nie działa na Linux
2. ❌ **Zewnętrzne pliki .exe** - 7z.exe, legendary.exe, updater.exe
3. ❌ **Hardcodowane ścieżki Windows** - %APPDATA%, %PROGRAMFILES%
4. ⚠️ **Epic Games Store** - Brak natywnego wsparcia na Linux
5. ✅ **Avalonia UI** - Pełna kompatybilność z Linux

### Szacowany wysiłek refaktoru:

- **Krytyczne zmiany:** ~40% kodu SUSModder.Core
- **Umiarkowane zmiany:** ~20% kodu SUSModder (UI)
- **Całkowity czas:** 8-12 tygodni pracy

---

## Metodologia analizy

Analiza została przeprowadzona w oparciu o:

1. **Przegląd kodu źródłowego** - Wszystkie pliki .cs w SUSModder i SUSModder.Core
2. **Analiza zależności** - Package references w .csproj
3. **Testowanie koncepcyjne** - Weryfikacja założeń dotyczących Proton/Wine
4. **Dokumentacja zewnętrzna** - BepInEx, Proton, Steam, legendary

### Obszary analizy:

- ✅ Warstwa UI (Avalonia)
- ✅ Business logic (SUSModder.Core)
- ✅ Integracje zewnętrzne (Steam, Epic, 7z)
- ✅ System operacyjny (ścieżki, procesy, uprawnienia)
- ✅ Among Us + mody (BepInEx, Proton)

---

## Zależności platformowe

### 1. System.Management (WMI) ❌ BLOKUJĄCE

**Lokalizacja:** `SUSModder.Core/Utilities/HardwareIdProvider.cs:56-106`

```csharp
using System.Management; // TYLKO WINDOWS!

private static string? GetWmiProperty(string wmiClass, string property)
{
    using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
    using var collection = searcher.Get();
    // ... używane do CPU ID, MB Serial, BIOS Serial
}
```

**Problem:**
- `System.Management` nie działa na Linux
- Używane do generowania Hardware ID dla telemetrii
- NuGet package jest Windows-specific

**Rozwiązanie:**
```csharp
// Linux: użyj /proc/cpuinfo, /sys/class/dmi/id/*, /etc/machine-id
private static string GetLinuxHardwareId()
{
    var sb = new StringBuilder();

    // Machine ID
    if (File.Exists("/etc/machine-id"))
        sb.Append(File.ReadAllText("/etc/machine-id").Trim());

    // DMI Info (jeśli dostępne)
    if (File.Exists("/sys/class/dmi/id/product_uuid"))
        sb.Append(File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim());

    return sb.ToString();
}
```

**Wysiłek:** 🟡 Średni (2-3 dni)

---

### 2. Microsoft.Win32.Registry ❌ BLOKUJĄCE

**Lokalizacja:** `SUSModder.Core/Utilities/HardwareIdProvider.cs:112-122`

```csharp
[SupportedOSPlatform("windows")]
private static string? GetMachineGuid()
{
    using var key = Microsoft.Win32.Registry.LocalMachine
        .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
    return key?.GetValue("MachineGuid")?.ToString();
}
```

**Problem:**
- Registry jest Windows-specific
- Brak odpowiednika na Linux

**Rozwiązanie:**
```csharp
private static string? GetMachineGuid()
{
    if (OperatingSystem.IsLinux())
    {
        if (File.Exists("/etc/machine-id"))
            return File.ReadAllText("/etc/machine-id").Trim();
    }
    else if (OperatingSystem.IsWindows())
    {
        // ... existing code
    }
    return null;
}
```

**Wysiłek:** 🟢 Niski (1 dzień)

---

### 3. Ścieżki systemowe ❌ KRYTYCZNE

#### A. %APPDATA% (ApplicationData)

**Lokalizacje:**
- `SUSModder.Core/Utilities/PathSettings.cs:22-23`
- `SUSModder.Core/Configuration/ModConfigHandler.cs:31,72,143`
- `SUSModder.Core/Utilities/LobbyUtils.cs:30`

```csharp
// Windows: %APPDATA%\Among Us - Mody
_defaultModsPath = Environment.ExpandEnvironmentVariables(
    "%APPDATA%\\Among Us - Mody");

// Windows: AppData\LocalLow\Innersloth\Among Us
string sourceDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    @"AppData\LocalLow\Innersloth\Among Us");
```

**Problem:**
- `%APPDATA%` nie istnieje na Linux
- Hardcodowane separatory `\` zamiast `Path.Combine()`
- Among Us config na Linux jest w Proton prefix

**Rozwiązanie:**

```csharp
public static string GetDefaultModsPath()
{
    if (OperatingSystem.IsWindows())
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Among Us - Mody");
    }
    else if (OperatingSystem.IsLinux())
    {
        // XDG Base Directory Specification
        string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".local/share");
        return Path.Combine(xdgDataHome, "among-us-mody");
    }
    return string.Empty;
}

public static string GetAmongUsConfigPath()
{
    if (OperatingSystem.IsWindows())
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Innersloth", "Among Us");
    }
    else if (OperatingSystem.IsLinux())
    {
        // Proton prefix dla Steam (AppID 945360)
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string protonPath = Path.Combine(home,
            ".steam/steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
            "AppData/LocalLow/Innersloth/Among Us");

        if (Directory.Exists(protonPath))
            return protonPath;

        // Fallback - szukaj w innych lokalizacjach
        return FindAmongUsConfigLinux();
    }
    return string.Empty;
}
```

**Wysiłek:** 🔴 Wysoki (5-7 dni, wymaga testowania na różnych konfiguracjach)

---

#### B. Program Files i zmienne środowiskowe

**Lokalizacja:** `SUSModder.Core/GameIntegration/GameLocator.cs:17-31`

```csharp
private static readonly string[] CommonSteamPaths =
{
    Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Steam"),
    Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Steam"),
    Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Steam"),
    "D:/Steam",
    "D:/Gry/Steam"
};

private static readonly string[] CommonEpicPaths =
{
    Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Epic Games"),
    Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Epic Games"),
    "D:/Epic Games"
};
```

**Problem:**
- `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`, `%LOCALAPPDATA%` nie istnieją na Linux
- Dyski `D:/` nie istnieją na Linux
- Steam i Epic na Linux mają inne lokalizacje

**Rozwiązanie:**

```csharp
private static string[] GetCommonSteamPaths()
{
    if (OperatingSystem.IsWindows())
    {
        return new[]
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Steam"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Steam"),
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%/Steam"),
            "D:/Steam",
            "D:/Gry/Steam"
        };
    }
    else if (OperatingSystem.IsLinux())
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),
            "/usr/share/steam",
            "/usr/local/share/steam",
            // Flatpak Steam
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam")
        };
    }
    return Array.Empty<string>();
}

private static string[] GetCommonEpicPaths()
{
    if (OperatingSystem.IsLinux())
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            // Heroic Games Launcher
            Path.Combine(home, "Games/Heroic"),
            Path.Combine(home, ".config/heroic"),
            // Flatpak Heroic
            Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic/GamesConfig"),
            // Lutris
            Path.Combine(home, "Games/epic-games-store")
        };
    }
    // ... Windows paths
}
```

**Wysiłek:** 🔴 Wysoki (4-6 dni)

---

### 4. Zewnętrzne narzędzia Windows ❌ KRYTYCZNE

#### A. 7z.exe

**Lokalizacja:** `SUSModder.Core/GameIntegration/ModManager.cs:457-509`

```csharp
private void Extract7zWithPassword(string archivePath, string extractPath, string password)
{
    string sevenZipPath = Path.Combine(appDirPath, "tools", "7z.exe");
    // ...
    process.StartInfo.FileName = sevenZipPath;
    process.StartInfo.Arguments = $"x \"{archivePath}\" -o\"{extractPath}\" -p{password} -y";
}
```

**Problem:**
- `7z.exe` jest Windows executable
- Nie zadziała na Linux

**Rozwiązanie:**

```csharp
private string Get7zExecutablePath()
{
    if (OperatingSystem.IsWindows())
    {
        string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        return Path.Combine(appDir, "tools", "7z.exe");
    }
    else if (OperatingSystem.IsLinux())
    {
        // Spróbuj znaleźć systemowy 7z
        string? path = FindExecutableInPath("7z")
                    ?? FindExecutableInPath("7zz")
                    ?? FindExecutableInPath("7za");

        if (path != null)
            return path;

        throw new FileNotFoundException(
            "7z nie jest zainstalowany. Zainstaluj przez:\n" +
            "Ubuntu/Debian: sudo apt install p7zip-full\n" +
            "Fedora: sudo dnf install p7zip\n" +
            "Arch: sudo pacman -S p7zip");
    }
    throw new PlatformNotSupportedException();
}

private static string? FindExecutableInPath(string name)
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
    return null;
}
```

**Wymagania Linux:**
- Pakiet: `p7zip-full` (Debian/Ubuntu), `p7zip` (Fedora/Arch)

**Wysiłek:** 🟡 Średni (2-3 dni)

---

#### B. legendary.exe (Epic Games)

**Lokalizacja:** `SUSModder.Core/GameIntegration/EpicVersionManager.cs:30,76,899-903`

```csharp
legendaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legendary.exe");

private async Task DownloadLegendaryAsync()
{
    string url = "https://github.com/whichtwix/legendary/releases/latest/download/legendary.exe";
    await DownloadFileAsync(url, legendaryPath);
}
```

**Problem:**
- `legendary.exe` jest Windows executable
- Epic Games Store nie działa natywnie na Linux

**Rozwiązanie - Opcja A: Native legendary**

```csharp
private async Task EnsureLegendaryAsync()
{
    if (OperatingSystem.IsWindows())
    {
        legendaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legendary.exe");
        if (!File.Exists(legendaryPath))
        {
            string url = "https://github.com/whichtwix/legendary/releases/latest/download/legendary.exe";
            await DownloadFileAsync(url, legendaryPath);
        }
    }
    else if (OperatingSystem.IsLinux())
    {
        // Szukaj systemowego legendary
        legendaryPath = FindExecutableInPath("legendary");

        if (legendaryPath == null)
        {
            throw new InvalidOperationException(
                "legendary nie jest zainstalowany.\n\n" +
                "Zainstaluj przez:\n" +
                "pip install legendary-gl\n\n" +
                "Lub:\n" +
                "Ubuntu/Debian: sudo apt install legendary\n" +
                "Arch (AUR): yay -S legendary");
        }
    }
}
```

**Rozwiązanie - Opcja B: Heroic Games Launcher (REKOMENDOWANE)**

```csharp
// Zamiast legendary, użyj Heroic CLI
private async Task LaunchGameThroughHeroicAsync(string gameName)
{
    if (!OperatingSystem.IsLinux())
        throw new PlatformNotSupportedException();

    string? heroicPath = FindExecutableInPath("heroic");

    if (heroicPath == null)
    {
        throw new InvalidOperationException(
            "Heroic Games Launcher nie jest zainstalowany.\n" +
            "Pobierz z: https://heroicgameslauncher.com/");
    }

    var psi = new ProcessStartInfo
    {
        FileName = heroicPath,
        Arguments = $"launch \"{gameName}\"",
        UseShellExecute = false
    };

    Process.Start(psi);
}
```

**Wymagania Linux:**
- **Opcja A:** `legendary` (pip install legendary-gl)
- **Opcja B:** Heroic Games Launcher (https://heroicgameslauncher.com/)

**Wysiłek:** 🔴 Wysoki (5-7 dni, wymaga całkowitej przepisania EpicVersionManager)

---

#### C. updater.exe

**Lokalizacja:** `SUSModder.Core/Services/AppUpdateService.cs:145-160`

```csharp
string updaterPath = Path.Combine(appDirPath, "updater", "updater.exe");
Process.Start(new ProcessStartInfo
{
    FileName = updaterPath,
    UseShellExecute = true,
    Arguments = $"\"{appDirPath}\" \"{updateFilePath}\""
});
```

**Problem:**
- Updater jest Windows executable
- Trzeba przeportować cały projekt Updater

**Rozwiązanie:**

1. Dodać `linux-x64` do Updater.csproj RuntimeIdentifiers
2. Publikować osobne updater dla Linux
3. Conditional logic w AppUpdateService

```csharp
private string GetUpdaterPath()
{
    string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;

    if (OperatingSystem.IsWindows())
        return Path.Combine(appDir, "updater", "updater.exe");
    else if (OperatingSystem.IsLinux())
        return Path.Combine(appDir, "updater", "updater");

    throw new PlatformNotSupportedException();
}

public bool RunUpdater(string updateFilePath)
{
    string updaterPath = GetUpdaterPath();

    // Linux: upewnij się, że ma uprawnienia wykonywania
    if (OperatingSystem.IsLinux())
    {
        try
        {
            Process.Start("chmod", $"+x \"{updaterPath}\"")?.WaitForExit();
        }
        catch { }
    }

    // ... reszta kodu
}
```

**Wysiłek:** 🟡 Średni (3-4 dni)

---

### 5. Process.Start i elevated permissions ❌ KRYTYCZNE

#### A. PowerShell elevation (UAC)

**Lokalizacja:** `SUSModder/Services/FileSystemHelper.cs:224-280`

```csharp
[SupportedOSPlatform("windows")]
private async Task<bool> DeleteWithElevatedPermissionsWindows(string directoryPath)
{
    var processInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = $"-Command \"{script}\"",
        UseShellExecute = true,
        Verb = "runas" // UAC elevation
    };
}
```

**Problem:**
- `Verb = "runas"` jest Windows-specific (UAC)
- PowerShell nie istnieje na większości systemów Linux
- Linux ma inny system uprawnień (`sudo`, `pkexec`, `polkit`)

**Rozwiązanie:**

```csharp
private async Task<bool> DeleteWithElevatedPermissions(string directoryPath)
{
    if (OperatingSystem.IsWindows())
    {
        return await DeleteWithElevatedPermissionsWindows(directoryPath);
    }
    else if (OperatingSystem.IsLinux())
    {
        return await DeleteWithElevatedPermissionsLinux(directoryPath);
    }
    return false;
}

private async Task<bool> DeleteWithElevatedPermissionsLinux(string directoryPath)
{
    // Opcja 1: pkexec (PolicyKit - graficzne prompt)
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pkexec",
            Arguments = $"rm -rf \"{directoryPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
            return !Directory.Exists(directoryPath);
        }
    }
    catch
    {
        // Fallback: terminal sudo (jeśli pkexec nie działa)
        // UWAGA: Wymaga otwartego terminala
    }

    return false;
}
```

**Wysiłek:** 🟡 Średni (3-4 dni)

---

#### B. Otwieranie folderów i plików

**Lokalizacja:** `SUSModder/ViewModels/MainWindowViewModel.ExternalActions.cs:133-138`

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = SelectedMod.InstallPath,
    UseShellExecute = true,
    Verb = "open" // Windows Explorer
});
```

**Problem:**
- `Verb = "open"` jest Windows-specific
- Linux ma różne menedżery plików (Nautilus, Dolphin, Thunar, etc.)

**Rozwiązanie:**

```csharp
private void OpenFolder(string path)
{
    if (OperatingSystem.IsWindows())
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "open"
        });
    }
    else if (OperatingSystem.IsLinux())
    {
        // xdg-open automatycznie wybierze odpowiedni file manager
        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        });
    }
    else if (OperatingSystem.IsMacOS())
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        });
    }
}
```

**Wymagania Linux:**
- Pakiet `xdg-utils` (zazwyczaj preinstalowany)

**Wysiłek:** 🟢 Niski (1 dzień)

---

#### C. Tworzenie skrótów na pulpicie

**Lokalizacja:** `SUSModder/ViewModels/MainWindowViewModel.ExternalActions.cs:194-228`

```csharp
[SupportedOSPlatform("windows")]
private async void CreateWindowsShortcut(...)
{
    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
    dynamic? shell = Activator.CreateInstance(shellType);
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = targetPath;
    shortcut.Save();
}
```

**Problem:**
- `WScript.Shell` jest COM object (Windows-only)
- Linux używa `.desktop` files

**Rozwiązanie:**

```csharp
private void CreateLinuxDesktopFile(string modName, string exePath, string workingDir)
{
    string desktopPath = GetDesktopPath();
    string desktopFile = Path.Combine(desktopPath, $"{modName}.desktop");

    var content = new StringBuilder();
    content.AppendLine("[Desktop Entry]");
    content.AppendLine($"Name={modName}");
    content.AppendLine("Type=Application");
    content.AppendLine($"Exec=steam steam://rungameid/945360");
    content.AppendLine($"Path={workingDir}");
    content.AppendLine("Terminal=false");
    content.AppendLine("Categories=Game;");

    // Ikona (jeśli istnieje)
    string iconPath = Path.Combine(workingDir, "icon.png");
    if (File.Exists(iconPath))
        content.AppendLine($"Icon={iconPath}");

    File.WriteAllText(desktopFile, content.ToString());

    // Ustaw uprawnienia wykonywania
    Process.Start("chmod", $"+x \"{desktopFile}\"")?.WaitForExit();
}

private static string GetDesktopPath()
{
    if (OperatingSystem.IsLinux())
    {
        // Użyj xdg-user-dir
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-user-dir",
                Arguments = "DESKTOP",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                string path = process.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
        }
        catch { }

        // Fallback
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
    }

    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
}
```

**Wymagania Linux:**
- Pakiet `xdg-utils` (dla xdg-user-dir)

**Wysiłek:** 🟡 Średni (2-3 dni)

---

## Kompatybilność UI (Avalonia)

### ✅ DOSKONAŁA KOMPATYBILNOŚĆ

**Wersja Avalonia:** 11.3.7

Avalonia UI jest **w pełni cross-platform** i działa na:
- ✅ Windows
- ✅ Linux (X11 i Wayland)
- ✅ macOS
- ✅ WebAssembly
- ✅ iOS/Android

### Analiza kodu UI:

#### 1. Brak Windows-specific kontrolek

Wszystkie kontrolki używane w SUSModder są standardowe Avalonia controls:
- `Window`, `UserControl`, `Button`, `TextBlock`
- `ListBox`, `ComboBox`, `ProgressBar`
- `Image`, `Border`, `Grid`, `StackPanel`

**Wniosek:** ✅ Wszystkie działają na Linux

---

#### 2. Dialogi i file pickers

SUSModder **NIE używa** natywnych Windows dialogów (Win32).

**Wniosek:** ✅ Brak problemów z dialogami

---

#### 3. Converters i behaviors

**Lokalizacja:** `SUSModder/Converters/UrlToCommandConverter.cs:22-43`

```csharp
private static void OpenUrl(string url)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        Process.Start("xdg-open", url);
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        Process.Start("open", url);
    }
}
```

**Wniosek:** ✅ DOSKONALE zaimplementowane! Cross-platform support jest już obecny.

---

#### 4. Themes i styles

**Lokalizacja:**
- `SUSModder/Themes/DarkTheme.axaml`
- `SUSModder/Themes/LightTheme.axaml`
- `SUSModder/Themes/PinkTheme.axaml`

Wszystkie motywy używają standardowych Avalonia styles.

**Wniosek:** ✅ Działają na wszystkich platformach

---

#### 5. Application manifest

**Lokalizacja:** `SUSModder/SUSModder.csproj:8`

```xml
<ApplicationManifest>app.manifest</ApplicationManifest>
```

**Problem (minimalny):**
- `app.manifest` jest Windows-specific (DPI awareness, UAC settings)
- Zostanie zignorowany na Linux (brak błędów)

**Rozwiązanie:**
```xml
<ApplicationManifest Condition="'$(RuntimeIdentifier)' == 'win-x64'">app.manifest</ApplicationManifest>
```

**Wysiłek:** 🟢 Trivial (5 minut)

---

#### 6. Application icon

**Lokalizacja:** `SUSModder/SUSModder.csproj:6`

```xml
<ApplicationIcon>Assets\icon.ico</ApplicationIcon>
```

**Problem (minimalny):**
- `.ico` jest Windows-specific format
- Linux preferuje `.png` lub `.svg`

**Rozwiązanie:**
```xml
<!-- Multi-platform icon -->
<ApplicationIcon Condition="'$(RuntimeIdentifier)' == 'win-x64'">Assets\icon.ico</ApplicationIcon>
<ApplicationIcon Condition="'$(RuntimeIdentifier)' != 'win-x64'">Assets\icon.png</ApplicationIcon>
```

**Wysiłek:** 🟢 Niski (1 dzień, wymaga konwersji ikony)

---

### PODSUMOWANIE UI:

| Aspekt | Status | Wymagane zmiany |
|--------|--------|----------------|
| Avalonia Framework | ✅ Gotowy | Brak |
| Kontrolki | ✅ Cross-platform | Brak |
| Converters | ✅ Już cross-platform | Brak |
| Themes | ✅ Cross-platform | Brak |
| Manifest | ⚠️ Windows-only | Conditional (trivial) |
| Icon | ⚠️ .ico format | Konwersja do .png |

**Całkowity wysiłek UI:** 🟢 Minimalny (1-2 dni)

---

## Among Us + BepInEx na Linux

### Czy Among Us działa na Linux?

**Odpowiedź:** ⚠️ **Częściowo - wymaga Proton/Wine**

Among Us jest aplikacją Windows (.NET Framework 4.7.2), ale działa na Linux przez:

1. **Steam Proton** (zalecane)
   - Among Us (AppID: 945360) ma oficjalny rating ProtonDB: **Gold/Platinum**
   - Działa out-of-the-box na Steam Linux

2. **Wine**
   - Możliwe, ale wymaga manualnej konfiguracji
   - Gorsze performance

### Czy BepInEx działa na Linux?

**Odpowiedź:** ✅ **TAK - istnieje BepInEx for Proton**

BepInEx ma oficjalne wsparcie dla Proton:
- **Wersja:** BepInEx 6.x (Unix/Proton)
- **Instalacja:** Specjalna struktura katalogów
- **Kompatybilność:** Większość modów działa

**Lokalizacja BepInEx na Linux:**
```
~/.steam/steam/steamapps/common/Among Us/
├── Among Us.exe
├── BepInEx/
│   ├── core/
│   ├── plugins/
│   └── config/
└── doorstop_config.ini
```

### Jak SUSModder uruchamia Among Us?

**Obecna implementacja (Windows):**

```csharp
// SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs:197-247
Process.Start(new ProcessStartInfo("steam://") { UseShellExecute = true });
await Task.Delay(1000);
Process.Start(exePath); // "Among Us.exe"
```

**Problem dla Linux:**
- Bezpośrednie `Process.Start(exePath)` na `.exe` nie zadziała
- Trzeba uruchomić przez Steam/Proton

**Rozwiązanie:**

```csharp
private async Task LaunchGameAsync(ModConfiguration modConfig)
{
    if (OperatingSystem.IsWindows())
    {
        // Istniejący kod - bezpośrednie uruchomienie .exe
        Process.Start(exePath);
    }
    else if (OperatingSystem.IsLinux())
    {
        // Uruchom przez Steam URI
        Process.Start(new ProcessStartInfo("steam://rungameid/945360")
        {
            UseShellExecute = true
        });

        // ALTERNATYWNIE: przez steamcmd
        // Process.Start("steam", "steam://rungameid/945360");
    }
}
```

**Wysiłek:** 🟡 Średni (3-4 dni, wymaga testowania z modami)

---

### Instalacja modów na Linux

**Problem:**
Obecna logika w `ModManager.cs` zakłada bezpośredni dostęp do plików gry.

**Na Linux z Proton:**
- Pliki gry: `~/.steam/steam/steamapps/common/Among Us/`
- Prefix Proton: `~/.steam/steam/steamapps/compatdata/945360/pfx/`
- Konfiguracja gry: `~/.steam/steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser/AppData/LocalLow/Innersloth/Among Us`

**Rozwiązanie:**

```csharp
public static string LocateAmongUsInstallation()
{
    if (OperatingSystem.IsLinux())
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Steam default location
        string steamPath = Path.Combine(home, ".steam/steam/steamapps/common/Among Us");
        if (Directory.Exists(steamPath))
            return steamPath;

        // Flatpak Steam
        string flatpakSteam = Path.Combine(home,
            ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Among Us");
        if (Directory.Exists(flatpakSteam))
            return flatpakSteam;

        // Szukaj w bibliotekach Steam
        return FindInSteamLibraries();
    }

    // Windows code...
}
```

**Wysiłek:** 🔴 Wysoki (5-7 dni)

---

### Testowanie modów na Linux

**Znane problemy:**

1. **Mody DLL** - Większość działa, ale niektóre mogą mieć problemy z .NET Framework dependencies
2. **Mody z native dependencies** - Mogą wymagać 32-bit Wine libraries
3. **Anti-cheat** - Among Us nie ma anti-cheata, więc brak problemów

**Rekomendacje:**

- Testować na czystej instalacji Steam Linux
- Testować z różnymi wersjami Proton (Proton Experimental, GE-Proton)
- Dokumentować kompatybilność per mod

**Wysiłek:** 🔴 Intensywne testowanie (2-3 tygodnie)

---

## Ocena komponentów

### Szczegółowa tabela kompatybilności:

| Komponent | Plik | Status Linux | Wysiłek | Priorytet |
|-----------|------|--------------|---------|-----------|
| **Avalonia UI** | Cała warstwa UI | ✅ Gotowy | Minimalny | Niski |
| **UrlToCommandConverter** | Converters/UrlToCommandConverter.cs | ✅ Gotowy | Brak | Niski |
| **PathSettings** | Core/Utilities/PathSettings.cs | ❌ Windows-only | 🔴 Wysoki | **Krytyczny** |
| **GameLocator** | Core/GameIntegration/GameLocator.cs | ❌ Windows-only | 🔴 Wysoki | **Krytyczny** |
| **ModManager** | Core/GameIntegration/ModManager.cs | ⚠️ Częściowo | 🔴 Wysoki | **Krytyczny** |
| **EpicVersionManager** | Core/GameIntegration/EpicVersionManager.cs | ❌ Windows-only | 🔴 Bardzo wysoki | Średni |
| **ModConfigHandler** | Core/Configuration/ModConfigHandler.cs | ⚠️ Częściowo | 🟡 Średni | Wysoki |
| **HardwareIdProvider** | Core/Utilities/HardwareIdProvider.cs | ❌ Windows-only (WMI) | 🟡 Średni | Niski |
| **FileSystemUtilities** | Core/Utilities/FileSystemUtilities.cs | ⚠️ Częściowo | 🟡 Średni | Wysoki |
| **FileSystemHelper** | Services/FileSystemHelper.cs | ⚠️ Częściowo | 🟡 Średni | Średni |
| **AppUpdateService** | Core/Services/AppUpdateService.cs | ⚠️ Częściowo | 🟡 Średni | Wysoki |
| **Updater** | Updater/Program.cs | ❌ Windows-only | 🟡 Średni | Wysoki |
| **MainWindowViewModel.GameLaunch** | ViewModels/MainWindowViewModel.GameLaunch.cs | ⚠️ Częściowo | 🟡 Średni | **Krytyczny** |
| **MainWindowViewModel.ExternalActions** | ViewModels/MainWindowViewModel.ExternalActions.cs | ⚠️ Częściowo | 🟡 Średni | Niski |

### Legenda:

- ✅ **Gotowy** - Działa na Linux bez zmian
- ⚠️ **Częściowo** - Wymaga modyfikacji, ale nie całkowitej przepisania
- ❌ **Windows-only** - Wymaga znaczących zmian lub przepisania
- 🟢 **Niski** - 1-2 dni
- 🟡 **Średni** - 3-5 dni
- 🔴 **Wysoki** - 5-10 dni

---

## Wnioski końcowe

### ✅ Wykonalność: **TAK, ale z zastrzeżeniami**

Port SUSModder na Linux jest **technicznie możliwy**, ale wymaga:

1. **Refaktoru ~40% kodu SUSModder.Core**
2. **Wprowadzenia warstwy abstrakcji platformy**
3. **Przepisania integracji Epic Games (lub wyłączenia)**
4. **Intensywnego testowania z BepInEx na Proton**
5. **Dokumentacji dla użytkowników Linux**

### Szacowany całkowity wysiłek:

| Faza | Czas | Opis |
|------|------|------|
| Faza 1: Podstawy | 2-3 tygodnie | RuntimeIdentifiers, PathProvider, podstawowa abstrakcja |
| Faza 2: Core Services | 3-4 tygodnie | GameLocator, ModManager, external tools |
| Faza 3: Epic + Advanced | 2-3 tygodnie | EpicVersionManager lub wyłączenie, permissions |
| Faza 4: Testing | 2-3 tygodnie | Testing na różnych distro + modach |
| **RAZEM** | **8-12 tygodni** | **Pełen port z testowaniem** |

### Rekomendacja:

**Rozpocząć od MVP (Minimum Viable Product):**

1. **MVP Scope:**
   - ✅ Wsparcie **tylko dla Steam** (bez Epic)
   - ✅ Podstawowa instalacja modów (bez advanced features)
   - ✅ Uruchamianie gry przez Steam URI
   - ✅ Debian/Ubuntu jako primary target

2. **MVP Timeline:** 4-6 tygodni

3. **Post-MVP:**
   - Epic Games support (Heroic Launcher)
   - Advanced features (shortcuts, etc.)
   - Packaging dla Arch, Fedora
   - Flatpak/AppImage

---

**Następny dokument:** [02-ARCHITECTURE.md](./02-ARCHITECTURE.md) - Szczegółowa architektura rozwiązania
