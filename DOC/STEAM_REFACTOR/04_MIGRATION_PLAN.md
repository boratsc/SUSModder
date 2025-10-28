# Migration Strategy - Przejście z 7z na Steam Depot

**Data utworzenia:** 2025-10-28  
**Status:** Migration plan  
**Target version:** SUSModder 2.1.0  

---

## 📋 Spis Treści

1. [Overview](#overview)
2. [Fazy Migracji](#fazy-migracji)
3. [Backwards Compatibility](#backwards-compatibility)
4. [Rollback Plan](#rollback-plan)
5. [Timeline](#timeline)
6. [Testing Strategy](#testing-strategy)
7. [User Communication](#user-communication)

---

## Overview

### Cel Migracji

Zastąpienie obecnego mechanizmu pobierania vanilla Among Us (zaszyfrowane archiwa 7z z własnego serwera) na legalny system pobierania bezpośrednio ze Steam CDN.

### Zakres Zmian

**Backend:**
- ❌ Usunięcie endpoint `/api/susmodder-download-version`
- ➕ Dodanie endpoint `/api/steam-manifests`
- ➕ Dodanie tabeli `steam_manifests` w bazie danych
- ❌ Usunięcie zaszyfrowanych archiwów 7z z serwera

**Frontend (Desktop App):**
- ➕ Nowy moduł `SteamDepotManager.cs`
- 🔧 Modyfikacja `ModManager.InstallSteamAsync()`
- 🔧 Rozszerzenie `ModConfig.cs` (pole `SteamManifestId`)
- ❌ Usunięcie `SecretProvider.Get7zPassword()`
- ➕ Dołączenie `DepotDownloader.exe` do `tools/`
- 🔧 UI: Komunikat "Uruchom Steam" jeśli Steam nie jest aktywny

**Infrastruktura:**
- ❌ Usunięcie archiwów 7z (~10-15 GB)
- ➕ GitHub repo `susmodder/steam-manifests`

---

## Fazy Migracji

### FAZA 0: Preparation (1-2 dni)

**Cel:** Przygotowanie infrastruktury i research

#### Zadania

**0.1. Research Manifest IDs (4h)**

Zidentyfikuj Manifest ID dla wszystkich obecnie wspieranych wersji Among Us:

```bash
# Dla każdej wersji w config.json
DepotDownloader -app 945360 -depot 945361 -info > manifests_list.txt

# Lub ręcznie ze SteamDB
https://steamdb.info/depot/945361/manifests/
```

**Przykładowy output:**
```
Wersja     → Manifest ID (do wypełnienia)
──────────────────────────────────────────
2024.10.29s → [TBD - research]
2024.8.13s  → [TBD - research]
2024.6.4s   → [TBD - research]
2024.3.5s   → [TBD - research]
2023.11.28s → [TBD - research]
```

**Weryfikacja:** Pobierz testowo każdy manifest i sprawdź wersję w grze.

**0.2. Stwórz GitHub Repo (1h)**

```bash
# Utwórz repo
gh repo create susmodder/steam-manifests --public

# Struktura
mkdir -p manifests
touch README.md
touch manifests/among_us.json

# Initial commit
git add .
git commit -m "Initial commit - Steam manifests repository"
git push origin main
```

**`manifests/among_us.json`:**
```json
{
  "app_id": 945360,
  "app_name": "Among Us",
  "depot_id": 945361,
  "platform": "windows",
  "last_updated": "2025-10-28T12:00:00Z",
  "manifests": [
    {
      "version": "2024.10.29s",
      "manifest_id": "[WYPEŁNIJ PO RESEARCH]",
      "build_id": 0,
      "release_date": "2024-10-29",
      "verified": true,
      "verified_by": "boratsc"
    }
  ]
}
```

**0.3. Przygotuj Backend DB (1h)**

```sql
-- Migration: add_steam_manifests_table.sql
CREATE TABLE IF NOT EXISTS steam_manifests (
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

CREATE INDEX idx_among_version ON steam_manifests(among_version);
CREATE INDEX idx_manifest_id ON steam_manifests(steam_manifest_id);

-- Populate z danymi z research
INSERT INTO steam_manifests (among_version, steam_manifest_id, steam_build_id, release_date, verified)
VALUES 
  ('2024.10.29s', '[MANIFEST_ID]', 0, '2024-10-29', true),
  ('2024.8.13s', '[MANIFEST_ID]', 0, '2024-08-13', true),
  ('2024.6.4s', '[MANIFEST_ID]', 0, '2024-06-04', true),
  ('2024.3.5s', '[MANIFEST_ID]', 0, '2024-03-05', true);

-- Rozszerzenie tabeli mod_configs
ALTER TABLE mod_configs 
ADD COLUMN IF NOT EXISTS steam_manifest_id VARCHAR(20),
ADD COLUMN IF NOT EXISTS steam_build_id INT;

-- Update istniejących rekordów
UPDATE mod_configs mc
SET steam_manifest_id = sm.steam_manifest_id,
    steam_build_id = sm.steam_build_id
FROM steam_manifests sm
WHERE mc.among_version = sm.among_version;
```

**Uruchom migrację:**
```bash
psql -h localhost -U susmodder_user -d susmodder_db -f migrations/add_steam_manifests_table.sql
```

**0.4. Test DepotDownloader (2h)**

```bash
# Pobierz DepotDownloader
wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-windows-x64.zip
unzip DepotDownloader-windows-x64.zip -d tools/DepotDownloader

# Test pobrania (z uruchomionym Steam)
cd tools/DepotDownloader
./DepotDownloader.exe -app 945360 -depot 945361 -manifest [MANIFEST_ID] -dir "C:\Temp\among_us_test"

# Weryfikacja
# 1. Sprawdź czy pliki zostały pobrane
# 2. Uruchom Among Us.exe
# 3. Sprawdź wersję w grze (menu główne)
```

---

### FAZA 1: Backend Implementation (1 dzień)

**Cel:** Dodanie API endpoints i migracja danych

#### Zadania

**1.1. Endpoint `/api/steam-manifests` (2h)**

**Plik:** `backend/routes/steamManifests.js`

```javascript
const express = require('express');
const router = express.Router();
const db = require('../config/database');

/**
 * GET /api/steam-manifests
 * Zwraca mapowanie among_version → manifest_id
 */
router.get('/steam-manifests', async (req, res) => {
  try {
    const result = await db.query(`
      SELECT 
        among_version,
        steam_manifest_id as "manifestId",
        steam_build_id as "buildId",
        release_date as "releaseDate",
        size_bytes as "sizeBytes",
        verified
      FROM steam_manifests
      ORDER BY release_date DESC
    `);
    
    // Format do dictionary
    const manifests = {};
    result.rows.forEach(row => {
      manifests[row.among_version] = {
        manifestId: row.manifestId,
        buildId: row.buildId,
        releaseDate: row.releaseDate,
        sizeBytes: row.sizeBytes,
        verified: row.verified
      };
    });
    
    res.json(manifests);
  } catch (error) {
    console.error('Error fetching steam manifests:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

/**
 * GET /api/steam-manifests/:version
 * Zwraca manifest dla konkretnej wersji
 */
router.get('/steam-manifests/:version', async (req, res) => {
  try {
    const { version } = req.params;
    
    const result = await db.query(
      `SELECT * FROM steam_manifests WHERE among_version = $1`,
      [version]
    );
    
    if (result.rows.length === 0) {
      return res.status(404).json({ error: 'Version not found' });
    }
    
    res.json(result.rows[0]);
  } catch (error) {
    console.error('Error fetching manifest:', error);
    res.status(500).json({ error: 'Internal server error' });
  }
});

module.exports = router;
```

**Rejestracja route w `app.js`:**
```javascript
const steamManifestsRouter = require('./routes/steamManifests');
app.use('/api', steamManifestsRouter);
```

**1.2. Update Endpoint `/api/mod-configs` (1h)**

Rozszerz response o `SteamManifestId`:

```javascript
// routes/modConfigs.js
router.get('/mod-configs', async (req, res) => {
  try {
    const result = await db.query(`
      SELECT 
        mc.*,
        sm.steam_manifest_id,
        sm.steam_build_id
      FROM mod_configs mc
      LEFT JOIN steam_manifests sm ON mc.among_version = sm.among_version
      ORDER BY mc.id
    `);
    
    res.json(result.rows);
  } catch (error) {
    res.status(500).json({ error: error.message });
  }
});
```

**1.3. Testing (1h)**

```bash
# Test endpoint
curl http://localhost:3000/api/steam-manifests

# Expected response:
{
  "2024.10.29s": {
    "manifestId": "...",
    "buildId": 12345,
    "releaseDate": "2024-10-29",
    "sizeBytes": 1288490188,
    "verified": true
  },
  ...
}

# Test konkretnej wersji
curl http://localhost:3000/api/steam-manifests/2024.3.5s

# Test mod-configs (sprawdź czy zawiera steam_manifest_id)
curl http://localhost:3000/api/mod-configs
```

**1.4. Deploy Backend (2h)**

```bash
# Push changes
git add .
git commit -m "feat: Add Steam manifests API endpoints"
git push origin main

# Deploy (zależnie od środowiska)
# Przykład dla Heroku:
heroku git:push heroku main

# Lub dla VPS:
ssh user@server
cd /var/www/susmodder-api
git pull
npm install
pm2 restart susmodder-api

# Weryfikacja produkcji
curl https://susmodder.boracik.pl/api/steam-manifests
```

---

### FAZA 2: Desktop App Implementation (3-4 dni)

**Cel:** Implementacja SteamDepotManager i integracja z ModManager

#### Zadania

**2.1. Rozszerzenie ModConfig.cs (30 min)**

```csharp
// SUSModder.Core/Configuration/ModConfig.cs
public class ModConfiguration
{
    // ... (existing fields)
    
    [JsonPropertyName("AmongVersion")]
    public string AmongVersion { get; set; } = string.Empty;
    
    // ✨ NOWE POLA
    [JsonPropertyName("SteamManifestId")]
    public string? SteamManifestId { get; set; }
    
    [JsonPropertyName("SteamBuildId")]
    public int? SteamBuildId { get; set; }
}
```

**2.2. Implementacja SteamDepotManager.cs (4h)**

**Plik:** `SUSModder.Core/GameIntegration/SteamDepotManager.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.GameIntegration
{
    public class SteamDepotManager
    {
        private readonly IConfiguration _configuration;
        private readonly IDiagnosticsOutput _log;
        private readonly HttpClient _httpClient;
        private readonly string _depotDownloaderPath;
        
        // Cache manifestów w pamięci (24h)
        private static Dictionary<string, string>? _manifestCache;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

        public SteamDepotManager(IConfiguration configuration, IDiagnosticsOutput log)
        {
            _configuration = configuration;
            _log = log;
            _httpClient = new HttpClient();
            
            _depotDownloaderPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tools",
                "DepotDownloader",
                "DepotDownloader.exe"
            );
        }

        /// <summary>
        /// Pobiera konkretną wersję Among Us przez Steam depot
        /// Automatycznie używa aktywnej sesji Steam (wymaga uruchomionego klienta Steam)
        /// </summary>
        public async Task<bool> DownloadAmongUsAsync(
            string manifestId,
            string targetDirectory,
            IProgressReporter progress)
        {
            try
            {
                _log.Write($"Rozpoczynam pobieranie vanilla z Steam depot (manifest: {manifestId})");
                
                // Sprawdź czy DepotDownloader istnieje
                if (!IsDepotDownloaderAvailable())
                {
                    throw new FileNotFoundException(
                        $"DepotDownloader nie znaleziony: {_depotDownloaderPath}");
                }
                
                // Sprawdź czy Steam jest uruchomiony
                if (!IsSteamRunning())
                {
                    throw new InvalidOperationException(
                        "Klient Steam musi być uruchomiony. Uruchom Steam i spróbuj ponownie.");
                }
                
                // Utwórz katalog docelowy
                Directory.CreateDirectory(targetDirectory);
                
                // Wywołaj DepotDownloader
                var startInfo = new ProcessStartInfo
                {
                    FileName = _depotDownloaderPath,
                    Arguments = $"-app 945360 -depot 945361 -manifest {manifestId} -dir \"{targetDirectory}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_depotDownloaderPath)
                };
                
                using var process = new Process { StartInfo = startInfo };
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        _log.Write($"[DepotDownloader] {e.Data}");
                        
                        // Parse progress: "[###########---------] 55%"
                        var match = Regex.Match(e.Data, @"(\d+)%");
                        if (match.Success)
                        {
                            int progressValue = int.Parse(match.Groups[1].Value);
                            progress.Report(progressValue, $"Pobieranie z Steam: {progressValue}%");
                        }
                    }
                };
                
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _log.Write($"[DepotDownloader ERROR] {e.Data}");
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0)
                {
                    _log.Write("✅ Pobieranie vanilla z Steam depot zakończone sukcesem");
                    progress.Report(100, "Pobrano vanilla ze Steam");
                    return true;
                }
                else
                {
                    _log.Write($"❌ DepotDownloader zakończył się z kodem błędu: {process.ExitCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Write($"❌ Błąd podczas pobierania z Steam depot: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Pobiera Manifest ID dla danej wersji gry
        /// Wykorzystuje cascading fallbacks: cache → API → GitHub → hardcoded
        /// </summary>
        public async Task<string?> GetManifestIdForVersionAsync(string amongVersion)
        {
            // In-memory cache
            if (_manifestCache != null && DateTime.Now < _cacheExpiry)
            {
                if (_manifestCache.TryGetValue(amongVersion, out var cached))
                {
                    _log.Write($"✅ Manifest ID dla {amongVersion} z cache (memory): {cached}");
                    return cached;
                }
            }

            // Layer 1: Local cache (config.json)
            var localManifest = await TryGetFromLocalCacheAsync(amongVersion);
            if (localManifest != null)
            {
                _log.Write($"✅ Manifest ID dla {amongVersion} z cache (local): {localManifest}");
                CacheManifest(amongVersion, localManifest);
                return localManifest;
            }

            // Layer 2: Backend API
            try
            {
                var apiManifest = await TryGetFromApiAsync(amongVersion);
                if (apiManifest != null)
                {
                    _log.Write($"✅ Manifest ID dla {amongVersion} z API: {apiManifest}");
                    await SaveToLocalCacheAsync(amongVersion, apiManifest);
                    CacheManifest(amongVersion, apiManifest);
                    return apiManifest;
                }
            }
            catch (Exception ex)
            {
                _log.Write($"⚠️ API niedostępne: {ex.Message}");
            }

            // Layer 3: GitHub Raw
            try
            {
                var githubManifest = await TryGetFromGitHubAsync(amongVersion);
                if (githubManifest != null)
                {
                    _log.Write($"✅ Manifest ID dla {amongVersion} z GitHub: {githubManifest}");
                    await SaveToLocalCacheAsync(amongVersion, githubManifest);
                    CacheManifest(amongVersion, githubManifest);
                    return githubManifest;
                }
            }
            catch (Exception ex)
            {
                _log.Write($"⚠️ GitHub niedostępny: {ex.Message}");
            }

            // Layer 4: Hardcoded fallback
            var hardcodedManifest = SteamManifests.GetManifestId(amongVersion);
            if (hardcodedManifest != null)
            {
                _log.Write($"✅ Manifest ID dla {amongVersion} z hardcoded: {hardcodedManifest}");
                CacheManifest(amongVersion, hardcodedManifest);
                return hardcodedManifest;
            }

            _log.Write($"❌ Nie znaleziono Manifest ID dla wersji {amongVersion}");
            return null;
        }

        public bool IsSteamRunning()
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }

        public bool IsDepotDownloaderAvailable()
        {
            return File.Exists(_depotDownloaderPath);
        }

        private async Task<string?> TryGetFromLocalCacheAsync(string amongVersion)
        {
            var configs = await ConfigManager.LoadConfigAsync();
            var match = configs.FirstOrDefault(c => 
                c.AmongVersion == amongVersion && 
                !string.IsNullOrEmpty(c.SteamManifestId));
            return match?.SteamManifestId;
        }

        private async Task<string?> TryGetFromApiAsync(string amongVersion)
        {
            string baseUrl = _configuration["Configuration:BaseUrl"] ?? "https://susmodder.boracik.pl";
            string url = $"{baseUrl}/api/steam-manifests";
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var manifests = JsonSerializer.Deserialize<Dictionary<string, SteamManifestInfo>>(json);
            
            return manifests?.TryGetValue(amongVersion, out var info) == true 
                ? info.ManifestId 
                : null;
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
            var configs = await ConfigManager.LoadConfigAsync();
            var matchingConfigs = configs.Where(c => c.AmongVersion == amongVersion).ToList();
            
            foreach (var config in matchingConfigs)
            {
                config.SteamManifestId = manifestId;
            }
            
            await ConfigManager.SaveConfigAsync(configs);
        }

        private void CacheManifest(string amongVersion, string manifestId)
        {
            _manifestCache ??= new Dictionary<string, string>();
            _manifestCache[amongVersion] = manifestId;
            _cacheExpiry = DateTime.Now.Add(CacheLifetime);
        }
    }

    // Helper models
    public record SteamManifestInfo(
        string ManifestId,
        int? BuildId,
        DateTime? ReleaseDate,
        long? SizeBytes,
        bool Verified);

    public record GitHubManifestData(
        int AppId,
        string AppName,
        int DepotId,
        List<GitHubManifestEntry> Manifests);

    public record GitHubManifestEntry(
        string Version,
        string ManifestId,
        int BuildId,
        DateTime ReleaseDate);
}
```

**2.3. Hardcoded Manifests Fallback (30 min)**

**Plik:** `SUSModder.Core/GameIntegration/SteamManifests.cs`

```csharp
using System.Collections.Generic;

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
            // [WYPEŁNIJ PO RESEARCH W FAZIE 0]
            ["2024.10.29s"] = "[MANIFEST_ID]",
            ["2024.8.13s"] = "[MANIFEST_ID]",
            ["2024.6.4s"] = "[MANIFEST_ID]",
            ["2024.3.5s"] = "[MANIFEST_ID]",
            ["2023.11.28s"] = "[MANIFEST_ID]",
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

**2.4. Modyfikacja ModManager.cs (2h)**

```csharp
// SUSModder.Core/GameIntegration/ModManager.cs

private async Task InstallSteamAsync(
    ModConfiguration modConfig,
    List<ModConfiguration> modConfigs,
    IProgressReporter progress,
    IDiagnosticsOutput log,
    ModManagerUserCallbacks userCallbacks)
{
    string modsInstallPath = PathSettings.ModsInstallPath;
    Directory.CreateDirectory(modsInstallPath);

    string modFolderPath = Path.Combine(modsInstallPath, modConfig.ModName);
    string tempDir = Path.Combine(modsInstallPath, "temp", Guid.NewGuid().ToString("N"));
    string modFile = Path.Combine(tempDir, "mod.zip");

    // ✨ Feature flag - przejściowy okres (możliwość rollback)
    bool useSteamDepot = configuration.GetValue<bool>("Features:UseSteamDepot", true);

    if (useSteamDepot)
    {
        // ========== NOWY PRZEPŁYW: Steam Depot ==========
        progress.Report(10, "Przygotowanie do pobrania vanilla...");
        
        var steamDepotManager = new SteamDepotManager(configuration, log);

        // Sprawdź czy Steam jest uruchomiony
        if (!steamDepotManager.IsSteamRunning())
        {
            log.Write("⚠️ Steam nie jest uruchomiony");
            
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

        // Pobierz Manifest ID dla wersji gry
        progress.Report(15, "Wyszukiwanie wersji gry...");
        string? manifestId = await steamDepotManager.GetManifestIdForVersionAsync(modConfig.AmongVersion);

        if (manifestId == null)
        {
            log.Write($"❌ Nie znaleziono Manifest ID dla wersji {modConfig.AmongVersion}");
            
            if (userCallbacks.ShowErrorAsync != null)
            {
                await userCallbacks.ShowErrorAsync(
                    $"Nie znaleziono danych dla wersji gry {modConfig.AmongVersion}.\n\n" +
                    "Spróbuj zaktualizować SUSModder lub skontaktuj się z supportem.",
                    "Błąd"
                );
            }
            return;
        }

        log.Write($"✅ Znaleziono Manifest ID: {manifestId}");

        // Pobierz vanilla ze Steam
        progress.Report(20, "Pobieranie vanilla ze Steam...");
        
        bool downloadSuccess = await steamDepotManager.DownloadAmongUsAsync(
            manifestId,
            modFolderPath,
            progress
        );

        if (!downloadSuccess)
        {
            log.Write("❌ Pobieranie vanilla z Steam nie powiodło się");
            
            if (userCallbacks.ShowErrorAsync != null)
            {
                await userCallbacks.ShowErrorAsync(
                    "Nie udało się pobrać plików gry ze Steam.\n\n" +
                    "Upewnij się że:\n" +
                    "• Steam jest uruchomiony\n" +
                    "• Jesteś zalogowany\n" +
                    "• Posiadasz Among Us w bibliotece",
                    "Błąd pobierania"
                );
            }
            return;
        }

        log.Write("✅ Vanilla pobrane ze Steam depot");
    }
    else
    {
        // ========== STARY PRZEPŁYW: 7z (backwards compatibility) ==========
        log.Write("⚠️ Używam starego mechanizmu (7z) - feature flag UseSteamDepot=false");
        
        string vanillaDir = Path.Combine(modsInstallPath, "Among Us - Vanilla");
        Directory.CreateDirectory(vanillaDir);

        string vanilla7zName = $"{modConfig.AmongVersion.Replace("-", "").Replace(".", "")}";
        string vanilla7zPath = Path.Combine(vanillaDir, vanilla7zName + ".7z");

        string baseUrl = configuration.GetSection("Configuration")["BaseUrl"] ?? "https://susmodder.boracik.pl/";
        string fileUrlAmongUs = $"{baseUrl}api/susmodder-download-version?version={vanilla7zName}";

        // ... (reszta starego kodu pobierania 7z)
        // [ZOSTAW BEZ ZMIAN - do usunięcia w FAZIE 4]
    }

    // ========== WSPÓLNA CZĘŚĆ: Instalacja Moda ==========
    // [RESZTA BEZ ZMIAN - pobieranie i rozpakowywanie moda]
    
    progress.Report(80, "Pobieranie moda...");
    // ... (existing code)
    
    progress.Report(90, "Instalowanie moda...");
    // ... (existing code)
    
    progress.Report(100, "Instalacja zakończona");
}
```

**2.5. Feature Flag w appsettings.json (5 min)**

```json
{
  "Configuration": {
    "Mode": "steam",
    "BaseUrl": "https://susmodder.boracik.pl",
    // ...
  },
  "Features": {
    "UseSteamDepot": true
  }
}
```

**2.6. Dołączenie DepotDownloader (30 min)**

```bash
# Pobierz najnowszą wersję
cd tools
mkdir DepotDownloader
cd DepotDownloader
wget https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-windows-x64.zip
unzip DepotDownloader-windows-x64.zip
rm DepotDownloader-windows-x64.zip

# Struktura:
# tools/
#   DepotDownloader/
#     DepotDownloader.exe
#     (inne DLLs)
#   7z.exe (existing)
```

**Uwaga w `.csproj`:**
```xml
<!-- Kopiuj DepotDownloader do output directory -->
<ItemGroup>
  <None Include="tools\DepotDownloader\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**2.7. Testing (4h)**

**Test Plan:**

```csharp
// 1. Test IsSteamRunning()
var manager = new SteamDepotManager(config, log);
Assert.True(manager.IsSteamRunning()); // Uruchom Steam przed testem

// 2. Test GetManifestIdForVersionAsync()
var manifestId = await manager.GetManifestIdForVersionAsync("2024.3.5s");
Assert.NotNull(manifestId);
Assert.Matches(@"^\d{19}$", manifestId); // 19-cyfrowy ID

// 3. Test DownloadAmongUsAsync()
var tempDir = Path.Combine(Path.GetTempPath(), "susmodder_test");
var progress = new TestProgressReporter();
var success = await manager.DownloadAmongUsAsync(manifestId, tempDir, progress);
Assert.True(success);
Assert.True(File.Exists(Path.Combine(tempDir, "Among Us.exe")));

// 4. Test pełnej instalacji moda
await modManager.ModifyAsync(
    sheriffModConfig,
    allConfigs,
    progress,
    log,
    callbacks,
    "steam"
);
// Sprawdź czy mod został zainstalowany poprawnie
```

---

### FAZA 3: Parallel Operation (1-2 tygodnie)

**Cel:** Oba systemy działają równolegle, zbieranie feedbacku

#### Feature Flag Strategy

**Stopniowe rollout:**

```
Dzień 1-3:   UseSteamDepot = false (wszyscy)  → Stary system
Dzień 4-7:   UseSteamDepot = true  (10% beta testerów)
Dzień 8-10:  UseSteamDepot = true  (50% użytkowników)
Dzień 11-14: UseSteamDepot = true  (100% użytkowników)
```

**Monitoring:**

```javascript
// Backend: Track który system jest używany
router.post('/api/telemetry/installation', async (req, res) => {
  const { modId, method, success, errorMessage } = req.body;
  // method: "steam_depot" | "7z_legacy"
  
  await db.query(`
    INSERT INTO installation_logs (mod_id, method, success, error_message, created_at)
    VALUES ($1, $2, $3, $4, NOW())
  `, [modId, method, success, errorMessage]);
  
  res.json({ ok: true });
});
```

**Metrics do śledzenia:**

- Sukces instalacji (Steam depot vs 7z)
- Czas instalacji (Steam depot vs 7z)
- Błędy (jakie i jak często)
- Liczba użytkowników z Steam nieaktywnym

#### Feedback Loop

**Discord announcements:**
```
🚀 SUSModder 2.1.0 Beta

Testujemy nowy system pobierania gry bezpośrednio ze Steam!

✅ Zalety:
- Legalny (pliki bezpośrednio z Valve)
- Szybszy (Steam CDN)
- Brak limitu transferu

⚠️ Wymaga:
- Uruchomiony klient Steam
- Zalogowanie w Steam
- Among Us w bibliotece

Zgłaszaj problemy na #bug-reports
```

---

### FAZA 4: Deprecation (Po 2 tygodniach success)

**Cel:** Usunięcie starego systemu 7z

#### Zadania

**4.1. Usunięcie Kodu 7z (2h)**

```csharp
// ModManager.cs - USUŃ cały blok "else"
private async Task InstallSteamAsync(...)
{
    // ❌ USUŃ feature flag
    // bool useSteamDepot = configuration.GetValue<bool>("Features:UseSteamDepot", true);
    
    // ❌ USUŃ if-else, zostaw tylko kod Steam depot
    
    var steamDepotManager = new SteamDepotManager(configuration, log);
    
    // ... (tylko nowy kod)
}
```

**Usuń pliki:**
```bash
# SecretProvider.Get7zPassword()
# Tools/7z.exe (opcjonalnie - może być używany gdzie indziej)
# Extract7zWithPassword() method
```

**4.2. Backend Cleanup (1h)**

```javascript
// ❌ USUŃ endpoint
// routes/vanillaDownload.js
router.delete('/api/susmodder-download-version', ...); // REMOVE ENTIRE FILE
```

**Usuń z `app.js`:**
```javascript
// const vanillaDownloadRouter = require('./routes/vanillaDownload');
// app.use('/api', vanillaDownloadRouter);
```

**4.3. Usuń Archiwa 7z z Serwera (Krytyczne!)**

```bash
# Backup przed usunięciem
ssh user@server
cd /var/www/susmodder/
tar -czf vanilla_7z_backup_$(date +%Y%m%d).tar.gz vanillas/
mv vanilla_7z_backup_*.tar.gz /backups/

# Usuń pliki (oszczędność ~10-15 GB)
rm -rf vanillas/

# Weryfikacja
df -h # Sprawdź czy zwolniono miejsce
```

**4.4. Update Dokumentacji (1h)**

- README.md - usunięcie wzmianek o 7z
- Instrukcje instalacji - update wymagań (Steam musi być uruchomiony)
- FAQ - dodać sekcję o Steam requirement

---

## Backwards Compatibility

### Handling Old Versions

**Problem:** Użytkownicy z starymi wersjami SUSModder (przed 2.1.0)

**Rozwiązanie:**

1. **Stary endpoint pozostaje aktywny przez 1 miesiąc po release 2.1.0**
   ```javascript
   // Deprecated ale działający
   router.get('/api/susmodder-download-version', (req, res) => {
     res.status(410).json({
       error: 'This endpoint is deprecated. Please update SUSModder to 2.1.0+',
       downloadUrl: 'https://susmodder.boracik.pl/download'
     });
   });
   ```

2. **Force update message w starych wersjach**
   - API zwraca `minimumVersion: "2.1.0"`
   - Stare wersje pokazują dialog: "Musisz zaktualizować SUSModder"

### Handling Broken Installations

**Problem:** Użytkownik zainstalował mod przez 7z, chce zaktualizować przez Steam depot

**Rozwiązanie:**

```csharp
// ModManager.cs - przed instalacją
if (Directory.Exists(modFolderPath))
{
    // Sprawdź czy jest to instalacja 7z czy Steam depot
    bool isLegacyInstall = !File.Exists(Path.Combine(modFolderPath, ".steam_depot_marker"));
    
    if (isLegacyInstall)
    {
        log.Write("⚠️ Wykryto starą instalację (7z), czyszczenie...");
        await SafeDeleteDirectory(modFolderPath);
    }
}

// Po instalacji przez Steam depot, zostaw marker
File.WriteAllText(Path.Combine(modFolderPath, ".steam_depot_marker"), DateTime.Now.ToString());
```

---

## Rollback Plan

### Jeśli Steam Depot Nie Działa

**Symptomy:**
- >10% błędów instalacji
- Masowe zgłoszenia błędów Steam auth
- DepotDownloader przestaje działać (update Valve)

**Rollback (natychmiastowy):**

```json
// appsettings.json - zmień feature flag
{
  "Features": {
    "UseSteamDepot": false  // ← Powrót do 7z
  }
}
```

**Lub przez API (remote toggle):**

```javascript
// Backend: Dynamic feature flags
router.get('/api/feature-flags', (req, res) => {
  res.json({
    useSteamDepot: false  // ← Kontrola z backendu
  });
});
```

```csharp
// Desktop: Sprawdź feature flag z API
bool useSteamDepot = await GetFeatureFlagAsync("useSteamDepot") 
    ?? configuration.GetValue<bool>("Features:UseSteamDepot", false);
```

### Rollback Checklist

1. ✅ Przywróć feature flag: `UseSteamDepot = false`
2. ✅ Deploy aktualizacji (hotfix 2.1.1)
3. ✅ Przywróć endpoint `/api/susmodder-download-version`
4. ✅ Przywróć archiwa 7z na serwer (z backup)
5. ✅ Komunikat Discord: "Tymczasowo powróciliśmy do starego systemu"
6. ✅ Investigate problem z Steam depot
7. ✅ Fix i ponowny deploy

---

## Timeline

### Optymistyczny (8-10 dni)

```
Dzień 1:    Faza 0 (research + preparation)
Dzień 2:    Faza 1 (backend implementation)
Dzień 3-5:  Faza 2 (desktop app implementation)
Dzień 6-7:  Faza 2 (testing + fixes)
Dzień 8-14: Faza 3 (parallel operation, beta)
Dzień 15:   Faza 4 (full rollout)
Dzień 30:   Faza 4 (cleanup - usuń 7z system)
```

### Realistyczny (14-21 dni)

```
Tydzień 1:  Faza 0 + Faza 1 + Faza 2 (dev)
Tydzień 2:  Faza 2 (testing + fixes)
Tydzień 3:  Faza 3 (beta rollout 10% → 50% → 100%)
Tydzień 4:  Faza 4 (deprecation + cleanup)
```

---

## Testing Strategy

### Unit Tests

```csharp
// SteamDepotManagerTests.cs
[Fact]
public async Task GetManifestIdForVersion_WhenApiAvailable_ReturnsCorrectId()
{
    // Arrange
    var mockConfig = new Mock<IConfiguration>();
    var mockLog = new Mock<IDiagnosticsOutput>();
    var manager = new SteamDepotManager(mockConfig.Object, mockLog.Object);
    
    // Act
    var manifestId = await manager.GetManifestIdForVersionAsync("2024.3.5s");
    
    // Assert
    Assert.NotNull(manifestId);
    Assert.Matches(@"^\d{19}$", manifestId);
}

[Fact]
public void IsSteamRunning_WhenSteamActive_ReturnsTrue()
{
    // Arrange
    var manager = new SteamDepotManager(config, log);
    
    // Act
    var isRunning = manager.IsSteamRunning();
    
    // Assert (wymaga uruchomionego Steam dla testu)
    Assert.True(isRunning);
}
```

### Integration Tests

```csharp
[Fact]
public async Task FullInstallation_UsingSteamDepot_Succeeds()
{
    // Arrange
    var modConfig = new ModConfiguration 
    { 
        ModName = "Test Mod",
        AmongVersion = "2024.3.5s",
        ModType = "full"
    };
    
    // Act
    await modManager.ModifyAsync(
        modConfig, 
        allConfigs, 
        progress, 
        log, 
        callbacks, 
        "steam"
    );
    
    // Assert
    var installPath = Path.Combine(PathSettings.ModsInstallPath, "Test Mod");
    Assert.True(Directory.Exists(installPath));
    Assert.True(File.Exists(Path.Combine(installPath, "Among Us.exe")));
}
```

### Manual Testing Checklist

**Pre-release:**
- [ ] Instalacja moda (Steam uruchomiony)
- [ ] Instalacja moda (Steam nieaktywny) - sprawdź komunikat błędu
- [ ] Aktualizacja moda
- [ ] Odinstalowanie moda
- [ ] Instalacja kilku modów równolegle
- [ ] Sprawdź czy gra się uruchamia
- [ ] Sprawdź wersję gry (czy zgadza się z AmongVersion)
- [ ] Offline mode (sprawdź fallbacki)
- [ ] Różne wersje gry (stara, najnowsza)

**Post-release (beta):**
- [ ] Monitor error logs (7 dni)
- [ ] Feedback z Discord
- [ ] Performance metrics (czas instalacji)
- [ ] Success rate porównanie (7z vs Steam depot)

---

## User Communication

### Announcement Template (Discord/Website)

```markdown
# 🚀 SUSModder 2.1.0 - Legalny System Pobierania

**Data release:** [TBD]

## Co się zmienia?

Od wersji 2.1.0 **SUSModder pobiera pliki gry bezpośrednio ze Steam** zamiast z naszego serwera.

### Zalety ✅

- **Legalność** - pliki bezpośrednio z oficjalnych serwerów Valve
- **Szybkość** - wykorzystanie Steam CDN (globalnie rozproszone serwery)
- **Bezpieczeństwo** - brak przechowywania plików gry po naszej stronie
- **Oszczędność** - brak limitów transferu dla użytkowników

### Wymagania ⚠️

**WAŻNE:** Aby zainstalować mod, musisz mieć:

1. ✅ **Uruchomiony klient Steam**
2. ✅ **Zalogowanie w Steam**
3. ✅ **Among Us w swojej bibliotece Steam**

To wszystko! Nie musisz podawać hasła ani żadnych danych - SUSModder automatycznie użyje Twojej aktywnej sesji Steam.

### FAQ

**Q: Czy muszę podawać hasło do Steam?**
A: NIE! SUSModder automatycznie wykorzystuje Twoją aktywną sesję Steam (podobnie jak Epic Legendary).

**Q: Co jeśli zapomnę uruchomić Steam?**
A: SUSModder wyświetli komunikat przypominający o uruchomieniu Steam.

**Q: Czy to bezpieczne?**
A: Tak! SUSModder NIE przechowuje żadnych danych logowania. Używa tylko tokenów sesji Steam (tak jak robi to sam Steam).

**Q: Co z moimi starymi modami?**
A: Wszystko będzie działać bez zmian. Możesz też je zaktualizować.

**Q: A użytkownicy Epic?**
A: Bez zmian - Epic nadal działa przez Legendary.

### Zgłaszanie Problemów

Jeśli napotkasz problemy:
1. Sprawdź czy Steam jest uruchomiony
2. Sprawdź logi w SUSModder
3. Zgłoś problem na Discord: #bug-reports
```

### In-App Notification

```csharp
// MainWindow.xaml - first launch po update
if (IsFirstLaunchAfterUpdate("2.1.0"))
{
    await ShowInfoDialogAsync(
        "🚀 SUSModder 2.1.0 - Nowy System Pobierania\n\n" +
        "Od teraz pobieramy pliki gry bezpośrednio ze Steam!\n\n" +
        "Wymagania:\n" +
        "✅ Uruchomiony klient Steam\n" +
        "✅ Among Us w bibliotece\n\n" +
        "Nie musisz podawać hasła - używamy Twojej aktywnej sesji Steam.\n\n" +
        "Więcej informacji: susmodder.pl/changelog",
        "Ważna Aktualizacja"
    );
}
```

---

## Success Criteria

### Faza 3 (Parallel Operation) - Go/No-Go Decision

**GO (proceed to Faza 4) jeśli:**
- ✅ Success rate instalacji ≥95%
- ✅ Czas instalacji podobny lub lepszy niż 7z
- ✅ <5% zgłoszeń problemów Steam auth
- ✅ Pozytywny feedback community

**NO-GO (rollback) jeśli:**
- ❌ Success rate <90%
- ❌ Masowe problemy z autoryzacją Steam
- ❌ DepotDownloader nie działa stabilnie
- ❌ Negatywny feedback community

### Faza 4 (Deprecation) - Complete Migration

**Complete jeśli:**
- ✅ 100% użytkowników na wersji 2.1.0+
- ✅ Brak aktywnych zgłoszeń problemów Steam depot
- ✅ Monitoring pokazuje stabilność
- ✅ Archiwa 7z usunięte z serwera

---

## Post-Migration

### Maintenance

**Co 2-3 miesiące:**
1. Aktualizuj `SteamManifests.cs` (nowe wersje Among Us)
2. Sprawdź czy DepotDownloader wymaga update
3. Review logów błędów (czy nowe problemy)
4. Update dokumentacji

**Monitoring:**
```sql
-- Statystyki instalacji
SELECT 
  DATE(created_at) as date,
  COUNT(*) as installations,
  SUM(CASE WHEN success THEN 1 ELSE 0 END) as successful,
  AVG(CASE WHEN success THEN duration_seconds END) as avg_duration
FROM installation_logs
WHERE method = 'steam_depot'
GROUP BY DATE(created_at)
ORDER BY date DESC
LIMIT 30;
```

### Future Improvements

**Potencjalne rozszerzenia:**

1. **Pre-download** - pobierz vanilla w tle przed instalacją moda
2. **Delta updates** - wykorzystaj Steam delta patching
3. **Shared vanilla** - jedna kopia vanilla dla wielu modów (hard links)
4. **Auto-update manifests** - automatyczne pobieranie nowych Manifest IDs
5. **Steam Workshop integration** - publikacja modów na Workshop

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
**Autor:** Claude (AI Assistant) & boratsc  
