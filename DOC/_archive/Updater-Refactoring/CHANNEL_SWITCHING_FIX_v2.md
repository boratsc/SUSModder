# Fix: Zmiana Kanałów Beta ↔ Release (v2)

**Data:** 2025-11-06
**Problem:** Przełączanie kanałów nie działa - pliki nie mogą zostać pobrane (404)

---

## 🔍 Root Cause Analysis

### Problem w przepływie URL

1. **Backend zwraca:**
   ```json
   {
     "File": "SUSModder-2.2.1-release-full.nupkg",
     "downloadBaseUrl": "https://susmodder.app/releases"
   }
   ```

2. **VelopackApiSource.cs:178 dodaje kanał:**
   ```csharp
   var fileNameWithPath = $"{channel}/{fileName}";
   // Rezultat: "release/SUSModder-2.2.1-release-full.nupkg"
   ```

3. **VelopackApiSource.cs:220 konstruuje URL:**
   ```csharp
   return new Uri(baseString + fileName);
   // Rezultat: "https://susmodder.app/releases/release/SUSModder-2.2.1-release-full.nupkg"
   ```

4. **Faktyczny URL pliku na serwerze:**
   ```
   https://susmodder.app/releases/SUSModder-2.2.1-release-full.nupkg
   ```

5. **Rezultat: 404 Not Found** ❌

---

## ✅ Rozwiązanie A: Backend zwraca absolute URLs (NAJLEPSZE)

### Zmiana w backend API

Zamiast zwracać tylko nazwę pliku:
```json
{
  "File": "SUSModder-2.2.1-release-full.nupkg"
}
```

Zwracaj **pełny URL**:
```json
{
  "File": "https://susmodder.app/releases/SUSModder-2.2.1-release-full.nupkg"
}
```

### Dlaczego to działa?

VelopackApiSource.cs:215 wykrywa absolute URI i używa go bezpośrednio:

```csharp
private Uri ResolveDownloadUri(string fileName)
{
    if (Uri.TryCreate(fileName, UriKind.Absolute, out var absoluteUri))
        return absoluteUri;  // ✅ Używa bezpośrednio - omija dodawanie kanału!

    // ... reszta kodu
}
```

### Przykładowa zmiana w backend (Node.js)

