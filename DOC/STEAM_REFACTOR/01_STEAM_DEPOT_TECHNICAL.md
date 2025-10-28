# Steam Depot System - Dokumentacja Techniczna

**Data utworzenia:** 2025-10-28  
**Status:** Analiza techniczna  

---

## 📋 Spis Treści

1. [Czym Jest Steam Depot](#czym-jest-steam-depot)
2. [Struktura Danych Steam](#struktura-danych-steam)
3. [DepotDownloader - Narzędzie](#depotdownloader---narzędzie)
4. [Jak Pozyskać Manifest ID](#jak-pozyskać-manifest-id)
5. [Alternatywne Narzędzia](#alternatywne-narzędzia)

---

## Czym Jest Steam Depot

### Definicja

**Steam Depot** to magazyn plików zarządzany przez platformę Steam. Każda gra/aplikacja na Steam składa się z jednego lub więcej depotów.

### Struktura Hierarchiczna

```
Steam Platform
│
├── App (Aplikacja)
│   ├── App ID: 945360 (Among Us)
│   │   ├── Depot 1: 945361 (Windows binaries)
│   │   ├── Depot 2: 945362 (macOS binaries)
│   │   ├── Depot 3: 945363 (Linux binaries)
│   │   └── ...
│   │
│   └── Każdy Depot zawiera:
│       ├── Manifest 1 (wersja 2023.11.28s)
│       ├── Manifest 2 (wersja 2024.3.5s)
│       ├── Manifest 3 (wersja 2024.6.4s)
│       └── ... (wszystkie historyczne wersje)
```

### Manifest

**Manifest** to snapshot plików w depotcie w danym momencie czasu:

```
Manifest ID: 7212344665024119693
├── Build ID: 12345678
├── Creation Date: 2024-03-05
├── Files:
│   ├── Among Us.exe (hash: abc123, size: 45MB)
│   ├── GameAssembly.dll (hash: def456, size: 12MB)
│   ├── UnityPlayer.dll (hash: ghi789, size: 23MB)
│   └── ... (wszystkie pliki gry)
```

**Kluczowe cechy:**
- 🔒 **Niezmienne** - raz stworzony manifest nigdy się nie zmienia
- 📅 **Historyczne** - Steam przechowuje wszystkie manifesty
- 🔗 **Identyfikowalne** - unikalny 64-bitowy Manifest ID
- 📦 **Kompletne** - zawiera hash i metadane każdego pliku

---

## Struktura Danych Steam

### App ID

**Globalny identyfikator aplikacji na Steam**

| Gra | App ID | URL |
|-----|--------|-----|
| Among Us | 945360 | https://steamdb.info/app/945360/ |
| CS:GO | 730 | https://steamdb.info/app/730/ |
| Dota 2 | 570 | https://steamdb.info/app/570/ |

### Depot ID

**Identyfikator magazynu plików dla danej platformy**

| App | Depot ID | Platforma | Typ |
|-----|----------|-----------|-----|
| Among Us | 945361 | Windows | Binaries |
| Among Us | 945362 | macOS | Binaries |
| Among Us | 945363 | Linux | Binaries |

**Dla SUSModder:** Interesuje nas tylko **945361** (Windows)

### Manifest ID

**Unikalny identyfikator wersji plików**

Przykłady dla Among Us (depot 945361):

```
Wersja Gry      → Manifest ID
─────────────────────────────
2023.11.28s     → 6234567890123456789
2024.3.5s       → 7212344665024119693
2024.6.4s       → 8901234567890123456
2024.8.13s      → 9012345678901234567
```

**Format:** 64-bitowa liczba (zwykle reprezentowana jako string)

### Build ID

**Wewnętrzny numer buildu w Steam**

```
Build ID: 12345678
  ↓
Odpowiada konkretnej wersji gry w Steam Content System
  ↓
Manifest ID: 7212344665024119693
```

**Różnica:**
- Build ID = inkrementalny numer nadawany przez dewelopera
- Manifest ID = hash snapshot plików w depotcie

---

## DepotDownloader - Narzędzie

### Overview

**DepotDownloader** to open-source narzędzie CLI do pobierania depotów Steam.

- 📦 **Repo:** https://github.com/SteamRE/DepotDownloader
- 🔧 **Język:** C# (.NET)
- 📄 **Licencja:** GPL-2.0
- ⭐ **Stars:** ~3.5k (bardzo popularne)

### Instalacja

**Opcja 1: Release binary**
```bash
# Pobierz z GitHub Releases
https://github.com/SteamRE/DepotDownloader/releases/latest

# Rozpakuj
DepotDownloader.exe
```

**Opcja 2: Dotnet tool (wymaga .NET SDK)**
```bash
dotnet tool install -g DepotDownloader
```

**Opcja 3: Build ze źródeł**
```bash
git clone https://github.com/SteamRE/DepotDownloader
cd DepotDownloader
dotnet build -c Release
```

### Podstawowe Użycie

#### 1. Pobranie najnowszej wersji

```bash
DepotDownloader -app 945360 -depot 945361 -dir "C:\Games\AmongUs"
```

**Parametry:**
- `-app 945360` - App ID (Among Us)
- `-depot 945361` - Depot ID (Windows)
- `-dir "path"` - Ścieżka docelowa

**Wynik:** Pobiera **najnowszą** wersję gry

#### 2. Pobranie konkretnej wersji (manifest)

```bash
DepotDownloader -app 945360 -depot 945361 -manifest 7212344665024119693 -dir "C:\Games\AmongUs_2024.3.5s"
```

**Parametry:**
- `-manifest {ID}` - Pobierz konkretny manifest

**Wynik:** Pobiera **dokładną wersję** gry (np. 2024.3.5s)

#### 3. Autoryzacja

**A. Automatyczne (Preferowane) - Użycie Aktywnej Sesji Steam** ⭐

```bash
# Jeśli klient Steam jest uruchomiony i użytkownik zalogowany
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -dir "path"
```

**Jak to działa:**
1. DepotDownloader sprawdza czy Steam jest uruchomiony
2. Odczytuje tokeny autoryzacyjne z `Steam/config/loginusers.vdf`
3. Używa istniejącej sesji - **ZERO interakcji z użytkownikiem** ✅

**Wymagania:**
- ✅ Klient Steam musi być uruchomiony
- ✅ Użytkownik musi być zalogowany w Steam
- ✅ Among Us musi być w bibliotece użytkownika

**B. Steam Guard Login (Fallback - BEZ hasła)**

Jeśli automatyczna metoda nie działa, DepotDownloader może poprosić o **kod Steam Guard**:

```bash
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -username "myuser" -dir "path"
```

**Interakcja:**
```
[DepotDownloader] Please enter your Steam Guard code:
> [Użytkownik wpisuje kod z aplikacji mobilnej]
```

⚠️ **Nie wymaga hasła** - tylko kod 2FA, który i tak użytkownik musi mieć pod ręką.

**C. Anonymous (ograniczone - NIE ZALECANE)**
```bash
DepotDownloader -app 945360 -depot 945361 -anonymous -dir "path"
```
❌ **Nie działa dla Among Us** - wymaga własności gry

#### 4. Informacje o depotcie

```bash
# Lista dostępnych manifestów
DepotDownloader -app 945360 -depot 945361 -info

# Szczegóły konkretnego manifestu
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -info
```

### Output Format

**Standardowy output:**
```
Connecting to Steam...
Logged in anonymously
Getting depot 945361 info...
Downloading manifest 7212344665024119693...

Downloading files:
  [###################################---] 85% - Among Us.exe
  [######################################] 100% - GameAssembly.dll
  
Download complete! 512 files (1.2 GB) in 45 seconds
```

**Exit codes:**
- `0` - Sukces
- `1` - Błąd (np. brak autoryzacji, nieprawidłowy manifest)

### C# Integration

**Wywołanie z SUSModder (Automatyczna Sesja Steam):**

```csharp
public async Task<bool> DownloadDepotAsync(
    string manifestId,
    string targetDirectory,
    IProgress<int> progress)
{
    // 1. Sprawdź czy Steam jest uruchomiony
    if (!IsSteamRunning())
    {
        throw new InvalidOperationException(
            "Klient Steam musi być uruchomiony. Uruchom Steam i spróbuj ponownie.");
    }

    // 2. Wywołaj DepotDownloader (automatycznie użyje aktywnej sesji)
    var startInfo = new ProcessStartInfo
    {
        FileName = "tools/DepotDownloader.exe",
        Arguments = $"-app 945360 -depot 945361 -manifest {manifestId} -dir \"{targetDirectory}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = new Process { StartInfo = startInfo };
    
    process.OutputDataReceived += (sender, e) =>
    {
        if (e.Data != null)
        {
            // Parse progress: "[###---] 45%"
            var match = Regex.Match(e.Data, @"(\d+)%");
            if (match.Success)
            {
                progress.Report(int.Parse(match.Groups[1].Value));
            }
        }
    };

    process.Start();
    process.BeginOutputReadLine();
    await process.WaitForExitAsync();

    return process.ExitCode == 0;
}

private bool IsSteamRunning()
{
    // Sprawdź czy proces Steam.exe jest uruchomiony
    return Process.GetProcessesByName("steam").Length > 0;
}
```

### Wymagania Systemowe

- **OS:** Windows, Linux, macOS
- **.NET Runtime:** 6.0 lub nowszy (self-contained builds nie wymagają)
- **RAM:** ~100-500 MB (zależnie od rozmiaru gry)
- **Disk:** Minimum 2x rozmiar gry (temp files + final)

---

## Jak Pozyskać Manifest ID

### Metoda 1: SteamDB (Ręcznie)

**Krok 1:** Otwórz SteamDB dla Among Us
```
https://steamdb.info/app/945360/depots/
```

**Krok 2:** Wybierz depot Windows (945361)
```
https://steamdb.info/depot/945361/manifests/
```

**Krok 3:** Znajdź interesującą wersję
```
| Date       | Build ID  | Manifest ID          | Size   | Files |
|------------|-----------|----------------------|--------|-------|
| 2024-08-13 | 14523456  | 9012345678901234567  | 1.2 GB | 512   |
| 2024-06-04 | 13987654  | 8901234567890123456  | 1.1 GB | 498   |
| 2024-03-05 | 12345678  | 7212344665024119693  | 1.0 GB | 485   |
```

**Krok 4:** Skoreluj z wersją gry
- Sprawdź patch notes Among Us
- Porównaj daty release
- Weryfikacja przez pobranie i sprawdzenie wersji w grze

### Metoda 2: DepotDownloader Info (Automatycznie)

```bash
DepotDownloader -app 945360 -depot 945361 -info > manifests.txt
```

**Output:**
```
Depot 945361 (Among Us - Windows)
Available Manifests:
  9012345678901234567 - 2024-08-13 14:23:45
  8901234567890123456 - 2024-06-04 10:12:33
  7212344665024119693 - 2024-03-05 09:05:21
  ...
```

**Parsing w C#:**
```csharp
var output = await RunCommandAsync("DepotDownloader.exe", "-app 945360 -depot 945361 -info");
var manifests = ParseManifests(output);

Dictionary<DateTime, string> ParseManifests(string output)
{
    var result = new Dictionary<DateTime, string>();
    var regex = new Regex(@"(\d{19})\s+-\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})");
    
    foreach (Match match in regex.Matches(output))
    {
        string manifestId = match.Groups[1].Value;
        DateTime date = DateTime.Parse(match.Groups[2].Value);
        result[date] = manifestId;
    }
    
    return result;
}
```

### Metoda 3: SteamKit2 (Programatyczne)

**SteamKit2** to C# library do komunikacji z Steam API.

```csharp
using SteamKit2;
using SteamKit2.CDN;

public async Task<List<string>> GetManifestIds(uint appId, uint depotId)
{
    var steamClient = new SteamClient();
    var manager = new CallbackManager(steamClient);
    var steamUser = steamClient.GetHandler<SteamUser>();
    var steamApps = steamClient.GetHandler<SteamApps>();

    // Connect + Login
    steamClient.Connect();
    await manager.WaitForCallback<SteamClient.ConnectedCallback>();
    steamUser.LogOnAnonymous();
    await manager.WaitForCallback<SteamUser.LoggedOnCallback>();

    // Pobierz info o depotcie
    var depotKey = await steamApps.GetDepotDecryptionKey(depotId, appId);
    var manifestIds = await steamApps.GetManifestRequestCode(depotId, appId);

    return manifestIds;
}
```

**Zalety:**
- ✅ Pełna kontrola
- ✅ Dostęp do wszystkich metadanych
- ✅ Brak dependency na CLI tool

**Wady:**
- ❌ Złożona implementacja
- ❌ Wymaga głębokiej znajomości Steam protocol
- ❌ Maintenance przy zmianach w Steam API

### Metoda 4: Community Database (Preferowane)

**Koncepcja:** Stworzenie centralnego repozytorium mapping'ów (analogicznie do Epic manifests).

**Przykład struktury:**

```
GitHub: susmodder/steam-manifests
├── README.md
├── manifests/
│   ├── 945360_among_us.json
│   └── ...
```

**`945360_among_us.json`:**
```json
{
  "app_id": 945360,
  "app_name": "Among Us",
  "depot_id": 945361,
  "platform": "windows",
  "manifests": [
    {
      "version": "2024.8.13s",
      "release_date": "2024-08-13",
      "manifest_id": "9012345678901234567",
      "build_id": 14523456,
      "size_bytes": 1288490188,
      "files_count": 512
    },
    {
      "version": "2024.6.4s",
      "release_date": "2024-06-04",
      "manifest_id": "8901234567890123456",
      "build_id": 13987654,
      "size_bytes": 1152921504,
      "files_count": 498
    },
    {
      "version": "2024.3.5s",
      "release_date": "2024-03-05",
      "manifest_id": "7212344665024119693",
      "build_id": 12345678,
      "size_bytes": 1073741824,
      "files_count": 485
    }
  ],
  "last_updated": "2025-10-28T12:00:00Z"
}
```

**Wykorzystanie w SUSModder:**

```csharp
public async Task<string?> GetManifestIdAsync(string amongVersion)
{
    // Pobierz mapping z GitHub raw
    string url = "https://raw.githubusercontent.com/susmodder/steam-manifests/main/manifests/945360_among_us.json";
    var json = await httpClient.GetStringAsync(url);
    var data = JsonSerializer.Deserialize<SteamManifestsData>(json);
    
    return data.Manifests
        .FirstOrDefault(m => m.Version == amongVersion)
        ?.ManifestId;
}
```

**Zalety:**
- ✅ Prosty w użyciu
- ✅ Community-driven (każdy może dodać nową wersję)
- ✅ Versioning (Git history)
- ✅ Fallback (można cache'ować lokalnie)

---

## Alternatywne Narzędzia

### 1. SteamCMD (Oficjalne Valve)

**Website:** https://developer.valvesoftware.com/wiki/SteamCMD

```bash
# Download
curl -sqL "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" -o steamcmd.zip
unzip steamcmd.zip

# Usage
steamcmd +login anonymous +download_depot 945360 945361 7212344665024119693 +quit
```

**Porównanie z DepotDownloader:**

| Feature | DepotDownloader | SteamCMD |
|---------|----------------|----------|
| **Rozmiar** | ~5 MB | ~20 MB (+ dodatkowe pliki przy pierwszym uruchomieniu) |
| **Łatwość** | ✅ Prosty | ⚠️ Wymaga znajomości składni |
| **Output** | User-friendly | Techniczny |
| **Manifest support** | ✅ Pełne | ✅ Pełne |
| **Cross-platform** | ✅ | ✅ |
| **Dokumentacja** | ⚠️ README | ✅ Oficjalna wiki |

**Rekomendacja:** DepotDownloader (lepszy UX, mniejszy rozmiar)

### 2. Własna Implementacja (SteamKit2)

**Pros:**
- Pełna kontrola
- Brak external dependency
- Integracja native w C#

**Cons:**
- ~1-2 tygodnie development
- Złożona implementacja (CDN chunks, encryption, manifests)
- Maintenance przy zmianach Steam protocol

**Rekomendacja:** Tylko jeśli DepotDownloader ma krytyczne ograniczenia

### 3. Web-based (SteamDB API)

**Koncepcja:** Wykorzystanie SteamDB jako data source.

⚠️ **Problem:** SteamDB nie oferuje oficjalnego API (tylko web scraping)

---

## Podsumowanie

### Dla SUSModder: Rekomendowany Stack

1. **Narzędzie:** DepotDownloader (CLI wrapper)
2. **Manifest source:** Community GitHub repo (fallback do hardcoded)
3. **Auth:** User Steam credentials (saved locally)

### Next Steps

1. ✅ Przeczytaj pełną dokumentację DepotDownloader
2. 🧪 Prototyp: Pobierz jedną wersję Among Us
3. 📝 Stwórz GitHub repo z manifest mappings
4. 🔨 Implementuj `SteamDepotManager.cs`

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
