# Installation Map System - Mapa Zainstalowanych Modów

## 🎯 Cel Dokumentu

Analiza i projekt systemu "Installation Map" - trwałej mapy zainstalowanych modów w katalogach instalacji, niezależnej od `config.json`.

---

## ⚠️ Problem - Obecny Stan

### Jak Działa Obecnie

**Cały system opiera się wyłącznie na `config.json`:**

```
Lokalizacja: {ExeDirectory}/config.json

Struktura:
[
  {
    "Id": 1,
    "ModName": "Town of Us",
    "ModType": "full",
    "InstallPath": "C:/Mody/Town of Us",
    "ModVersion": "5.3.1",
    ...
  }
]
```

**Problem**: `config.json` jest w katalogu aplikacji, NIE w katalogu modów!

### Scenariusze Awarii

#### Scenariusz 1: Utrata config.json ❌

```
1. Użytkownik przypadkowo usuwa config.json
2. Aplikacja uruchamia się → config.json nie istnieje
3. ConfigManager.LoadConfig() pobiera nowy config z API
4. Nowy config zawiera tylko dostępne mody, ale bez InstallPath
5. ❌ REZULTAT: Aplikacja "nie widzi" zainstalowanych modów!
   - Użytkownik ma mody w C:/Mody/Town of Us
   - Ale config.json mówi że InstallPath = null
   - Aplikacja oferuje ponowną instalację (duplikacja!)
```

#### Scenariusz 2: Przeniesienie Aplikacji ❌

```
1. Użytkownik przenosi SUSModder.exe do innego folderu
2. config.json zostaje w starym folderze lub jest stworzony nowy
3. ❌ REZULTAT: Utrata informacji o zainstalowanych modach
```

#### Scenariusz 3: Reinstalacja Aplikacji ❌

```
1. Użytkownik usuwa aplikację
2. AppUpdateService może zachować config, ale nie zawsze
3. Po reinstalacji - brak informacji o zainstalowanych modach
4. ❌ REZULTAT: Trzeba "ręcznie" powiązać zainstalowane mody
```

#### Scenariusz 4: Rozsynchronizacja ❌

```
1. Użytkownik ręcznie usuwa folder moda: rm -rf "C:/Mody/Town of Us"
2. config.json nadal mówi: InstallPath = "C:/Mody/Town of Us"
3. ❌ REZULTAT: Aplikacja myśli że mod jest zainstalowany, ale go nie ma
   - Przycisk "Uruchom" nie działa
   - Przycisk "Odinstaluj" próbuje usunąć nieistniejący katalog
```

#### Scenariusz 5: Instalacja DLL w Nieznanych Lokalizacjach ❌

```
1. Użytkownik instaluje AleLuduMod (DLL) do Town of Us i The Other Roles
2. Tracona jest config.json
3. ❌ REZULTAT: System nie wie gdzie AleLuduMod jest zainstalowany
   - Aktualizacja DLL nie może znaleźć lokalizacji
   - DllModificationService.GetModsWithDllInstalled() zwraca puste
```

---

## 💡 Rozwiązanie - Installation Map System

### Koncepcja

**Każdy mod ma własny plik `.susmodder-install.json` w swoim katalogu instalacji:**

```
C:/Mody/
├── Town of Us/
│   ├── Among Us.exe
│   ├── BepInEx/
│   │   └── plugins/
│   │       ├── AleLuduMod.dll
│   │       └── AUnlocker.dll
│   └── .susmodder-install.json  ← NOWY PLIK
│
├── The Other Roles/
│   ├── Among Us.exe
│   ├── BepInEx/
│   │   └── plugins/
│   │       └── AleLuduMod.dll
│   └── .susmodder-install.json  ← NOWY PLIK
│
└── Vanilla/
    ├── Among Us.exe
    └── .susmodder-install.json  ← NOWY PLIK
```

### Struktura `.susmodder-install.json`

