# Architektura Rozwiązania - Steam Depot Integration

**Data utworzenia:** 2025-10-28  
**Status:** Design phase  

---

## 📋 Spis Treści

1. [Overview Architektury](#overview-architektury)
2. [Komponenty Systemu](#komponenty-systemu)
3. [Przepływy Danych](#przepływy-danych)
4. [Integracja z Obecnym Kodem](#integracja-z-obecnym-kodem)
5. [Model Danych](#model-danych)
6. [Diagramy](#diagramy)

---

## Overview Architektury

### High-Level Vision

```
┌─────────────────────────────────────────────────────────────────┐
│                        SUSModder Desktop                         │
│  ┌────────────────┐  ┌────────────────┐  ┌──────────────────┐  │
│  │  MainWindow    │  │   ModManager   │  │ SteamDepotMgr   │  │
│  │   ViewModel    │──│                │──│  (NEW)          │  │
│  └────────────────┘  └────────────────┘  └──────────────────┘  │
│                             │                       │            │
└─────────────────────────────┼───────────────────────┼────────────┘
                              │                       │
                    ┌─────────┴───────┐      ┌────────▼─────────┐
                    │   config.json   │      │ DepotDownloader  │
                    │   (local)       │      │   (CLI tool)     │
                    └─────────┬───────┘      └────────┬─────────┘
                              │                       │
                    ┌─────────▼────────┐    ┌─────────▼──────────┐
                    │  SUSModder API   │    │   Steam CDN        │
                    │ (susmodder.app)  │    │ (Valve servers)    │
                    └──────────────────┘    └────────────────────┘
```

### Kluczowe Decyzje Architektoniczne

1. **Nowy Moduł:** `SteamDepotManager`
   - Enkapsulacja logiki Steam depot download
   - Wrapper dla DepotDownloader.exe
   - Analogiczny do `EpicVersionManager`

2. **Separacja Odpowiedzialności:**
   - `ModManager` - orchestration (instalacja modów)
   - `SteamDepotManager` - pobieranie vanilla ze Steam
   - `EpicVersionManager` - pobieranie vanilla z Epic (bez zmian)

3. **Backwards Compatibility:**
   - Stary mechanizm (7z) usuwany stopniowo
   - Transition period: oba systemy działają równolegle (feature flag)

4. **Manifest Source:**
   - Primary: API endpoint (`/api/steam-manifests`)
   - Fallback: GitHub raw (community repo)
   - Last resort: Hardcoded mapping w kodzie

---

## Komponenty Systemu

### 1. SteamDepotManager (NEW)

**Lokalizacja:** `SUSModder.Core/GameIntegration/SteamDepotManager.cs`

**Odpowiedzialności:**
- Wywołanie DepotDownloader.exe
- Parsowanie output (progress reporting)
- Obsługa błędów (auth, network, invalid manifest)
- Cache manifestów (lokalny)

**Public API:**

```csharp
public class SteamDepotManager
{
    private readonly IConfiguration _configuration;
    private readonly IDiagnosticsOutput _log;
    private readonly string _depotDownloaderPath;

    public SteamDepotManager(IConfiguration configuration, IDiagnosticsOutput log)
    {
        _configuration = configuration;
        _log = log;
        _depotDownloaderPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "tools",
            "DepotDownloader.exe"
        );
    }

    /// <summary>
    /// Pobiera konkretną wersję Among Us przez Steam depot
    /// AUTOMATYCZNIE używa aktywnej sesji Steam (ZERO interakcji użytkownika)
    /// </summary>
    /// <param name="manifestId">Steam Manifest ID (np. "7212344665024119693")</param>
    /// <param name="targetDirectory">Katalog docelowy</param>
    /// <param name="progress">Progress reporter (0-100)</param>
    public async Task<bool> DownloadAmongUsAsync(
        string manifestId,
        string targetDirectory,
        IProgressReporter progress
    );

    /// <summary>
    /// Pobiera Manifest ID dla danej wersji gry (np. "2024.3.5s")
    /// </summary>
    public async Task<string?> GetManifestIdForVersionAsync(string amongVersion);

    /// <summary>
    /// Sprawdza czy Steam jest uruchomiony (wymagane do automatycznej autoryzacji)
    /// </summary>
    public bool IsSteamRunning();

    /// <summary>
    /// Sprawdza czy DepotDownloader jest dostępny
    /// </summary>
    public bool IsDepotDownloaderAvailable();

    /// <summary>
    /// Weryfikuje czy manifest istnieje w Steam
    /// </summary>
    public async Task<bool> ValidateManifestAsync(string manifestId);
}
```

**Kluczowa zmiana:** Usunięto parametr `SteamCredentials` - nie jest potrzebny, używamy automatycznej autoryzacji.
```

### 2. ModManager (MODIFY)

**Zmiany w `InstallSteamAsync`:**

```csharp
// PRZED (obecny kod)
private async Task InstallSteamAsync(...)
{
    // 1. Pobierz 7z z własnego serwera
    string fileUrlAmongUs = $"{baseUrl}api/susmodder-download-version?version={vanilla7zName}";
    await DownloadFileWithMemoryManagementAsync(fileUrlAmongUs, vanilla7zPath, log);
    
    // 2. Rozpakuj z hasłem
    string zipPassword = SecretProvider.Get7zPassword();
    await Task.Run(() => Extract7zWithPassword(vanilla7zPath, modFolderPath, zipPassword));
    
    // ...
}

private async Task InstallSteamAsync(...)
{
    // Feature flag (przejściowy okres)
    bool useSteamDepot = configuration.GetValue<bool>("Features:UseSteamDepot", true);
    
    if (useSteamDepot)
    {
        // KROK 1: Sprawdź czy Steam jest uruchomiony
        var steamDepotManager = new SteamDepotManager(configuration, log);
        
        if (!steamDepotManager.IsSteamRunning())
        {
            // Pokaż komunikat użytkownikowi
            if (userCallbacks.ShowInfoAsync != null)
            {
                await userCallbacks.ShowInfoAsync(
                    "Klient Steam musi być uruchomiony do pobrania plików gry.\n\n" +
                    "Uruchom Steam i spróbuj ponownie.",
                    "Steam wymagany"
                );
            }
            return;
        }
        
        // KROK 2: Pobierz Manifest ID dla wersji gry
        string? manifestId = await steamDepotManager.GetManifestIdForVersionAsync(modConfig.AmongVersion);
        
        if (manifestId == null)
        {
            throw new InvalidOperationException($"Nie znaleziono Manifest ID dla wersji {modConfig.AmongVersion}");
        }
        
        // KROK 3: Pobierz pliki vanilla ze Steam (automatycznie używa aktywnej sesji)
        progress.Report(10, "Pobieranie vanilla z Steam...");
        bool success = await steamDepotManager.DownloadAmongUsAsync(
            manifestId,
            modFolderPath,
            progress
        );
        
        if (!success)
        {
            throw new Exception("Pobieranie vanilla z Steam depot nie powiodło się");
        }
        
        log.Write("✅ Vanilla pobrane ze Steam depot");
    }
    else
    {
        // STARY: Pobierz 7z (backwards compatibility)
        // ... (obecny kod)
    }
    
    // Reszta bez zmian (instalacja moda, kopiowanie plików)
    // ...
}
```

### 3. ModConfig (EXTEND)

**Rozszerzenie modelu:**

```csharp
public class ModConfiguration
{
    // ... (existing fields)
    
    [JsonPropertyName("AmongVersion")]
    public string AmongVersion { get; set; } = string.Empty;
    
    // ✨ NOWE POLE
    [JsonPropertyName("SteamManifestId")]
    public string? SteamManifestId { get; set; }
    
    // ✨ NOWE POLE (opcjonalnie)
    [JsonPropertyName("SteamBuildId")]
    public int? SteamBuildId { get; set; }
}
```

**Przykład konfiguracji:**

```json
{
  "Id": 0,
  "ModName": "AmongUs",
  "ModType": "Vanilla",
  "AmongVersion": "2024.3.5s",
  "SteamManifestId": "7212344665024119693",
  "SteamBuildId": 12345678,
  "GitHubRepoOrLink": "",
  "InstallPath": "C:\\Users\\...\\Among Us - Vanilla\\AmongUs"
}
```

### 4. ConfigRepository (EXTEND)

**Nowy endpoint API:**

```csharp
// SUSModder.Core/Repositories/ConfigRepository.cs

public async Task<Dictionary<string, string>> GetSteamManifestsAsync()
{
    string url = $"{baseUrl}/api/steam-manifests";
    
    var response = await httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();
    
    var json = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    
    // Format: { "2024.3.5s": "7212344665024119693", ... }
}
```

### 5. Backend API (NEW)

**Nowy endpoint:** `GET /api/steam-manifests`

```javascript
// routes/steamManifests.js
router.get('/steam-manifests', async (req, res) => {
  try {
    const manifests = await db.query(`
      SELECT among_version, steam_manifest_id, steam_build_id
      FROM mod_configs
      WHERE steam_manifest_id IS NOT NULL
    `);
    
    const result = {};
    manifests.forEach(m => {
      result[m.among_version] = {
        manifestId: m.steam_manifest_id,
        buildId: m.steam_build_id
      };
    });
    
    res.json(result);
  } catch (error) {
    res.status(500).json({ error: error.message });
  }
});
```

**Migracja bazy danych:**

```sql
-- migrations/add_steam_manifest_fields.sql
ALTER TABLE mod_configs 
ADD COLUMN steam_manifest_id VARCHAR(20),
ADD COLUMN steam_build_id INT;

-- Przykładowe dane
UPDATE mod_configs SET steam_manifest_id = '7212344665024119693', steam_build_id = 12345678 WHERE among_version = '2024.3.5s';
UPDATE mod_configs SET steam_manifest_id = '8901234567890123456', steam_build_id = 13987654 WHERE among_version = '2024.6.4s';
```

---

## Przepływy Danych

### Flow 1: Instalacja Moda (Steam)

```
┌─────────────┐
│ Użytkownik  │ Kliknij "Install" (np. Sheriff Mod wymagający 2024.3.5s)
└──────┬──────┘
       │
       ▼
┌──────────────────────────────────────────────────────────────┐
│ MainWindowViewModel.InstallModAsync()                         │
│                                                               │
│ 1. Sprawdź czy mod wymaga vanilla (ModType = "full")         │
│ 2. Wywołaj ModManager.ModifyAsync()                          │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ ModManager.InstallSteamAsync()                                │
│                                                               │
│ 1. Sprawdź feature flag: UseSteamDepot?                      │
│ 2. Tak → Wywołaj SteamDepotManager                           │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ SteamDepotManager.DownloadAmongUsAsync()                     │
│                                                               │
│ 1. GetManifestIdForVersionAsync("2024.3.5s")                │
│    ├─ Sprawdź lokalny cache (config.json)                    │
│    ├─ Fallback: API /api/steam-manifests                     │
│    └─ Fallback: GitHub raw                                   │
│                                                               │
│ 2. Manifest ID: "7212344665024119693"                        │
│                                                               │
│ 3. Wywołaj DepotDownloader.exe:                              │
│    DepotDownloader -app 945360 -depot 945361 \               │
│                    -manifest 7212344665024119693 \            │
│                    -dir "C:\...\Sheriff Mod"                  │
│                                                               │
│ 4. Parsuj output → Report progress (0-100%)                  │
│ 5. Czekaj na zakończenie                                     │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ Steam CDN                                                     │
│                                                               │
│ 1. Pobierz manifest 7212344665024119693                      │
│ 2. Pobierz pliki vanilla Among Us 2024.3.5s                  │
│ 3. Zwróć pliki → DepotDownloader zapisuje lokalnie           │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ ModManager.InstallSteamAsync() (cd.)                          │
│                                                               │
│ 1. Vanilla pobrane ✅                                         │
│ 2. Pobierz archiwum moda (GitHub)                            │
│ 3. Rozpakuj mod                                              │
│ 4. Skopiuj pliki moda do katalogu vanilla                    │
│ 5. Zapisz InstallPath do config.json                         │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ UI Update                                                     │
│                                                               │
│ - InstallProgress = 100%                                      │
│ - InstallStatusMessage = "Instalacja zakończona"             │
│ - IsInstalled = true                                          │
└──────────────────────────────────────────────────────────────┘
```

### Flow 2: Pobranie Manifest ID (Multiple Fallbacks)

```
┌────────────────────────────────────────────────────────────────┐
│ GetManifestIdForVersionAsync("2024.3.5s")                      │
└────────────────────┬───────────────────────────────────────────┘
                     │
        ┌────────────┴─────────────┐
        │                          │
        ▼                          ▼
┌──────────────────┐      ┌──────────────────┐
│ Source 1:        │      │ Sprawdź lokalnie │
│ config.json      │◄─────│ (pierwszy)       │
│                  │      └──────────────────┘
│ ModConfig:       │
│ SteamManifestId  │
└────────┬─────────┘
         │
         │ ❌ Brak lub null?
         │
         ▼
┌──────────────────┐
│ Source 2:        │
│ API              │
│ /api/steam-      │
│  manifests       │
│                  │
│ {                │
│   "2024.3.5s":   │
│   "721234..."    │
│ }                │
└────────┬─────────┘
         │
         │ ❌ API offline?
         │
         ▼
┌──────────────────┐
│ Source 3:        │
│ GitHub Raw       │
│                  │
│ github.com/      │
│ susmodder/       │
│ steam-manifests  │
└────────┬─────────┘
         │
         │ ❌ GitHub offline?
         │
         ▼
┌──────────────────┐
│ Source 4:        │
│ Hardcoded        │
│ Dictionary       │
│                  │
│ {                │
│   ["2024.3.5s"]  │
│    = "721234..." │
│ }                │
└────────┬─────────┘
         │
         │ ❌ Wersja nie znaleziona?
         │
         ▼
   ┌──────────┐
   │  ERROR   │
   │  null    │
   └──────────┘
```

### Flow 3: Autoryzacja Steam (First Time)

```
┌──────────────┐
│ Pierwsze     │ DepotDownloader wymaga logowania
│ uruchomienie │
└──────┬───────┘
       │
       ▼
┌────────────────────────────────────────────────────┐
│ UI Dialog: Steam Login                             │
│                                                     │
│ ┌─────────────────────────────────────────────┐   │
│ │ Username: [________________]                │   │
│ │ Password: [________________]                │   │
│ │                                             │   │
│ │ ☑ Zapamiętaj credentials                   │   │
│ │                                             │   │
│ │ [Zaloguj]  [Anuluj]                        │   │
│ └─────────────────────────────────────────────┘   │
└───────────────────────┬────────────────────────────┘
                        │
                        ▼
        ┌───────────────────────────────┐
        │ Zapisz lokalnie (szyfrowane)  │
        │                               │
        │ %APPDATA%/SUSModder/          │
        │   steam_credentials.dat       │
        └───────────────┬───────────────┘
                        │
                        ▼
        ┌───────────────────────────────┐
        │ DepotDownloader -username ... │
        │                 -password ... │
        │                 -remember-    │
        │                  password     │
        └───────────────┬───────────────┘
                        │
                        ▼
                ┌──────────────┐
                │ Steam Auth   │
                │ Success ✅   │
                └──────────────┘
```

---

## Integracja z Obecnym Kodem

### Porównanie: Epic vs Steam (Po Zmianach)

| Aspekt | Epic (obecnie) | Steam (nowy) |
|--------|----------------|--------------|
| **Manager Class** | `EpicVersionManager` | `SteamDepotManager` |
| **CLI Tool** | Legendary | DepotDownloader |
| **Tool Location** | `%APPDATA%\..\Legendary` | `tools\DepotDownloader.exe` |
| **Manifest Source** | GitHub (whichtwix/Data) | API + GitHub fallback |
| **Auth** | Legendary login | Steam credentials |
| **InstallPath** | Epic structure (AmongUs subfolder) | Flat structure |
| **Wywołanie** | `ModManager` → `EpicVersionManager` | `ModManager` → `SteamDepotManager` |

### Zmiany w Istniejących Plikach

#### 1. `ModManager.cs`

```diff
  private async Task InstallSteamAsync(...)
  {
+     bool useSteamDepot = configuration.GetValue<bool>("Features:UseSteamDepot", true);
+     
+     if (useSteamDepot)
+     {
+         var steamDepotManager = new SteamDepotManager(configuration, log);
+         string? manifestId = await steamDepotManager.GetManifestIdForVersionAsync(modConfig.AmongVersion);
+         
+         if (manifestId == null)
+             throw new InvalidOperationException($"Brak Manifest ID dla {modConfig.AmongVersion}");
+         
+         bool success = await steamDepotManager.DownloadAmongUsAsync(manifestId, modFolderPath, progress);
+         if (!success)
+             throw new Exception("Pobieranie vanilla nie powiodło się");
+     }
+     else
+     {
+         // STARY KOD (7z)
-         string vanilla7zName = $"{modConfig.AmongVersion.Replace("-", "").Replace(".", "")}";
-         string fileUrlAmongUs = $"{baseUrl}api/susmodder-download-version?version={vanilla7zName}";
-         await DownloadFileWithMemoryManagementAsync(fileUrlAmongUs, vanilla7zPath, log);
-         string zipPassword = SecretProvider.Get7zPassword();
-         await Task.Run(() => Extract7zWithPassword(vanilla7zPath, modFolderPath, zipPassword));
+     }
      
      // Reszta bez zmian
      // ...
  }
```

#### 2. `ModConfig.cs`

```diff
  public class ModConfiguration
  {
      // ... (existing)
      
      [JsonPropertyName("AmongVersion")]
      public string AmongVersion { get; set; } = string.Empty;
      
+     [JsonPropertyName("SteamManifestId")]
+     public string? SteamManifestId { get; set; }
+     
+     [JsonPropertyName("SteamBuildId")]
+     public int? SteamBuildId { get; set; }
  }
```

#### 3. `appsettings.json`

```diff
  {
    "Configuration": {
      "Mode": "steam",
      "BaseUrl": "https://susmodder.boracik.pl",
      // ...
    },
+   "Features": {
+     "UseSteamDepot": true
+   }
  }
```

#### 4. `SecretProvider.cs`

```diff
  public static class SecretProvider
  {
      // ...
      
-     public static string Get7zPassword()
-     {
-         return DecodeSecret("BASE64_ENCODED_PASSWORD");
-     }
      
+     // ❌ USUŃ - niepotrzebne
  }
```

### Nowe Pliki

1. **`SUSModder.Core/GameIntegration/SteamDepotManager.cs`**
   - Główna logika Steam depot download

2. **`SUSModder.Core/Models/SteamManifestData.cs`**
   - Model danych dla manifestów

3. **`SUSModder/ViewModels/SteamLoginViewModel.cs`** (opcjonalnie)
   - VM dla okna logowania Steam

4. **`SUSModder/Views/SteamLoginDialog.axaml`** (opcjonalnie)
   - UI dla okna logowania Steam

---

## Model Danych

### SteamManifestData

```csharp
// SUSModder.Core/Models/SteamManifestData.cs
namespace SUSModder.Core.Models
{
    public record SteamManifestData
    {
        [JsonPropertyName("app_id")]
        public int AppId { get; init; } = 945360;
        
        [JsonPropertyName("app_name")]
        public string AppName { get; init; } = "Among Us";
        
        [JsonPropertyName("depot_id")]
        public int DepotId { get; init; } = 945361;
        
        [JsonPropertyName("platform")]
        public string Platform { get; init; } = "windows";
        
        [JsonPropertyName("manifests")]
        public List<ManifestEntry> Manifests { get; init; } = new();
        
        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; init; }
    }
    
    public record ManifestEntry
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;
        
        [JsonPropertyName("release_date")]
        public DateTime ReleaseDate { get; init; }
        
        [JsonPropertyName("manifest_id")]
        public string ManifestId { get; init; } = string.Empty;
        
        [JsonPropertyName("build_id")]
        public int BuildId { get; init; }
        
        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; init; }
        
        [JsonPropertyName("files_count")]
        public int FilesCount { get; init; }
    }
}
```

### SteamCredentials

```csharp
// SUSModder.Core/Models/SteamCredentials.cs
namespace SUSModder.Core.Models
{
    public record SteamCredentials
    {
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public bool RememberPassword { get; init; }
        public DateTime? LastLogin { get; init; }
    }
}
```

---

## Diagramy

### Class Diagram (Nowe Komponenty)

```
┌──────────────────────────────────────────────────────────────┐
│                      ModManager                              │
├──────────────────────────────────────────────────────────────┤
│ - configuration: IConfiguration                              │
│ - log: IDiagnosticsOutput                                    │
├──────────────────────────────────────────────────────────────┤
│ + ModifyAsync(...)                                           │
│ - InstallSteamAsync(...)                                     │
│ - InstallEpicAsync(...)  ← Bez zmian                         │
└───────────────────────┬──────────────────────────────────────┘
                        │ uses
        ┌───────────────┼───────────────┐
        │               │               │
        ▼               ▼               ▼
┌──────────────┐ ┌─────────────────┐ ┌──────────────────┐
│ EpicVersion  │ │ SteamDepotMgr  │ │ ModConfiguration │
│ Manager      │ │ (NEW)           │ │                  │
├──────────────┤ ├─────────────────┤ ├──────────────────┤
│ Legendary    │ │ DepotDownloader │ │ + AmongVersion   │
│ integration  │ │ integration     │ │ + SteamManifest  │
│              │ │                 │ │   Id (NEW)       │
└──────────────┘ └────────┬────────┘ └──────────────────┘
                          │
                          │ executes
                          ▼
                 ┌─────────────────┐
                 │ DepotDownloader │
                 │ (External Tool) │
                 └─────────────────┘
```

### Sequence Diagram: Instalacja Moda

```
User          MainWindow    ModManager   SteamDepot   DepotDL   Steam
 │               │             │            │           │        │
 │─Install Mod──>│             │            │           │        │
 │               │─ModifyAsync>│            │           │        │
 │               │             │─GetManifest>           │        │
 │               │             │            │─API Call─>│        │
 │               │             │            │<─ManifestID        │
 │               │             │<───────────│           │        │
 │               │             │            │           │        │
 │               │             │─DownloadAmongUs──────>│        │
 │               │             │            │─Process.Start────>│
 │               │             │            │           │─Download>
 │               │             │            │<─Progress─│        │
 │               │<─Report(50%)│            │           │        │
 │<─UI Update────│             │            │           │        │
 │               │             │            │<─Complete─│        │
 │               │             │<───────────│           │        │
 │               │<─Complete───│            │           │        │
 │<─Installed────│             │            │           │        │
```

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
