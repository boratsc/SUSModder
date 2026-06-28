# SUSModder.Core - Models, Diagnostics, Repositories i inne

## Przegląd
Ten dokument opisuje pozostałe komponenty SUSModder.Core: modele danych (Models), system diagnostyki (Diagnostics), repozytoria dostępu do danych (Repositories) oraz moduł sekretów (Secrets).

---

## Models

Moduł `Models` zawiera klasy DTO (Data Transfer Objects) używane do deserializacji odpowiedzi API oraz transferu danych między warstwami aplikacji.

### ✅ **AmongTokensResponse.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Model odpowiedzi API dla tokenów/serwerów Among Us (SUStats)  
**Analiza użycia:** Używany przez `SUStatsService` i `AmongTokensService`

**Klasy:**
```csharp
class AmongTokensResponse
{
    bool Success
    int Count
    List<AmongToken> Tokens
}

class AmongToken
{
    int Id
    string Token          // Token API SUStats
    string Secret         // Secret SUStats
    string Endpoint       // Endpoint API
    string ServerName     // Nazwa serwera
}
```

**Format JSON (przykład):**
```json
{
  "success": true,
  "count": 3,
  "tokens": [
    {
      "id": 1,
      "token": "abc123",
      "secret": "xyz789",
      "endpoint": "https://api.sustats.com",
      "server_name": "SUStats Main"
    }
  ]
}
```

**Używane przez:**
- `SUStatsService.GetSUStatsServersAsync()` - pobieranie serwerów SUStats
- `AmongTokensService.GetAmongTokensAsync()` - [NIEUŻYWANY SERWIS]

**Serializacja:** `System.Text.Json` z atrybutami `[JsonPropertyName]`

**Rekomendacja:** ✅ **ZACHOWAĆ** - Model aktywnie używany.

---

### ✅ **DiscordFavoritesResponse.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Model odpowiedzi API dla polecanych serwerów Discord  
**Analiza użycia:** Używany przez `DiscordFavoritesService`

**Klasy:**
```csharp
class DiscordFavoritesResponse
{
    bool Success
    int Count
    List<DiscordServerData> DiscordFavs
}

class DiscordServerData
{
    int Id
    string? Icon          // URL ikony
    string Link           // Link invite Discord
    string Name           // Nazwa serwera
    string Description    // Opis serwera
    bool IsActive         // Czy aktywny (filtrowanie)
}
```

**Format JSON (przykład):**
```json
{
  "success": true,
  "count": 5,
  "discordFavs": [
    {
      "id": 1,
      "icon": "https://cdn.discordapp.com/icons/...",
      "link": "https://discord.gg/xyz",
      "name": "Among Us PL",
      "description": "Polski serwer Among Us",
      "is_active": true
    }
  ]
}
```

**Używane przez:**
- `DiscordFavoritesService.GetDiscordFavoritesAsync()` - pobieranie Discordów
- `DiscordServerAdapter.FromServerDataList()` - konwersja do ViewModel

**Filtrowanie:**
- UI pokazuje tylko serwery gdzie `IsActive == true`

**Serializacja:** `System.Text.Json` z atrybutami `[JsonPropertyName]`

**Rekomendacja:** ✅ **ZACHOWAĆ** - Model aktywnie używany.

---

### ✅ **DiscordServer.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Model UI dla serwera Discord (po konwersji z `DiscordServerData`)  
**Analiza użycia:** Używany przez UI ViewModels

**Klasa:**
```csharp
class DiscordServer
{
    string Name
    string InviteLink
    string Description
    string? IconPath
}
```

**Mapowanie (przez DiscordServerAdapter):**
```
DiscordServerData → DiscordServer
─────────────────   ──────────────
Name              → Name
Link              → InviteLink
Description       → Description
Icon              → IconPath
```

**Używane przez:**
- `RecommendedDiscordsViewModel` - wyświetlanie listy
- `DiscordIconPreloader` - preloadowanie ikon

