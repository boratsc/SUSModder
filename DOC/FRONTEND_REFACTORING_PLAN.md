# Plan Refaktoringu MainWindowViewModel

**Data:** 2025-10-20
**Branch:** feature-1.2.0
**Cel:** Zmniejszenie MainWindowViewModel.cs z 2799 linii do <1000 linii

---

## 📊 Aktualna Sytuacja

### Statystyki pliku:
- **Aktualna wielkość:** 2799 linii
- **Cel:** <1000 linii
- **Redukcja wymagana:** ~65% (1800+ linii)
- **Istniejący partial:** `MainWindowViewModel.Helpers.cs` (138 linii)

### Obecna struktura:
```
MainWindowViewModel.cs (2799 linii)
├─ Properties & Fields (150 linii)
├─ Constructor (100 linii)
├─ Commands & Event Handlers (200 linii)
├─ Initialization (200 linii)
├─ Mod Operations (Install/Update/Uninstall) (800 linii)
├─ Launch Logic (400 linii)
├─ Dialog Helpers (150 linii)
├─ Update Management (500 linii)
├─ DLL Management (300 linii)
├─ File Deletion Logic (400 linii)
├─ SUStats Integration (200 linii)
└─ Utility Methods (500 linii)

MainWindowViewModel.Helpers.cs (138 linii)
├─ DeterminePlatform() (40 linii)
├─ RefreshModsSortingKeepSelection() (20 linii)
├─ RefreshModsListAsync() (45 linii)
└─ DebugDiagnosticsOutput class (30 linii)
```

---

## 🎯 Strategia Refaktoringu

### Poziom 1: Wydzielenie Logiki Biznesowej do Core (Priorytет Wysoki)
**Oszczędność:** ~600 linii

Następujące fragmenty zawierają logikę biznesową, która POWINNA być w `SUSModder.Core`:

#### 1.1. File Deletion Logic → Core.Utilities
**Linie:** 2056-2347 (~300 linii)
**Metody do przeniesienia:**
- `SafeDeleteDirectoryAsync()`
- `ForceDeleteDirectoryAsync()`
- `RemoveReadOnlyAttributes()`
- `KillProcessesUsingDirectory()`
- `TryDeleteWithElevatedPermissionsAsync()`
- `DeleteWithElevatedPermissionsWindows()`
- `DeleteWithCmdElevated()`

**Cel:** `SUSModder.Core/Utilities/FileSystemUtilities.cs`
```csharp
namespace SUSModder.Core.Utilities
{
    public static class FileSystemUtilities
    {
        public static Task<bool> SafeDeleteDirectoryAsync(string path, IDiagnosticsOutput? diagnostics = null);
        public static Task<bool> ForceDeleteDirectoryAsync(string path);
        // ... etc
    }
}
```

#### 1.2. Epic Error Handling → Core.GameIntegration
**Linie:** 1522-1540 (~20 linii)
**Metoda:** `ShowEpicErrorDialogAsync()` - logika parsowania i formatowania błędów powinna być w Core

**Cel:** `EpicVersionManager` - dodaj metodę `FormatErrorMessage(string logContent)`

#### 1.3. SUStats Logic → Core (już istnieje, ale nie używane!)
**Linie:** 2571-2796 (~225 linii)
**Metody:**
- `CreateApiSetFileIfNeeded()` - DUPLIKAT z `ApiSetManager`
- `RemoveApiSetFileIfExists()` - DUPLIKAT z `ApiSetManager`
- `HandleSUStatsChoice()` - UI logic (zostaje)
- `ClearSUStatsSelection()` - wrapper

**Akcja:** Usuń duplikaty, używaj bezpośrednio `ApiSetManager` z Core

---

### Poziom 2: Partial Classes dla Operacji Modów (Priorytet Wysoki)
**Oszczędność:** ~1100 linii

Stwórz dedykowane partial classes dla każdej głównej operacji:

#### 2.1. `MainWindowViewModel.ModOperations.cs`
**Linie z głównego pliku:** 1553-1742 + 1744-1937 (~600 linii)
**Metody:**
- `Install()` - instalacja modów
- `Update()` - aktualizacja modów
- `Uninstall()` - odinstalowanie modów
- Helper methods dla progress reporting

#### 2.2. `MainWindowViewModel.GameLaunch.cs`
**Linie:** 1322-1520 (~400 linii)
**Metody:**
- `Launch()` - główna logika uruchamiania
- `LaunchAsync()` - async wrapper
- Steam launch logic
- Epic launch logic + progress handling

#### 2.3. `MainWindowViewModel.Updates.cs`
**Linie:** 698-1005 (~300 linii)
**Metody:**
- `CheckForModUpdatesAsync()`
- `ShowUpdateDialogAsync()`
- `ProcessSelectedUpdatesWithProgressAsync()`
- `GetOrCreateModItemAsync()`
- `UpdateSingleModWithProgressAsync()`

