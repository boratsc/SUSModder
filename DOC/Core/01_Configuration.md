# SUSModder.Core - Configuration

## Przegląd
Moduł `Configuration` zawiera klasy odpowiedzialne za zarządzanie konfiguracją aplikacji, pobieranie danych z API, zarządzanie ustawieniami modów i konfiguracji gry Among Us.

## Struktura plików

### ✅ **ApiSetManager.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany w `MainWindowViewModel`  
**Opis:** Manager do zarządzania plikami konfiguracyjnymi ApiSet.ini dla SUStats  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:2706` - zapis pliku ApiSet
- `MainWindowViewModel.cs:2767` - zapis pliku ApiSet

**Funkcjonalność:**
- Zapisuje plik `ApiSet.ini` z konfiguracją SUStats
- Waliduje zawartość pliku ApiSet.ini
- Parsuje istniejące pliki ApiSet.ini
- Tworzy katalogi jeśli nie istnieją
- Obsługuje błędy uprawnień (UnauthorizedAccessException)

**Publiczne metody:**
```csharp
static bool SaveApiSetFile(string filePath, string token, string endpoint, 
                          string secret, IDiagnosticsOutput? diagnosticsOutput)
static bool ValidateApiSetFile(string filePath, IDiagnosticsOutput? diagnosticsOutput)
static Dictionary<string, string>? ParseApiSetFile(string filePath, IDiagnosticsOutput? diagnosticsOutput)
```

**Format pliku ApiSet.ini:**
```ini
EnableApiExport=true
ApiToken={token}
ApiEndpoint={endpoint}
SaveLocalBackup=true
Secret={secret}
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Aktywnie używany do konfiguracji SUStats.

### ✅ **DeveloperModeSettings.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Statyczna klasa do zarządzania trybem deweloperskim aplikacji  
**Analiza użycia:** 5 użyć

**Lokalizacje użycia:**
- `AppSettingsViewModel.cs:131` - sprawdzenie stanu
- `AppSettingsViewModel.cs:276` - ustawienie trybu
- `MainWindowViewModel.cs:60` - inicjalizacja
- `MainWindowViewModel.cs:2463` - warunek
- `MainWindowViewModel.cs:2471` - warunek

**Funkcjonalność:**
- Odczytuje/zapisuje ustawienie `DeveloperMode` z `appsettings.json`
- Cache'uje stan dla wydajności
- Umożliwia odświeżenie ustawień (`RefreshSettings`)
- Programowe ustawianie trybu dewelopera

**Publiczne właściwości/metody:**
```csharp
static bool IsEnabled { get; }
static void RefreshSettings()
static void SetDeveloperMode(bool enabled)
```

**Ścieżka konfiguracji:**
```json
{
  "AppSettings": {
    "DeveloperMode": true/false
  }
}
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Używany do włączania funkcji developerskich w UI.

---

### ✅ **DiscordFavoritesService.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Serwis do pobierania listy polecanych serwerów Discord z API  
**Analiza użycia:** 3 użycia

**Lokalizacje użycia:**
- `DiscordIconPreloader.cs:40` - preload ikon Discordów
- `RecommendedDiscordsViewModel.cs:88` - ładowanie listy serwerów

**Funkcjonalność:**
- Pobiera listę serwerów Discord z API
- Filtruje tylko aktywne serwery (`IsActive=true`)
- Używa autoryzacji przez token
- Deserializuje odpowiedź do `DiscordServerData`
- Obsługuje timeout i błędy HTTP
- Implementuje `IDisposable`

**Publiczne metody:**
```csharp
Task<List<DiscordServerData>> GetDiscordFavoritesAsync()
```

**Endpoint API:**
- Klucz konfiguracji: `Configuration:BaseUrl` + `Configuration:DiscordEndpoint`
- Autoryzacja: Token z `SecretProvider.GetDownloadToken()` (bez prefiksu "Bearer")
- Timeout: 30 sekund

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy dla funkcji polecanych Discordów.

---

### ✅ **DiscordServerAdapter.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Adapter konwertujący `DiscordServerData` (z API) na `DiscordServer` (model UI)  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `DiscordIconPreloader.cs:42` - konwersja listy
- `RecommendedDiscordsViewModel.cs:90` - konwersja listy

**Funkcjonalność:**
- Mapuje `DiscordServerData` → `DiscordServer`
- Konwertuje listę serwerów
- Pattern: Adapter

**Publiczne metody:**
```csharp
static DiscordServer FromServerData(DiscordServerData serverData)
static List<DiscordServer> FromServerDataList(List<DiscordServerData> serverDataList)
```

**Mapowanie:**
```
DiscordServerData.Name → DiscordServer.Name
DiscordServerData.Link → DiscordServer.InviteLink
DiscordServerData.Description → DiscordServer.Description
DiscordServerData.Icon → DiscordServer.IconPath
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Prosty ale ważny adapter między warstwami.