```json
{
  "version": "1.0",
  "installedAt": "2025-10-22T14:30:00Z",
  "installedBy": "SUSModder v1.2.0",
  "platform": "steam",

  "fullMod": {
    "modId": 1,
    "modName": "Town of Us",
    "modVersion": "5.3.1",
    "amongVersion": "2024.10.29",
    "installPath": "C:/Mody/Town of Us",
    "installedFrom": "https://github.com/townofus/v5.3.1.zip",
    "lastUpdated": "2025-10-22T14:30:00Z"
  },

  "installedDlls": [
    {
      "modId": 5,
      "modName": "AleLuduMod",
      "modVersion": "2.0.0",
      "installPath": "BepInEx/plugins/AleLuduMod.dll",
      "installedFrom": "https://github.com/aleludu/v2.0.0.dll",
      "installedAt": "2025-10-22T15:00:00Z",
      "lastUpdated": "2025-10-22T15:00:00Z"
    },
    {
      "modId": 8,
      "modName": "AUnlocker",
      "modVersion": "latest",
      "installPath": "BepInEx/plugins/AUnlocker.dll",
      "installedFrom": "https://github.com/aunlocker/latest.dll",
      "installedAt": "2025-10-20T10:00:00Z",
      "lastUpdated": "2025-10-20T10:00:00Z"
    }
  ],

  "metadata": {
    "notes": "Installed via version selection dialog",
    "customTags": []
  }
}
```

### Zalety Tego Podejścia

✅ **Trwałość**: Plik jest w katalogu moda - przetrwa utratę config.json
✅ **Import/Odkrycie**: Możliwość automatycznego odkrycia zainstalowanych modów
✅ **Śledzenie DLL**: Wiemy dokładnie które DLL są gdzie zainstalowane
✅ **Historia**: Zapisujemy kiedy, skąd i przez kogo mod został zainstalowany
✅ **Synchronizacja**: Łatwe sprawdzenie czy stan na dysku = stan w config.json
✅ **Backup**: Użytkownik może skopiować folder moda i przenieść na inny komputer

---

## 🏗️ Architektura - Nowy System

### Nowa Klasa: InstallationMapManager

**Lokalizacja**: `SUSModder.Core/Services/InstallationMapManager.cs`

**Odpowiedzialności**:
1. Tworzenie `.susmodder-install.json` przy instalacji
2. Aktualizacja przy każdej zmianie (instalacja DLL, aktualizacja, etc.)
3. Odczyt istniejących plików (import/odkrycie)
4. Synchronizacja z config.json
5. Migracja (jeśli katalog istnieje ale brak pliku)

### Nowy Model: InstallationMap

```csharp
public class InstallationMap
{
    public string Version { get; set; } = "1.0";
    public DateTime InstalledAt { get; set; }
    public string InstalledBy { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty; // "steam" lub "epic"

    public FullModInstallation FullMod { get; set; } = new();
    public List<DllModInstallation> InstalledDlls { get; set; } = new();
    public InstallationMetadata Metadata { get; set; } = new();
}

public class FullModInstallation
{
    public int ModId { get; set; }
    public string ModName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string AmongVersion { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string InstalledFrom { get; set; } = string.Empty; // URL
    public DateTime LastUpdated { get; set; }
}

public class DllModInstallation
{
    public int ModId { get; set; }
    public string ModName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty; // Relatywna: BepInEx/plugins/Mod.dll
    public string InstalledFrom { get; set; } = string.Empty; // URL
    public DateTime InstalledAt { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class InstallationMetadata
{
    public string Notes { get; set; } = string.Empty;
    public List<string> CustomTags { get; set; } = new();
}
```

---

## 🔄 Przepływ - Nowy vs Stary

### STARY PRZEPŁYW (obecnie):

```
InstallMod()
    ↓
1. Pobierz plik moda
    ↓
2. Rozpakuj do katalogu
    ↓
3. Zaktualizuj config.json:
   modConfig.InstallPath = "C:/Mody/TownOfUs"
    ↓
4. ConfigManager.SaveConfig(modConfigs)
    ↓
KONIEC
```

**Problem**: Jeśli config.json zniknie, tracisz informację o instalacji!

---

### NOWY PRZEPŁYW (z Installation Map):

