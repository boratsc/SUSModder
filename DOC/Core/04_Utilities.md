# SUSModder.Core - Utilities

## Przegląd
Moduł `Utilities` zawiera interfejsy i klasy pomocnicze wykorzystywane w całej aplikacji. Obejmuje narzędzia do operacji na plikach, konfiguracji ścieżek oraz interfejsy abstrakcji dla komunikacji z użytkownikiem.

## Struktura plików

### ✅ **FixBlackScreen.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Narzędzie do resetowania ustawień gry Among Us (fix czarnego ekranu)  
**Analiza użycia:** 1 użycie

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:1316` - wywołanie funkcji fixu

**Funkcjonalność:**
- Resetuje ustawienia gry zachowując pliki konfiguracyjne (.txt)
- Usuwa cache i dane tymczasowe z folderu Among Us
- Pozostawia nietknięte: pliki .txt i regionInfo.json

**Ścieżka docelowa:**
```
%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us
```

**Publiczne metody:**
```csharp
static void ExecuteFixCore()              // Core logic (bez UI)
static void ExecuteFix(IUserInteraction userInteraction)  // Z potwierdzeniem UI
```

**Proces:**
1. Sprawdź istnienie katalogu Among Us
2. Iteruj przez pliki:
   - Zachowaj: `*.txt` i `regionInfo.json`
   - Usuń: wszystkie inne pliki
3. Usuń wszystkie podkatalogi (cache, logs, etc.)

**Przypadki użycia:**
- Czarny ekran przy uruchomieniu gry
- Problemy z corrupted cache
- Reset ustawień do domyślnych

**Obsługa błędów:**
- `DirectoryNotFoundException` - katalog Among Us nie znaleziony
- Inne wyjątki IO - raportowane przez UI

**Rekomendacja:** ✅ **ZACHOWAĆ** - Przydatne narzędzie diagnostyczne.

---

### ✅ **LobbyUtils.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Narzędzia do modyfikacji ustawień lobby (liczba graczy) w modzie Town of Us  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `ToUConfigService.cs:47` - wrapper w serwisie
- `LobbySetDialog.axaml.cs:30` - bezpośrednie wywołanie z UI

**Funkcjonalność:**
- Ustawia maksymalną liczbę graczy w lobby (4-255)
- Modyfikuje pliki konfiguracyjne ToU (JSON)
- Generuje custom code dla Base64

**Obsługiwane pliki:**
```
%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us\settings.amogus_TOU
%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us\settings.amogus_TOUMira
```

**Publiczne metody:**
```csharp
static string GenerateCustomCode(int value)
static bool SetLobbyPlayers(int numPlayers, out string errorMessage)
```

**Algorytm GenerateCustomCode:**
```csharp
// Tworzy Base64 z tablicy bajtów
byte[] bytes = { 0x00, (byte)value, 0x01 };
return Convert.ToBase64String(bytes);

// Przykład dla 15 graczy:
// bytes = [0x00, 0x0F, 0x01]
// Base64 = "AA8B"
```

**Proces SetLobbyPlayers:**
1. Waliduj zakres: 4-255 graczy
2. Dla każdego pliku konfiguracyjnego:
   - Odczytaj JSON
   - Wyciągnij `multiplayer.normalHostOptions` (string)
   - Wygeneruj custom code dla liczby graczy
   - Zamień bajty 8-11 (custom code)
   - Zapisz zmodyfikowany JSON

**Format `normalHostOptions`:**
```
"OriginalPart[0-7] + CustomCode[8-11] + RemainingPart[12+]"
Długość minimalna: 12 znaków (Base64)
```

**Obsługa błędów:**
```csharp
// Walidacja
numPlayers < 4 || > 255 → "Liczba graczy musi być w zakresie 4-255."

// Brak plików
errorMessage = "Brak pliku konfiguracyjnego ToU - uruchom grę z modem..."

// Nieprawidłowy format
errorMessage = "Plik konfiguracyjny ma nieprawidłowy format."

// Exception podczas przetwarzania
errorMessage = $"Błąd podczas przetwarzania pliku: {ex.Message}"
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Funkcja specyficzna dla ToU, aktywnie używana.

---

### ✅ **PathSettings.cs** ✔️ [W UŻYCIU]
**Status:** Intensywnie używany  
**Opis:** Centralna klasa zarządzająca ścieżkami instalacji modów  
**Analiza użycia:** 17 użyć w całym projekcie

