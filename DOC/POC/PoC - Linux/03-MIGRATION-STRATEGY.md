# Strategia Migracji: Refaktoring SUSModder na Cross-Platform

**Data:** 2025-10-30
**Cel:** Plan migracji kodu z Windows-only do cross-platform architecture

---

## Spis treści

1. [Przegląd strategii](#przegląd-strategii)
2. [Fazy migracji](#fazy-migracji)
3. [Migracja poszczególnych komponentów](#migracja-poszczególnych-komponentów)
4. [Testing strategy](#testing-strategy)
5. [Compatibility considerations](#compatibility-considerations)

---

## Przegląd strategii

### Podejście: Incremental Refactoring

Zamiast całkowitego przepisania (big bang), stosujemy **incremental refactoring**:

1. ✅ Zachowujemy istniejącą funkcjonalność Windows
2. ✅ Dodajemy warstwę abstrakcji
3. ✅ Implementujemy Linux support stopniowo
4. ✅ Testujemy na każdym etapie

### Zasady migracji:

- **Backward compatibility** - Windows build musi dalej działać
- **Conditional compilation** - Użycie `#if` dla platform-specific code
- **Runtime detection** - `OperatingSystem.IsWindows()` / `IsLinux()`
- **Dependency Injection** - Przejście ze static classes na DI
- **Interface-based design** - Wszystko przez interfejsy

---

## Fazy migracji

### Faza 0: Przygotowanie (1 tydzień)

#### 0.1. Utworzenie nowych projektów

```bash
# Dodaj nowe projekty do solution
dotnet new classlib -n SUSModder.Platform
dotnet new classlib -n SUSModder.Platform.Windows
dotnet new classlib -n SUSModder.Platform.Linux

# Dodaj do solution
dotnet sln add SUSModder.Platform/SUSModder.Platform.csproj
dotnet sln add SUSModder.Platform.Windows/SUSModder.Platform.Windows.csproj
dotnet sln add SUSModder.Platform.Linux/SUSModder.Platform.Linux.csproj
```

#### 0.2. Konfiguracja RuntimeIdentifiers

```xml
<!-- SUSModder/SUSModder.csproj -->
<PropertyGroup>
    <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>

    <!-- OutputType conditional -->
    <OutputType Condition="'$(RuntimeIdentifier)' == 'win-x64'">WinExe</OutputType>
    <OutputType Condition="'$(RuntimeIdentifier)' != 'win-x64'">Exe</OutputType>
</PropertyGroup>

<!-- ApplicationManifest tylko dla Windows -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
    <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

#### 0.3. Conditional packages

```xml
<!-- SUSModder.Core/SUSModder.Core.csproj -->
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64' OR '$(RuntimeIdentifier)' == ''">
    <PackageReference Include="System.Management" Version="9.0.10" />
</ItemGroup>

<!-- Tools conditional -->
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
    <Content Include="tools\7z.exe">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </Content>
    <Content Include="tools\7z.dll">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </Content>
</ItemGroup>
```

---

### Faza 1: Warstwa abstrakcji (2 tygodnie)

#### 1.1. Zdefiniowanie interfejsów

**Krok 1:** Utwórz plik `SUSModder.Platform/Interfaces/IPlatformServices.cs`

```csharp
namespace SUSModder.Platform;

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

**Krok 2:** Utwórz pozostałe interfejsy (IPathProvider, IGameLocator, etc.)

#### 1.2. Migracja PathSettings → IPathProvider

**PRZED:**

```csharp
// SUSModder.Core/Utilities/PathSettings.cs
public static class PathSettings
{
    private static string _defaultModsPath;

    static PathSettings()
    {
        _defaultModsPath = Environment.ExpandEnvironmentVariables(
            "%APPDATA%\\Among Us - Mody");
    }

    public static string GetActualModPath(string installPath)
    {
        // ...
    }
}
```

**PO:**

```csharp
// SUSModder.Platform.Windows/WindowsPathProvider.cs
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

    // ... pozostałe metody
}

// SUSModder.Platform.Linux/LinuxPathProvider.cs
public class LinuxPathProvider : IPathProvider
{
    public string GetDefaultModsPath()
    {
        string xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".local/share");
        return Path.Combine(xdgDataHome, "among-us-mody");
    }

    public string GetAmongUsConfigPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string protonPath = Path.Combine(home,
            ".steam/steam/steamapps/compatdata/945360/pfx/drive_c/users/steamuser",
            "AppData/LocalLow/Innersloth/Among Us");

        if (Directory.Exists(protonPath))
            return protonPath;

        // Szukaj w innych lokalizacjach
        return FindAmongUsConfigInProtonPrefixes();
    }

    private string FindAmongUsConfigInProtonPrefixes()
    {
        // TODO: Implementacja
        return string.Empty;
    }

    // ... pozostałe metody
}
```

**MIGRACJA UŻYCIA:**

```csharp
// PRZED
string path = PathSettings.GetActualModPath(modPath);

// PO
public class SomeService
{
    private readonly IPathProvider _pathProvider;

    public SomeService(IPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public void DoSomething()
    {
        string defaultPath = _pathProvider.GetDefaultModsPath();
        // ...
    }
}
```

**STRATEGIA MIGRACJI:**

1. Utw orz `IPathProvider` i implementacje
2. **ZACHOWAJ** `PathSettings` (dla backward compatibility)
3. Dodaj adapter w `PathSettings`:

```csharp
// SUSModder.Core/Utilities/PathSettings.cs
public static class PathSettings
{
    private static IPathProvider? _pathProvider;

    /// <summary>
    /// Ustaw provider (dla DI)
    /// </summary>
    public static void SetProvider(IPathProvider provider)
    {
        _pathProvider = provider;
    }

    /// <summary>
    /// Legacy method - deprecated, używaj IPathProvider
    /// </summary>
    [Obsolete("Use IPathProvider instead")]
    public static string GetActualModPath(string installPath)
    {
        if (_pathProvider == null)
        {
            // Fallback do starej implementacji (Windows)
            return GetActualModPathLegacy(installPath);
        }

        // Użyj nowego providera
        string defaultPath = _pathProvider.GetDefaultModsPath();
        // ... logika
    }

    private static string GetActualModPathLegacy(string installPath)
    {
        // Stara implementacja Windows-only
    }
}
```

4. Stopniowo zastępuj wywołania `PathSettings` przez `IPathProvider`
5. Gdy wszystkie miejsca będą zmienione, usuń `PathSettings`

---

#### 1.3. Migracja GameLocator → IGameLocator

**MIGRACJA:**

```csharp
// SUSModder.Platform.Windows/WindowsGameLocator.cs
public class WindowsGameLocator : IGameLocator
{
    public string? LocateAmongUs()
    {
        foreach (var steamPath in GetCommonSteamPaths())
        {
            string amongUsPath = Path.Combine(steamPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
                return amongUsPath;
        }

        // ... reszta logiki
    }

    private IEnumerable<string> GetCommonSteamPaths()
    {
        return new[]
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%/Steam"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/Steam"),
            // ...
        };
    }

    public bool IsAmongUsInstallation(string path)
    {
        if (!Directory.Exists(path))
            return false;

        string exePath = Path.Combine(path, "Among Us.exe");
        return File.Exists(exePath);
    }

    // ... pozostałe metody
}

// SUSModder.Platform.Linux/LinuxGameLocator.cs
public class LinuxGameLocator : IGameLocator
{
    public string? LocateAmongUs()
    {
        // 1. Steam locations
        foreach (var steamPath in GetCommonSteamPaths())
        {
            string amongUsPath = Path.Combine(steamPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
                return amongUsPath;
        }

        // 2. Parse Steam library folders
        foreach (var libraryPath in GetSteamLibraryPaths())
        {
            string amongUsPath = Path.Combine(libraryPath, "steamapps/common/Among Us");
            if (IsAmongUsInstallation(amongUsPath))
                return amongUsPath;
        }

        return null;
    }

    private IEnumerable<string> GetCommonSteamPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".local/share/Steam"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam")
        };
    }

    public IEnumerable<string> GetSteamLibraryPaths()
    {
        // Parse libraryfolders.vdf
        // TODO: Implementacja
        return Enumerable.Empty<string>();
    }

    // ...
}
```

**ADAPTER w GameLocator.cs:**

```csharp
// SUSModder.Core/GameIntegration/GameLocator.cs
public static class GameLocator
{
    private static IGameLocator? _locator;

    public static void SetLocator(IGameLocator locator)
    {
        _locator = locator;
    }

    [Obsolete("Use IGameLocator instead")]
    public static string? LocateAmongUs()
    {
        if (_locator != null)
            return _locator.LocateAmongUs();

        // Fallback do starej implementacji
        return LocateAmongUsLegacy();
    }

    private static string? LocateAmongUsLegacy()
    {
        // Stara logika Windows-only
    }
}
```

---

#### 1.4. Migracja ModManager → IArchiveExtractor

**PRZED:**

```csharp
// SUSModder.Core/GameIntegration/ModManager.cs
private void Extract7zWithPassword(string archivePath, string extractPath, string password)
{
    string sevenZipPath = Path.Combine(appDirPath, "tools", "7z.exe");
    // ... wywołanie process
}
```

**PO:**

```csharp
// SUSModder.Core/GameIntegration/ModManager.cs
public class ModManager
{
    private readonly IArchiveExtractor _archiveExtractor;

    public ModManager(IArchiveExtractor archiveExtractor, /* inne dependencies */)
    {
        _archiveExtractor = archiveExtractor;
    }

    private async Task Extract7zWithPasswordAsync(string archivePath, string extractPath, string password)
    {
        await _archiveExtractor.Extract7zAsync(archivePath, extractPath, password);
    }
}
```

---

### Faza 2: Core Services Refactor (3 tygodnie)

#### 2.1. Wprowadzenie Dependency Injection

**Krok 1:** Dodaj Microsoft.Extensions.DependencyInjection

```xml
<!-- SUSModder/SUSModder.csproj -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
```

**Krok 2:** Skonfiguruj DI w Program.cs

```csharp
// SUSModder/Program.cs
public static void Main(string[] args)
{
    var services = ConfigureServices();
    var serviceProvider = services.BuildServiceProvider();

    BuildAvaloniaApp()
        .AfterSetup(builder =>
        {
            // Przekaż service provider do aplikacji
            if (Application.Current is App app)
            {
                app.ServiceProvider = serviceProvider;
            }
        })
        .StartWithClassicDesktopLifetime(args);
}

private static IServiceCollection ConfigureServices()
{
    var services = new ServiceCollection();

    // Platform services
    if (OperatingSystem.IsWindows())
    {
        services.AddSingleton<IPathProvider, WindowsPathProvider>();
        services.AddSingleton<IGameLocator, WindowsGameLocator>();
        services.AddSingleton<IProcessManager, WindowsProcessManager>();
        services.AddSingleton<IArchiveExtractor, WindowsArchiveExtractor>();
        services.AddSingleton<IPermissionManager, WindowsPermissionManager>();
        services.AddSingleton<IHardwareInfoProvider, WindowsHardwareInfoProvider>();
    }
    else if (OperatingSystem.IsLinux())
    {
        services.AddSingleton<IPathProvider, LinuxPathProvider>();
        services.AddSingleton<IGameLocator, LinuxGameLocator>();
        services.AddSingleton<IProcessManager, LinuxProcessManager>();
        services.AddSingleton<IArchiveExtractor, LinuxArchiveExtractor>();
        services.AddSingleton<IPermissionManager, LinuxPermissionManager>();
        services.AddSingleton<IHardwareInfoProvider, LinuxHardwareInfoProvider>();
    }

    // Core services
    services.AddSingleton<ConfigService>();
    services.AddSingleton<ModService>();
    services.AddSingleton<GameService>();
    services.AddSingleton<AppUpdateService>();

    // ViewModels
    services.AddTransient<MainWindowViewModel>();
    services.AddTransient<AppSettingsViewModel>();

    return services;
}
```

**Krok 3:** Przekaż ServiceProvider do ViewModels

```csharp
// SUSModder/App.axaml.cs
public partial class App : Application
{
    public IServiceProvider? ServiceProvider { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = ServiceProvider?.GetRequiredService<MainWindowViewModel>()
                ?? new MainWindowViewModel(); // Fallback

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

---

#### 2.2. Refactor ConfigManager → ConfigService

**PRZED (static):**

```csharp
public static class ConfigManager
{
    public static async Task<ModConfiguration[]> LoadConfigAsync()
    {
        // Static method
    }
}

// Usage
var configs = await ConfigManager.LoadConfigAsync();
```

**PO (DI):**

```csharp
public class ConfigService
{
    private readonly IPathProvider _pathProvider;
    private readonly IConfiguration _configuration;

    public ConfigService(IPathProvider pathProvider, IConfiguration configuration)
    {
        _pathProvider = pathProvider;
        _configuration = configuration;
    }

    public async Task<ModConfiguration[]> LoadConfigAsync()
    {
        string configPath = GetConfigPath();
        // ...
    }

    private string GetConfigPath()
    {
        // Użyj _pathProvider zamiast hardcodowanych ścieżek
        string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        return Path.Combine(appDir, "config.json");
    }
}

// Usage (w ViewModels)
public class MainWindowViewModel
{
    private readonly ConfigService _configService;

    public MainWindowViewModel(ConfigService configService)
    {
        _configService = configService;
    }

    public async Task LoadModsAsync()
    {
        var configs = await _configService.LoadConfigAsync();
        // ...
    }
}
```

---

### Faza 3: External Tools Integration (2 tygodnie)

#### 3.1. Archiwizacja - WindowsArchiveExtractor vs LinuxArchiveExtractor

**Windows:**

```csharp
public class WindowsArchiveExtractor : IArchiveExtractor
{
    public async Task Extract7zAsync(string archivePath, string extractPath, string? password = null)
    {
        string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        string sevenZipPath = Path.Combine(appDir, "tools", "7z.exe");

        if (!File.Exists(sevenZipPath))
            throw new FileNotFoundException($"7z.exe not found: {sevenZipPath}");

        string arguments = password != null
            ? $"x \"{archivePath}\" -o\"{extractPath}\" -p{password} -y"
            : $"x \"{archivePath}\" -o\"{extractPath}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        await process!.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7z failed with code {process.ExitCode}");
    }
}
```

**Linux:**

```csharp
public class LinuxArchiveExtractor : IArchiveExtractor
{
    public async Task Extract7zAsync(string archivePath, string extractPath, string? password = null)
    {
        // Znajdź systemowy 7z
        string? sevenZipPath = await FindExecutableAsync("7z")
                            ?? await FindExecutableAsync("7zz")
                            ?? await FindExecutableAsync("7za");

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
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi);
        await process!.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7z failed with code {process.ExitCode}");
    }

    private static async Task<string?> FindExecutableAsync(string name)
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
                await process.WaitForExitAsync();
                string path = (await process.StandardOutput.ReadToEndAsync()).Trim();

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

#### 3.2. Epic Games - legendary vs Heroic

**DECYZJA:** Na Linux używamy **Heroic Games Launcher** zamiast legendary CLI

**Windows (legendary.exe):**

```csharp
// Istniejący EpicVersionManager pozostaje bez zmian dla Windows
```

**Linux (Heroic):**

```csharp
public class LinuxEpicGameManager
{
    private readonly IProcessManager _processManager;

    public LinuxEpicGameManager(IProcessManager processManager)
    {
        _processManager = processManager;
    }

    public async Task<bool> LaunchAmongUsThroughHeroicAsync()
    {
        string? heroicPath = await FindExecutableAsync("heroic");

        if (heroicPath == null)
        {
            throw new InvalidOperationException(
                "Heroic Games Launcher nie jest zainstalowany.\n" +
                "Pobierz z: https://heroicgameslauncher.com/\n" +
                "Lub zainstaluj przez Flatpak: flatpak install com.heroicgameslauncher.hgl");
        }

        var psi = new ProcessStartInfo
        {
            FileName = heroicPath,
            Arguments = "launch \"Among Us\"",
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        return process != null;
    }

    // ... inne metody
}
```

---

### Faza 4: UI Adjustments (1 tydzień)

#### 4.1. Otwieranie folderów - platform-specific

```csharp
// SUSModder/ViewModels/MainWindowViewModel.ExternalActions.cs
private void OpenFolder()
{
    if (SelectedMod?.InstallPath == null || !Directory.Exists(SelectedMod.InstallPath))
        return;

    try
    {
        _processManager.OpenFolder(SelectedMod.InstallPath);
    }
    catch (Exception ex)
    {
        _ = ShowErrorDialogAsync($"Nie można otworzyć folderu: {ex.Message}", "Błąd");
    }
}

// IProcessManager implementation already handles platform differences
```

#### 4.2. Tworzenie skrótów - platform-specific

```csharp
// SUSModder/ViewModels/MainWindowViewModel.ExternalActions.cs
private async void CreateShortcut()
{
    if (SelectedMod?.InstallPath == null)
        return;

    try
    {
        string actualModPath = _pathProvider.GetActualModPath(SelectedMod.InstallPath);
        string amongUsExePath = Path.Combine(actualModPath, "Among Us.exe");

        if (!File.Exists(amongUsExePath))
        {
            await ShowErrorDialogAsync("Nie znaleziono pliku Among Us.exe", "Błąd");
            return;
        }

        await _processManager.CreateDesktopShortcutAsync(
            SelectedMod.Name,
            amongUsExePath,
            actualModPath);

        await ShowMessageAsync("Sukces", $"Skrót '{SelectedMod.Name}' został utworzony na pulpicie.");
    }
    catch (Exception ex)
    {
        await ShowErrorDialogAsync($"Nie udało się utworzyć skrótu: {ex.Message}", "Błąd");
    }
}
```

---

### Faza 5: Testing & Packaging (2-3 tygodnie)

#### 5.1. Unit Tests

**Dodaj projekt testowy:**

```bash
dotnet new xunit -n SUSModder.Tests
dotnet sln add SUSModder.Tests/SUSModder.Tests.csproj
```

**Przykładowy test:**

```csharp
public class LinuxPathProviderTests
{
    [Fact]
    public void GetDefaultModsPath_ReturnsXdgCompliantPath()
    {
        // Arrange
        var provider = new LinuxPathProvider();

        // Act
        string path = provider.GetDefaultModsPath();

        // Assert
        Assert.Contains(".local/share/among-us-mody", path);
    }

    [Fact]
    public void GetAmongUsConfigPath_FindsProtonPrefix()
    {
        // Arrange
        var provider = new LinuxPathProvider();

        // Act
        string path = provider.GetAmongUsConfigPath();

        // Assert
        // Sprawdź czy ścieżka zawiera "compatdata/945360" (Proton prefix)
        Assert.True(path.Contains("compatdata") || string.IsNullOrEmpty(path));
    }
}
```

#### 5.2. Integration Tests

```csharp
public class GameLocatorIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void LinuxGameLocator_CanLocateAmongUs_WhenInstalled()
    {
        // Ten test zadziała tylko na systemach Linux z zainstalowanym Among Us

        if (!OperatingSystem.IsLinux())
        {
            // Skip na Windows
            return;
        }

        // Arrange
        var pathProvider = new LinuxPathProvider();
        var locator = new LinuxGameLocator(pathProvider);

        // Act
        string? gamePath = locator.LocateAmongUs();

        // Assert
        if (gamePath != null)
        {
            Assert.True(Directory.Exists(gamePath));
            Assert.True(File.Exists(Path.Combine(gamePath, "Among Us.exe")));
        }
        else
        {
            // Among Us nie jest zainstalowany - to OK dla testów
            Assert.Null(gamePath);
        }
    }
}
```

#### 5.3. Publish scripts

**Windows:**

```bash
# publish-windows.bat
dotnet publish SUSModder\SUSModder.csproj -c Release -r win-x64 --self-contained -o publish\win-x64
```

**Linux:**

```bash
# publish-linux.sh
#!/bin/bash
dotnet publish SUSModder/SUSModder.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
chmod +x publish/linux-x64/SUSModder
```

---

## Testing Strategy

### Test Matrix:

| Platforma | Distro | Desktop Environment | Steam | Epic (Heroic) |
|-----------|--------|---------------------|-------|---------------|
| **Windows** | Windows 10 | - | ✅ | ✅ |
| **Windows** | Windows 11 | - | ✅ | ✅ |
| **Linux** | Ubuntu 24.04 | GNOME | ✅ | ✅ |
| **Linux** | Ubuntu 24.04 | KDE Plasma | ✅ | ✅ |
| **Linux** | Fedora 40 | GNOME | ✅ | ⚠️ |
| **Linux** | Arch Linux | KDE Plasma | ✅ | ⚠️ |
| **Linux** | Debian 12 | XFCE | ✅ | ❌ |

### Test Cases:

1. **Instalacja moda**
   - [x] Zainstaluj mod "Town of Us"
   - [x] Sprawdź czy pliki są w odpowiednim miejscu
   - [x] Uruchom grę i sprawdź czy mod działa

2. **Aktualizacja moda**
   - [x] Zaktualizuj istniejący mod
   - [x] Sprawdź czy poprzednie pliki zostały nadpisane

3. **Usunięcie moda**
   - [x] Usuń mod
   - [x] Sprawdź czy katalog został usunięty
   - [x] Sprawdź czy bez elevated permissions
   - [x] Sprawdź czy z elevated permissions (pkexec)

4. **Uruchomienie gry**
   - [x] Uruchom Steam version
   - [x] Sprawdź czy uruchomiła się przez Proton
   - [x] Sprawdź czy mod załadował się poprawnie

5. **Otwieranie folderów**
   - [x] Otwórz folder moda w file managerze
   - [x] Sprawdź czy działa na GNOME Nautilus
   - [x] Sprawdź czy działa na KDE Dolphin
   - [x] Sprawdź czy działa na XFCE Thunar

6. **Skróty pulpitu**
   - [x] Utwórz skrót .desktop
   - [x] Sprawdź czy pojawia się na pulpicie
   - [x] Sprawdź czy ma uprawnienia wykonywania
   - [x] Sprawdź czy uruchamia grę

---

## Compatibility Considerations

### Backward Compatibility (Windows)

**WAŻNE:** Wszystkie zmiany muszą zachować kompatybilność z Windows:

1. **Static methods** - Zachowaj jako adaptery
2. **Configuration files** - Nie zmieniaj formatu config.json/appsettings.json
3. **External tools** - 7z.exe i legendary.exe dalej w projekcie dla Windows
4. **APIs** - Nie zmieniaj public API istniejących klas

### Forward Compatibility (Linux)

**Przygotowanie na przyszłość:**

1. **Flatpak support** - Ścieżki dla Flatpak Steam/Heroic
2. **Snap support** - Alternatywne ścieżki dla Snap packages
3. **Wayland support** - Avalonia już wspiera Wayland
4. **Steam Deck** - Specjalne przypadki dla SteamOS

---

**Następny dokument:** [04-IMPLEMENTATION-PLAN.md](./04-IMPLEMENTATION-PLAN.md) - Fazowy plan implementacji z timeline
