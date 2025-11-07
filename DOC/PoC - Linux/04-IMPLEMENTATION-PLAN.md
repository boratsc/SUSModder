# Plan Implementacji: SUSModder Linux Port

**Data:** 2025-10-30
**Timeline całkowity:** 8-12 tygodni
**Zasoby:** 1 developer full-time

---

## Spis treści

1. [Timeline i Milestones](#timeline-i-milestones)
2. [Faza 1: Fundament (Tydzień 1-2)](#faza-1-fundament-tydzień-1-2)
3. [Faza 2: Core Refactor (Tydzień 3-5)](#faza-2-core-refactor-tydzień-3-5)
4. [Faza 3: Platform Integration (Tydzień 6-7)](#faza-3-platform-integration-tydzień-6-7)
5. [Faza 4: Epic Games Support (Tydzień 8)](#faza-4-epic-games-support-tydzień-8)
6. [Faza 5: Testing & Bug Fixes (Tydzień 9-10)](#faza-5-testing--bug-fixes-tydzień-9-10)
7. [Faza 6: Packaging & Release (Tydzień 11-12)](#faza-6-packaging--release-tydzień-11-12)
8. [Post-Release](#post-release)

---

## Timeline i Milestones

```
Tydzień 1-2:  [████████████████░░░░░░░░░░░░] Fundament (Platform Layer)
Tydzień 3-5:  [░░░░░░░░░░░░░░░░████████████░░░░░░░░░░░░] Core Refactor
Tydzień 6-7:  [░░░░░░░░░░░░░░░░░░░░░░░░░░░░████████░░░░] Platform Integration
Tydzień 8:    [░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░████] Epic Support
Tydzień 9-10: [░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░████████] Testing
Tydzień 11-12:[░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░████] Packaging
```

### Milestones:

- **M1 (Tydzień 2):** Platform Layer completed + DI setup
- **M2 (Tydzień 5):** Core Services refactored + Unit tests passing
- **M3 (Tydzień 7):** Linux build installuje pierwszy mod
- **M4 (Tydzień 8):** Epic Games support (optional)
- **M5 (Tydzień 10):** All tests passing on Ubuntu + Arch
- **M6 (Tydzień 12):** Release packages (.deb, .rpm, .tar.gz)

---

## Faza 1: Fundament (Tydzień 1-2)

**Cel:** Stworzyć infrastrukturę platform layer i skonfigurować project dla cross-platform build

### Tydzień 1

#### Dzień 1: Setup projektów

**Zadania:**
- [x] Utworzyć projekty: `SUSModder.Platform`, `SUSModder.Platform.Windows`, `SUSModder.Platform.Linux`
- [x] Dodać do solution
- [x] Skonfigurować RuntimeIdentifiers w .csproj (win-x64, linux-x64)
- [x] Conditional packages (System.Management tylko dla Windows)

**Oczekiwany wynik:**
```bash
dotnet sln SUSModder.sln
# Powinno pokazać 6 projektów:
# - SUSModder
# - SUSModder.Core
# - SUSModder.Platform
# - SUSModder.Platform.Windows
# - SUSModder.Platform.Linux
# - Updater
```

**Deliverable:** Solution compiles dla win-x64 i linux-x64

---

#### Dzień 2-3: Interfejsy platformy

**Zadania:**
- [x] Zdefiniować `IPlatformServices.cs`
- [x] Zdefiniować `IPathProvider.cs`
- [x] Zdefiniować `IGameLocator.cs`
- [x] Zdefiniować `IProcessManager.cs`
- [x] Zdefiniować `IArchiveExtractor.cs`
- [x] Zdefiniować `IPermissionManager.cs`
- [x] Zdefiniować `IHardwareInfoProvider.cs`

**Lokalizacja:** `SUSModder.Platform/Interfaces/`

**Deliverable:** Wszystkie interfejsy zdefiniowane i udokumentowane (XML docs)

---

#### Dzień 4-5: Windows Implementations

**Zadania:**
- [x] `WindowsPathProvider.cs` - Implementacja IPathProvider
- [x] `WindowsGameLocator.cs` - Przeniesienie logiki z GameLocator
- [x] `WindowsProcessManager.cs` - Implementacja uruchamiania gier/folderów
- [x] `WindowsArchiveExtractor.cs` - Wrapper dla 7z.exe
- [x] `WindowsPermissionManager.cs` - UAC elevation (PowerShell)
- [x] `WindowsHardwareInfoProvider.cs` - WMI wrapper
- [x] `WindowsPlatformServices.cs` - Root class

**Lokalizacja:** `SUSModder.Platform.Windows/`

**Deliverable:** Windows implementations kompletne, kompilują się

---

### Tydzień 2

#### Dzień 6-8: Linux Implementations

**Zadania:**
- [x] `LinuxPathProvider.cs` - XDG paths, Proton prefix paths
- [x] `LinuxGameLocator.cs` - Steam library parsing
- [x] `LinuxProcessManager.cs` - xdg-open, Steam URI
- [x] `LinuxArchiveExtractor.cs` - System 7z wrapper
- [x] `LinuxPermissionManager.cs` - pkexec/sudo
- [x] `LinuxHardwareInfoProvider.cs` - /proc, /sys, /etc/machine-id
- [x] `LinuxPlatformServices.cs` - Root class

**Lokalizacja:** `SUSModder.Platform.Linux/`

**Deliverable:** Linux implementations kompletne, kompilują się

---

#### Dzień 9-10: Dependency Injection Setup

**Zadania:**
- [x] Dodać Microsoft.Extensions.DependencyInjection do SUSModder
- [x] Skonfigurować DI w `Program.cs`
- [x] Zarejestrować platform services (conditional per OS)
- [x] Zarejestrować core services
- [x] Przekazać `IServiceProvider` do `App.axaml.cs`

**Deliverable:**
- DI działa na Windows
- DI działa na Linux
- Aplikacja uruchamia się (nawet jeśli funkcjonalność jeszcze nie działa)

**Test:**
```bash
# Windows
dotnet run --project SUSModder\SUSModder.csproj

# Linux
dotnet run --project SUSModder/SUSModder.csproj
```

---

**Milestone M1 Checklist:**
- [ ] 6 projektów w solution
- [ ] Wszystkie interfejsy zdefiniowane
- [ ] Windows implementations kompletne
- [ ] Linux implementations kompletne
- [ ] DI skonfigurowane
- [ ] Aplikacja uruchamia się na Windows i Linux

---

## Faza 2: Core Refactor (Tydzień 3-5)

**Cel:** Przepisać core services na DI, zastąpić static classes

### Tydzień 3

#### Dzień 11-12: PathSettings → IPathProvider Migration

**Zadania:**
- [x] Dodać adapter w `PathSettings.cs` (używa IPathProvider jeśli dostępny)
- [x] Refaktor `ModConfigHandler.cs` - użyj IPathProvider przez constructor injection
- [x] Refaktor `LobbyUtils.cs`
- [x] Refaktor `FixBlackScreen.cs`
- [x] Zastąp wszystkie wywołania `PathSettings` w ViewModels

**Dotknięte pliki:**
- `SUSModder.Core/Utilities/PathSettings.cs`
- `SUSModder.Core/Configuration/ModConfigHandler.cs`
- `SUSModder.Core/Utilities/LobbyUtils.cs`
- `SUSModder.Core/Utilities/FixBlackScreen.cs`
- `SUSModder/ViewModels/*.cs`

**Deliverable:** PathSettings jest deprecated, wszędzie używa się IPathProvider

---

#### Dzień 13-14: GameLocator → IGameLocator Migration

**Zadania:**
- [x] Dodać adapter w `GameLocator.cs`
- [x] Refaktor `GameService.cs` - użyj IGameLocator
- [x] Refaktor `MainWindowViewModel` - użyj IGameLocator
- [x] Zastąp wszystkie wywołania `GameLocator.LocateAmongUs()`

**Dotknięte pliki:**
- `SUSModder.Core/GameIntegration/GameLocator.cs`
- `SUSModder.Core/Services/GameService.cs`
- `SUSModder/ViewModels/MainWindowViewModel*.cs`

**Deliverable:** GameLocator działa przez DI

**Test:**
```csharp
// Powinna znaleźć Among Us
var gameLocator = serviceProvider.GetService<IGameLocator>();
string? gamePath = gameLocator.LocateAmongUs();
Assert.NotNull(gamePath); // jeśli gra zainstalowana
```

---

#### Dzień 15: ConfigManager → ConfigService Migration

**Zadania:**
- [x] Utworzyć `ConfigService.cs` (non-static)
- [x] Przenieść logikę z `ConfigManager` do `ConfigService`
- [x] Dodać DI dependencies (IPathProvider, IConfiguration)
- [x] Zastąpić wywołania `ConfigManager` w ViewModels

**Deliverable:** ConfigService działał z DI

---

### Tydzień 4

#### Dzień 16-17: ModManager → IArchiveExtractor Migration

**Zadania:**
- [x] Refaktor `ModManager.cs` - dodaj IArchiveExtractor przez constructor
- [x] Zastąp `Extract7zWithPassword()` przez `_archiveExtractor.Extract7zAsync()`
- [x] Testuj na Windows (7z.exe)
- [x] Testuj na Linux (system 7z)

**Dotknięte pliki:**
- `SUSModder.Core/GameIntegration/ModManager.cs`

**Deliverable:** ModManager używa IArchiveExtractor

**Test:**
```bash
# Linux - upewnij się że 7z zainstalowany
sudo apt install p7zip-full  # Ubuntu/Debian

# Test ekstrakcji
dotnet test SUSModder.Tests/ArchiveExtractorTests.cs
```

---

#### Dzień 18-19: FileSystemUtilities → IPermissionManager Migration

**Zadania:**
- [x] Refaktor `FileSystemUtilities.cs` - użyj IPermissionManager
- [x] Refaktor `FileSystemHelper.cs` - użyj IPermissionManager
- [x] Testuj elevated deletion na Windows (PowerShell UAC)
- [x] Testuj elevated deletion na Linux (pkexec)

**Dotknięte pliki:**
- `SUSModder.Core/Utilities/FileSystemUtilities.cs`
- `SUSModder/Services/FileSystemHelper.cs`

**Deliverable:** Usuwanie plików z elevated permissions działa na obu platformach

---

#### Dzień 20: HardwareIdProvider → IHardwareInfoProvider Migration

**Zadania:**
- [x] Refaktor `HardwareIdProvider.cs` - użyj IHardwareInfoProvider
- [x] Conditional compilation dla WMI (tylko Windows)
- [x] Testuj telemetry na Windows
- [x] Testuj telemetry na Linux

**Dotknięte pliki:**
- `SUSModder.Core/Utilities/HardwareIdProvider.cs`
- `SUSModder.Core/Services/TelemetryService.cs`

**Deliverable:** Telemetria działa na obu platformach

---

### Tydzień 5

#### Dzień 21-23: MainWindowViewModel Refactor

**Zadania:**
- [x] `MainWindowViewModel.GameLaunch.cs` - użyj IProcessManager
- [x] `MainWindowViewModel.ExternalActions.cs` - użyj IProcessManager
- [x] Zastąp wszystkie `Process.Start()` przez `_processManager`
- [x] Testuj uruchamianie gry na Windows
- [x] Testuj uruchamianie gry na Linux (Steam URI)

**Deliverable:** Uruchamianie gry działa na obu platformach

---

#### Dzień 24-25: Unit Tests

**Zadania:**
- [x] Utworzyć `SUSModder.Tests` project
- [x] Dodać testy dla `LinuxPathProvider`
- [x] Dodać testy dla `LinuxGameLocator`
- [x] Dodać testy dla `LinuxProcessManager`
- [x] Dodać testy dla `WindowsPathProvider`
- [x] Dodać testy dla `WindowsGameLocator`

**Deliverable:** Wszystkie unit tests passing

**Test:**
```bash
dotnet test SUSModder.Tests/SUSModder.Tests.csproj
# Powinno być ~20-30 testów, wszystkie GREEN
```

---

**Milestone M2 Checklist:**
- [ ] Wszystkie static classes zamienione na DI
- [ ] PathSettings → IPathProvider ✅
- [ ] GameLocator → IGameLocator ✅
- [ ] ConfigManager → ConfigService ✅
- [ ] ModManager → IArchiveExtractor ✅
- [ ] FileSystemUtilities → IPermissionManager ✅
- [ ] HardwareIdProvider → IHardwareInfoProvider ✅
- [ ] MainWindowViewModel używa DI ✅
- [ ] Unit tests passing ✅

---

## Faza 3: Platform Integration (Tydzień 6-7)

**Cel:** End-to-end testing - instalacja pierwszego moda na Linux

### Tydzień 6

#### Dzień 26-27: Steam Integration Testing

**Zadania:**
- [x] Test lokalizacji gry na Linux
- [x] Test instalacji vanilla Among Us
- [x] Test instalacji moda "Town of Us"
- [x] Test uruchamiania gry przez Steam URI
- [x] Weryfikacja że BepInEx działa na Proton

**Environment:** Ubuntu 24.04 z Steam zainstalowanym

**Test scenario:**
1. Uruchom SUSModder na Linux
2. Auto-detect Among Us installation
3. Zainstaluj mod "Town of Us"
4. Uruchom grę
5. Zweryfikuj że mod działa

**Deliverable:** Pierwszy mod zainstalowany i działający na Linux

---

#### Dzień 28-29: File Manager Integration

**Zadania:**
- [x] Test `IProcessManager.OpenFolder()` na GNOME (Nautilus)
- [x] Test `IProcessManager.OpenFolder()` na KDE (Dolphin)
- [x] Test `IProcessManager.OpenFolder()` na XFCE (Thunar)
- [x] Test `IProcessManager.OpenUrl()` (Firefox, Chrome, default browser)

**Deliverable:** Otwieranie folderów działa na wszystkich DE

---

#### Dzień 30: Desktop Shortcuts

**Zadania:**
- [x] Test tworzenia .desktop files
- [x] Weryfikacja uprawnień (chmod +x)
- [x] Test uruchamiania skrótu z pulpitu
- [x] Test ikon (jeśli dostępne)

**Deliverable:** Skróty pulpitu działają

---

### Tydzień 7

#### Dzień 31-32: Elevated Permissions Testing

**Zadania:**
- [x] Test usuwania modów bez uprawnień
- [x] Test usuwania modów z pkexec
- [x] Test przypadku gdy pkexec nie jest dostępny
- [x] Test fallback na sudo (w terminalu)

**Deliverable:** Usuwanie modów z elevated permissions działa

---

#### Dzień 33-34: Multi-mod Testing

**Zadania:**
- [x] Zainstaluj 5 różnych modów
- [x] Przełączaj między modami
- [x] Aktualizuj mody
- [x] Usuń mody
- [x] Test DLL mods

**Mody do testowania:**
- Town of Us
- Mira HQ
- The Other Roles
- Sheriff Mod
- Jester Mod (DLL)

**Deliverable:** Wszystkie operacje modów działają

---

#### Dzień 35: Bug Fixing

**Zadania:**
- [x] Przejrzyj wszystkie znalezione bugi
- [x] Priorytetyzuj (Critical, High, Medium, Low)
- [x] Napraw critical bugs
- [x] Napraw high priority bugs

**Deliverable:** Żadnych critical bugs, aplikacja stabilna

---

**Milestone M3 Checklist:**
- [ ] Mod instaluje się poprawnie na Linux ✅
- [ ] Gra uruchamia się przez Steam ✅
- [ ] Mody działają w grze (BepInEx + Proton) ✅
- [ ] Otwieranie folderów działa ✅
- [ ] Skróty pulpitu działają ✅
- [ ] Elevated permissions działają ✅
- [ ] Multi-mod operations działają ✅
- [ ] Critical bugs naprawione ✅

---

## Faza 4: Epic Games Support (Tydzień 8)

**Cel:** Wsparcie dla Epic Games Store (opcjonalnie - przez Heroic Launcher)

### Dzień 36-38: Heroic Launcher Integration

**Zadania:**
- [x] Implementuj `LinuxEpicGameManager.cs`
- [x] Wykrywanie Heroic Launcher installation
- [x] Parsing Heroic config files
- [x] Uruchamianie gry przez Heroic CLI
- [x] Test instalacji moda dla Epic version

**Deliverable:** Epic Games wsparcie działające (jeśli Heroic zainstalowany)

---

### Dzień 39-40: Fallback i Error Handling

**Zadania:**
- [x] Komunikat jeśli Heroic nie zainstalowany
- [x] Link do instalacji Heroic
- [x] Dokumentacja dla użytkowników Epic
- [x] Wyłącz Epic support jeśli nie wspierane

**Deliverable:** Graceful degradation jeśli Epic nie dostępny

---

**Milestone M4 Checklist (OPTIONAL):**
- [ ] Heroic Launcher detection ✅
- [ ] Epic games launching ✅
- [ ] Epic mods installing ✅
- [ ] User documentation for Epic ✅

---

## Faza 5: Testing & Bug Fixes (Tydzień 9-10)

**Cel:** Comprehensive testing na różnych dystrybucjach Linux

### Tydzień 9

#### Dzień 41-42: Ubuntu Testing

**Zadania:**
- [x] Setup VM Ubuntu 24.04 LTS
- [x] Install Steam
- [x] Install Among Us
- [x] Full test suite (wszystkie operacje modów)
- [x] Performance testing
- [x] Memory leak testing

**Deliverable:** Wszystkie testy passing na Ubuntu

---

#### Dzień 43-44: Fedora Testing

**Zadania:**
- [x] Setup VM Fedora 40
- [x] Install Steam (native RPM)
- [x] Test SUSModder
- [x] Bug fixes specyficzne dla Fedora

**Deliverable:** Działa na Fedora

---

#### Dzień 45: Arch Linux Testing

**Zadania:**
- [x] Setup VM Arch Linux
- [x] Install Steam (pacman)
- [x] Test SUSModder
- [x] Bug fixes specyficzne dla Arch

**Deliverable:** Działa na Arch

---

### Tydzień 10

#### Dzień 46-47: Regression Testing

**Zadania:**
- [x] Re-test wszystkich features na Windows (upewnij się że nic nie złamane)
- [x] Re-test wszystkich features na Ubuntu
- [x] Automated test suite
- [x] CI/CD setup (GitHub Actions)

**Deliverable:** Automated tests w CI/CD

---

#### Dzień 48-50: Bug Bash

**Zadania:**
- [x] Napraw wszystkie remaining bugs
- [x] Performance optimizations
- [x] Memory leaks fixes
- [x] UI polish

**Deliverable:** Zero known critical/high bugs

---

**Milestone M5 Checklist:**
- [ ] All tests passing on Ubuntu ✅
- [ ] All tests passing on Fedora ✅
- [ ] All tests passing on Arch ✅
- [ ] Regression tests passing on Windows ✅
- [ ] CI/CD configured ✅
- [ ] Zero critical bugs ✅

---

## Faza 6: Packaging & Release (Tydzień 11-12)

**Cel:** Utworzyć pakiety instalacyjne i przygotować release

### Tydzień 11

#### Dzień 51-52: .deb Package (Debian/Ubuntu)

**Zadania:**
- [x] Utworzyć debian/ directory structure
- [x] DEBIAN/control file
- [x] DEBIAN/postinst script
- [x] .desktop file
- [x] Icon files
- [x] Build .deb package

**Deliverable:** `susmodder_2.1.0_amd64.deb`

**Test:**
```bash
sudo dpkg -i susmodder_2.1.0_amd64.deb
susmodder  # Powinno uruchomić aplikację
```

---

#### Dzień 53: .rpm Package (Fedora/RHEL)

**Zadania:**
- [x] Utworzyć .spec file
- [x] Build RPM package
- [x] Test na Fedora

**Deliverable:** `susmodder-2.1.0-1.x86_64.rpm`

**Test:**
```bash
sudo dnf install susmodder-2.1.0-1.x86_64.rpm
susmodder
```

---

#### Dzień 54-55: AUR Package (Arch Linux)

**Zadania:**
- [x] Utworzyć PKGBUILD
- [x] Test lokalnie
- [x] Submit do AUR

**Deliverable:** `susmodder` w AUR

**Test:**
```bash
yay -S susmodder
susmodder
```

---

### Tydzień 12

#### Dzień 56: AppImage (Universal)

**Zadania:**
- [x] Skonfigurować linuxdeploy
- [x] Bundle wszystkich dependencies
- [x] Utworzyć AppImage
- [x] Test na różnych distro

**Deliverable:** `SUSModder-2.1.0-x86_64.AppImage`

**Test:**
```bash
chmod +x SUSModder-2.1.0-x86_64.AppImage
./SUSModder-2.1.0-x86_64.AppImage
```

---

#### Dzień 57: Flatpak (Optional)

**Zadania:**
- [x] Utworzyć flatpak manifest
- [x] Build flatpak
- [x] Submit do Flathub

**Deliverable:** `com.susmodder.SUSModder` na Flathub

---

#### Dzień 58-60: Documentation & Release

**Zadania:**
- [x] Napisać CHANGELOG dla 2.1.0
- [x] Zaktualizować README.md
- [x] Utworzyć Linux-specific documentation
- [x] Utworzyć release notes
- [x] Tag v2.1.0 w Git
- [x] Publish GitHub Release
- [x] Upload packages

**Deliverable:** v2.1.0 Release published

**Release artifacts:**
- `susmodder_2.1.0_amd64.deb`
- `susmodder-2.1.0-1.x86_64.rpm`
- `SUSModder-2.1.0-x86_64.AppImage`
- `susmodder-2.1.0-win-x64.zip` (Windows)
- `susmodder-2.1.0-linux-x64.tar.gz` (Linux portable)

---

**Milestone M6 Checklist:**
- [ ] .deb package created ✅
- [ ] .rpm package created ✅
- [ ] AUR package submitted ✅
- [ ] AppImage created ✅
- [ ] Flatpak submitted (optional) ✅
- [ ] Documentation updated ✅
- [ ] v2.1.0 released on GitHub ✅

---

## Post-Release

### Monitoring (Tydzień 13+)

**Zadania:**
- [x] Monitor bug reports
- [x] Community feedback
- [x] Quick hotfixes jeśli potrzebne
- [x] Plan for v2.1.1 patch release

### Future Enhancements

**Potencjalne features do v2.2.0:**
- macOS support
- Steam Deck optimizations
- Automatic mod updates
- Mod dependency management
- Cloud sync settings

---

## Risk Mitigation

### Jeśli timeline się nie zgadza:

**Plan B - MVP Release (6 tygodni):**

Scope reduction:
- ❌ Epic Games support (postpone to v2.2)
- ❌ Flatpak packaging (tylko .deb, .rpm, AppImage)
- ❌ macOS support
- ✅ Tylko Ubuntu/Debian jako primary target
- ✅ Podstawowa funkcjonalność (instalacja modów Steam)

**MVP Timeline:**
- Week 1-2: Platform Layer + DI
- Week 3-4: Core Refactor (tylko Steam)
- Week 5: Integration Testing (Ubuntu only)
- Week 6: Packaging (.deb, AppImage)

---

**Następny dokument:** [05-DEPENDENCIES.md](./05-DEPENDENCIES.md) - Zewnętrzne zależności i wymagania systemowe