**Lokalizacje użycia:**
- `ModConfigHandler.cs` - 4 użycia (operacje na presetach)
- `Diagnostics.cs` - 2 użycia (logi)
- `EpicVersionManager.cs` - 2 użycia (instalacja Epic)
- `ModManager.cs` - 2 użycia (instalacja Steam)
- `AppSettingsViewModel.cs` - 2 użycia (ustawienia)
- `MainWindowViewModel.cs` - 2 użycia (główny VM)
- `AdditionalActionsPanel.axaml.cs` - 1 użycie

**Funkcjonalność:**
- Wczytuje i cache'uje ścieżkę instalacji modów
- Obsługuje dwie konfiguracje:
  - `ModsInstallPath` - custom ścieżka użytkownika
  - `DefaultModsPath` - domyślna ścieżka fallback
- Cache z możliwością odświeżenia
- Rozwijanie zmiennych środowiskowych (`%APPDATA%`)

**Publiczne właściwości/metody:**
```csharp
static string ModsInstallPath { get; }        // Główna ścieżka (cached)
static string DefaultModsPath { get; }        // Domyślna ścieżka
static void RefreshSettings()                 // Wyczyść cache
static string GetDefaultModsPath()            // Getter dla domyślnej
static void SetCustomPath(string path)        // Dla testów
```

**Hierarchia ścieżek:**
```
1. ModsInstallPath (z appsettings.json)
   ↓ jeśli puste
2. DefaultModsPath (z appsettings.json)
   ↓ jeśli puste
3. Fallback: %APPDATA%\Among Us - Mody
```

**Konfiguracja appsettings.json:**
```json
{
  "AppSettings": {
    "ModsInstallPath": "D:\\Games\\Among Us Mods",  // Custom (opcjonalne)
    "DefaultModsPath": "%APPDATA%\\Among Us - Mody" // Domyślna
  }
}
```

**Caching:**
```csharp
private static string? _cachedModsInstallPath = null;

public static string ModsInstallPath
{
    get
    {
        if (_cachedModsInstallPath != null)
            return _cachedModsInstallPath;  // Zwróć z cache
        
        return LoadModsInstallPath();       // Załaduj z pliku
    }
}
```

**Inicjalizacja statyczna:**
- Wykonywana przy pierwszym dostępie do klasy
- Wczytuje konfigurację z `appsettings.json`
- Ustawia domyślne wartości

**Rozwijanie zmiennych środowiskowych:**
```csharp
Environment.ExpandEnvironmentVariables(path)

// Przykłady:
"%APPDATA%\Among Us - Mody" → "C:\Users\User\AppData\Roaming\Among Us - Mody"
"%USERPROFILE%\Documents" → "C:\Users\User\Documents"
```

**Obsługa błędów:**
- Brak pliku konfiguracyjnego → użyj fallback `%APPDATA%\Among Us - Mody`
- Wyjątek podczas odczytu → użyj fallback
- Logowanie do Debug output

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowa klasa dla całej aplikacji.

⚠️ **Sugestia:** Rozważyć dodanie metody `SavePathToConfig` (jest private i niekompletna).

---

### ✅ **IProgressReporter.cs** ✔️ [W UŻYCIU]
**Status:** Szeroko używany jako interfejs  
**Opis:** Interfejs do raportowania postępu długotrwałych operacji  
**Analiza użycia:** Używany w całej aplikacji (ModManager, EpicVersionManager, etc.)

**Definicja:**
```csharp
public interface IProgressReporter
{
    void Report(int percent, string? message = null);
}
```

**Parametry:**
- `percent` - postęp w procentach (0-100)
- `message` - opcjonalny opis aktualnej operacji

**Implementacje:**
- UI: Przekazywane jako lambda lub klasa adaptująca do ProgressBar
- Przykład:
  ```csharp
  var progress = new Progress<(int, string)>(tuple => 
  {
      ProgressBar.Value = tuple.Item1;
      StatusText.Text = tuple.Item2;
  });
  ```

**Użycie w Core:**
- `ModManager.ModifyAsync` - instalacja moda
- `EpicVersionManager.InstallOrUpdateModAsync` - instalacja Epic
- `AppUpdateService.DownloadUpdateAsync` - pobieranie aktualizacji
- `ModService.InstallModAsync` - fasada instalacji