---

### ✅ **ModConfig.cs** ✔️ [W UŻYCIU]
**Status:** Główny model konfiguracji modów + ConfigManager  
**Opis:** Definicja modelu `ModConfiguration` oraz statyczny `ConfigManager` do zarządzania konfiguracją  
**Analiza użycia:** Szeroko używany w całym projekcie

**Klasa `ModConfiguration`:**
- Implementuje `INotifyPropertyChanged` (dla bindingu UI)
- Właściwość `IsSelected` z powiadomieniem
- JSON serializacja przez atrybuty `[JsonPropertyName]`

**Właściwości modelu:**
```csharp
int Id
string ModName
string PngFileName
string? InstallPath
string GitHubRepoOrLink
string? EpicGitHubRepoOrLink
string ModType          // "full" | "dll" | "Vanilla"
string? DllInstallPath
string ModVersion
DateTime? LastUpdated
string AmongVersion
string Description
```

**Klasa `ConfigManager` (statyczna):**

**Publiczne metody:**
```csharp
static List<ModConfiguration> LoadConfig()
static void SaveConfig(List<ModConfiguration> configurations)
static string GetMode()
static void SetMode(string mode)
static string GetCurrentVersion()
static void SetCurrentVersion(string version)
```

**Logika LoadConfig:**
1. Próbuje załadować lokalny `config.json` (przez `ConfigRepository`)
2. Jeśli nie istnieje → pobiera z API
3. Zapisuje pobraną konfigurację lokalnie
4. Zwraca listę konfiguracji

**API Fallback:**
- Używa `SecretProvider.GetDownloadToken()` dla autoryzacji
- URL z `appsettings.json`: `Configuration:UpdateServerUrl`
- Domyślny URL: `https://susmodder.app/api/susmodder-config`

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy model i manager aplikacji.

---

### ✅ **ModConfigHandler.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Handler do zarządzania zapisywaniem/wczytywaniem konfiguracji gry Among Us (presets)  
**Analiza użycia:** 8 użyć

**Lokalizacje użycia:**
- `MainWindowViewModel.cs:193` - inicjalizacja
- `AdditionalActionsPanel.axaml.cs` - 7 wywołań różnych metod

**Funkcjonalność:**
- Zapisuje lokalne konfiguracje gry do ZIP
- Wczytuje konfiguracje z ZIP
- Wysyła konfiguracje na serwer
- Pobiera konfiguracje z serwera (przez hash)
- Zarządzanie folderem `mira_presets`
- Zmiana nazw presetów

**Ważne ścieżki:**
- Źródło: `%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us`
- Docelowe: `{ModsInstallPath}\Konfiguracje`
- Pliki: `*.txt` + folder `mira_presets`