**Pattern:** DTO → ViewModel model

**Rekomendacja:** ✅ **ZACHOWAĆ** - Prosty model dla UI.

---

## Diagnostics

Moduł `Diagnostics` zapewnia abstrakcję logowania i diagnostyki stanu aplikacji.

### ✅ **IDiagnosticsOutput.cs** ✔️ [W UŻYCIU]
**Status:** Szeroko używany interfejs  
**Opis:** Interfejs abstrakcji dla wyjścia diagnostycznego  
**Analiza użycia:** 39 użyć w całym projekcie

**Definicja:**
```csharp
public interface IDiagnosticsOutput
{
    void Write(string line);
}
```

**Implementacje:**
- `UIDiagnosticsOutput` (w UI) - wypisuje do TextBox/konsoli w oknie
- `ConsoleLogger` (w UI) - deleguje do konsoli Debug/Output
- Mock implementations - dla testów

**Używane przez:**
- Wszystkie serwisy API (`AmongTokensService`, `DiscordFavoritesService`, `SUStatsService`)
- `ApiSetManager` - logowanie operacji na ApiSet.ini
- `EpicVersionManager` - logi instalacji Epic
- `ModManager` - logi instalacji Steam
- `DllModificationService` - logi instalacji DLL
- `AppUpdateService` - logi aktualizacji aplikacji
- `Diagnostics.LogModsAndPlugins` - diagnostyka stanu

**Pattern:** Dependency Inversion Principle (DIP)

**Rekomendacja:** ✅ **ZACHOWAĆ** - Fundamentalny interfejs logowania.

---

### ✅ **Diagnostics.cs** ✔️ [W UŻYCIU]
**Status:** Aktywnie używany  
**Opis:** Klasa statyczna do generowania raportów diagnostycznych  
**Analiza użycia:** Używany przez `MainWindowViewModel`

**Funkcjonalność:**
- Raportuje zainstalowane mody (z config.json)
- Lista katalogów w `ModsInstallPath`
- Niestandardowe pluginy w `BepInEx\plugins`
- Wykluczanie standardowych plików (Mini.RegionInstall.dll, Reactor.dll, etc.)

**Publiczne metody:**
```csharp
static void SetOutput(IDiagnosticsOutput output)
static void LogModsAndPlugins(string? appVersion = null)
```

**Wykluczane pliki:**
```csharp
HashSet<string> _excluded = {
    "Mini.RegionInstall.dll",
    "Reactor.dll",
    "touhats.bundle",
    "touhats.catalog"
}
```

**Format raportu:**
```
=== Zainstalowane mody (config.json) ===
Town of Us
Sheriff Mod

=== Katalogi w folderze: C:\Users\...\Among Us - Mody ===
Town of Us
Sheriff Mod
Among Us - Vanilla

=== Nie-standardowe pluginy w BepInEx\plugins ===
Town of Us\CustomPlugin.dll
Sheriff Mod\ExtraRole.dll

========================================
         SUSModder 1.0.0       
========================================
      === Koniec diagnostyki ===      
========================================
```

**Przypadki użycia:**
- Debugowanie problemów z instalacją
- Weryfikacja stanu zainstalowanych modów
- Raport dla supportu

**Rekomendacja:** ✅ **ZACHOWAĆ** - Przydatne narzędzie diagnostyczne.

---

## Repositories

Moduł `Repositories` zawiera klasy dostępu do danych (pliki konfiguracyjne, API).

### ✅ **ConfigRepository.cs** ✔️ [W UŻYCIU]
**Status:** Intensywnie używany  
**Opis:** Repozytorium do odczytu/zapisu plików konfiguracyjnych i komunikacji z API  
**Analiza użycia:** 12 użyć

**Lokalizacje użycia:**
- `ModConfig.cs` - LoadConfig/SaveConfig
- `ModConfigHandler.cs` - operacje na presetach
- `ConfigService.cs` - fasada
- `ModUpdateManager.cs` - sprawdzanie aktualizacji
- `MainWindowViewModel.cs` - inicjalizacja
- `AdditionalActionsPanel.axaml.cs` - akcje użytkownika

