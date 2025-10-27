# SUSModder.Core - Services

## Przegląd
Moduł `Services` zawiera serwisy biznesowe aplikacji, które orkiestrują operacje na wyższym poziomie abstrakcji niż klasy GameIntegration. Serwisy dostarczają API dla warstwy UI.

## Struktura plików

### ✅ **AppUpdateService.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Serwis zarządzający aktualizacjami samej aplikacji SUSModder  
**Analiza użycia:** 6 użyć

**Lokalizacje użycia:**
- `Program.cs:15` - przywracanie ustawień po update
- `MainWindowViewModel.cs:409` - sprawdzanie aktualizacji
- `AppUpdateDialog.axaml.cs:14, 34, 53` - dialog aktualizacji (3x)

**Funkcjonalność:**
- Sprawdzanie dostępności nowych wersji aplikacji
- Pobieranie paczki aktualizacji z serwera
- Uruchamianie procesu Updater.exe
- Backup i przywracanie ustawień użytkownika po update

**Klasy pomocnicze:**
```csharp
class UpdateCheckResult
{
    bool IsUpdateAvailable
    string CurrentVersion
    string LatestVersion
    bool Success
    string? ErrorMessage
}

class UpdateDownloadResult
{
    bool Success
    string? FilePath
    string? ErrorMessage
}
```

**Publiczne metody:**
```csharp
Task<UpdateCheckResult> CheckForUpdateAsync()
Task<UpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null)
bool RunUpdater(string updateFilePath)
static void RestoreUserSettingsIfNeeded()
```

**Proces aktualizacji:**
1. **Sprawdzanie:** `CheckForUpdateAsync()`
   - Pobiera najnowszą wersję z API
   - Porównuje z `CurrentVersion` w appsettings.json
   - Zwraca `UpdateCheckResult`

2. **Pobieranie:** `DownloadUpdateAsync(IProgress<int>)`
   - URL: `GetDownloadUrl()` z konfiguracji
   - Zapis do: `%TEMP%/SUSModder_Update.zip`
   - Raportowanie postępu (0-100%)

3. **Instalacja:** `RunUpdater(string updateFilePath)`
   - Zapisuje kopię ustawień użytkownika do `user_settings_backup.json`
   - Uruchamia `updater/Updater.exe` z argumentami:
     - Ścieżka ZIP
     - Ścieżka aplikacji
     - Ścieżka backup ustawień
   - Zamyka aplikację

4. **Przywracanie:** `RestoreUserSettingsIfNeeded()` (static)
   - Wywoływane w `Program.Main` przy starcie
   - Odczytuje `user_settings_backup.json`
   - Przywraca: Mode, Theme, lastLaunchId, ModsInstallPath
   - Usuwa plik backup

**Backup ustawień:**
```json
{
  "Mode": "steam/epic",
  "Theme": "Dark/Light/Pink",
  "lastLaunchId": "guid",
  "ModsInstallPath": "C:\\..."
}
```

**Endpoint API:**
- Klucz: `Configuration:UpdateServerUrl` lub `Configuration:BaseUrl`
- Metoda: GET

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy dla auto-update aplikacji.

---

### ✅ **ConfigService.cs** ✔️ [W UŻYCIU]
**Status:** Intensywnie używany  
**Opis:** Fasada do zarządzania konfiguracją modów, wrapper nad ConfigManager  
**Analiza użycia:** 14 użyć w całym projekcie

**Lokalizacje użycia:**
- `DllModificationService.cs:13, 19` - dependency injection
- `ModUpdateManager.cs:14, 19` - dependency injection
- `MainWindowViewModel.cs` - 10 wywołań różnych metod

**Funkcjonalność:**
- Wrapper nad `ConfigManager` (fasada)
- Dodatkowe metody sprawdzania aktualizacji pojedynczych modów
- Odczyt wersji aplikacji
- Uproszczone API dla UI