```
InstallMod()
    ↓
1. Pobierz plik moda
    ↓
2. Rozpakuj do katalogu
    ↓
3. **NOWE**: InstallationMapManager.CreateInstallationMap()
   - Stwórz .susmodder-install.json w katalogu moda
   - Zapisz: modId, modVersion, installPath, timestamp, url
    ↓
4. Zaktualizuj config.json:
   modConfig.InstallPath = "C:/Mody/TownOfUs"
    ↓
5. ConfigManager.SaveConfig(modConfigs)
    ↓
KONIEC
```

**Rezultat**: Masz 2 źródła prawdy:
- config.json (w katalogu aplikacji)
- .susmodder-install.json (w katalogu moda) ← NOWE, TRWAŁE

---

## 📍 Miejsca Wymagające Modyfikacji

### 1. ModManager.InstallSteamAsync (Steam - FULL Mods)

**Plik**: `SUSModder.Core/GameIntegration/ModManager.cs`
**Metoda**: `InstallSteamAsync` (linia 57-300+)

**Gdzie dodać**:
```csharp
// Linia ~290 (po skopiowaniu plików)
// Zapisz konfigurację i posprzątaj temp
var existingConfig = modConfigs.FirstOrDefault(c => c.Id == modConfig.Id);
if (existingConfig != null)
{
    existingConfig.InstallPath = modFolderPath;
    existingConfig.LastUpdated = DateTime.Now;
    log.Write($"Zaktualizowano konfigurację dla istniejącego moda: {modConfig.ModName}");
}
else
{
    modConfig.InstallPath = modFolderPath;
    modConfigs.Add(modConfig);
    log.Write($"Dodano nową konfigurację dla moda: {modConfig.ModName}");
}

// ===== NOWY KOD =====
// Stwórz Installation Map
var installationMap = new InstallationMap
{
    InstalledAt = DateTime.Now,
    InstalledBy = $"SUSModder v{GetAppVersion()}",
    Platform = "steam",
    FullMod = new FullModInstallation
    {
        ModId = modConfig.Id,
        ModName = modConfig.ModName,
        ModVersion = modConfig.ModVersion,
        AmongVersion = modConfig.AmongVersion,
        InstallPath = modFolderPath,
        InstalledFrom = downloadUrl,
        LastUpdated = DateTime.Now
    },
    InstalledDlls = new List<DllModInstallation>()
};

await InstallationMapManager.SaveInstallationMapAsync(modFolderPath, installationMap);
log.Write($"Zapisano Installation Map w: {modFolderPath}");
// ===== KONIEC NOWEGO KODU =====

ConfigManager.SaveConfig(modConfigs);
```

---

### 2. EpicVersionManager (Epic - FULL Mods)

**Plik**: `SUSModder.Core/GameIntegration/EpicVersionManager.cs`

**Problem**: Epic ma inny proces instalacji (legendary.exe, manifesty)

**Gdzie dodać**:
Po zakończeniu `legendary.exe install` i weryfikacji instalacji:

```csharp
// Po pomyślnej instalacji
var installPath = GetEpicInstallPath(modConfig);

// ===== NOWY KOD =====
var installationMap = new InstallationMap
{
    InstalledAt = DateTime.Now,
    InstalledBy = $"SUSModder v{GetAppVersion()}",
    Platform = "epic",
    FullMod = new FullModInstallation
    {
        ModId = modConfig.Id,
        ModName = modConfig.ModName,
        ModVersion = modConfig.ModVersion,
        AmongVersion = modConfig.AmongVersion,
        InstallPath = installPath,
        InstalledFrom = manifestUrl,
        LastUpdated = DateTime.Now
    },
    InstalledDlls = new List<DllModInstallation>()
};

await InstallationMapManager.SaveInstallationMapAsync(installPath, installationMap);
// ===== KONIEC NOWEGO KODU =====
```

---

### 3. DllModificationService.InstallDllToModAsync (Instalacja DLL)

**Plik**: `SUSModder.Core/Services/DllModificationService.cs`
**Metoda**: `InstallDllToModAsync` (linia 102-171)