**Funkcjonalność:**
- Odczyt/zapis `config.json` (lista modów)
- Odczyt/zapis `appsettings.json` (ustawienia aplikacji)
- Pobieranie konfiguracji z API (fallback gdy brak lokalnego pliku)

**Zarządzane pliki:**
```
{ExeDir}/
├─ config.json         # Lista ModConfiguration
└─ appsettings.json    # Ustawienia aplikacji
```

**Publiczne metody:**
```csharp
List<ModConfiguration> LoadConfig()
void SaveConfig(List<ModConfiguration> configs)
Dictionary<string, object>? LoadAppSettings()
void SaveAppSettings(Dictionary<string, object> settings)
Task<List<ModConfiguration>?> LoadConfigFromApiAsync()
```

**Proces LoadConfigFromApiAsync:**
1. Odczytaj `UpdateServerUrl` z `appsettings.json`
2. HttpClient.GetAsync(updateServerUrl)
3. Deserializuj JSON → `List<ModConfiguration>`
4. Obsługa timeout (30s) i błędów HTTP/JSON
5. Zwróć listę lub null przy błędzie

**Endpoint API:**
- URL: `Configuration:UpdateServerUrl` z appsettings.json
- Domyślny: `https://susmodder.app/api/susmodder-config`
- Format: JSON array `ModConfiguration[]`
- Autoryzacja: BRAK (publiczne API)

**Obsługa błędów:**
- `HttpRequestException` → null
- `TaskCanceledException` (timeout) → null
- `JsonException` → null
- Wszystkie logowane do Debug output

**Pattern:** Repository Pattern

**Rekomendacja:** ✅ **ZACHOWAĆ** - Kluczowy komponent dostępu do danych.

---

## Secrets

### ✅ **Secrets.cs** (SecretProvider) ✔️ [W UŻYCIU]
**Status:** Krytyczny dla bezpieczeństwa  
**Opis:** Statyczna klasa dostarczająca wrażliwe dane (tokeny, hasła)  
**Analiza użycia:** 11 użyć w kluczowych miejscach

**Lokalizacje użycia:**
- `AmongTokensService.cs:46` - token HTTP
- `DiscordFavoritesService.cs:48` - token HTTP
- `ModConfig.cs:122` - token HTTP
- `ModConfigHandler.cs` - 3x token HTTP
- `SUStatsService.cs:46` - token HTTP
- `ModManager.cs` - 3x hasło 7z i token HTTP
- `ModUpdateChecker.cs:137` - token HTTP

**Publiczne metody:**
```csharp
static string GetDownloadToken()    // Token autoryzacji HTTP
static string Get7zPassword()       // Hasło do archiwów vanilla 7z
```

**Implementacja:**
```csharp
private static string Decrypt(string encrypted)
{
    return System.Text.Encoding.UTF8.GetString(
        Convert.FromBase64String(encrypted)
    );
}
```

**"Szyfrowanie":**
- Base64 encoding (NIE jest to prawdziwe szyfrowanie!)
- Przykład:
  ```csharp
  // Token zakodowany Base64
  "ZTRhMWM3YjJmM2Q4ZTlhMGI1YzZkN2U4ZjlhMWIyYzNkNGU1ZjZhN2I4YzlkMGUxZjJhM2I0YzVkNmU3ZjhhOQ=="
  
  // Po dekodowaniu
  "e4a1c7b2f3d8e9a0b5c6d7e8f9a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9"
  ```

**⚠️ UWAGA BEZPIECZEŃSTWA:**
- Base64 to **OBFUSCATION**, NIE szyfrowanie
- Każdy może zdekodować Base64
- Sekrety są widoczne w skompilowanym .exe (przez decompiler)
- Powinno używać się:
  - Azure Key Vault / AWS Secrets Manager (produkcja)
  - Zmienne środowiskowe (development)
  - Encrypted configuration files