**Pattern:** Observer / Callback

**Rekomendacja:** ✅ **ZACHOWAĆ** - Podstawowy interfejs dla UI feedback.

---

### ✅ **IUserInteraction.cs** ✔️ [W UŻYCIU]
**Status:** Główny interfejs komunikacji Core ↔ UI  
**Opis:** Interfejs abstrakcji dla dialogów i interakcji z użytkownikiem  
**Analiza użycia:** Szeroko używany w całym projekcie

**Definicja:**
```csharp
public interface IUserInteraction
{
    // Synchroniczne (deprecated, kompatybilność)
    bool Confirm(string message, string title = "");
    void ShowInfo(string message, string title = "");
    void ShowError(string message, string title = "");
    string? Prompt(string message, string title = "");
    string? SelectFile(string filter, string initialDirectory = "");

    // Asynchroniczne (preferowane)
    Task ShowInfoAsync(string message, string title = "");
    Task ShowErrorAsync(string message, string title = "");
    Task<bool> ShowConfirmAsync(string message, string title = "");
    Task<string?> ShowPromptAsync(string message, string title = "");
    Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "");
}
```

**Implementacje:**
- `UserInteractionService` (Services) - główna implementacja dla UI
- Implementacje testowe - mock dla unit testów

**Użycie w Core:**
- `ModManager` - potwierdzenia, błędy podczas instalacji
- `ModDelete` - informacje o usunięciu
- `FixBlackScreen` - potwierdzenie resetu
- `ModConfigHandler` - operacje na presetach
- Wszystkie serwisy biznesowe

**Przykład użycia:**
```csharp
// Potwierdzenie
if (await userInteraction.ShowConfirmAsync(
    "Czy chcesz kontynuować?", 
    "Pytanie"))
{
    // User kliknął "Tak"
}

// Błąd
await userInteraction.ShowErrorAsync(
    "Nie znaleziono pliku.", 
    "Błąd");

// Wybór pliku
string? path = await userInteraction.ShowSelectFileDialogAsync(
    "Pliki exe|*.exe",
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
```

**Pattern:** Dependency Inversion Principle (DIP), Bridge

**Rekomendacja:** ✅ **ZACHOWAĆ** - Fundamentalny interfejs architektury.

⚠️ **Uwaga:** Preferuj async metody, sync wersje blokują wątek UI.

---

### ✅ **IUserInteractionAsync.cs** ✔️ [W UŻYCIU JAKO KONTRAKT]
**Status:** Używany jako część interfejsu, implementacja w UserInteractionService  
**Opis:** Czysta wersja asynchroniczna interfejsu interakcji  
**Analiza użycia:** Nie ma dedykowanej implementacji (połączona z IUserInteraction)

**Definicja:**
```csharp
public interface IUserInteractionAsync
{
    Task<bool> ConfirmAsync(string message, string title = "");
    Task ShowInfoAsync(string message, string title = "");
    Task ShowErrorAsync(string message, string title = "");
    Task<string?> PromptAsync(string message, string title = "");
    Task<string?> SelectFileAsync(string filter, string initialDirectory = "");
}
```

**Status implementacji:**
- Metody są częścią `IUserInteraction` (rozszerzone)
- `UserInteractionService` implementuje obie wersje (sync+async)
- `UserInteractionAsyncService` ma dedykowaną implementację (ale nieużywaną)

**Różnica nazw metod:**
```
IUserInteractionAsync        IUserInteraction
────────────────────        ────────────────
ConfirmAsync         vs      ShowConfirmAsync
PromptAsync          vs      ShowPromptAsync
SelectFileAsync      vs      ShowSelectFileDialogAsync
```

**Rekomendacja:** ⚠️ **ROZWAŻYĆ REFAKTORING**
- Opcja 1: Usunąć interfejs, używać tylko `IUserInteraction` (zawiera async metody)
- Opcja 2: Zunifikować nazwy metod (np. wszędzie `ShowXxxAsync`)
- Opcja 3: Połączyć oba interfejsy w jeden

**Obecnie:** Interfejs istnieje ale nie jest aktywnie wykorzystywany w izolacji.

---

## Podsumowanie analizy