**Publiczne metody:**
```csharp
static void Initialize(ConfigRepository configRepository, IUserInteraction userInteraction)
static void SaveLocalConfig(string? configName = null)
static void SaveLocalConfigWithName(string? configName)
static void LoadLocalConfig(string? selectedFilePath = null)
static Task<string> SaveServerConfigAsync()
static Task<bool> LoadServerConfigAsync(string hash)
static List<string> GetAvailableConfigs()
static List<PresetFileInfo> GetAvailablePresetsWithNames()
static void DeleteConfig(string filePath)
static void ChangePresetNames(string zipFilePath, Dictionary<string, string> nameChanges)
```

**Format pliku ZIP:**
```
Konfiguracja.zip
├── *.txt (pliki konfiguracyjne)
└── mira_presets/ (folder z presetami)
```

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowa funkcjonalność zarządzania konfiguracjami gry.

---

### ✅ **SUStatsService.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Serwis do pobierania listy serwerów SUStats z API  
**Analiza użycia:** 2 użycia

**Lokalizacje użycia:**
- `SUStatsConfigViewModel.cs:336` - pobieranie serwerów

**Funkcjonalność:**
- Pobiera listę serwerów SUStats z API
- Deserializuje do `List<AmongToken>`
- Używa autoryzacji przez token
- Implementuje `IDisposable`

**Publiczne metody:**
```csharp
Task<List<AmongToken>> GetSUStatsServersAsync()
```

**Endpoint API:**
- Klucz: `Configuration:BaseUrl` + `Configuration:ApiConfig`
- Autoryzacja: Token z `SecretProvider.GetDownloadToken()`
- Timeout: 30 sekund

**Rekomendacja:** ✅ **ZACHOWAĆ** - Używany do konfiguracji SUStats.

---

## Podsumowanie analizy

### ✅ Klasy w aktywnym użyciu:
1. **ApiSetManager.cs** - zarządzanie ApiSet.ini dla SUStats
2. **DeveloperModeSettings.cs** - tryb deweloperski
3. **DiscordFavoritesService.cs** - pobieranie serwerów Discord
4. **DiscordServerAdapter.cs** - adapter Discord modeli
5. **ModConfig.cs** - główny model + ConfigManager
6. **ModConfigHandler.cs** - zarządzanie presetami gry
7. **SUStatsService.cs** - pobieranie serwerów SUStats

### Statystyki:
- **Pliki ogółem:** 7
- **Aktywne:** 7 (100%)
- **Do usunięcia:** 0

---

## Architektura i zależności

```
Configuration/
│
├─ API Services (HTTP)
│  ├─ DiscordFavoritesService → API Discord
│  └─ SUStatsService → API SUStats
│
├─ Models & Managers
│  ├─ ModConfig.cs
│  │  ├─ ModConfiguration (model)
│  │  └─ ConfigManager (static)
│  └─ ModConfigHandler (game presets)
│
├─ Utilities
│  ├─ ApiSetManager (SUStats INI)
│  ├─ DeveloperModeSettings
│  └─ DiscordServerAdapter
│
└─ (brak nieużywanych plików)
```

### Zależności zewnętrzne:
- `Microsoft.Extensions.Configuration` - odczyt appsettings.json
- `System.Net.Http` - komunikacja z API
- `System.Text.Json` / `Newtonsoft.Json` - serializacja
- `SUSModder.Core.Diagnostics` - logowanie
- `SUSModder.Core.Repositories` - ConfigRepository
- `SUSModder.Core.Utilities` - PathSettings, IUserInteraction
- `SecretProvider` - tokeny autoryzacji

---

## Następne kroki refaktoringu

1. ⚠️ Rozważyć dodanie interfejsów dla serwisów API (IDiscordService, ISUStatsService)
2. ⚠️ Rozważyć wydzielenie ConfigManager do osobnego pliku (obecnie w ModConfig.cs)

---

*Dokumentacja wygenerowana: 2025-10-19*  
*Autor: GitHub Copilot AI Assistant*