**Dlaczego to rozwiązanie istnieje:**
- Prosta obfuscation dla casualnych użytkowników
- Unikanie jawnych sekretów w kodzie źródłowym (GitHub)
- Kompromis między bezpieczeństwem a prostotą

**Używane sekrety:**
1. **Download Token:**
   - Autoryzacja HTTP do API `susmodder.app`
   - Dodawany jako header: `Authorization: {token}` (bez "Bearer")
   - Używany dla:
     - Pobieranie config.json
     - Pobieranie listy Discordów
     - Pobieranie serwerów SUStats

2. **7z Password:**
   - Hasło do rozpakowywania archiwów vanilla Among Us
   - Archiwa są zabezpieczone hasłem dla ochrony plików gry
   - Używane przez: `ModManager.Extract7zWithPassword()`

**Logowanie sekretów:**
```csharp
// ❌ ZŁE - nie loguj pełnych wartości
log.Write($"Token: {token}");

// ✅ DOBRE - maskuj wartości
log.Write($"Token length: {token?.Length ?? 0}");
log.Write($"Token: {token?.Substring(0, 10)}...");
```

**Rekomendacja:** ✅ **ZACHOWAĆ z zastrzeżeniami**
- Obecna implementacja jest OK dla małego projektu
- Dla produkcji: rozważyć lepsze rozwiązanie (Azure Key Vault, etc.)
- **NIGDY** nie commituj jawnych sekretów do repo
- Dokumentuj że to obfuscation, nie security

---

## Podsumowanie analizy

### ✅ Wszystkie komponenty w użyciu:

#### Models (3 pliki):
1. **AmongTokensResponse.cs** - DTO dla tokenów SUStats
2. **DiscordFavoritesResponse.cs** - DTO dla Discordów
3. **DiscordServer.cs** - model UI

#### Diagnostics (2 pliki):
1. **IDiagnosticsOutput.cs** - interfejs logowania
2. **Diagnostics.cs** - raport diagnostyczny

#### Repositories (1 plik):
1. **ConfigRepository.cs** - dostęp do plików i API

#### Inne (1 plik):
1. **Secrets.cs** (SecretProvider) - wrażliwe dane

### Statystyki:
- **Pliki ogółem:** 7
- **Aktywne:** 7 (100%)
- **Do usunięcia:** 0 (0%)
- **Security concerns:** 1 (SecretProvider - Base64 obfuscation)

---

## Architektura dostępu do danych

```
                    UI Layer
                       │
                       ↓
        ┌──────────────┴──────────────┐
        │                             │
        ↓                             ↓
  ConfigService                  HTTP Clients
  (Fasada)                       (API Services)
        │                             │
        ↓                             ↓
  ConfigRepository              SecretProvider
        │                             │
        ├─────────────────┬───────────┘
        ↓                 ↓
    config.json      HTTP API
    appsettings.json (susmodder.app)
        │                 │
        └─────────┬───────┘
                  ↓
         ModConfiguration[]
         DiscordServerData[]
         AmongToken[]
```

---

## Format plików konfiguracyjnych

### config.json
```json
[
  {
    "Id": 1,
    "ModName": "Town of Us",
    "PngFileName": "TOU.png",
    "InstallPath": "C:\\Users\\...\\Among Us - Mody\\Town of Us",
    "GitHubRepoOrLink": "https://github.com/.../releases/download/v1.0/TOU.zip",
    "EpicGitHubRepoOrLink": null,
    "ModType": "full",
    "DllInstallPath": null,
    "ModVersion": "4.5.0",
    "LastUpdated": "2024-11-20T10:00:00Z",
    "AmongVersion": "2024.11.1",
    "Description": "Town of Us mod"
  },
  {
    "Id": 5,
    "ModName": "CustomRole",
    "ModType": "dll",
    "DllInstallPath": "BepInEx\\plugins",
    "InstallPath": null,
    ...
  }
]
```

