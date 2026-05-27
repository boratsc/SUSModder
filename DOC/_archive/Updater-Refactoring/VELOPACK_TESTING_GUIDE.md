# Velopack - Instrukcja Testowania

**Data:** 2025-11-04
**Status:** Backend gotowy, wymaga pliku testowego

---

## Obecny Stan

### ✅ Co działa:
- Backend API: `https://susmodder.app/api/releases`
- Kod aplikacji z pełną obsługą Velopack
- Fallback do legacy updater jeśli Velopack nie jest wykryty
- UI dialog do aktualizacji

### ❌ Co brakuje:
- **Testowy pakiet .nupkg** do serwowania przez API

---

## Szybki Test (Krok po kroku)

### 1. Zainstaluj Velopack CLI

```powershell
dotnet tool install -g vpk
```

Sprawdź instalację:
```powershell
vpk --version
```

### 2. Zbuduj testowy pakiet

```powershell
cd d:\Development\SUSModder
.\build-velopack-test.ps1
```

Ten skrypt:
- Buduje aplikację (publish)
- Pakuje ją z Velopack CLI
- Tworzy katalog `velopack-releases/` z plikami:
  - `SUSModder-2.1.0-win-full.nupkg` - pełny pakiet
  - `releases.win.json` - manifest z metadanymi
  - `RELEASES` - plik z checksum (Squirrel legacy compatibility)

### 3. Upload plików na serwer

Skopiuj **całą zawartość** `velopack-releases/` do:
```
https://susmodder.app/releases/
```

Struktura na serwerze powinna wyglądać:
```
https://susmodder.app/releases/
├── SUSModder-2.1.0-win-full.nupkg
├── releases.win.json
└── RELEASES
```

### 4. Przetestuj API endpoint

Otwórz w przeglądarce lub curl:
```
https://susmodder.app/api/releases?channel=win
```

**Oczekiwana odpowiedź:**
```json
{
  "success": true,
  "channel": "win",
  "arch": "x64",
  "latestVersion": "2.1.0",
  "updatedAt": "2025-11-03T23:52:25.566Z",
  "manifest": {
    "LatestVersion": "2.1.0",
    "Releases": [
      {
        "Version": "2.1.0",
        "File": "SUSModder-2.1.0-win-full.nupkg",
        "SHA256": "[prawdziwy checksum]",
        "Channel": "win",
        "CreateTime": "2025-11-03T10:00:00Z"
      }
    ],
    "downloadBaseUrl": "https://susmodder.app/releases"
  },
  "downloadBaseUrl": "https://susmodder.app/releases"
}
```

⚠️ **UWAGA:** Twój obecny response ma `"SHA256":"dummychecksum"` - to trzeba podmienić na prawdziwy checksum z pliku `RELEASES`!

### 5. Test w aplikacji (dev mode)

Opcja A - **Symulacja środowiska Velopack:**
```powershell
# Stwórz strukturę katalogów Velopack lokalnie
cd publish\
mkdir packages
copy ..\velopack-releases\*.nupkg packages\

# Uruchom aplikację
.\SUSModder.exe
```

Opcja B - **Test pełnej instalacji:**
```powershell
# Zainstaluj aplikację przez Velopack installer (gdy będzie gotowy)
.\Setup.exe

# Zainstalowana aplikacja będzie w:
# C:\Users\[user]\AppData\Local\SUSModder\current\SUSModder.exe
```

### 6. Kliknij "Sprawdź aktualizacje"

Aplikacja powinna:
1. Wykryć środowisko Velopack (lub użyć legacy)
2. Pobrać manifest z API
3. Porównać wersje (2.0.1 vs 2.1.0)
4. Pokazać dialog z pytaniem o update
5. Pobrać pakiet i zainstalować

---

## Debugowanie

### Problem: "No updates available" mimo że API zwraca 2.1.0

**Możliwe przyczyny:**
1. Aplikacja ma już wersję 2.1.0 w `appsettings.json`
2. Velopack nie jest wykryty → używa legacy updater

**Sprawdź logi:**
```csharp
// MainWindowViewModel.Initialization.cs, linia ~263
_diagnosticsOutput.Write("[Velopack] Initializing UpdateManager...");
```

### Problem: "Failed to check for updates"

**Debug VelopackApiSource.cs:**
```csharp
// VelopackApiSource.cs, linia ~38
logger.Info($"[VelopackApiSource] Fetching manifest from '{requestUri}'.");

// VelopackApiSource.cs, linia ~100
throw new InvalidOperationException($"Velopack API error ({error}): {message}");
```

### Problem: Aplikacja używa legacy updater zamiast Velopack

**Sprawdź detekcję:**
```csharp
// MainWindowViewModel.Initialization.cs, linia ~263
velopackEnvironmentDetected = await velopackUpdateService.IsInstalledAsync();
```