### ✅ Wszystkie klasy/interfejsy w użyciu:
1. **FixBlackScreen.cs** - reset ustawień gry
2. **LobbyUtils.cs** - ustawienia lobby ToU
3. **PathSettings.cs** - zarządzanie ścieżkami (KLUCZOWE)
4. **IProgressReporter.cs** - interfejs postępu
5. **IUserInteraction.cs** - interfejs komunikacji UI (KLUCZOWY)
6. **IUserInteractionAsync.cs** - część async (do refaktoringu)

### Statystyki:
- **Pliki ogółem:** 6
- **Aktywne:** 6 (100%)
- **Do usunięcia:** 0 (0%)
- **Do refaktoringu:** 1 interfejs (IUserInteractionAsync - zunifikować nazwy)

---

## Architektura interfejsów

```
Core Layer
│
├─ Abstrakcje UI
│  ├─ IUserInteraction (sync + async)
│  │  └─ Implementacja: UserInteractionService
│  │     └─ Delegates → UI (MainWindow dialogi)
│  │
│  ├─ IUserInteractionAsync (pure async)
│  │  └─ Zbędna? (duplikacja IUserInteraction)
│  │
│  └─ IProgressReporter
│     └─ Implementacja: Lambda/Progress<T> w UI
│
├─ Utilities
│  ├─ FixBlackScreen (narzędzie diagnostyczne)
│  ├─ LobbyUtils (ToU specific)
│  └─ PathSettings (centralna konfiguracja ścieżek)
│
└─ Pattern: Dependency Inversion
   Core definiuje interfejsy (IUserInteraction)
   UI dostarcza implementacje (UserInteractionService)
```

---

## Dependency Inversion w praktyce

### Bez DIP (złe):
```csharp
// Core bezpośrednio zależy od UI
public class ModManager
{
    public void Install()
    {
        MessageBox.Show("Error!"); // ❌ Zależność od UI
    }
}
```

### Z DIP (dobre):
```csharp
// Core zależy od abstrakcji
public class ModManager
{
    private IUserInteraction _ui;
    
    public ModManager(IUserInteraction ui)
    {
        _ui = ui;
    }
    
    public void Install()
    {
        _ui.ShowError("Error!", "Title"); // ✅ Abstrakcja
    }
}

// UI dostarcza implementację
var modManager = new ModManager(new UserInteractionService(...));
```

**Zalety:**
- Core jest testowalny (mock IUserInteraction)
- Core nie zależy od konkretnego UI framework
- Możliwość wymiany UI (Avalonia → WPF → Console)

---

## Kluczowe ścieżki używane przez PathSettings

### ModsInstallPath (zazwyczaj):
```
Windows: C:\Users\{User}\AppData\Roaming\Among Us - Mody\
    ├─ Among Us - Vanilla\    # Archiwa vanilla
    │  ├─ 2024111.7z
    │  └─ 2024322.7z
    ├─ Town of Us\             # Mod full
    │  ├─ Among Us.exe
    │  └─ BepInEx\
    │     └─ plugins\
    ├─ Sheriff Mod\            # Mod full
    ├─ Konfiguracje\           # Presety gry (ZIP)
    └─ temp\                   # Tymczasowe rozpakowania
```

### Inne ścieżki w Utilities:

**FixBlackScreen:**
```
%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us\
```

**LobbyUtils:**
```
%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us\
    ├─ settings.amogus_TOU
    └─ settings.amogus_TOUMira
```

---

## Następne kroki refaktoringu

1. ⚠️ **IUserInteractionAsync** - zunifikować nazwy metod:
   - `ConfirmAsync` → `ShowConfirmAsync`
   - `PromptAsync` → `ShowPromptAsync`
   - Lub usunąć interfejs (używaj tylko IUserInteraction)

2. ⚠️ **PathSettings** - dokończyć metodę `SavePathToConfig`:
   - Jest private i incomplete
   - Dodać publiczny `SetModsInstallPath(string path)`

3. ✅ **Dodać XML documentation comments** do wszystkich interfejsów

4. ✅ **Unit testy** dla:
   - `LobbyUtils.GenerateCustomCode`
   - `PathSettings` (różne konfiguracje)
   - Mock implementations dla interfejsów

---

*Dokumentacja wygenerowana: 2025-10-19*  
*Autor: GitHub Copilot AI Assistant*