**Publiczne metody:**
```csharp
List<ModConfiguration> LoadConfig()
void SaveConfig(List<ModConfiguration> configs)
void SaveConfigurationSetting(string key, string value)
string GetAppVersion()
Task<ModConfiguration?> CheckSingleModUpdateAsync(string modName)
Task<bool> UpdateSingleModConfigAsync(ModConfiguration updatedMod)
Task<List<ModConfiguration>?> LoadConfigFromApiAsync()
bool IsNewerVersion(string remoteVersion, string localVersion)
```

**Logika `CheckSingleModUpdateAsync`:**
1. Pobiera lokalną konfigurację moda
2. Pobiera najnowszą konfigurację z API
3. Porównuje wersje (`IsNewerVersion`)
4. Zachowuje `InstallPath` i `LastUpdated` z lokalnej konfiguracji
5. Zwraca `ModConfiguration` lub `null` jeśli brak aktualizacji

**Logika `IsNewerVersion`:**
- Parsuje wersje przez `Version.Parse()`
- Porównuje: remote > local

**Delegacja do ConfigManager:**
```csharp
LoadConfig() → ConfigManager.LoadConfig()
SaveConfig() → ConfigManager.SaveConfig()
SaveConfigurationSetting() → ConfigManager.SaveConfigurationSetting()
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Główny punkt dostępu do konfiguracji.

---

### ✅ **DialogService.cs** ❌ [NIEUŻYWANY - DO USUNIĘCIA]
**Status:** Interfejs i implementacja nieużywane  
**Opis:** Serwis do wyświetlania dialogów (pustych metod placeholder)  
**Analiza użycia:** 0 użyć (tylko definicja)

**Funkcjonalność:**
- Interfejs `IDialogService` z 2 metodami
- Implementacja `DialogService` z pustymi metodami
- Wszystkie metody zwracają wartości domyślne lub Task.CompletedTask

**Publiczne metody:**
```csharp
interface IDialogService
{
    Task<bool> ShowLobbySetDialogAsync()
    Task ShowMessageAsync(string title, string message)
}
```

**Stan implementacji:**
- Metody nie mają implementacji (komentarze "będzie w MainWindowViewModel")
- Prawdopodobnie zastąpione przez `IUserInteraction`/`IUserInteractionAsync`

**Rekomendacja:** ⚠️ **USUNĄĆ** - Nieużywany serwis, funkcjonalność przeniesiona do UserInteraction.

---

### ✅ **DllModificationService.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Serwis zarządzający instalacją i usuwaniem modów DLL do/z modów full  
**Analiza użycia:** 5 użyć

**Lokalizacje użycia:**
- `DllModSelectionViewModel.cs:15, 56` - ViewModel DLL selection
- `MainWindowViewModel.cs:53, 189` - inicjalizacja i użycie

**Funkcjonalność:**
- Pobieranie listy modów DLL
- Pobieranie listy modów full (zainstalowanych)
- Sprawdzanie które mody full mają zainstalowany dany DLL
- Instalacja DLL do wybranego moda full
- Usuwanie DLL z wybranego moda full
- Wsparcie dla różnych linków (Steam/Epic)

**Publiczne metody:**
```csharp
List<ModConfiguration> GetDllMods()
List<ModConfiguration> GetAvailableFullMods()
List<ModConfiguration> GetModsWithDllInstalled(ModConfiguration dllMod, string platform)
List<ModConfiguration> GetModsWithoutDllInstalled(ModConfiguration dllMod, string platform)
Task<bool> InstallDllToModAsync(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
Task<bool> UninstallDllFromModAsync(ModConfiguration dllMod, ModConfiguration targetMod)
```

**Logika wyboru linku (platform-aware):**
```csharp
private string GetDllDownloadUrl(ModConfiguration dllMod, string platform)
{
    if (platform == "epic" && !string.IsNullOrEmpty(dllMod.EpicGitHubRepoOrLink))
        return dllMod.EpicGitHubRepoOrLink;
    
    return dllMod.GitHubRepoOrLink;
}
```

**Proces instalacji DLL:**
1. Sprawdź `targetMod.InstallPath` (musi być zainstalowany)
2. Wybierz URL pobrania (Steam/Epic)
3. Wyciągnij nazwę pliku DLL z URL
4. Ustal ścieżkę docelową: `{InstallPath}/{DllInstallPath}/{fileName}`
5. Utwórz katalog jeśli nie istnieje (np. `BepInEx\plugins`)
6. Pobierz plik przez HttpClient
7. Zapisz do dysku
8. Aktualizuj `dllMod.InstallPath` w konfiguracji

**Domyślna ścieżka DLL:**
- `DllInstallPath` z konfiguracji moda
- Fallback: `BepInEx\plugins`

**Detekcja zainstalowanego DLL:**
```csharp
private bool IsDllInstalledInMod(ModConfiguration dllMod, ModConfiguration fullMod, string platform)
{
    string url = GetDllDownloadUrl(dllMod, platform);
    string fileName = Path.GetFileName(new Uri(url).LocalPath);
    string dllPath = Path.Combine(fullMod.InstallPath, dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName);
    return File.Exists(dllPath);
}
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy dla instalacji DLL modów.

---

### ✅ **GameService.cs** ❌ [NIEUŻYWANY - DO USUNIĘCIA]
**Status:** Klasa nieużywana  
**Opis:** Duplikat funkcjonalności z `GameLocator`  
**Analiza użycia:** 0 użyć (tylko definicja)

**Funkcjonalność:**
- Próba znalezienia ścieżki Among Us (Steam/Epic)
- Pobieranie wersji gry z .exe
- Konfiguracja vanilla moda

**Publiczne metody:**
```csharp
(string? Path, string? Mode) TryFindAmongUsPath()
string GetGameVersion(string path)
void CheckAndSetupVanillaMod(List<ModConfiguration> modConfigs, IConfiguration configuration)
```

**Dlaczego nieużywany:**
- Identyczna funkcjonalność jest w `GameLocator` (GameIntegration)
- `GameLocator` jest używany w całym projekcie
- Prawdopodobnie próba refaktoringu która nie została ukończona

**Porównanie z GameLocator:**
| Funkcja | GameService | GameLocator | Status |
|---------|-------------|-------------|--------|
| TryFindAmongUsPath | ✅ | ✅ | Duplikat |
| GetGameVersion | ✅ | ✅ | Duplikat |
| CheckAndSetupVanillaMod | ✅ | ✅ (+ async) | Duplikat |

**Rekomendacja:** ⚠️ **USUNĄĆ** - Kompletny duplikat GameLocator, zero użyć.

---

### ✅ **ModService.cs** ✔️ [W UŻYCIU WEWNĘTRZNIE]
**Status:** Używany tylko wewnętrznie (w samym pliku)  
**Opis:** Fasada orchestrująca operacje na modach (instalacja, aktualizacja, usuwanie)  
**Analiza użycia:** 1 użycie (samo-referencja w definicji konstruktora)

**⚠️ UWAGA:** Klasa jest zdefiniowana ale nie jest używana bezpośrednio w innych częściach projektu. Operacje na modach są wywoływane bezpośrednio przez:
- `ModManager.ModifyAsync` (GameIntegration)
- `ModUpdates.UpdateModAsync` (GameIntegration)
- `ModDelete.DeleteMod` (GameIntegration)

**Funkcjonalność:**
- Wrapper nad klasami GameIntegration
- Uproszczone API dla operacji na modach

**Publiczne metody:**
```csharp
Task InstallModAsync(ModConfiguration modConfig, List<ModConfiguration> modConfigs,
                    IProgressReporter progress, IDiagnosticsOutput log,
                    IUserInteraction userInteraction, string mode,
                    List<ModConfiguration>? selectedFullModsForDll = null)

Task UpdateModAsync(ModConfiguration modConfig, List<ModConfiguration> modConfigs,
                   IProgressReporter progress, IDiagnosticsOutput log,
                   IUserInteraction userInteraction)

void DeleteMod(ModConfiguration modConfig, List<ModConfiguration> modConfigs,
              IUserInteraction userInteraction)

void DeleteDllFromFullMods(ModConfiguration dllModConfig, List<ModConfiguration> fullMods,
                          IDiagnosticsOutput log, IUserInteraction userInteraction)
```

**Delegacja:**
- `InstallModAsync` → `ModManager.ModifyAsync` / `ModManager.ModifyDllAsync`
- `UpdateModAsync` → `ModUpdates.UpdateModAsync`
- `DeleteMod` → `ModDelete.DeleteMod`

**Rekomendacja:** ⚠️ **ROZWAŻYĆ USUNIĘCIE lub AKTYWNE UŻYCIE**
- Klasa jest dobrze zaprojektowana jako fasada
- NIE jest używana w projekcie (UI wywołuje bezpośrednio klasy GameIntegration)
- Opcje:
  1. Usunąć jako nieużywaną
  2. Refaktorować UI aby używało ModService zamiast bezpośrednich wywołań

---

### ✅ **ModUpdateManager.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Manager sprawdzający i zarządzający aktualizacjami modów  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:702` - sprawdzanie aktualizacji przy starcie

**Funkcjonalność:**
- Sprawdzanie dostępnych aktualizacji dla zainstalowanych modów
- Aktualizacja konfiguracji niezainstalowanych modów (w tle)
- Uzupełnianie brakujących pól w lokalnej konfiguracji (PngFileName, DllInstallPath)
- Zwracanie listy `ModUpdateInfo`

**Klasa `ModUpdateResult`:**
```csharp
class ModUpdateResult
{
    List<ModUpdateInfo> InstalledModUpdates    // Lista modów do aktualizacji
    bool ConfigWasUpdated                      // Czy config.json został zaktualizowany
    bool Success
    string? ErrorMessage
}
```

**Klasa `ModUpdateInfo`:**
```csharp
class ModUpdateInfo : INotifyPropertyChanged
{
    ModConfiguration LocalMod
    ModConfiguration RemoteMod
    bool IsSelected
    string UpdateDescription    // Opis różnic wersji
}
```

**Publiczne metody:**
```csharp
Task<ModUpdateResult> CheckForUpdatesAsync()
```

**Proces sprawdzania aktualizacji:**
1. **Załaduj konfiguracje:**
   - Lokalna: `ConfigService.LoadConfig()`
   - Zdalna: `ConfigRepository.LoadConfigFromApiAsync()`

2. **Podziel mody:**
   - Zainstalowane: `InstallPath != null`
   - Niezainstalowane: `InstallPath == null`

3. **Sprawdź aktualizacje zainstalowanych:**
   - Porównaj `ModVersion` i `AmongVersion`
   - Uzupełnij brakujące pola (PngFileName, DllInstallPath) z API
   - Dodaj do listy `InstalledModUpdates`

4. **Aktualizuj niezainstalowane (cicho):**
   - Aktualizuj wszystkie pola z API
   - Zachowaj `InstallPath = null`
   - Zapisz do `config.json`

5. **Zwróć wynik:**
   - `ModUpdateResult` z listą aktualizacji

**Auto-uzupełnianie pól:**
```csharp
// Jeśli lokalna konfiguracja ma puste pola, uzupełnij z API
if (string.IsNullOrEmpty(config.PngFileName) && !string.IsNullOrEmpty(updatedConfig.PngFileName))
{
    config.PngFileName = updatedConfig.PngFileName;
    configChanged = true;
}
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy dla systemu aktualizacji modów.

---

### ✅ **ToUConfigService.cs** ✔️ [W UŻYCIU]
**Status:** Częściowo używany (jedna metoda aktywna)  
**Opis:** Serwis zarządzający konfiguracjami gry Among Us (Town of Us presets)  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:49, 174` - inicjalizacja i ustawienie lobby size

**Funkcjonalność:**
- Wrapper nad `ModConfigHandler` (zakomentowane)
- Aktywna metoda: `SetLobbySize` → delegacja do `LobbyUtils`

**Publiczne metody:**
```csharp
void SaveLocalConfig()              // ❌ Pusta (zakomentowana)
void LoadLocalConfig()              // ❌ Pusta (zakomentowana)
Task SaveServerConfigAsync()        // ❌ Pusta (placeholder)
Task LoadServerConfigAsync()        // ❌ Pusta (placeholder)
void LoadLocalTxtConfig()           // ❌ Pusta (zakomentowana)
void ChangePresetNames()            // ❌ Pusta (zakomentowana)
bool SetLobbySize(int playerCount, out string errorMessage)  // ✅ AKTYWNA
```

**Aktywna funkcjonalność:**
```csharp
public bool SetLobbySize(int playerCount, out string errorMessage)
{
    return LobbyUtils.SetLobbyPlayers(playerCount, out errorMessage);
}
```

**Stan kodu:**
- Wszystkie metody poza `SetLobbySize` są zakomentowane lub placeholdery
- Prawdopodobnie niedokończony refaktoring
- UI używa bezpośrednio `ModConfigHandler` dla zapisywania/wczytywania konfiguracji

**Rekomendacja:** ⚠️ **REFAKTOROWAĆ lub USUNĄĆ**
- Opcja 1: Dokończyć refaktoring (odkomentować metody, przenieść logikę z ModConfigHandler)
- Opcja 2: Usunąć klasę, zostawić tylko `LobbyUtils.SetLobbyPlayers` jako metodę statyczną
- Opcja 3: Zmienić nazwę na `LobbyService` i zostawić tylko `SetLobbySize`

---

### ✅ **UserInteractionService.cs** ✔️ [W UŻYCIU]
**Status:** Intensywnie używany  
**Opis:** Implementacja `IUserInteraction` - serwis do interakcji z użytkownikiem (dialogi)  
**Analiza użycia:** 10 użyć

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:52, 175, 2928, 2930, 2954, 2956` - 6 wywołań
- `AdditionalActionsPanel.axaml.cs:25, 42` - 2 wywołania
- `UpdateDialog.axaml.cs:232` - 1 wywołanie

**Funkcjonalność:**
- Bridge między Core a UI
- Deleguje wywołania do rzeczywistych dialogów UI (przekazanych jako delegate)
- Implementuje zarówno synchroniczne jak i asynchroniczne wersje metod

**Konstruktor (Dependency Injection przez delegates):**
```csharp
public UserInteractionService(
    Func<string, string, Task<bool>> confirmDialog,
    Func<string, string, Task> infoDialog,
    Func<string, string, Task> errorDialog,
    Func<string, string, Task<string?>> promptDialog,
    Func<string, string, Task<string?>> selectFileDialog)
```

**Publiczne metody (synchroniczne):**
```csharp
bool Confirm(string message, string title = "")
void ShowInfo(string message, string title = "")
void ShowError(string message, string title = "")
string? Prompt(string message, string title = "")
string? SelectFile(string filter, string initialDirectory = "")
```

**Publiczne metody (asynchroniczne):**
```csharp
Task ShowInfoAsync(string message, string title = "")
Task ShowErrorAsync(string message, string title = "")
Task<bool> ShowConfirmAsync(string message, string title = "")
Task<string?> ShowPromptAsync(string message, string title = "")
Task<string?> ShowSelectFileDialogAsync(string filter, string initialDirectory = "")
```

**Implementacja synchroniczna (adapter):**
```csharp
public bool Confirm(string message, string title = "")
{
    return _confirmDialog(message, title)
        .ConfigureAwait(false)
        .GetAwaiter()
        .GetResult();
}
```

**Pattern:** Dependency Injection przez delegates + Adapter (sync/async)

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy bridge Core ↔ UI.

⚠️ **Uwaga:** Synchroniczne metody blokują wątek (GetAwaiter().GetResult()) - preferuj asynchroniczne wersje.

---

### ✅ **UserInteractionAsyncService.cs** ✔️ [W UŻYCIU WEWNĘTRZNIE]
**Status:** Używany tylko wewnętrznie (samo-referencja)  
**Opis:** Implementacja `IUserInteractionAsync` - pełna wersja asynchroniczna  
**Analiza użycia:** 1 użycie (definicja własna)

**⚠️ UWAGA:** Klasa jest zdefiniowana ale prawdopodobnie nie jest używana bezpośrednio. Projekt preferuje `UserInteractionService` która implementuje **OBA** interfejsy (`IUserInteraction` + async metody).

**Funkcjonalność:**
- Identyczna jak `UserInteractionService` ale tylko async metody
- Brak wersji synchronicznych

**Publiczne metody:**
```csharp
Task<bool> ConfirmAsync(string message, string title = "")
Task ShowInfoAsync(string message, string title = "")
Task ShowErrorAsync(string message, string title = "")
Task<string?> PromptAsync(string message, string title = "")
Task<string?> SelectFileAsync(string filter, string initialDirectory = "")
```

**Rekomendacja:** ⚠️ **ROZWAŻYĆ USUNIĘCIE**
- `UserInteractionService` już implementuje metody async
- Duplikacja funkcjonalności
- Brak rzeczywistego użycia w projekcie
- Jeśli ma być zachowana, trzeba ją aktywnie używać zamiast UserInteractionService

---

## Podsumowanie analizy

### ❌ Kandydaci do usunięcia:
1. **DialogService.cs** - puste placeholder-y, zastąpione przez UserInteraction
2. **GameService.cs** - kompletny duplikat GameLocator, zero użyć
3. **UserInteractionAsyncService.cs** - duplikacja UserInteractionService

### ⚠️ Do refaktoringu:
1. **ModService.cs** - dobrze zaprojektowany ale nieużywany, rozważyć:
   - Usunięcie (jeśli nie planowane użycie)
   - Aktywne użycie w UI (refaktoring VM → używaj ModService zamiast bezpośrednich wywołań)
   
2. **ToUConfigService.cs** - większość metod zakomentowana/placeholder, rozważyć:
   - Dokończenie refaktoringu (aktywacja wszystkich metod)
   - Zmiana nazwy na `LobbyService` (tylko SetLobbySize)
   - Usunięcie (wywołuj bezpośrednio LobbyUtils)

### ✅ Klasy do zachowania (w użyciu):
1. **AppUpdateService.cs** - auto-update aplikacji
2. **ConfigService.cs** - fasada nad ConfigManager
3. **DllModificationService.cs** - instalacja DLL modów
4. **ModUpdateManager.cs** - sprawdzanie aktualizacji modów
5. **UserInteractionService.cs** - bridge Core ↔ UI

### Statystyki:
- **Pliki ogółem:** 10
- **Aktywne:** 5 (50%)
- **Nieużywane:** 3 (30%)
- **Do refaktoringu:** 2 (20%)

---

## Architektura i zależności

```
Services/
│
├─ Update Management
│  ├─ AppUpdateService (aplikacja)
│  └─ ModUpdateManager (mody)
│
├─ Configuration Management
│  └─ ConfigService
│     └─ Wrapper: ConfigManager
│
├─ Mod Operations
│  ├─ ModService ⚠️ (nieużywany, fasada)
│  │  └─ Deleguje: ModManager, ModUpdates, ModDelete
│  └─ DllModificationService
│     └─ Instalacja DLL do modów full
│
├─ User Interaction (Bridge Core ↔ UI)
│  ├─ UserInteractionService ✅
│  └─ UserInteractionAsyncService ⚠️ (duplikat)
│
├─ Game Management
│  ├─ GameService ❌ (nieużywany, duplikat GameLocator)
│  └─ ToUConfigService ⚠️ (większość placeholder)
│
└─ [DEPRECATED]
   └─ DialogService ❌ (puste placeholder-y)
```

### Zależności zewnętrzne:
- `Microsoft.Extensions.Configuration` - dostęp do appsettings.json
- `System.Net.Http` - pobieranie aktualizacji
- `SUSModder.Core.Configuration` - ConfigManager, ModConfiguration
- `SUSModder.Core.GameIntegration` - ModManager, ModUpdates, ModDelete
- `SUSModder.Core.Utilities` - IUserInteraction, IProgressReporter, PathSettings, LobbyUtils
- `SUSModder.Core.Repositories` - ConfigRepository
- `SUSModder.Core.Diagnostics` - IDiagnosticsOutput

---

## Wzorce projektowe

### Fasada (Facade)
- **ConfigService** - upraszcza dostęp do ConfigManager
- **ModService** - upraszcza operacje na modach (nieużywany)

### Dependency Injection
- Wszystkie serwisy przyjmują zależności przez konstruktor
- `UserInteractionService` - injection przez delegates

### Bridge
- **UserInteractionService** - łączy Core z konkretną implementacją UI

### Adapter
- **UserInteractionService** - adaptuje async metody do sync (ConfigureAwait + GetAwaiter)

---

## Kluczowe przepływy

### Aktualizacja aplikacji
```
UI: Check for updates
  → AppUpdateService.CheckForUpdateAsync()
     → API: Pobierz najnowszą wersję
     → Porównaj z CurrentVersion
  → AppUpdateService.DownloadUpdateAsync(progress)
     → Pobierz ZIP do %TEMP%
     → Raportuj postęp
  → AppUpdateService.RunUpdater(zipPath)
     → Backup user settings → user_settings_backup.json
     → Uruchom updater/Updater.exe
     → Zamknij aplikację

Restart aplikacji:
  → Program.Main
     → AppUpdateService.RestoreUserSettingsIfNeeded()
        → Odczytaj user_settings_backup.json
        → Przywróć: Mode, Theme, lastLaunchId, ModsInstallPath
        → Usuń backup
```

### Sprawdzanie aktualizacji modów
```
UI: Startup / Manual check
  → ModUpdateManager.CheckForUpdatesAsync()
     → ConfigService.LoadConfig() (lokalna)
     → ConfigRepository.LoadConfigFromApiAsync() (zdalna)
     
     Dla każdego zainstalowanego moda:
       → Porównaj ModVersion i AmongVersion
       → Uzupełnij brakujące pola (PngFileName, DllInstallPath)
       → Dodaj do InstalledModUpdates jeśli różne wersje
       
     Dla każdego niezainstalowanego moda:
       → Aktualizuj wszystkie pola z API (zachowaj InstallPath=null)
       → Zapisz cicho do config.json
       
  → Zwróć ModUpdateResult
     → UI: Wyświetl dialog z listą dostępnych aktualizacji
```

### Instalacja DLL moda
```
UI: Select DLL + Target Full Mods
  → DllModificationService.InstallDllToModAsync(dllMod, targetMod, platform)
     → Wybierz link (Epic lub Steam)
     → Wyciągnij nazwę pliku DLL z URL
     → Ustal ścieżkę: {InstallPath}/{DllInstallPath}/{fileName}
     → Utwórz katalog (np. BepInEx\plugins)
     → HttpClient.GetAsync(downloadUrl)
     → Zapisz do dysku
     → Aktualizuj dllMod.InstallPath w config.json
```

---

## Następne kroki refaktoringu

1. ✅ **Usunąć nieużywane klasy:**
   - `DialogService.cs`
   - `GameService.cs`
   - `UserInteractionAsyncService.cs`

2. ⚠️ **Zdecydować o ModService:**
   - Opcja A: Usunąć (nieużywany)
   - Opcja B: Refaktorować UI aby używało ModService jako głównego API

3. ⚠️ **Refaktorować ToUConfigService:**
   - Opcja A: Dokończyć implementację (odkomentować metody)
   - Opcja B: Zmienić nazwę na LobbyService (tylko SetLobbySize)
   - Opcja C: Usunąć (wywołuj bezpośrednio LobbyUtils)

4. ⚠️ **UserInteractionService:**
   - Preferuj async metody
   - Unikaj sync wersji (blokują wątek)

5. ✅ **Dodać XML documentation comments**

---

*Dokumentacja wygenerowana: 2025-10-19*  
*Autor: GitHub Copilot AI Assistant*
