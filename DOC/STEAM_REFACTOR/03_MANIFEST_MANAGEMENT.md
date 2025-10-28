# Manifest Management - Zarządzanie Mappingami Steam Manifest ID

**Data utworzenia:** 2025-10-28  
**Status:** Design phase  

---

## 📋 Spis Treści

1. [Problem: Mapowanie Wersji](#problem-mapowanie-wersji)
2. [Strategie Storage](#strategie-storage)
3. [Hybrydowe Rozwiązanie (Rekomendowane)](#hybrydowe-rozwiązanie-rekomendowane)
4. [Aktualizacja Mappingów](#aktualizacja-mappingów)
5. [Implementacja](#implementacja)

---

## Problem: Mapowanie Wersji

### Co Musimy Mapować?

Potrzebujemy przypisać **każdej wersji Among Us** odpowiadający jej **Steam Manifest ID**:

```
AmongVersion (string)  →  SteamManifestId (string)
──────────────────────────────────────────────────
"2024.3.5s"           →  "7212344665024119693"
"2024.6.4s"           →  "8901234567890123456"
"2024.8.13s"          →  "9012345678901234567"
"2024.10.29s"         →  "1023456789012345678"
```

### Wyzwania

1. **Zmienność** - nowe wersje gry co kilka tygodni
2. **Odkrywanie** - Manifest ID nie jest publicznie dostępny (wymaga research)
3. **Wiarygodność** - błędny Manifest ID = pobrana zła wersja gry
4. **Dostępność** - musi działać offline (fallback)
5. **Performance** - szybkie lookup bez opóźnień

---

## Strategie Storage

### Opcja 1: Tylko Backend API ❌

**Struktura:**
```
Backend API endpoint: GET /api/steam-manifests
{
  "2024.3.5s": { "manifestId": "7212344665024119693", "buildId": 12345678 },
  "2024.6.4s": { "manifestId": "8901234567890123456", "buildId": 13987654 }
}
```

**Zalety:**
- ✅ Centralna kontrola
- ✅ Łatwa aktualizacja (bez release aplikacji)
- ✅ Metrics (widzimy jakie wersje są pobierane)

**Wady:**
- ❌ Single point of failure (API offline = brak instalacji)
- ❌ Wymaga połączenia internetowego zawsze
- ❌ Latency przy każdym lookup

### Opcja 2: Tylko GitHub Raw ❌

**Struktura:**
```
GitHub: susmodder/steam-manifests
├── manifests/
│   └── among_us.json
```

**`among_us.json`:**
```json
{
  "manifests": [
    { "version": "2024.3.5s", "manifestId": "7212344665024119693" },
    { "version": "2024.6.4s", "manifestId": "8901234567890123456" }
  ]
}
```

**Zalety:**
- ✅ Version control (Git history)
- ✅ Community contributions (Pull Requests)
- ✅ Free hosting

**Wady:**
- ❌ GitHub outage = brak instalacji
- ❌ Rate limiting (60 req/hour dla anonymous)
- ❌ Latency (pobieranie pliku przy każdym lookup)

### Opcja 3: Tylko config.json (lokalnie) ❌

**Struktura:**
```json
{
  "Id": 1,
  "ModName": "Sheriff Mod",
  "AmongVersion": "2024.3.5s",
  "SteamManifestId": "7212344665024119693"
}
```

**Zalety:**
- ✅ Offline support
- ✅ Zero latency
- ✅ Brak external dependencies

**Wady:**
- ❌ Wymaga update aplikacji dla nowych wersji
- ❌ Brak centralnej kontroli
- ❌ Trudna aktualizacja (użytkownik musi zaktualizować SUSModder)

### Opcja 4: Tylko Hardcoded Dictionary ❌

**Struktura:**
```csharp
public static class SteamManifests
{
    public static readonly Dictionary<string, string> Mapping = new()
    {
        ["2024.3.5s"] = "7212344665024119693",
        ["2024.6.4s"] = "8901234567890123456",
        ["2024.8.13s"] = "9012345678901234567"
    };
}
```

**Zalety:**
- ✅ Offline
- ✅ Najszybsze (compile-time)
- ✅ Zero external calls

**Wady:**
- ❌ Wymaga recompile dla każdej nowej wersji gry
- ❌ Najgorszy maintenance burden

---

## Hybrydowe Rozwiązanie (Rekomendowane) ⭐

### Koncepcja: Cascading Fallbacks

```
Lookup Manifest ID dla "2024.3.5s"
│
├─► 1. Sprawdź config.json (lokalny cache)
│   └─► Znaleziono? → RETURN ✅
│
├─► 2. Sprawdź API /api/steam-manifests
│   ├─► Success? → Zapisz do cache → RETURN ✅
│   └─► Offline/Error? → Next fallback
│
├─► 3. Sprawdź GitHub Raw
│   ├─► Success? → Zapisz do cache → RETURN ✅
│   └─► Offline/Error? → Next fallback
│
└─► 4. Hardcoded Dictionary (ostateczny fallback)
    ├─► Znaleziono? → RETURN ✅
    └─► Nie znaleziono? → ERROR ❌
```

### Implementacja Strategii

#### Layer 1: config.json (Local Cache)

```json
// config.json
[
  {
    "Id": 0,
    "ModName": "AmongUs",
    "ModType": "Vanilla",
    "AmongVersion": "2024.3.5s",
    "SteamManifestId": "7212344665024119693",  // ← Cache
    "LastUpdated": "2024-03-05T12:00:00Z"
  },
  {
    "Id": 1,
    "ModName": "Sheriff Mod",
    "AmongVersion": "2024.3.5s",
    "SteamManifestId": "7212344665024119693"
  }
]
```

**Kiedy aktualizowane:**
- Po pierwszym pobraniu z API/GitHub
- Automatycznie przy aktualizacji config.json z API

#### Layer 2: Backend API (Primary Source)

**Endpoint:** `GET /api/steam-manifests`

**Response:**
```json
{
  "2024.3.5s": {
    "manifestId": "7212344665024119693",
    "buildId": 12345678,
    "releaseDate": "2024-03-05",
    "sizeBytes": 1073741824,
    "verified": true
  },
  "2024.6.4s": {
    "manifestId": "8901234567890123456",
    "buildId": 13987654,
    "releaseDate": "2024-06-04",
    "sizeBytes": 1152921504,
    "verified": true
  }
}
```

**Backend (Node.js/Express):**
```javascript
// routes/steamManifests.js
router.get('/steam-manifests', async (req, res) => {
  try {
    // Pobierz z bazy danych
    const manifests = await db.query(`
      SELECT 
        among_version,
        steam_manifest_id,
        steam_build_id,
        release_date,
        size_bytes,
        verified
      FROM steam_manifests
      ORDER BY release_date DESC
    `);
    
    // Format do dictionary
    const result = {};
    manifests.rows.forEach(m => {
      result[m.among_version] = {
        manifestId: m.steam_manifest_id,
        buildId: m.steam_build_id,
        releaseDate: m.release_date,
        sizeBytes: m.size_bytes,
        verified: m.verified
      };
    });
    
    res.json(result);
  } catch (error) {
    res.status(500).json({ error: error.message });
  }
});
```

**Baza danych:**
```sql
CREATE TABLE steam_manifests (
  id SERIAL PRIMARY KEY,
  among_version VARCHAR(20) NOT NULL UNIQUE,
  steam_manifest_id VARCHAR(20) NOT NULL,
  steam_build_id INT,
  release_date DATE,
  size_bytes BIGINT,
  verified BOOLEAN DEFAULT false,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);

-- Przykładowe dane
INSERT INTO steam_manifests (among_version, steam_manifest_id, steam_build_id, release_date, size_bytes, verified)
VALUES 
  ('2024.3.5s', '7212344665024119693', 12345678, '2024-03-05', 1073741824, true),
  ('2024.6.4s', '8901234567890123456', 13987654, '2024-06-04', 1152921504, true),
  ('2024.8.13s', '9012345678901234567', 14523456, '2024-08-13', 1288490188, true);
```

#### Layer 3: GitHub Raw (Community Backup)

**Repo:** `github.com/susmodder/steam-manifests`

**Struktura:**
```
steam-manifests/
├── README.md
├── manifests/
│   └── among_us.json
└── tools/
    └── update_manifest.py  # Skrypt do dodawania nowych wersji
```

**`among_us.json`:**
```json
{
  "app_id": 945360,
  "app_name": "Among Us",
  "depot_id": 945361,
  "platform": "windows",
  "last_updated": "2024-10-28T12:00:00Z",
  "manifests": [
    {
      "version": "2024.8.13s",
      "manifest_id": "9012345678901234567",
      "build_id": 14523456,
      "release_date": "2024-08-13",
      "size_bytes": 1288490188,
      "files_count": 512,
      "verified": true,
      "verified_by": "boratsc",
      "notes": "Patch 2024.8.13 - Halloween update"
    },
    {
      "version": "2024.6.4s",
      "manifest_id": "8901234567890123456",
      "build_id": 13987654,
      "release_date": "2024-06-04",
      "size_bytes": 1152921504,
      "files_count": 498,
      "verified": true,
      "verified_by": "boratsc",
      "notes": "Patch 2024.6.4 - Summer update"
    },
    {
      "version": "2024.3.5s",
      "manifest_id": "7212344665024119693",
      "build_id": 12345678,
      "release_date": "2024-03-05",
      "size_bytes": 1073741824,
      "files_count": 485,
      "verified": true,
      "verified_by": "boratsc",
      "notes": "Patch 2024.3.5 - Spring update"
    }
  ]
}
```

**URL dla raw file:**
```
https://raw.githubusercontent.com/susmodder/steam-manifests/main/manifests/among_us.json
```

#### Layer 4: Hardcoded Dictionary (Last Resort)

```csharp
// SUSModder.Core/GameIntegration/SteamManifests.cs
namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Hardcoded mapping dla najpopularniejszych wersji Among Us
    /// LAST RESORT fallback gdy API, GitHub i cache są niedostępne
    /// </summary>
    public static class SteamManifests
    {
        public static readonly Dictionary<string, string> KnownManifests = new()
        {
            // 2024 releases
            ["2024.10.29s"] = "1023456789012345678", // Latest (przykład)
            ["2024.8.13s"] = "9012345678901234567",
            ["2024.6.4s"] = "8901234567890123456",
            ["2024.3.5s"] = "7212344665024119693",
            
            // 2023 releases
            ["2023.11.28s"] = "6234567890123456789",
            ["2023.7.11s"] = "5345678901234567890",
            
            // Najstarsze wspierane
            ["2023.3.28s"] = "4456789012345678901"
        };
        
        public static string? GetManifestId(string amongVersion)
        {
            return KnownManifests.TryGetValue(amongVersion, out var manifestId) 
                ? manifestId 
                : null;
        }
    }
}
```

---

## Aktualizacja Mappingów

### Proces Dodawania Nowej Wersji

```
Nowa wersja Among Us (np. 2024.11.5s)
│
├─► 1. Research Manifest ID
│   ├─► SteamDB: https://steamdb.info/app/945360/depots/
│   ├─► DepotDownloader -app 945360 -depot 945361 -info
│   └─► Community reports
│
├─► 2. Weryfikacja
│   └─► Pobierz testowo: DepotDownloader -manifest {ID} -dir "test"
│       └─► Sprawdź wersję gry w pliku
│
├─► 3. Aktualizacja Backend (Priority)
│   └─► INSERT INTO steam_manifests (...) VALUES (...)
│
├─► 4. Aktualizacja GitHub (Backup)
│   └─► PR do repo susmodder/steam-manifests
│
├─► 5. Aktualizacja Hardcoded (Opcjonalnie)
│   └─► Update SteamManifests.cs w następnym release SUSModder
│
└─► 6. Update config.json Template
    └─► API automatycznie zwróci nowy mapping przy następnym fetch
```

### Timeline dla Nowej Wersji

```
T+0h:   Innersloth release nowej wersji Among Us
T+1h:   SteamDB aktualizuje dane
T+2h:   Research Manifest ID (manual/automated)
T+3h:   Weryfikacja poprawności
T+4h:   Update backend DB
T+4h:   GitHub PR (community może pomóc)
T+8h:   SUSModder automatycznie pobiera nowy mapping z API ✅
```

**Kluczowe:** Użytkownicy nie muszą czekać na update aplikacji - wystarczy restart (refresh config z API).

---

## Implementacja

### SteamDepotManager.GetManifestIdForVersionAsync()

```csharp
public async Task<string?> GetManifestIdForVersionAsync(string amongVersion)
{
    // LAYER 1: Local cache (config.json)
    var localManifest = await TryGetFromLocalCacheAsync(amongVersion);
    if (localManifest != null)
    {
        _log.Write($"✅ Manifest ID dla {amongVersion} znaleziony w cache: {localManifest}");
        return localManifest;
    }

    // LAYER 2: Backend API
    try
    {
        var apiManifest = await TryGetFromApiAsync(amongVersion);
        if (apiManifest != null)
        {
            _log.Write($"✅ Manifest ID dla {amongVersion} pobrany z API: {apiManifest}");
            
            // Zapisz do cache
            await SaveToLocalCacheAsync(amongVersion, apiManifest);
            
            return apiManifest;
        }
    }
    catch (Exception ex)
    {
        _log.Write($"⚠️ API niedostępne: {ex.Message}");
    }

    // LAYER 3: GitHub Raw
    try
    {
        var githubManifest = await TryGetFromGitHubAsync(amongVersion);
        if (githubManifest != null)
        {
            _log.Write($"✅ Manifest ID dla {amongVersion} pobrany z GitHub: {githubManifest}");
            
            // Zapisz do cache
            await SaveToLocalCacheAsync(amongVersion, githubManifest);
            
            return githubManifest;
        }
    }
    catch (Exception ex)
    {
        _log.Write($"⚠️ GitHub niedostępny: {ex.Message}");
    }

    // LAYER 4: Hardcoded fallback
    var hardcodedManifest = SteamManifests.GetManifestId(amongVersion);
    if (hardcodedManifest != null)
    {
        _log.Write($"✅ Manifest ID dla {amongVersion} z hardcoded fallback: {hardcodedManifest}");
        return hardcodedManifest;
    }

    // Wszystkie źródła zawilodły
    _log.Write($"❌ Nie znaleziono Manifest ID dla wersji {amongVersion}");
    return null;
}

private async Task<string?> TryGetFromLocalCacheAsync(string amongVersion)
{
    // Sprawdź config.json
    var configs = await ConfigManager.LoadConfigAsync();
    var vanilla = configs.FirstOrDefault(c => c.AmongVersion == amongVersion && !string.IsNullOrEmpty(c.SteamManifestId));
    return vanilla?.SteamManifestId;
}

private async Task<string?> TryGetFromApiAsync(string amongVersion)
{
    string baseUrl = _configuration["Configuration:BaseUrl"] ?? "https://susmodder.boracik.pl";
    string url = $"{baseUrl}/api/steam-manifests";
    
    var response = await _httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();
    
    var json = await response.Content.ReadAsStringAsync();
    var manifests = JsonSerializer.Deserialize<Dictionary<string, SteamManifestInfo>>(json);
    
    return manifests?.TryGetValue(amongVersion, out var info) == true ? info.ManifestId : null;
}

private async Task<string?> TryGetFromGitHubAsync(string amongVersion)
{
    string url = "https://raw.githubusercontent.com/susmodder/steam-manifests/main/manifests/among_us.json";
    
    var response = await _httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();
    
    var json = await response.Content.ReadAsStringAsync();
    var data = JsonSerializer.Deserialize<GitHubManifestData>(json);
    
    var manifest = data?.Manifests.FirstOrDefault(m => m.Version == amongVersion);
    return manifest?.ManifestId;
}

private async Task SaveToLocalCacheAsync(string amongVersion, string manifestId)
{
    // Aktualizuj config.json z nowym Manifest ID
    var configs = await ConfigManager.LoadConfigAsync();
    var matchingConfigs = configs.Where(c => c.AmongVersion == amongVersion).ToList();
    
    foreach (var config in matchingConfigs)
    {
        config.SteamManifestId = manifestId;
    }
    
    await ConfigManager.SaveConfigAsync(configs);
}
```

### Performance Optimization

**Caching:**
```csharp
// Cache w pamięci (in-memory) dla szybszych lookup
private static Dictionary<string, string>? _manifestCache;
private static DateTime _cacheExpiry = DateTime.MinValue;
private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

public async Task<string?> GetManifestIdForVersionAsync(string amongVersion)
{
    // Sprawdź in-memory cache
    if (_manifestCache != null && DateTime.Now < _cacheExpiry)
    {
        if (_manifestCache.TryGetValue(amongVersion, out var cached))
        {
            return cached;
        }
    }

    // Pobierz z warstw (jak wyżej)
    var manifestId = await GetFromLayersAsync(amongVersion);
    
    // Zapisz do in-memory cache
    if (manifestId != null)
    {
        _manifestCache ??= new Dictionary<string, string>();
        _manifestCache[amongVersion] = manifestId;
        _cacheExpiry = DateTime.Now.Add(CacheLifetime);
    }
    
    return manifestId;
}
```

---

## Podsumowanie

### Wybrane Rozwiązanie: Hybrydowe ⭐

**Kolejność fallbacków:**
1. config.json (local cache) - najszybszy, offline
2. API /api/steam-manifests - zawsze aktualne, kontrola centralna
3. GitHub raw - community backup, version control
4. Hardcoded dictionary - ostateczny fallback dla popularnych wersji

**Zalety:**
- ✅ Offline support (cache + hardcoded)
- ✅ Zawsze aktualne (API)
- ✅ Resilient (multiple fallbacks)
- ✅ Performance (in-memory + local cache)
- ✅ Community-driven (GitHub PRs)

**Maintenance:**
- Nowa wersja gry → Update DB → API automatycznie zwraca
- Użytkownicy nie muszą czekać na update aplikacji
- GitHub jako backup dla długoterminowej dostępności

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