#### 2.4. `MainWindowViewModel.DllManagement.cs`
**Linie:** 2414-2569 (~300 linii)
**Metody:**
- `ShowDllModifications()`
- `SelectDllMod()`
- `CloseDllDialog()`
- `LoadDllMods()`
- `LoadAvailableFullMods()`
- `InstallDllToMod()`
- `UninstallDllFromMod()`

---

### Poziom 3: Partial Classes dla UI & Dialogs (Priorytet Średni)
**Oszczędność:** ~400 linii

#### 3.1. `MainWindowViewModel.Dialogs.cs`
**Linie:** 641-694, 1254-1315, 1290-1308, 1008-1028 (~200 linii)
**Metody:**
- `ShowMessageAsync()`
- `ShowErrorDialogAsync()`
- `ShowConfirmDialogAsync()`
- `ShowPromptDialogAsync()`
- `ShowSelectFileDialogAsync()`
- `ShowDetailedErrorDialogAsync()`
- `ShowEpicErrorDialogAsync()` (może zostać jako UI wrapper)

#### 3.2. `MainWindowViewModel.Initialization.cs`
**Linie:** 351-431, 570-639 (~200 linii)
**Metody:**
- `InitializeApplicationAsync()`
- `SetupVanillaGameAsync()`
- `CheckForAppUpdatesOnStartup()`
- `LoadAppVersion()`
- `LoadWindowTitle()`
- `LoadSavedTheme()`
- `ClearEpicLogsOnStartup()`

---

### Poziom 4: Partial Classes dla UI State & Settings (Priorytet Średni)
**Oszczędność:** ~300 linii

#### 4.1. `MainWindowViewModel.ThemeManagement.cs`
**Linie:** 1089-1185 (~100 linii)
**Metody:**
- `LoadSavedTheme()`
- `ToggleTheme()`
- `ApplyTheme()`
- Theme-related properties

#### 4.2. `MainWindowViewModel.AppSettings.cs`
**Linie:** 2350-2412, 1209-1237 (~100 linii)
**Metody:**
- `ShowAppSettings()`
- `OnSettingsSaved()`
- `ShowAdditionalActions()`
- `ShowInfo()`

#### 4.3. `MainWindowViewModel.ExternalActions.cs`
**Linie:** 288-350, 432-494, 1239-1288 (~200 linii)
**Metody:**
- `OpenDonationPage()`
- `ShowRecommendedDiscords()`
- `ShowSUStatsConfig()`
- `ShowLobbySetDialog()`
- `ExecuteFixBlackScreenAsync()`
- `OpenFolder()`
- `CreateShortcut()`
- `CreateWindowsShortcut()`
- `CreateShortcutWithPowerShell()`
- `ShowRoles()`

---

### Poziom 5: Wydzielenie Serwisów (Priorytet Niski - Future)
**Oszczędność:** 0 linii teraz, ale lepsza architektura

Rozważyć stworzenie dedykowanych serwisów:

#### 5.1. `ModInstallationService` (Core lub UI.Services)
- Enkapsuluj całą logikę Install/Update/Uninstall
- Progress reporting
- Platform detection
- Epic/Steam handling

#### 5.2. `GameLaunchService` (Core.GameIntegration)
- Steam launch logic
- Epic launch logic
- Platform detection
- ApiSet creation

#### 5.3. `ThemeService` (UI.Services)
- Theme loading/saving
- Theme switching
- Resource dictionary management

---

## 📋 Plan Implementacji

### Faza 1: Cleanup & Duplikaty (1-2h)
**Priorytet:** Krytyczny
**Oszczędność:** ~250 linii

- [ ] Usuń duplikaty SUStats logic (używaj `ApiSetManager` bezpośrednio)
- [ ] Przenieś file deletion logic do Core
- [ ] Wyczyść komentarze debug i martwy kod

### Faza 2: Partial Classes - Mod Operations (2-3h)
**Priorytet:** Wysoki
**Oszczędność:** ~1100 linii

- [ ] Stwórz `MainWindowViewModel.ModOperations.cs`
- [ ] Stwórz `MainWindowViewModel.GameLaunch.cs`
- [ ] Stwórz `MainWindowViewModel.Updates.cs`
- [ ] Stwórz `MainWindowViewModel.DllManagement.cs`

### Faza 3: Partial Classes - UI & Helpers (1-2h)
**Priorytet:** Średni
**Oszczędność:** ~400 linii

- [ ] Stwórz `MainWindowViewModel.Dialogs.cs`
- [ ] Stwórz `MainWindowViewModel.Initialization.cs`
- [ ] Rozszerz istniejący `MainWindowViewModel.Helpers.cs`