```javascript
router.get('/releases', async (req, res) => {
  const channel = req.query.channel || 'release';

  // Odczytaj manifest
  const manifest = await readManifest(channel);

  // ZMIANA: Dodaj pełne URLe do każdego release
  const releasesWithUrls = manifest.Releases.map(release => ({
    ...release,
    File: `https://susmodder.app/releases/${release.File}` // ✅ Pełny URL
  }));

  res.json({
    success: true,
    channel,
    latestVersion: manifest.LatestVersion,
    downloadBaseUrl: "https://susmodder.app/releases",
    manifest: {
      ...manifest,
      Releases: releasesWithUrls
    }
  });
});
```

### Zalety
- ✅ Zero zmian w kodzie aplikacji
- ✅ Backend ma pełną kontrolę nad URL
- ✅ Pozwala na CDN, S3, różne domeny dla różnych kanałów
- ✅ Zgodne ze standardem Velopack (absolute URLs są wspierane)

### Wady
- ⚠️ Wymaga zmiany w backend API

---

## ✅ Rozwiązanie B: Usunąć dodawanie kanału w VelopackApiSource (NAJSZYBSZE)

### Zmiana w kodzie

**Plik:** `SUSModder.Core/Services/VelopackApiSource.cs`

```csharp
// PRZED (linia 162-208):
private string ConvertCustomManifestToVelopackFormat(JsonElement manifestElement, string channel = "release")
{
    var assets = new List<Dictionary<string, object>>();

    if (manifestElement.TryGetProperty("Releases", out var releasesElement) && releasesElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var release in releasesElement.EnumerateArray())
        {
            if (!release.TryGetProperty("File", out var fileElement) || fileElement.ValueKind != JsonValueKind.String)
                continue;

            var fileName = fileElement.GetString();
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            // ❌ USUŃ TĘ LINIĘ:
            // var fileNameWithPath = $"{channel}/{fileName}";

            // ✅ DODAJ TĘ LINIĘ:
            var fileNameWithPath = fileName;  // Nie dodawaj prefiksu kanału

            var asset = new Dictionary<string, object>
            {
                ["PackageId"] = "SUSModder",
                ["FileName"] = fileNameWithPath  // Użyj oryginalnej nazwy
            };

            // ... reszta kodu bez zmian
        }
    }
    // ...
}
```

### PO zmianie

```csharp
private string ConvertCustomManifestToVelopackFormat(JsonElement manifestElement, string channel = "release")
{
    var assets = new List<Dictionary<string, object>>();

    if (manifestElement.TryGetProperty("Releases", out var releasesElement) && releasesElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var release in releasesElement.EnumerateArray())
        {
            if (!release.TryGetProperty("File", out var fileElement) || fileElement.ValueKind != JsonValueKind.String)
                continue;

            var fileName = fileElement.GetString();
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            // ✅ Nie dodawaj kanału do ścieżki - backend zwraca już poprawną nazwę
            var fileNameWithPath = fileName;

            var asset = new Dictionary<string, object>
            {
                ["PackageId"] = "SUSModder",
                ["FileName"] = fileNameWithPath
            };

            if (release.TryGetProperty("Version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String)
                asset["Version"] = versionElement.GetString()!;

            if (release.TryGetProperty("SHA256", out var sha256Element) && sha256Element.ValueKind == JsonValueKind.String)
                asset["SHA256"] = sha256Element.GetString()!;

            if (release.TryGetProperty("Size", out var sizeElement) && (sizeElement.ValueKind == JsonValueKind.Number || sizeElement.ValueKind == JsonValueKind.String))
                asset["Size"] = sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : long.Parse(sizeElement.GetString()!);

            asset["Type"] = fileName.Contains("-delta.nupkg", StringComparison.OrdinalIgnoreCase) ? "Delta" : "Full";

            assets.Add(asset);
        }
    }

    var velopackManifest = new Dictionary<string, object>
    {
        ["Assets"] = assets
    };

    return JsonSerializer.Serialize(velopackManifest);
}
```

### Zalety
- ✅ Najszybsze rozwiązanie - jedna linia kodu
- ✅ Działa natychmiast
- ✅ Nie wymaga zmian w backend
- ✅ Nie wymaga zmian w strukturze katalogów na serwerze

### Wady
- ⚠️ Traci multi-channel directory structure
- ⚠️ Nie jest zgodne z Velopack best practices (kanały w podkatalogach)

---

## ✅ Rozwiązanie C: Zmienić strukturę katalogów na serwerze (DŁUGOTERMINOWE)

### Obecna struktura (FLAT)

```
https://susmodder.app/releases/
├─ SUSModder-2.2.1-release-full.nupkg
├─ SUSModder-2.3.7-beta-beta-full.nupkg
└─ RELEASES
```

### Docelowa struktura (MULTI-CHANNEL)

```
https://susmodder.app/releases/
├─ release/
│   ├─ SUSModder-2.2.1-release-full.nupkg
│   ├─ RELEASES
│   └─ releases.release.json
└─ beta/
    ├─ SUSModder-2.3.7-beta-beta-full.nupkg
    ├─ RELEASES
    └─ releases.beta.json
```

### Kroki implementacji

1. **Zmień build script** aby uploadował do podkatalogów:

```powershell
# W build-release-2.2.0.ps1 lub deploy script

# PRZED:
scp ./releases-release/* deploy@susmodder.app:/var/www/susmodder/releases/

# PO:
scp ./releases-release/* deploy@susmodder.app:/var/www/susmodder/releases/release/
scp ./releases-beta/* deploy@susmodder.app:/var/www/susmodder/releases/beta/
```

2. **Backend zwraca nazwę bez kanału** (jak teraz):
```json
{
  "File": "SUSModder-2.2.1-release-full.nupkg"
}
```

3. **VelopackApiSource dodaje kanał** (jak teraz - linia 178):
```csharp
var fileNameWithPath = $"{channel}/{fileName}";
// Rezultat: "release/SUSModder-2.2.1-release-full.nupkg"
```

4. **Finał URL:**
```
https://susmodder.app/releases/release/SUSModder-2.2.1-release-full.nupkg ✅
```

### Zalety
- ✅ Zgodne z Velopack best practices
- ✅ Czysta separacja kanałów
- ✅ Łatwiejsze zarządzanie uprawnieniami (różne katalogi dla różnych kanałów)
- ✅ Przygotowane pod przyszłe rozszerzenia (staging, alpha, etc.)

### Wady
- ⚠️ Wymaga zmian w infrastrukturze serwera
- ⚠️ Wymaga aktualizacji deploy scripts
- ⚠️ Breaking change - trzeba przenieść istniejące pliki

---

## 🎯 Rekomendacja

### Dla szybkiego fix: **Rozwiązanie B**
Usunąć dodawanie kanału w VelopackApiSource (jedna linia kodu).

### Dla production long-term: **Rozwiązanie A + C**
1. Zmienić strukturę katalogów na serwerze (Rozwiązanie C)
2. Backend może zwracać absolute URLs dla CDN support (Rozwiązanie A)

---

## 🧪 Testing

### Test 1: Sprawdź czy pliki są dostępne

```powershell
# Testuj obecną strukturę (flat)
Invoke-WebRequest -Uri "https://susmodder.app/releases/SUSModder-2.2.1-release-full.nupkg" -Method Head

# Testuj nową strukturę (multi-channel)
Invoke-WebRequest -Uri "https://susmodder.app/releases/release/SUSModder-2.2.1-release-full.nupkg" -Method Head
Invoke-WebRequest -Uri "https://susmodder.app/releases/beta/SUSModder-2.3.7-beta-beta-full.nupkg" -Method Head
```

### Test 2: Sprawdź zmianę kanału w aplikacji

```
1. Uruchom aplikację w wersji beta (2.3.x)
2. Settings → Update Channel → "Release"
3. Save
4. ✅ POWINIEN pokazać dialog: "Update available: 2.3.7-beta → 2.2.1"
5. Download update
6. ✅ POWINIEN pobrać plik bez błędów 404
```

---

## 📝 Checklist wdrożenia

### Rozwiązanie B (Quick Fix)

- [ ] Zmienić linię 178 w VelopackApiSource.cs
- [ ] Build aplikacji
- [ ] Przetestować zmianę kanału lokalnie
- [ ] Deploy nowej wersji

### Rozwiązanie C (Long-term)

- [ ] Utworzyć podkatalogi `/releases/release/` i `/releases/beta/` na serwerze
- [ ] Zaktualizować deploy script aby uploadował do podkatalogów
- [ ] Przenieść istniejące pliki do właściwych podkatalogów
- [ ] Przetestować dostępność plików (HEAD request)
- [ ] Przetestować API endpoint (test-velopack-api.ps1)
- [ ] Przetestować zmianę kanału w aplikacji
- [ ] Zaktualizować dokumentację deployment

---

## 🔗 Related Files

- `SUSModder.Core/Services/VelopackApiSource.cs` - główny plik do zmiany
- `SKRYPTY/Build/build-release-2.2.0.ps1` - build script
- `SKRYPTY/Build/deploy-to-server.ps1` - deployment script
- `DOC/Updater-Refactoring/BACKEND_SETUP.md` - dokumentacja backend
