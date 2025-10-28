# Steam Depot Download - Podsumowanie Projektu

**Data utworzenia:** 2025-10-28  
**Status:** Analiza wstępna  
**Cel:** Zastąpienie obecnego mechanizmu pobierania wersji vanilla Among Us (z zaszyfrowanych archiwów 7z) na legalny system pobierania bezpośrednio z Steam Depot

---

## 📋 Spis Treści

1. [Problem i Motywacja](#problem-i-motywacja)
2. [Obecny Mechanizm (Do Zastąpienia)](#obecny-mechanizm-do-zastąpienia)
3. [Proponowane Rozwiązanie](#proponowane-rozwiązanie)
4. [Podobieństwa z Epic Games](#podobieństwa-z-epic-games)
5. [Kluczowe Pytania](#kluczowe-pytania)
6. [Zakres Zmian](#zakres-zmian)
7. [Struktura Dokumentacji](#struktura-dokumentacji)

---

## Problem i Motywacja

### Obecna Sytuacja

SUSModder obecnie pobiera starsze wersje gry Among Us z **własnych zasobów serwera**:

```
BaseUrl/api/susmodder-download-version?version={version}
  ↓
Zaszyfrowane archiwum 7z (hasło z SecretProvider)
  ↓
Rozpakowanie do katalogu moda
```

### Problemy

1. **Legalność** 🚨
   - Przechowywanie i dystrybucja plików gry jest na granicy legalności
   - Potencjalne naruszenie ToS Steam
   - Ryzyko DMCA takedown

2. **Koszty infrastruktury** 💰
   - Każde archiwum vanilla ~400-600 MB
   - Transfer danych na serwerze
   - Storage dla wielu wersji gry

3. **Maintenance** 🔧
   - Ręczne przygotowanie archiwów dla nowych wersji
   - Szyfrowanie 7z
   - Upload na serwer

4. **Bezpieczeństwo** 🔐
   - Hasło 7z w SecretProvider (Base64 obfuscation)
   - Token HTTP do autoryzacji pobierania

### Dlaczego Zmiana Jest Potrzebna?

✅ **Legalność** - pobieranie bezpośrednio z oficjalnych źródeł Steam  
✅ **Zero kosztów** - brak transferu danych przez nasz serwer  
✅ **Automatyzacja** - Steam automatycznie zarządza wersjami  
✅ **Bezpieczeństwo** - brak przechowywania plików gry  

---

## Obecny Mechanizm (Do Zastąpienia)

### Przepływ dla Steam (ModManager.cs)

```csharp
// 1. Określenie wersji vanilla
string vanilla7zName = $"{modConfig.AmongVersion.Replace("-", "").Replace(".", "")}";
string vanilla7zPath = Path.Combine(vanillaDir, vanilla7zName + ".7z");

// 2. URL do pobrania z własnego serwera
string baseUrl = configuration["Configuration:BaseUrl"] ?? "https://susmodder.boracik.pl/";
string fileUrlAmongUs = $"{baseUrl}api/susmodder-download-version?version={vanilla7zName}";

// 3. Pobieranie z autoryzacją (token z SecretProvider)
await DownloadFileWithMemoryManagementAsync(fileUrlAmongUs, vanilla7zPath, log);

// 4. Rozpakowanie z hasłem
string zipPassword = SecretProvider.Get7zPassword();
await Task.Run(() => Extract7zWithPassword(vanilla7zPath, modFolderPath, zipPassword));
```

### Co Musimy Usunąć?

- ❌ `SecretProvider.Get7zPassword()` - niepotrzebne
- ❌ `Extract7zWithPassword()` - niepotrzebne
- ❌ API endpoint `/api/susmodder-download-version` - niepotrzebne
- ❌ Zaszyfrowane archiwa 7z na serwerze - niepotrzebne
- ❌ Token autoryzacji do pobierania vanilla - niepotrzebne

---

## Proponowane Rozwiązanie

### Koncepcja: Steam Depot Download

Steam przechowuje **wszystkie historyczne wersje gry** w swoich **depot** (magazynach plików).

Każda wersja gry ma:
- **App ID** - ID aplikacji (Among Us = 945360)
- **Depot ID** - ID magazynu plików (np. 945361 dla Windows)
- **Manifest ID** - unikalny identyfikator konkretnej wersji (snapshot plików)

### Przykładowy Przepływ (Nowy System)

```
1. Użytkownik wybiera mod (np. "Sheriff Mod" wymaga Among Us 2024.3.5s)
   ↓
2. SUSModder sprawdza w config.json/API jaki Manifest ID odpowiada 2024.3.5s
   ↓
3. Wywołanie narzędzia DepotDownloader:
   DepotDownloader -app 945360 -depot 945361 -manifest {MANIFEST_ID} -dir "{target}"
   ↓
4. Steam pobiera pliki bezpośrednio z CDN (wymaga logowania do Steam)
   ↓
5. Pliki gry są gotowe do użycia
```

### Narzędzia

Istnieją gotowe rozwiązania open-source:

1. **DepotDownloader** ⭐ (C#, najpopularniejsze)
   - Repo: https://github.com/SteamRE/DepotDownloader
   - Obsługa manifestów
   - CLI tool (można wywołać z Process.Start)
   - Wymaga logowania do Steam (username/password lub saved credentials)

2. **SteamCMD** (oficjalne narzędzie Valve)
   - Wspiera `download_depot <app_id> <depot_id> <manifest_id>`
   - Cięższe, bardziej złożone

3. **Alternatywa: Steam API + własna implementacja**
   - Bezpośrednie wywołania Steam API
   - Wymaga dużo pracy (parsowanie manifestów, chunki itp.)

---

## Podobieństwa z Epic Games

### Epic Games (Obecna Implementacja)

SUSModder już **NIE przechowuje** plików vanilla dla Epic - zamiast tego używa **Legendary** (CLI tool) do pobierania bezpośrednio z Epic CDN:

```csharp
// EpicVersionManager.cs - linie ~593-609
private async Task DownloadManifestAsync(string amongVersionFormatted)
{
    // Manifest z zewnętrznego repo (GitHub)
    string manifestUrl = $"https://github.com/whichtwix/Data/raw/master/epic/manifests/{EpicAppId}_{amongVersionFormatted}.manifest";
    string manifestPath = Path.Combine(manifestDirectory, $"{EpicAppId}_{amongVersionFormatted}.manifest");
    await DownloadFileAsync(manifestUrl, manifestPath);
}

public async Task InstallGameAsync(ModConfiguration modConfig, string amongVersionFormatted)
{
    // Instalacja przez Legendary z manifestem
    string commandArguments = $"install {EpicAppId} -y --manifest \"{manifestFilePath}\" --base-path \"{installDirectory}\"";
    await RunLegendaryCommandAsync(commandArguments);
}
```

**Kluczowa różnica Epic vs Steam (obecnie):**

| Aspekt | Epic (obecne) | Steam (obecne) | Steam (proponowane) |
|--------|---------------|----------------|---------------------|
| Źródło plików | Epic CDN ✅ | Własny serwer ❌ | Steam CDN ✅ |
| Narzędzie | Legendary | Brak | DepotDownloader |
| Manifesty | GitHub repo | Brak | config.json/API |
| Autoryzacja | Legendary login | Token HTTP | Steam login |
| Legalność | ✅ Oficjalne | ⚠️ Szara strefa | ✅ Oficjalne |

### Analogia: Epic Manifesty ≈ Steam Manifesty

**Epic:**
```json
// Manifest: 963137e4c29d4c79a81323b8fab03a40_2024.3.5.manifest
{
  "app_name": "963137e4c29d4c79a81323b8fab03a40",
  "version": "2024.3.5",
  "files": [ ... ]
}
```

**Steam:**
```
Depot: 945361 (Windows)
Manifest ID: 7212344665024119693 (np. dla wersji 2024.3.5s)
```

---

## Kluczowe Pytania

### 1. Gdzie przechowywać mapowanie Manifest ID?

**Opcja A: Rozszerzyć config.json (lokalnie)**
```json
{
  "Id": 0,
  "ModName": "AmongUs",
  "ModType": "Vanilla",
  "AmongVersion": "2024.3.5s",
  "SteamManifestId": "7212344665024119693",  // ← NOWE POLE
  "EpicManifestFile": "963137e4c29d4c79a81323b8fab03a40_2024.3.5.manifest"
}
```

**Opcja B: API endpoint (backend)**
```javascript
// GET /api/steam-manifests
{
  "2024.3.5s": "7212344665024119693",
  "2024.6.4s": "8901234567890123456",
  "2024.8.13s": "9012345678901234567"
}
```

**Opcja C: Hybrydowo (preferowane)**
- config.json przechowuje `SteamManifestId` dla każdego moda
- API `/api/mod-configs` zwraca rozszerzone dane
- Fallback do hardcoded mapping w kodzie dla najczęstszych wersji

### 2. Jak zdobyć Manifest ID dla każdej wersji?

**Problem:** Steam nie publikuje oficjalnej listy manifest ID per wersja gry.

**Rozwiązania:**

1. **SteamDB** (https://steamdb.info/app/945360/depots/)
   - Historia wszystkich buildów
   - Manifest ID dla każdej wersji
   - Ręczne pobranie danych (scraping lub ręcznie)

2. **DepotDownloader - komenda `info`**
   ```bash
   DepotDownloader -app 945360 -depot 945361 -info
   # Zwraca listę wszystkich dostępnych manifestów
   ```

3. **Steam API (nieoficjalne)**
   - SteamKit2 (C# library)
   - Programatyczne odpytywanie o manifesty

4. **Community database**
   - GitHub repo z mappingiem (podobnie jak manifesty Epic)
   - Aktualizowane przez community

### 3. Jak zautoryzować pobieranie?

**Problem:** DepotDownloader wymaga potwierdzenia własności Among Us.

**✅ ROZWIĄZANIE: Użycie Aktywnej Sesji Steam (ZERO interakcji)**

**A. Automatyczne (Preferowane)** ⭐
```csharp
// Wymaga tylko uruchomionego klienta Steam
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -dir "{target}"
```
- ✅ **BEZ podawania hasła/loginu**
- ✅ Automatycznie używa tokenów z `Steam/config/loginusers.vdf`
- ✅ Analogiczne do Legendary (Epic) - wykorzystuje istniejącą sesję
- ✅ Bezpieczne - brak przechowywania credentials
- ⚠️ Wymaga: Steam uruchomiony + zalogowany użytkownik + Among Us w bibliotece

**B. Steam Guard Code (Fallback)**
```bash
# Tylko kod 2FA z aplikacji mobilnej (BEZ hasła)
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -username {user}
> [Prompt: Enter Steam Guard code]
```
- ✅ Nie wymaga hasła
- ✅ Tylko jednorazowy kod 2FA
- ⚠️ Wymaga interakcji użytkownika

**C. Anonymous** ❌
- Nie działa dla płatnych gier

**PORÓWNANIE Z EPIC:**
Legendary (Epic) również wykorzystuje istniejącą sesję - użytkownik loguje się raz przez przeglądarkę, Legendary zapisuje token. Identyczny flow możemy osiągnąć z Steam.

### 4. Integracja z obecnym kodem?

**Miejsce zmian:**

```
SUSModder.Core/
├── GameIntegration/
│   ├── ModManager.cs           # ❌ Usunąć logikę 7z, dodać DepotDownloader
│   ├── SteamDepotManager.cs    # ✨ NOWY - wrapper dla DepotDownloader
│   └── EpicVersionManager.cs   # ✅ Bez zmian (referencja)
│
├── Configuration/
│   └── ModConfig.cs            # ➕ Dodać pole SteamManifestId
│
├── Repositories/
│   └── ConfigRepository.cs     # ➕ API endpoint dla manifestów
│
└── Utilities/
    └── SecretProvider.cs       # ❌ Usunąć Get7zPassword()
```

---

## Zakres Zmian

### Backend (SUSModder API)

1. **Rozszerzyć model `mod_configs`**
   ```sql
   ALTER TABLE mod_configs ADD COLUMN steam_manifest_id VARCHAR(20);
   ```

2. **Endpoint `/api/steam-manifests`** (opcjonalnie)
   ```javascript
   router.get('/steam-manifests', async (req, res) => {
     const manifests = await db.steamManifests.findAll();
     res.json(manifests);
   });
   ```

3. **Usunąć endpoint `/api/susmodder-download-version`**
   - Niepotrzebny po migracji

### Frontend (SUSModder Desktop)

1. **Nowy moduł `SteamDepotManager.cs`**
   - Wrapper dla DepotDownloader
   - Obsługa logowania Steam
   - Progress reporting

2. **Modyfikacja `ModManager.cs`**
   - Usunąć logikę pobierania 7z
   - Zastąpić wywołaniem `SteamDepotManager`

3. **UI dla logowania Steam**
   - Dialog z username/password (opcjonalnie)
   - Zapisywanie credentials lokalnie (szyfrowane)

4. **Aktualizacja `ModConfig.cs`**
   - Dodać pole `SteamManifestId`

### Infrastruktura

1. **DepotDownloader.exe**
   - Dołączyć do `tools/` (podobnie jak 7z.exe)
   - Lub pobierać dynamicznie przy pierwszym użyciu

2. **Usunąć z serwera**
   - Zaszyfrowane archiwa vanilla 7z
   - Skrypty szyfrowania

---

## Struktura Dokumentacji

Pełna dokumentacja składa się z następujących plików:

1. **00_PROJECT_SUMMARY.md** ← Jesteś tutaj
   - Overview projektu
   - Problem i motywacja
   - Wysokopoziomowa koncepcja

2. **01_STEAM_DEPOT_TECHNICAL.md**
   - Jak działają Steam Depots
   - DepotDownloader - szczegóły techniczne
   - Manifest ID - jak je pozyskać

3. **02_ARCHITECTURE.md**
   - Architektura rozwiązania
   - Przepływy danych
   - Integracja z obecnym kodem

4. **03_MANIFEST_MANAGEMENT.md**
   - Mapowanie AmongVersion ↔ Manifest ID
   - API vs config.json vs hardcoded
   - Strategia aktualizacji

5. **04_AUTHENTICATION.md**
   - Strategie logowania Steam
   - Bezpieczeństwo credentials
   - User experience

6. **05_IMPLEMENTATION_PLAN.md**
   - Krok po kroku plan wdrożenia
   - Kolejność zmian
   - Testy i weryfikacja

7. **06_MIGRATION_STRATEGY.md**
   - Przejście z 7z na DepotDownloader
   - Backward compatibility
   - Rollback plan

8. **07_LEGAL_CONSIDERATIONS.md**
   - Legalność rozwiązania
   - Steam ToS analysis
   - Alternatywy

---

## Status i Next Steps

### Obecnie
- ✅ Analiza problemu
- ✅ Research Steam Depot system
- ✅ Porównanie z Epic (Legendary)
- 🔄 Prototyp DepotDownloader integration

### Następne Kroki
1. 📖 Dokończyć dokumentację techniczną (pliki 01-07)
2. 🧪 Proof of Concept - pobranie jednej wersji przez DepotDownloader
3. 🔨 Implementacja `SteamDepotManager`
4. 🎨 UI dla Steam authentication
5. 🧪 Testy end-to-end
6. 🚀 Deployment

---

## Pytania Otwarte (Do Dyskusji)

1. ~~Czy użytkownicy SUSModder mają konta Steam?~~ ✅ TAK - zakładamy że tak (gra jest na Steam)
2. ~~Czy akceptowalne jest wymaganie logowania Steam?~~ ✅ NIE POTRZEBNE - używamy aktywnej sesji
3. Czy preferujemy prosty CLI wrapper (DepotDownloader) czy pełną integrację SteamKit2? → **CLI wrapper**
4. Gdzie hostować bazę Manifest ID? (API, GitHub, hardcoded?) → **Hybrydowo** (wszystkie 3 z fallbackami)
5. Jak obsłużyć użytkowników Epic? → **Dual-system** (zostawić EpicVersionManager bez zmian)

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
**Autor:** Claude (AI Assistant) & boratsc  