**Gdzie dodać**:
```csharp
// Linia ~162 (po zapisaniu pliku DLL)
var content = await response.Content.ReadAsByteArrayAsync();
await File.WriteAllBytesAsync(targetPath, content);

_diagnosticsOutput.Write($"DLL installation completed successfully");

// ===== NOWY KOD =====
// Zaktualizuj Installation Map moda FULL
var installationMap = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);

if (installationMap != null)
{
    // Sprawdź czy DLL już istnieje w mapie
    var existingDll = installationMap.InstalledDlls
        .FirstOrDefault(d => d.ModId == dllMod.Id);

    if (existingDll != null)
    {
        // Aktualizuj istniejący wpis
        existingDll.ModVersion = dllMod.ModVersion;
        existingDll.LastUpdated = DateTime.Now;
        existingDll.InstalledFrom = downloadUrl;
    }
    else
    {
        // Dodaj nowy wpis
        installationMap.InstalledDlls.Add(new DllModInstallation
        {
            ModId = dllMod.Id,
            ModName = dllMod.ModName,
            ModVersion = dllMod.ModVersion,
            InstallPath = Path.Combine(dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName),
            InstalledFrom = downloadUrl,
            InstalledAt = DateTime.Now,
            LastUpdated = DateTime.Now
        });
    }

    await InstallationMapManager.SaveInstallationMapAsync(targetMod.InstallPath, installationMap);
    _diagnosticsOutput.Write($"Zaktualizowano Installation Map dla {targetMod.ModName}");
}
else
{
    _diagnosticsOutput.Write($"[WARNING] Brak Installation Map dla {targetMod.ModName} - pominięto aktualizację");
}
// ===== KONIEC NOWEGO KODU =====

return targetPath;
```

---

### 4. DllModificationService.UninstallDllFromModAsync (Deinstalacja DLL)

**Plik**: `SUSModder.Core/Services/DllModificationService.cs`
**Metoda**: `UninstallDllFromModAsync` (linia 173-219)

**Gdzie dodać**:
```csharp
// Linia ~210 (po usunięciu pliku)
File.Delete(filePath);
_diagnosticsOutput.Write($"DLL uninstallation completed successfully");

// ===== NOWY KOD =====
// Zaktualizuj Installation Map
var installationMap = await InstallationMapManager.LoadInstallationMapAsync(targetMod.InstallPath);

if (installationMap != null)
{
    var dllEntry = installationMap.InstalledDlls
        .FirstOrDefault(d => d.ModId == dllMod.Id);

    if (dllEntry != null)
    {
        installationMap.InstalledDlls.Remove(dllEntry);
        await InstallationMapManager.SaveInstallationMapAsync(targetMod.InstallPath, installationMap);
        _diagnosticsOutput.Write($"Usunięto {dllMod.ModName} z Installation Map");
    }
}
// ===== KONIEC NOWEGO KODU =====

return true;
```

---

### 5. ModDelete.DeleteFullMod (Deinstalacja FULL)

**Plik**: `SUSModder.Core/GameIntegration/ModDelete.cs`
**Metoda**: `DeleteFullMod` (linia 24-40)

**Zmiana**:
```csharp
private static void DeleteFullMod(ModConfiguration modConfig, List<ModConfiguration> modConfigs, IUserInteraction userInteraction)
{
    try
    {
        if (Directory.Exists(modConfig.InstallPath))
        {
            // ===== NOWY KOD - usunięcie Installation Map jest automatyczne =====
            // .susmodder-install.json zostanie usunięty razem z katalogiem
            // Opcjonalnie: można najpierw odczytać i zalogować co było zainstalowane

            Directory.Delete(modConfig.InstallPath, true);
            modConfig.InstallPath = string.Empty;
            ConfigManager.SaveConfig(modConfigs);
            userInteraction.ShowInfo($"Mod '{modConfig.ModName}' został pomyślnie usunięty.", "Sukces");
        }
    }
    catch (Exception ex)
    {
        userInteraction.ShowError($"Wystąpił błąd podczas usuwania: {ex.Message}", "Błąd");
    }
}
```