### Faza 4: Partial Classes - Settings & State (1h)
**Priorytet:** Średni
**Oszczędność:** ~300 linii

- [ ] Stwórz `MainWindowViewModel.ThemeManagement.cs`
- [ ] Stwórz `MainWindowViewModel.AppSettings.cs`
- [ ] Stwórz `MainWindowViewModel.ExternalActions.cs`

### Faza 5: Weryfikacja & Testy (1h)
**Priorytet:** Krytyczny

- [ ] Build solution - weryfikacja kompilacji
- [ ] Testy manualne wszystkich głównych funkcji
- [ ] Sprawdzenie wydajności
- [ ] Code review

---

## 🎯 Przewidywany Rezultat

### Po Fazie 1 (Cleanup):
```
MainWindowViewModel.cs: ~2550 linii (-250)
```

### Po Fazie 2 (Mod Operations Partials):
```
MainWindowViewModel.cs: ~1450 linii (-1100)
MainWindowViewModel.ModOperations.cs: ~600 linii [NEW]
MainWindowViewModel.GameLaunch.cs: ~400 linii [NEW]
MainWindowViewModel.Updates.cs: ~300 linii [NEW]
MainWindowViewModel.DllManagement.cs: ~300 linii [NEW]
```

### Po Fazie 3 (UI & Helpers):
```
MainWindowViewModel.cs: ~1050 linii (-400)
MainWindowViewModel.Dialogs.cs: ~200 linii [NEW]
MainWindowViewModel.Initialization.cs: ~200 linii [NEW]
```

### Po Fazie 4 (Settings & State):
```
MainWindowViewModel.cs: ~750 linii (-300) ✅ CEL OSIĄGNIĘTY!
MainWindowViewModel.ThemeManagement.cs: ~100 linii [NEW]
MainWindowViewModel.AppSettings.cs: ~100 linii [NEW]
MainWindowViewModel.ExternalActions.cs: ~200 linii [NEW]
```

### Finalna struktura:
```
ViewModels/
├─ MainWindowViewModel.cs (~750 linii) - Core properties, constructor, simple commands
├─ MainWindowViewModel.Helpers.cs (138 linii) - Platform detection, refresh logic
├─ MainWindowViewModel.ModOperations.cs (~600 linii) - Install, Update, Uninstall
├─ MainWindowViewModel.GameLaunch.cs (~400 linii) - Launch logic
├─ MainWindowViewModel.Updates.cs (~300 linii) - Update checking & processing
├─ MainWindowViewModel.DllManagement.cs (~300 linii) - DLL mod management
├─ MainWindowViewModel.Dialogs.cs (~200 linii) - All dialog methods
├─ MainWindowViewModel.Initialization.cs (~200 linii) - App initialization
├─ MainWindowViewModel.ThemeManagement.cs (~100 linii) - Theme switching
├─ MainWindowViewModel.AppSettings.cs (~100 linii) - Settings management
└─ MainWindowViewModel.ExternalActions.cs (~200 linii) - External actions

Core/Utilities/
└─ FileSystemUtilities.cs (~300 linii) [NEW] - Safe file deletion logic
```

**Całkowita liczba linii:** ~3288 (było 2937)
**Główny plik:** ~750 linii ✅ **CEL: <1000 linii OSIĄGNIĘTY**
**Średnia wielkość partial class:** ~250 linii

---

## ⚠️ Ryzyka i Uwagi

### Ryzyka:
1. **Breaking changes** - upewnij się że wszystkie partial classes mają dostęp do private members
2. **Circular dependencies** - uważaj na wzajemne zależności między partial classes
3. **Testy** - projekt nie ma unit testów, więc wymagane obszerne testy manualne

### Dobre praktyki:
1. Każdy partial class powinien mieć jasny, pojedynczy zakres odpowiedzialności
2. Używaj XML comments dla każdego partial class opisującego jego rolę
3. Zachowaj spójne nazewnictwo: `MainWindowViewModel.{Feature}.cs`
4. Prywatne metody powinny być w tym samym partial class co publiczne API

### Kompatybilność:
- ✅ Partial classes są w pełni kompatybilne z Avalonia/ReactiveUI
- ✅ Nie wpływają na data binding
- ✅ Nie wpływają na XAML
- ✅ Zachowują wszystkie modyfikatory dostępu

---

## 📊 Metryki Sukcesu

- [x] Redukcja głównego pliku do <1000 linii
- [ ] Build solution bez błędów
- [ ] Wszystkie funkcje działają poprawnie
- [ ] Kod jest bardziej czytelny i utrzymywalny
- [ ] Każdy partial class ma pojedynczą odpowiedzialność
- [ ] Logika biznesowa przeniesiona do Core gdzie to możliwe

---

**Autor:** Claude Code AI Assistant
**Data utworzenia:** 2025-10-20
**Status:** ✅ Plan gotowy do implementacji