### appsettings.json
```json
{
  "Configuration": {
    "Mode": "steam",
    "BaseUrl": "https://susmodder.app/",
    "UpdateServerUrl": "https://susmodder.app/api/susmodder-config",
    "CurrentVersion": "1.0.0",
    "ApiConfig": "/api/sustats-servers",
    "DiscordEndpoint": "/api/discord-favorites",
    "Theme": "Dark",
    "lastLaunchId": "guid-..."
  },
  "AppSettings": {
    "ModsInstallPath": "D:\\Games\\Among Us Mods",
    "DefaultModsPath": "%APPDATA%\\Among Us - Mody",
    "DeveloperMode": false
  }
}
```

---

## API Endpoints (susmodder.app)

| Endpoint | Zwraca | Autoryzacja | Używany przez |
|----------|---------|-------------|---------------|
| `/api/susmodder-config` | `ModConfiguration[]` | Token | ConfigRepository, ConfigManager |
| `/api/sustats-servers` | `AmongTokensResponse` | Token | SUStatsService |
| `/api/discord-favorites` | `DiscordFavoritesResponse` | Token | DiscordFavoritesService |
| `/api/susmodder-download-version?version={ver}` | Binary (7z) | Token | ModManager (vanilla) |

**Token:**
- Header: `Authorization: {token}`
- **BEZ** prefiksu "Bearer"
- Źródło: `SecretProvider.GetDownloadToken()`

---

## Pattern: Repository Pattern

**Zalety:**
- Abstrakcja dostępu do danych (plik vs API vs database)
- Łatwe testowanie (mock repository)
- Centralizacja logiki dostępu
- Zmiana źródła danych bez zmiany biznes logiki

**Implementacja w projekcie:**
```csharp
// Repository (abstrakcja)
public class ConfigRepository
{
    public List<ModConfiguration> LoadConfig()
    {
        // Może załadować z pliku
        if (File.Exists(configFile))
            return LoadFromFile();
        
        // Lub z API
        return LoadFromApiAsync().Result;
    }
}

// Business logic (nie wie skąd dane)
public class ConfigService
{
    private ConfigRepository _repo;
    
    public List<ModConfiguration> GetMods()
    {
        return _repo.LoadConfig();  // Nie wie czy z pliku czy API
    }
}
```

---

## Następne kroki refaktoringu

1. ⚠️ **SecretProvider - Security:**
   - Rozważyć lepsze rozwiązanie dla produkcji
   - Azure Key Vault / AWS Secrets Manager
   - Encrypted appsettings.json
   - Zmienne środowiskowe

2. ✅ **Models - Validation:**
   - Dodać Data Annotations dla walidacji
   - Required fields, Range, RegularExpression
   ```csharp
   [Required]
   [Range(4, 255)]
   public int LobbySize { get; set; }
   ```

3. ✅ **ConfigRepository - Retry Logic:**
   - Dodać retry policy dla API calls (Polly library)
   - Exponential backoff
   - Circuit breaker

4. ✅ **Diagnostics - Structured Logging:**
   - Rozważyć Serilog / NLog
   - Structured logs (JSON format)
   - Log levels (Debug, Info, Warning, Error)

5. ✅ **XML documentation comments** dla wszystkich publicznych API

---

## Wzorce projektowe w użyciu

| Pattern | Gdzie | Przykład |
|---------|-------|----------|
| **Repository** | ConfigRepository | Abstrakcja dostępu do danych |
| **DTO** | Models/* | Separacja warstw (API ↔ Business) |
| **Adapter** | DiscordServerAdapter | DiscordServerData → DiscordServer |
| **Singleton** | SecretProvider (static) | Jeden punkt dostępu do sekretów |
| **Dependency Inversion** | IDiagnosticsOutput | Core nie zależy od UI |
| **Facade** | ConfigService | Uproszczenie ConfigRepository |

---

*Dokumentacja wygenerowana: 2025-10-19*  
*Autor: GitHub Copilot AI Assistant*