---

### 6. ModUpdates.UpdateModAsync (Aktualizacja FULL)

**Plik**: `SUSModder.Core/GameIntegration/ModUpdate.cs`
**Metoda**: `UpdateModAsync` (linia 13-69)

**Analiza**: Aktualizacja FULL = usunięcie + ponowna instalacja
- `ModDelete.DeleteMod()` usuwa katalog (w tym .susmodder-install.json)
- `ModManager.ModifyAsync()` instaluje od nowa (tworzy nowy .susmodder-install.json)

**Wniosek**: **Nie wymaga zmian** - działa automatycznie!

---

### 7. DllUpdateManager.UpdateDllInLocationsAsync (Aktualizacja DLL - NOWY)

**Plik**: `SUSModder.Core/Services/DllUpdateManager.cs` (z Fazy 3)

**Gdzie dodać**:
W pętli aktualizacji:

```csharp
foreach (var fullMod in updateInfo.SelectedLocations)
{
    try
    {
        _log.Write($"[DllUpdate] Aktualizowanie {updateInfo.DllMod.ModName} w {fullMod.ModName}");

        var installedPath = await _dllModService.InstallDllToModAsync(
            updateInfo.DllMod,
            fullMod,
            platform
        );

        // ===== InstallDllToModAsync już aktualizuje Installation Map =====
        // Nie trzeba dodawać kodu tutaj!

        if (!string.IsNullOrEmpty(installedPath))
        {
            result.SuccessfulUpdates++;
            result.UpdatedLocations.Add(fullMod.ModName);
        }
        ...
    }
}
```

**Wniosek**: **Nie wymaga zmian** - `InstallDllToModAsync` już to robi!

---

## 🔍 InstallationMapManager - Implementacja

### Pełna Implementacja

**Plik**: `SUSModder.Core/Services/InstallationMapManager.cs`

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SUSModder.Core.Models;
using SUSModder.Core.Diagnostics;

namespace SUSModder.Core.Services
{
    public static class InstallationMapManager
    {
        private const string MapFileName = ".susmodder-install.json";

        /// <summary>
        /// Zapisz Installation Map w katalogu moda
        /// </summary>
        public static async Task SaveInstallationMapAsync(
            string modInstallPath,
            InstallationMap map)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                throw new ArgumentNullException(nameof(modInstallPath));

            if (!Directory.Exists(modInstallPath))
                throw new DirectoryNotFoundException($"Katalog nie istnieje: {modInstallPath}");

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);

            var json = JsonSerializer.Serialize(map, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(mapFilePath, json);
        }

        /// <summary>
        /// Wczytaj Installation Map z katalogu moda
        /// </summary>
        public static async Task<InstallationMap?> LoadInstallationMapAsync(
            string modInstallPath)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                return null;