`IsInstalledAsync()` zwraca `true` tylko jeśli:
- Aplikacja jest zainstalowana przez Velopack installer
- Istnieje plik `Update.exe` w katalogu nadrzędnym

**Symulacja dla testów:**
```powershell
# W katalogu publish\
echo "dummy" > ..\Update.exe
```

---

## Aktualizacja Backendu (WAŻNE!)

### Problem z obecnym API response

```json
"SHA256": "dummychecksum"  ← TO MUSI BYĆ PRAWDZIWY CHECKSUM!
```

**Velopack wymaga poprawnego checksum do weryfikacji pakietu.**

### Jak wygenerować prawdziwy checksum?

Po zbudowaniu pakietu, plik `RELEASES` zawiera:
```
[SHA256] SUSModder-2.1.0-win-full.nupkg [size] [date]
```

**Przykład:**
```
A1B2C3D4E5F6... SUSModder-2.1.0-win-full.nupkg 52843520 2025-11-03
```

Backend musi:
1. Przeczytać plik `RELEASES` z serwera
2. Wyciągnąć checksum dla żądanej wersji
3. Zwrócić go w `manifest.Releases[0].SHA256`

### Aktualizacja backend endpoint (propozycja)

```typescript
// Backend: /api/releases
app.get('/api/releases', async (req, res) => {
  const channel = req.query.channel || 'win';
  
  // Przeczytaj plik RELEASES
  const releasesPath = path.join(__dirname, 'releases', 'RELEASES');
  const releasesContent = await fs.readFile(releasesPath, 'utf-8');
  
  // Parse: [checksum] [filename] [size] [date]
  const lines = releasesContent.trim().split('\n');
  const releases = lines.map(line => {
    const [checksum, filename, size, date] = line.split(' ');
    const version = filename.match(/SUSModder-([\d.]+)-/)?.[1];
    
    return {
      Version: version,
      File: filename,
      SHA256: checksum,
      Channel: channel,
      CreateTime: new Date(date).toISOString()
    };
  });
  
  const latest = releases[0]; // Zakładając że jest posortowane
  
  res.json({
    success: true,
    channel,
    arch: 'x64',
    latestVersion: latest.Version,
    updatedAt: new Date().toISOString(),
    manifest: {
      LatestVersion: latest.Version,
      Releases: releases,
      downloadBaseUrl: 'https://susmodder.app/releases'
    },
    downloadBaseUrl: 'https://susmodder.app/releases'
  });
});
```

---

## Następne Kroki

### Dla pełnego testowania:

1. ✅ **Zbuduj pakiet:** `.\build-velopack-test.ps1`
2. ✅ **Upload plików:** `velopack-releases/*` → serwer
3. ⚠️ **Popraw backend:** Zwracaj prawdziwy SHA256 z pliku `RELEASES`
4. ✅ **Test:** Uruchom aplikację i kliknij "Sprawdź aktualizacje"

### Dla produkcji:

1. Stwórz **installer** (Setup.exe) dla nowych użytkowników:
   ```powershell
   vpk pack --packId SUSModder --packVersion 2.1.0 --packDir publish/ --outputDir releases/ --channel win
   ```

2. Dodaj **code signing** do pakietów (opcjonalne ale zalecane):
   ```powershell
   vpk pack ... --signTemplate "signtool.exe" --signParams "/f cert.pfx /p password /tr http://timestamp.digicert.com"
   ```

3. Stwórz **delta updates** (kolejne wersje 2.1.1, 2.1.2...):
   ```powershell
   vpk pack --packVersion 2.1.1 --packDir publish/ --outputDir releases/ --delta releases/SUSModder-2.1.0-win-full.nupkg
   ```

---

## Troubleshooting Checklist

- [ ] Velopack CLI zainstalowane (`vpk --version`)
- [ ] Pakiet .nupkg wygenerowany w `velopack-releases/`
- [ ] Pliki uploadowane na serwer (`https://susmodder.app/releases/`)
- [ ] API zwraca prawdziwy checksum (nie "dummychecksum")
- [ ] Plik dostępny pod URL: `https://susmodder.app/releases/SUSModder-2.1.0-win-full.nupkg`
- [ ] Aplikacja wykrywa środowisko Velopack (`IsInstalledAsync() == true`)
- [ ] Log pokazuje `[Velopack] Checking for updates...`

---

## Kontakt przy problemach

Jeśli coś nie działa, sprawdź logi:
```csharp
// Dodaj więcej logowania w VelopackApiSource.cs
logger.Info($"[DEBUG] Response payload: {payload}");
```

Lub uruchom z debuggerem i postaw breakpoint w:
- `VelopackUpdateService.CheckForUpdateAsync()`
- `VelopackApiSource.GetReleaseFeed()`