            if (!Directory.Exists(modInstallPath))
                return null;

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);

            if (!File.Exists(mapFilePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(mapFilePath);
                var map = JsonSerializer.Deserialize<InstallationMap>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return map;
            }
            catch (Exception)
            {
                // Jeśli plik jest uszkodzony, zwróć null
                return null;
            }
        }

        /// <summary>
        /// Sprawdź czy Installation Map istnieje
        /// </summary>
        public static bool InstallationMapExists(string modInstallPath)
        {
            if (string.IsNullOrEmpty(modInstallPath))
                return false;

            string mapFilePath = Path.Combine(modInstallPath, MapFileName);
            return File.Exists(mapFilePath);
        }

        /// <summary>
        /// Odkryj wszystkie zainstalowane mody skanując katalogi
        /// </summary>
        public static async Task<List<InstallationMap>> DiscoverInstalledModsAsync(
            string modsBasePath,
            IDiagnosticsOutput log)
        {
            var discoveredMods = new List<InstallationMap>();

            if (!Directory.Exists(modsBasePath))
                return discoveredMods;

            log.Write($"[InstallationMapManager] Skanowanie katalogu: {modsBasePath}");

            // Przeszukaj wszystkie podkatalogi
            var directories = Directory.GetDirectories(modsBasePath);

            foreach (var dir in directories)
            {
                var map = await LoadInstallationMapAsync(dir);

                if (map != null)
                {
                    log.Write($"[Odkryto] {map.FullMod.ModName} v{map.FullMod.ModVersion} w {dir}");
                    discoveredMods.Add(map);
                }
            }

            log.Write($"[InstallationMapManager] Znaleziono {discoveredMods.Count} modów z Installation Map");
            return discoveredMods;
        }

        /// <summary>
        /// Zaimportuj odkryte mody do config.json
        /// </summary>
        public static List<ModConfiguration> ImportDiscoveredMods(
            List<InstallationMap> discoveredMaps,
            List<ModConfiguration> existingConfigs,
            IDiagnosticsOutput log)
        {
            var imported = new List<ModConfiguration>();

            foreach (var map in discoveredMaps)
            {
                // Sprawdź czy mod już istnieje w config
                var existing = existingConfigs.FirstOrDefault(c => c.Id == map.FullMod.ModId);

                if (existing != null)
                {
                    // Aktualizuj InstallPath jeśli jest inny
                    if (existing.InstallPath != map.FullMod.InstallPath)
                    {
                        log.Write($"[Import] Aktualizuję InstallPath dla {existing.ModName}");
                        existing.InstallPath = map.FullMod.InstallPath;
                        existing.ModVersion = map.FullMod.ModVersion;
                        existing.LastUpdated = map.FullMod.LastUpdated;
                        imported.Add(existing);
                    }
                }
                else
                {
                    // Dodaj nowy mod do config
                    var newConfig = new ModConfiguration
                    {
                        Id = map.FullMod.ModId,
                        ModName = map.FullMod.ModName,
                        ModType = "full",
                        ModVersion = map.FullMod.ModVersion,
                        AmongVersion = map.FullMod.AmongVersion,
                        InstallPath = map.FullMod.InstallPath,
                        LastUpdated = map.FullMod.LastUpdated,
                        GitHubRepoOrLink = map.FullMod.InstalledFrom
                    };

                    log.Write($"[Import] Dodaję nowy mod: {newConfig.ModName}");
                    existingConfigs.Add(newConfig);
                    imported.Add(newConfig);
                }
            }

            return imported;
        }

        /// <summary>
        /// Migruj istniejące instalacje (stwórz Installation Map dla modów bez niego)
        /// </summary>
        public static async Task MigrateExistingInstallationsAsync(
            List<ModConfiguration> modConfigs,
            string platform,
            IDiagnosticsOutput log)
        {
            log.Write("[InstallationMapManager] Rozpoczynam migrację istniejących instalacji...");

            int migrated = 0;

            foreach (var modConfig in modConfigs)
            {
                // Tylko dla FULL modów z InstallPath
                if (modConfig.ModType != "full" || string.IsNullOrEmpty(modConfig.InstallPath))
                    continue;

                // Sprawdź czy katalog istnieje
                if (!Directory.Exists(modConfig.InstallPath))
                    continue;

                // Sprawdź czy Installation Map już istnieje
                if (InstallationMapExists(modConfig.InstallPath))
                {
                    log.Write($"[Migracja] {modConfig.ModName} - już ma Installation Map");
                    continue;
                }

                // Stwórz Installation Map
                var installationMap = new InstallationMap
                {
                    InstalledAt = modConfig.LastUpdated ?? DateTime.Now,
                    InstalledBy = "SUSModder (migrated)",
                    Platform = platform,
                    FullMod = new FullModInstallation
                    {
                        ModId = modConfig.Id,
                        ModName = modConfig.ModName,
                        ModVersion = modConfig.ModVersion ?? "unknown",
                        AmongVersion = modConfig.AmongVersion ?? "unknown",
                        InstallPath = modConfig.InstallPath,
                        InstalledFrom = modConfig.GitHubRepoOrLink ?? "unknown",
                        LastUpdated = modConfig.LastUpdated ?? DateTime.Now
                    },
                    InstalledDlls = new List<DllModInstallation>(),
                    Metadata = new InstallationMetadata
                    {
                        Notes = "Migrated from existing installation"
                    }
                };

                try
                {
                    await SaveInstallationMapAsync(modConfig.InstallPath, installationMap);
                    log.Write($"[Migracja] ✓ {modConfig.ModName}");
                    migrated++;
                }
                catch (Exception ex)
                {
                    log.Write($"[Migracja] ✗ {modConfig.ModName}: {ex.Message}");
                }
            }

            log.Write($"[InstallationMapManager] Migracja zakończona: {migrated} modów");
        }
    }
}
```

---

## 🚀 Feature: Import/Odkrycie Modów

### User Story

> Jako użytkownik, tracę config.json. Uruchamiam aplikację i klikam "Odkryj zainstalowane mody".
> Aplikacja skanuje katalog modów, znajduje wszystkie `.susmodder-install.json` i automatycznie
> importuje mody do config.json. Widzę listę odkrytych modów i mogę wybrać które zaimportować.

### Implementacja

#### 1. Przycisk w UI

```xml
<!-- MainWindow.axaml -->
<Button Content="🔍 Odkryj zainstalowane mody"
        Command="{Binding DiscoverInstalledModsCommand}"/>
```

#### 2. ViewModel

```csharp
// MainWindowViewModel.cs
public ReactiveCommand<Unit, Unit> DiscoverInstalledModsCommand { get; }

public MainWindowViewModel(...)
{
    DiscoverInstalledModsCommand = ReactiveCommand.CreateFromTask(async () =>
    {
        await DiscoverAndImportModsAsync();
    });
}

private async Task DiscoverAndImportModsAsync()
{
    try
    {
        _log.Write("[DiscoverMods] Rozpoczynam odkrywanie modów...");

        // Skanuj katalog modów
        var discoveredMaps = await InstallationMapManager.DiscoverInstalledModsAsync(
            PathSettings.ModsInstallPath,
            _log
        );

        if (!discoveredMaps.Any())
        {
            await ShowInfoAsync("Nie znaleziono zainstalowanych modów z Installation Map");
            return;
        }

        // Pokaż dialog z odkrytymi modami
        var dialog = new DiscoveredModsDialog
        {
            DataContext = new DiscoveredModsDialogViewModel(discoveredMaps)
        };

        var selectedMaps = await dialog.ShowDialog<List<InstallationMap>?>(GetWindow());

        if (selectedMaps == null || !selectedMaps.Any())
            return;

        // Zaimportuj wybrane mody
        var imported = InstallationMapManager.ImportDiscoveredMods(
            selectedMaps,
            ModConfigs.ToList(),
            _log
        );

        // Zapisz zaktualizowany config
        ConfigManager.SaveConfig(ModConfigs.ToList());

        // Odśwież UI
        RefreshModsList();

        await ShowInfoAsync($"Zaimportowano {imported.Count} modów");
    }
    catch (Exception ex)
    {
        _log.Write($"[ERROR] Błąd odkrywania modów: {ex.Message}");
        await ShowErrorAsync($"Nie udało się odkryć modów:\n{ex.Message}");
    }
}
```

---

## 📋 Plan Implementacji - Stopniowy

### Faza 0: Modele (1h)

1. **Utwórz modele** (0.5h)
   - `InstallationMap.cs`
   - `FullModInstallation.cs`
   - `DllModInstallation.cs`
   - `InstallationMetadata.cs`

2. **Utwórz InstallationMapManager** (0.5h)
   - `SaveInstallationMapAsync()`
   - `LoadInstallationMapAsync()`
   - `InstallationMapExists()`

### Faza 1: Instalacja Modów FULL (2h)

1. **ModManager.InstallSteamAsync** (1h)
   - Dodaj tworzenie Installation Map po instalacji
   - Test: Zainstaluj mod → sprawdź czy .susmodder-install.json istnieje

2. **EpicVersionManager** (1h)
   - Dodaj tworzenie Installation Map po instalacji Epic
   - Test: Zainstaluj mod Epic → sprawdź plik

### Faza 2: Instalacja/Deinstalacja DLL (2h)

1. **DllModificationService.InstallDllToModAsync** (1h)
   - Aktualizuj Installation Map przy instalacji DLL

2. **DllModificationService.UninstallDllFromModAsync** (1h)
   - Aktualizuj Installation Map przy deinstalacji DLL

### Faza 3: Import/Odkrycie (3h)

1. **InstallationMapManager - funkcje odkrywania** (1h)
   - `DiscoverInstalledModsAsync()`
   - `ImportDiscoveredMods()`

2. **UI - DiscoveredModsDialog** (1h)
   - Dialog pokazujący odkryte mody
   - Możliwość wyboru które zaimportować

3. **MainWindowViewModel - integracja** (1h)
   - Dodać przycisk "Odkryj mody"
   - Command i logika

### Faza 4: Migracja Istniejących (1h)

1. **InstallationMapManager.MigrateExistingInstallationsAsync** (0.5h)
   - Stwórz Installation Map dla modów które już istnieją

2. **Wywołaj przy starcie aplikacji** (0.5h)
   - Jednorazowa migracja przy pierwszym uruchomieniu nowej wersji

### Faza 5: Testy i QA (2h)

1. **Testy jednostkowe** (1h)
2. **Testy E2E** (1h)
   - Zainstaluj mod → sprawdź map
   - Usuń config.json → odkryj mody → zweryfikuj import
   - Zainstaluj DLL → sprawdź czy dodany do map

---

## ✅ Checklist Implementacji

### Modele
- [ ] `InstallationMap.cs`
- [ ] `FullModInstallation.cs`
- [ ] `DllModInstallation.cs`
- [ ] `InstallationMetadata.cs`

### InstallationMapManager
- [ ] `SaveInstallationMapAsync()`
- [ ] `LoadInstallationMapAsync()`
- [ ] `InstallationMapExists()`
- [ ] `DiscoverInstalledModsAsync()`
- [ ] `ImportDiscoveredMods()`
- [ ] `MigrateExistingInstallationsAsync()`

### Integracje
- [ ] ModManager.InstallSteamAsync
- [ ] EpicVersionManager (instalacja Epic)
- [ ] DllModificationService.InstallDllToModAsync
- [ ] DllModificationService.UninstallDllFromModAsync
- [ ] ModDelete.DeleteFullMod (opcjonalne logowanie)

### UI
- [ ] DiscoveredModsDialog.axaml
- [ ] DiscoveredModsDialogViewModel.cs
- [ ] MainWindowViewModel - "Odkryj mody" button
- [ ] Migracja przy pierwszym starcie (hook)

### Testy
- [ ] Unit testy dla InstallationMapManager
- [ ] E2E test instalacji z mapą
- [ ] E2E test odkrywania modów
- [ ] E2E test migracji

---

## 🎯 Podsumowanie

### Problem
config.json jest w katalogu aplikacji i może być utracony/uszkodzony, prowadząc do utraty informacji o zainstalowanych modach.

### Rozwiązanie
Każdy mod ma `.susmodder-install.json` w swoim katalogu z pełną informacją o instalacji (FULL mod + DLL mody).

### Korzyści
✅ Trwałość - przetrwa utratę config.json
✅ Import - automatyczne odkrywanie zainstalowanych modów
✅ Śledzenie DLL - dokładna informacja gdzie są zainstalowane
✅ Historia - kiedy, skąd, przez kogo
✅ Synchronizacja - łatwe sprawdzenie stanu

### Nakład Pracy
**Szacowany czas**: 11 godzin (1.5 dnia roboczego)

**Priorytet**: 🔴 **Wysoki** - krytyczne dla stabilności systemu

### Zalecenie
**Zaimplementować PRZED** systemem wersjonowania i kompatybilności, ponieważ te systemy będą polegać na prawidłowym śledzeniu instalacji.

---

**Ostatnia aktualizacja:** 2025-10-22
**Wersja:** 1.0
**Status:** 📋 Projekt - Gotowy do implementacji
