# 16 – Mod Pack Sharing (Kody udostępniania zestawów modów)

**Priorytet:** 🟡 P1 (wysoki — feature społecznościowy)  
**Effort:** ~8-12 dni (pełna implementacja z backendem)  
**Status:** ⏳ **Nie rozpoczęte** — specyfikacja gotowa, plan w DOC/PLAN  
**Plan wdrożenia:** [`DOC/PLAN/2026-05-29-mod-pack-sharing-plan.md`](../PLAN/2026-05-29-mod-pack-sharing-plan.md)  
**Zależy od:** susmodder-backend (`/api/mod-packs`), SUSModder.Core (`ModPackService`, deep link)

---

## Cel

Stworzyc system **dzielenia sie zestawami modow** (mod packs) za pomoca krotkich kodow i linkow. Uzytkownik tworzy "paczke" - wybiera mod full, wersje, mody DLL, opcjonalnie config ToU, integracje susmodder-integration.dll i serwer Discord - i dostaje link/kod, ktory inny uzytkownik moze kliknac lub wkleic, aby zainstalowac dokladnie ten sam zestaw.

**Analogia:** System configow Town of Us (hash -> zapisany config), ale na sterydach - dotyczy calego zestawu modow, nie tylko configu ToU.

---

## Non-goals

- :x: Nie tworzymy marketplace'a modow - paczki sa prywatne, tworzone przez uzytkownikow
- :x: Nie hostujemy plikow DLL modow na serwerze SUSModder - paczka referencjuje mody z publicznego katalogu API (z wyjatkiem zewnetrznych DLL - patrz sekcja bezpieczenstwa)
- :x: Nie tworzymy systemu wersjonowania paczek - paczka jest snapshotem, nie zywym obiektem
- :x: Nie integrujemy z zewnetrznymi repozytoriami modow (CurseForge, Modrinth itp.)
- :x: Nie tworzymy systemu komentarzy/ocen paczek (MVP)

---

## User Workflow

### Tworca paczki (Creator)

`
1. Otwiera SUSModder -> "Udostepnij zestaw modow" (FAB lub menu kontekstowe)
2. Widzi kreator paczki:
   +-------------------------------------------------------------+
   |  :package: Udostepnij zestaw modow                           |
   |                                                             |
   |  Nazwa zestawu: [Moj zestaw do gry z kolegami    ]         |
   |  Twoj nick:     [Boracik                         ]         |
   |                                                             |
   |  -- Mod glowny (full) -----------------------------------   |
   |  Mod:     [Town of Us v]                                   |
   |  Wersja:  [5.4.0 v]  (lub "Najnowsza wersja")             |
   |                                                             |
   |  -- Mody DLL -------------------------------------------    |
   |  :white_check_mark: AleLuduMod v2.0                                         |
   |  :white_check_mark: ExtraRoles v1.3                                         |
   |  :white_square: MayorMod                                                 |
   |  [+ Dodaj wlasny DLL...]                                   |
   |                                                             |
   |  -- Opcjonalnie ----------------------------------------    |
   |  :white_check_mark: Dolacz config Town of Us (zapisany config)              |
   |      Config: [Moj config v2 v]                              |
   |  :white_square: Dolacz susmodder-integration.dll                         |
   |  Serwer Discord: [Psychopaci v] (opcjonalnie)              |
   |                                                             |
   |  -- Czas trwania ----------------------------------------    |
   |  :white_check_mark: 30 dni (domyslny)   :white_square: 7 dni   :white_square: 90 dni      |
   |                                                             |
   |  [Anuluj]                          [:package: Utworz zestaw]       |
   +-------------------------------------------------------------+

3. Po utworzeniu:
   +-------------------------------------------------------------+
   |  :white_check_mark: Zestaw utworzony!                                        |
   |                                                             |
   |  Kod:  ABC-XYZ-123                                          |
   |  Link: https://susmodder.app/pack/ABC-XYZ-123               |
   |                                                             |
   |  [:clipboard: Kopiuj kod]  [:link: Kopiuj link]  [:outbox: Udostepnij na Discord] |
   +-------------------------------------------------------------+
`

### Odbiorca paczki (Receiver)

`
Sposob 1: Klikniecie linku
  https://susmodder.app/pack/ABC-XYZ-123
  -> Otwiera SUSModder (deep link / protocol handler)
  -> Pokazuje podglad zestawu
  -> "Czy chcesz zainstalowac ten zestaw?"

Sposob 2: Wklejenie kodu w SUSModder
  -> Pole "Wklej kod zestawu" w glownym oknie
  -> Pobiera dane z API
  -> Pokazuje podglad i potwierdzenie

Podglad instalacji:
   +-------------------------------------------------------------+
   |  :package: Zestaw: "Moj zestaw do gry z kolegami"                  |
   |  Autor: Boracik                                             |
   |                                                             |
   |  Mod glowny: Town of Us v5.4.0                              |
   |  Mody DLL:                                                  |
   |    * AleLuduMod v2.0                                        |
   |    * ExtraRoles v1.3                                        |
   |  Config ToU: :white_check_mark: Dolaczony                                   |
   |  Discord: Psychopaci                                        |
   |                                                             |
   |  :warning: Ten zestaw zawiera 1 zewnetrzny mod DLL:                |
   |    * CustomMod.dll (nie z publicznego katalogu)             |
   |    :mag: Przeskanowano przez VirusTotal: :white_check_mark: Bezpieczny          |
   |                                                             |
   |  :warning: Zewnetrzne mody DLL moga byc niebezpieczne!             |
   |  Zachowaj szczegolna ostroznosc. Instalujesz na wlasne ryzyko.|
   |                                                             |
   |  [:x: Anuluj]                    [:white_check_mark: Zainstaluj zestaw]       |
   +-------------------------------------------------------------+
`

---

## Core Business Logic (SUSModder.Core)

### Nowe modele

`
SUSModder.Core/Models/ModPack.cs
+-- ModPack                    // Glowny model paczki
|   +-- PackId                 // Krotki kod: "ABC-XYZ-123"
|   +-- Name                   // Nazwa zestawu
|   +-- CreatorName            // Nick tworcy
|   +-- CreatedAt              // Data utworzenia
|   +-- FullModId              // ID moda full z katalogu
|   +-- FullModVersion         // Dokladna wersja moda full
|   +-- DllMods                // Lista modow DLL
|   +-- TouConfigHash          // Hash configu ToU (opcjonalnie)
|   +-- IncludeIntegrationDll  // susmodder-integration.dll
|   +-- DiscordServerId        // ID serwera Discord (opcjonalnie)
|
+-- ModPackDll                 // Mod DLL w paczce
|   +-- DllModId               // ID z katalogu (null = zewnetrzny)
|   +-- DllModName             // Nazwa moda DLL
|   +-- DllModVersion          // Wersja (jesli znana)
|   +-- DownloadUrl            // URL dla zewnetrznych DLL
|   +-- Sha256Hash             // Hash SHA256 (weryfikacja)
|   +-- IsExternal             // true = nie z publicznego katalogu
|   +-- VirusTotalStatus       // "clean" | "suspicious" | "unknown"
|
+-- ModPackInstallResult       // Wynik instalacji
    +-- Success
    +-- ErrorMessage
    +-- InstalledMods           // Lista zainstalowanych
    +-- SkippedMods             // Juz zainstalowane
    +-- FailedMods              // Nieudane
`

### Nowe serwisy

`
SUSModder.Core/Services/
+-- ModPackService.cs          // Tworzenie, pobieranie, instalacja paczek
|   +-- CreatePackAsync()      // POST /api/mod-packs
|   +-- GetPackAsync()         // GET /api/mod-packs/:id
|   +-- InstallPackAsync()     // Instalacja (deleguje do ModManager)
|   +-- ValidatePackAsync()    // Walidacja przed instalacja
|   +-- ScanExternalDllAsync() // VirusTotal check
|
+-- VirusTotalService.cs       // Klient VirusTotal (display only w UI)
|   +-- CheckHashAsync()       // Sprawdzenie statusu skanowania
|   +-- GetScanResultAsync()   // Pobranie wyniku (cache)
|
+-- DeepLinkService.cs         // Obsluga susmodder:// protocol
    +-- RegisterProtocolHandlerAsync()  // Rejestracja w Windows
    +-- ParseDeepLink()        // Parsowanie URI
    +-- HandleDeepLinkAsync()  // Obsluga przychodzacego linku
`

### Deep Link Protocol

`
susmodder://pack/ABC-XYZ-123          -> Otwiera podglad paczki
susmodder://pack/ABC-XYZ-123?install=1 -> Od razu instaluje (z potwierdzeniem)
`

Rejestracja w Windows Registry:
`
HKEY_CLASSES_ROOT\susmodder
    (Default) = "URL:SUSModder Protocol"
    URL Protocol = ""
    shell\open\command
        (Default) = "C:\...\SUSModder.exe" "%1"
`

---

## UI / Avalonia Responsibilities

### Nowe widoki

| Widok | Opis |
|-------|------|
| ModPackCreatorDialog | Kreator tworzenia paczki (wybor modow, config, Discord) |
| ModPackPreviewDialog | Podglad paczki przed instalacja (z ostrzezeniami) |
| ModPackCodeEntryDialog | Pole do wklejenia kodu paczki |
| ModPackResultDialog | Wynik tworzenia paczki (kod + link do skopiowania) |

### Integracja z istniejacym UI

- **FAB** -> nowa opcja ":package: Udostepnij zestaw"
- **Menu kontekstowe moda** -> "Udostepnij jako zestaw"
- **Pasek wyszukiwania** -> pole na kod paczki (obok search)
- **Deep link** -> App.axaml.cs obsluguje susmodder:// URI

### Lokalizacja (PL/EN)

Nowe klucze i18n (sekcja ModPacks):

**PL:**
- CreatorTitle: Udostepnij zestaw modow
- CreatorNameLabel: Twoj nick
- PackNameLabel: Nazwa zestawu
- FullModLabel: Mod glowny
- VersionLabel: Wersja
- DllModsLabel: Mody DLL
- TouConfigLabel: Dolacz config Town of Us
- IntegrationLabel: Dolacz susmodder-integration.dll
- DiscordLabel: Serwer Discord
- CreateButton: Utworz zestaw
- CreatedTitle: Zestaw utworzony!
- CopyCode: Kopiuj kod
- CopyLink: Kopiuj link
- ShareDiscord: Udostepnij na Discord
- PreviewTitle: Podglad zestawu
- InstallButton: Zainstaluj zestaw
- ExternalDllWarning: Ten zestaw zawiera zewnetrzne mody DLL, ktore moga byc niebezpieczne!
- ExternalDllCaution: Zachowaj szczegolna ostroznosc. Instalujesz na wlasne ryzyko.
- VirusTotalClean: Przeskanowano przez VirusTotal: Bezpieczny
- VirusTotalSuspicious: Przeskanowano przez VirusTotal: Podejrzany!
- VirusTotalUnknown: Nie przeskanowano przez VirusTotal
- CodeEntryPlaceholder: Wklej kod zestawu...
- PackNotFound: Zestaw nie istnieje lub wygasl
- InstallSuccess: Zestaw zainstalowany pomyslnie!
- InstallPartial: Zestaw zainstalowany czesciowo (niektore mody pominiete)
- InstallFailed: Nie udalo sie zainstalowac zestawu
TtlLabel: Czas trwania
Ttl7Days: 7 dni
Ttl30Days: 30 dni (domyslny)
Ttl90Days: 90 dni
VersionLatest: Najnowsza wersja
VersionSpecific: Dokladna wersja
PackLimitReached: Osiagnieto limit 10 aktywnych paczek. Usun stara paczke aby utworzyc nowa.
CreatorNameOptional: (opcjonalnie - zostaw puste dla anonimowej paczki)

**EN:**
- CreatorTitle: Share mod pack
- CreatorNameLabel: Your nickname
- PackNameLabel: Pack name
- FullModLabel: Main mod
- VersionLabel: Version
- DllModsLabel: DLL mods
- TouConfigLabel: Include Town of Us config
- IntegrationLabel: Include susmodder-integration.dll
- DiscordLabel: Discord server
- CreateButton: Create pack
- CreatedTitle: Pack created!
- CopyCode: Copy code
- CopyLink: Copy link
- ShareDiscord: Share on Discord
- PreviewTitle: Pack preview
- InstallButton: Install pack
- ExternalDllWarning: This pack contains external DLL mods that may be unsafe!
- ExternalDllCaution: Exercise extreme caution. You install at your own risk.
- VirusTotalClean: Scanned by VirusTotal: Safe
- VirusTotalSuspicious: Scanned by VirusTotal: Suspicious!
- VirusTotalUnknown: Not scanned by VirusTotal
- CodeEntryPlaceholder: Paste pack code...
- PackNotFound: Pack does not exist or has expired
- InstallSuccess: Pack installed successfully!
- InstallPartial: Pack installed partially (some mods skipped)
- InstallFailed: Failed to install pack
TtlLabel: Duration
Ttl7Days: 7 days
Ttl30Days: 30 days (default)
Ttl90Days: 90 days
VersionLatest: Latest version
VersionSpecific: Specific version
PackLimitReached: Reached limit of 10 active packs. Delete an old pack to create a new one.
CreatorNameOptional: (optional - leave empty for anonymous pack)

---

## Backend API Contract (susmodder-backend)

### Nowy endpoint: /api/mod-packs

#### POST /api/mod-packs - Tworzenie paczki

**Auth:** Bearer token (istniejacy SecretProvider.GetDownloadToken())

**Request:**
```json
{
  "name": "Moj zestaw do gry z kolegami",
  "creatorName": "Boracik",
  "creatorHash": "a1b2c3d4e5f6...",
  "fullModId": 1,
  "fullModVersion": "5.4.0",
  "ttl": 30,
  "dllMods": [
    { "dllModId": 5, "dllModVersion": "2.0" },
    { "dllModId": 8, "dllModVersion": "1.3" }
  ],
  "touConfigHash": "abc123def456",
  "includeIntegrationDll": false,
  "discordServerId": "1372226857294106644"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "pack": {
    "packId": "ABC-XYZ-123",
    "name": "Moj zestaw do gry z kolegami",
    "creatorName": "Boracik",
    "createdAt": "2026-05-29T12:00:00Z",
    "fullModId": 1,
    "fullModVersion": "5.4.0",
    "ttl": 30,
    "dllMods": [
      { "dllModId": 5, "dllModName": "AleLuduMod", "dllModVersion": "2.0", "isExternal": false },
      { "dllModId": 8, "dllModName": "ExtraRoles", "dllModVersion": "1.3", "isExternal": false }
    ],
    "touConfigHash": "abc123def456",
    "includeIntegrationDll": false,
    "discordServerId": "1372226857294106644",
    "shareUrl": "https://susmodder.app/pack/ABC-XYZ-123",
    "expiresAt": "2026-06-29T12:00:00Z"
  }
}
```

#### GET /api/mod-packs/:packId - Pobieranie paczki

**Auth:** Brak (publiczny endpoint)

**Response (200 OK):**
```json
{
  "success": true,
  "pack": {
    "packId": "ABC-XYZ-123",
    "name": "Moj zestaw do gry z kolegami",
    "creatorName": "Boracik",
    "createdAt": "2026-05-29T12:00:00Z",
    "fullModId": 1,
    "fullModName": "Town of Us",
    "fullModVersion": "5.4.0",
    "ttl": 30,
    "dllMods": [
      { "dllModId": 5, "dllModName": "AleLuduMod", "dllModVersion": "2.0", "isExternal": false, "downloadUrl": null },
      { "dllModId": 8, "dllModName": "ExtraRoles", "dllModVersion": "1.3", "isExternal": false, "downloadUrl": null }
    ],
    "touConfigHash": "abc123def456",
    "includeIntegrationDll": false,
    "discordServerId": "1372226857294106644",
    "discordServerName": "Psychopaci",
    "expiresAt": "2026-06-29T12:00:00Z"
  }
}
```

#### POST /api/mod-packs/:packId/external-dll - Upload zewnetrznego DLL

**Auth:** Bearer token

**Request:** multipart/form-data z plikiem DLL

**Response (200 OK):**
```json
{
  "success": true,
  "dllEntry": {
    "dllModId": null,
    "dllModName": "CustomMod.dll",
    "isExternal": true,
    "downloadUrl": "https://susmodder.app/api/mod-packs/ABC-XYZ-123/dlls/abc123",
    "sha256Hash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "virusTotalStatus": "pending",
    "fileSize": 524288
  }
}
```

#### GET /api/mod-packs/:packId/dlls/:dllHash - Pobieranie zewnetrznego DLL

**Auth:** Brak (publiczny - ale sprawdzane z packId)

**Response:** Plik DLL z naglowkiem Content-Disposition: attachment

### Baza danych - nowe tabele (PostgreSQL, susmodder-backend)

```sql
CREATE TABLE mod_packs (
    id              SERIAL PRIMARY KEY,
    pack_id         VARCHAR(16) UNIQUE NOT NULL,
    name            VARCHAR(200) NOT NULL,
    creator_name    VARCHAR(100),              -- opcjonalny nick (moze byc null = anonim)
    creator_hash    VARCHAR(64) NOT NULL,      -- SHA256 z Hardware ID (limit 10 paczek/user)
    full_mod_id     INTEGER NOT NULL REFERENCES config(id),
    full_mod_version VARCHAR(50) NOT NULL,     -- "5.4.0" lub "latest"
    tou_config_hash VARCHAR(100),
    include_integration_dll BOOLEAN DEFAULT FALSE,
    discord_server_id VARCHAR(50),
    ttl             INTEGER NOT NULL DEFAULT 30, -- 7, 30 lub 90 dni
    created_at      TIMESTAMP DEFAULT NOW(),
    expires_at      TIMESTAMP NOT NULL,         -- obliczane: created_at + ttl
    
    CONSTRAINT uq_pack_id UNIQUE (pack_id)
);

CREATE INDEX idx_mod_packs_expires ON mod_packs(expires_at);

CREATE TABLE mod_pack_dlls (
    id              SERIAL PRIMARY KEY,
    pack_id         VARCHAR(16) NOT NULL REFERENCES mod_packs(pack_id) ON DELETE CASCADE,
    dll_mod_id      INTEGER REFERENCES config(id),
    dll_mod_name    VARCHAR(200) NOT NULL,
    dll_mod_version VARCHAR(50),
    is_external     BOOLEAN DEFAULT FALSE,
    download_url     VARCHAR(500),
    sha256_hash     VARCHAR(64),
    virus_total_status VARCHAR(20) DEFAULT 'unknown',
    file_size       INTEGER,
    created_at      TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_mod_pack_dlls_pack ON mod_pack_dlls(pack_id);

CREATE TABLE mod_pack_tou_configs (
    id              SERIAL PRIMARY KEY,
    pack_id         VARCHAR(16) NOT NULL REFERENCES mod_packs(pack_id) ON DELETE CASCADE,
    config_hash     VARCHAR(100) NOT NULL,
    config_data     JSONB NOT NULL,            -- Pelny config ToU
    created_at      TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_mod_pack_tou_configs_pack ON mod_pack_tou_configs(pack_id);
```

### Cron: Cleanup wygaslych paczek

```sql
DELETE FROM mod_packs WHERE expires_at < NOW();
```

---

## Zewnetrzne mody DLL - Bezpieczenstwo

### Problem

Uzytkownik moze dodac do paczki wlasny mod DLL, ktory nie jest w publicznym katalogu SUSModder. To stwarza ryzyko bezpieczenstwa - DLL moze zawierac malware.

### Rozwiazanie: VirusTotal + wielopoziomowe ostrzezenia

#### Poziom 1: VirusTotal scan (automatyczny, backend-side)

1. Uzytkownik uploaduje DLL przez POST /api/mod-packs/:packId/external-dll
2. Backend oblicza SHA256 i wysyla do VirusTotal API v3
3. Backend zapisuje status: pending -> clean / suspicious
4. Wynik jest cachowany - ponowne skanowanie tego samego hasha nie wymaga ponownego uploadu

#### Poziom 2: UI warning (wymagany, nie do pominiencia)

Przed instalacja paczki z zewnetrznym DLL:
- **Czerwone ostrzezenie** z ikona :warning:
- Tekst: "Ten zestaw zawiera zewnetrzne mody DLL, ktore moga byc niebezpieczne!"
- Dodatkowy tekst: "Zachowaj szczegolna ostroznosc. Instalujesz na wlasne ryzyko."
- Jesli VirusTotal = suspicious: **DRUGIE ostrzezenie** z informacja o podejrzeniu
- Jesli VirusTotal = unknown: ostrzezenie ze plik nie zostal przeskanowany
- Przycisk "Zainstaluj" jest **domyslnie ukryty** - trzeba kliknac checkbox "Rozumiem ryzyko"

#### Poziom 3: Ograniczenia zewnetrznych DLL

- Maksymalny rozmiar pliku: **10 MB** (wiekszosc modow DLL to <1 MB)
- Maksymalna liczba zewnetrznych DLL w paczce: **3**
- Pliki DLL sa przechowywane na CDN z 30-dniowym TTL (jak paczka)
- Po wygasnieciu paczki, pliki DLL sa usuwane z CDN
- **Nigdy** nie wykonujemy zewnetrznych DLL automatycznie - instalacja wymaga jawnej zgody

#### VirusTotal API (backend-side)

`
susmodder-backend/routes/mod-packs.js
+-- POST /api/mod-packs/:packId/external-dll
|   +-- Multer upload (max 10MB)
|   +-- SHA256 hash calculation
|   +-- Check VirusTotal cache (by hash)
|   +-- If not cached: POST /files to VirusTotal
|   +-- Store file on CDN
|   +-- Return status: pending/clean/suspicious
|
+-- Background job: Poll VirusTotal results
|   +-- GET /analyses/{id} for pending scans
|   +-- Update mod_pack_dlls.virus_total_status
|   +-- Delete suspicious files from CDN
|
+-- GET /api/mod-packs/:packId/dlls/:dllHash
    +-- Verify pack exists and not expired
    +-- Serve file from CDN
    +-- Content-Disposition: attachment
`

---

## Config i migracja

### Nowe ustawienia uzytkownika (SQLite)

| Klucz | Typ | Domyslny | Opis |
|-------|-----|----------|------|
| modPacksEnabled | bool | true | Czy pokazywac opcje udostepniania zestawow |
| modPacksAutoInstall | bool | false | Czy automatycznie instalowac po kliknieciu linku (bez potwierdzenia) |

Dodane do user_settings przez migracje PRAGMA user_version.

### Lokalna historia paczek (SQLite)

```sql
CREATE TABLE IF NOT EXISTS mod_pack_history (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    pack_id         TEXT NOT NULL,
    name            TEXT NOT NULL,
    creator_name    TEXT NOT NULL,
    installed_at    TEXT NOT NULL,
    mods_installed  TEXT NOT NULL
);
```

### appsettings.json - nowy endpoint

```json
{
  "Configuration": {
    "ModPacksEndpoint": "/api/mod-packs"
  }
}
```

Uwaga: appsettings.json jest read-only w runtime. Nowe endpointy sa dodawane do sekcji Configuration i odczytywane przez IConfiguration.

---

## Platform, Packaging, Updater, Telemetry, Privacy, AV

### Platform (Windows)

- Deep link protocol handler (susmodder://) wymaga rejestracji w Windows Registry
- Rejestracja w HKEY_CURRENT_USER\Software\Classes\susmodder (nie wymaga UAC)
- Rejestracja przy pierwszym uruchomieniu lub przez Velopack custom step

### Packaging (Velopack)

- Brak zmian w procesie budowania
- Deep link handler rejestrowany w Program.cs przy starcie

### Updater

- Brak wplywu na Velopack - paczki sa runtime-only

### Telemetry

- Nowe eventy: pack_created, pack_installed, pack_install_failed
- Anonimizowane: pack_id, full_mod_id, dll_count, has_external_dll
- **NIE** wysylamy: nazwy zewnetrznych DLL, download URLs

### Privacy

- Zewnetrzne DLL sa uploadowane na CDN SUSModder - uzytkownik musi byc swiadomy
- VirusTotal scan jest wykonywany automatycznie - uzytkownik nie moze go pominac
- Paczki wygasaja po 30 dniach - dane sa usuwane
- creatorName jest opcjonalny - paczki moga byc anonimowe

### AV (Antivirus)

- Zewnetrzne DLL moga wywolac false positive w AV
- VirusTotal clean status jest pokazywany w UI jako dodatkowa informacja
- **Nie** probujemy omijac AV - transparentnosc jest kluczowa

---

## Verification Plan

### Testy jednostkowe (SUSModder.Core)

| Test | Opis |
|------|------|
| ModPackService_CreatePack_ValidRequest_ReturnsPack | Tworzenie paczki z poprawnymi danymi |
| ModPackService_CreatePack_InvalidModId_Throws | Bledny ID moda full |
| ModPackService_GetPack_ValidId_ReturnsPack | Pobieranie paczki |
| ModPackService_GetPack_ExpiredId_ReturnsNull | Wygasla paczka |
| ModPackService_InstallPack_Success | Pelna instalacja |
| ModPackService_InstallPack_ModAlreadyInstalled_Skips | Pomieniecie zainstalowanego |
| ModPackService_ValidatePack_ExternalDll_Warns | Ostrzezenie o zewnetrznym DLL |
| VirusTotalService_CheckHash_Clean | VirusTotal clean |
| VirusTotalService_CheckHash_Suspicious | VirusTotal suspicious |
| DeepLinkService_ParseDeepLink_Valid | Parsowanie susmodder://pack/ABC |
| DeepLinkService_ParseDeepLink_Invalid | Bledny deep link |

### Testy integracyjne (backend)

| Test | Opis |
|------|------|
| POST /api/mod-packs - 201 | Tworzenie paczki |
| POST /api/mod-packs - 400 | Bledne dane |
| GET /api/mod-packs/:id - 200 | Pobieranie paczki |
| GET /api/mod-packs/:id - 404 | Nieistniejaca paczka |
| GET /api/mod-packs/:id - 410 | Wygasla paczka |
| POST /api/mod-packs/:id/external-dll - 200 | Upload DLL |
| POST /api/mod-packs/:id/external-dll - 413 | Za duzy plik |
| Cleanup cron - usuwa wygasle paczki | |

### Testy E2E (UI)

| Test | Opis |
|------|------|
| Tworzenie paczki -> kopiowanie kodu -> wklejenie w drugiej instancji | Pelny flow |
| Klikniecie linku susmodder://pack/ABC -> podglad -> instalacja | Deep link |
| Paczka z zewnetrznym DLL -> ostrzezenie -> checkbox -> instalacja | Bezpieczenstwo |
| Wygasla paczka -> komunikat "nie istnieje lub wygasla" | Expiry |

---

## Suggested Implementation Order

### Faza 1: Backend API (3-4 dni) - rownolegle z Faza 2

| # | Zadanie | Effort | Zaleznosci |
|---|---------|--------|------------|
| 1.1 | Nowa tabela mod_packs + mod_pack_dlls w PostgreSQL | ~2h | - |
| 1.2 | POST /api/mod-packs - tworzenie paczki | ~4h | 1.1 |
| 1.3 | GET /api/mod-packs/:packId - pobieranie paczki | ~2h | 1.1 |
| 1.4 | POST /api/mod-packs/:packId/external-dll - upload DLL | ~4h | 1.1, VirusTotal API key |
| 1.5 | GET /api/mod-packs/:packId/dlls/:dllHash - download DLL | ~2h | 1.4 |
| 1.6 | Cron: cleanup wygaslych paczek | ~1h | 1.1 |
| 1.7 | VirusTotal integration (backend-side) | ~3h | 1.4 |
| 1.8 | Swagger docs + testy | ~2h | 1.2-1.7 |

### Faza 2: Core Logic (3-4 dni) - rownolegle z Faza 1

| # | Zadanie | Effort | Zaleznosci |
|---|---------|--------|------------|
| 2.1 | ModPack model + ModPackDll model | ~1h | - |
| 2.2 | ModPackService - create, get, validate | ~4h | 2.1 |
| 2.3 | ModPackService - install (integracja z ModManager) | ~6h | 2.2, Faza 1 |
| 2.4 | VirusTotalService - client-side display | ~2h | 2.1 |
| 2.5 | DeepLinkService - protocol handler | ~3h | - |
| 2.6 | SQLite migration: mod_pack_history | ~1h | - |
| 2.7 | appsettings.json - nowe endpointy | ~0.5h | - |

### Faza 3: UI (3-4 dni) - po Fazie 2

| # | Zadanie | Effort | Zaleznosci |
|---|---------|--------|------------|
| 3.1 | ModPackCreatorDialog - kreator paczki | ~6h | 2.2 |
| 3.2 | ModPackPreviewDialog - podglad + ostrzezenia | ~4h | 2.3, 2.4 |
| 3.3 | ModPackCodeEntryDialog - pole na kod | ~2h | 2.2 |
| 3.4 | ModPackResultDialog - kod + link | ~2h | 2.2 |
| 3.5 | Integracja z FAB / menu kontekstowym | ~2h | 3.1 |
| 3.6 | Deep link handling w App.axaml.cs | ~2h | 2.5 |
| 3.7 | Lokalizacja PL/EN - klucze i18n | ~2h | 3.1-3.4 |

### Faza 4: Testy i polish (2 dni) - po Fazie 3

| # | Zadanie | Effort | Zaleznosci |
|---|---------|--------|------------|
| 4.1 | Testy jednostkowe Core | ~4h | Faza 2 |
| 4.2 | Testy integracyjne backend | ~3h | Faza 1 |
| 4.3 | Testy E2E (manualne) | ~3h | Faza 3 |
| 4.4 | Edge cases: offline, wygasla paczka, bledny kod | ~2h | Faza 3 |
| 4.5 | AV false positive testing | ~2h | 1.7 |

---

## Language / i18n Impact

- **Nowe klucze:** ~25 kluczy w sekcji ModPacks (PL + EN)
- **Placeholdery:** {0} dla nazw modow, wersji, kodow paczek
- **Ostrzezenia bezpieczenstwa:** musza byc bardzo wyrazne w obu jezykach
- **Future locale:** dodanie nowego jezyka = dodanie pliku JSON, zero zmian w logice
- **Product names:** SUSModder, Among Us, Steam, Epic Games, Discord, VirusTotal - nie tlumaczone

---


## Resolved Decisions

### 1. TTL paczek: Wybor uzytkownika (7 / 30 / 90 dni)

Uzytkownik wybiera TTL przy tworzeniu paczki:
- **7 dni** - szybkie udostepnienie na sesje
- **30 dni** - domyslny, wystarczajacy dla wiekszosci przypadkow
- **90 dni** - dlugoterminowe zestawy (np. polecane zestawy od spolecznosci)

Backend: pole expires_at obliczane na podstawie wybranego TTL. Cron usuwa wygasle paczki co 24h.

### 2. Limit paczek: 10 aktywnych na userhash

- Limit: **10 aktywnych paczek** na userHash (SHA256 z Hardware ID, juz uzywany w telemetrii)
- Przy probie utworzenia 11. paczki: komunikat o limicie + opcja usuniecia najstarszej
- Backend: SELECT COUNT(*) FROM mod_packs WHERE creator_hash = ? AND expires_at > NOW() przed INSERT

### 3. Edycja paczek: Tak, ale nizszy priorytet (post-MVP)

**MVP:** Brak edycji - paczka jest snapshotem, nie da sie jej zmienic po utworzeniu.
**Post-MVP (P2):**
- Edycja nazwy paczki
- Aktualizacja konkretnych modow (zarowno full jak i DLL) - tworzy nowa wersje paczki
- Zachowanie historii zmian (opcjonalnie)
- Stara wersja paczki pozostaje dostepna pod tym samym kodem do wygasniecia

### 4. Anonimowosc tworcy: Opcjonalny nick, domyslnie anonim

- creatorName jest **opcjonalny** - domyslnie pusty (anonim)
- Uzytkownik moze podac nick, ale nie jest to wymagane
- SUSModder zawsze byl anonimowy (telemetria uzywajaca userHash) - zachowujemy ta zasade
- Nick jest czysto spoleczniowa funkcja - ulatwia identyfikacje zestawu

### 5. susmodder-integration.dll

**Co robi ten DLL:**
- BepInEx plugin w procesie Among Us — auto-detekcja kodu lobby
- Integracja z BetterCrewLink (plan 12) — przyszłość
- Powiązany z planem 10 — bridge file → `LobbyBridgeFileReader`

**Status:** ✅ **E2E działa** w repo `D:\Development\susmodder-integration` (TOU-Mira, Syzyf). Brak dystrybucji przez katalog API — plan V2.0.3.

**W paczce:** Opcja „Dołącz susmodder-integration.dll" dostępna gdy DLL jest w katalogu / zainstalowany w folderze moda.
**W paczce:** Opcja "Dolacz susmodder-integration.dll" jest dostepna tylko jesli DLL istnieje w katalogu modow.
**Bezpieczenstwo:** Poniewaz jest to wlasny DLL SUSModder (nie zewnetrzny), nie wymaga VirusTotal scan - jest traktowany jak katalogowy mod DLL.

### 6. VirusTotal API key: SUSModder backend

- Klucz VirusTotal jest juz dostepny na backendzie (uzywany do skanowania wersji SUSModder)
- Backend wykonuje skanowanie - klient tylko wyswietla status
- Brak potrzeby podawania klucza przez uzytkownika

### 7. Deep link fallback: Przekierowanie na susmodder.app

- Jesli SUSModder nie jest zainstalowany: https://susmodder.app/pack/ABC-XYZ-123
- Strona web pokazuje: nazwe zestawu, liste modow, przycisk "Pobierz SUSModder"
- Po zainstalowaniu SUSModder: deep link susmodder://pack/ABC-XYZ-123 zadziala automatycznie
- Wymaga strony web na susmodder.app (prosta strona z informacja o paczce + download)

### 8. ToU config w paczce: Pelny config na serwerze (lepsza opcja)

- Zamiast samego hasha, paczka zawiera **pelny config ToU** (zapisany na serwerze)
- Backend przechowuje config ToU w tabeli mod_pack_tou_configs
- Przy instalacji: SUSModder pobiera pelny config i aplikuje go
- Hash jest uzywany jako klucz do identyfikacji (deduplikacja)
- To pozwala odbiorcy zainstalowac dokladnie ten sam config bez recznego kopiowania

Nowa tabela:

```sql
CREATE TABLE mod_pack_tou_configs (
    id              SERIAL PRIMARY KEY,
    pack_id         VARCHAR(16) NOT NULL REFERENCES mod_packs(pack_id) ON DELETE CASCADE,
    config_hash     VARCHAR(100) NOT NULL,
    config_data     JSONB NOT NULL,        -- Pelny config ToU
    created_at      TIMESTAMP DEFAULT NOW()
);
```

### 9. Wersja moda full: Uzytkownik sam decyduje

- **Dokladna wersja** (np. "5.4.0") - instaluje konkretna wersje
- **Zawsze najnowsza** (opcja "latest") - instaluje najnowsza dostepna wersje w momencie instalacji
- W kreatorze paczki: dropdown z opcja "Najnowsza wersja" lub wybor konkretnej wersji
- W API: pole fullModVersion przyjmuje "latest" lub konkretna wersje (np. "5.4.0")
- Przy instalacji paczki z "latest": SUSModder sprawdza aktualna wersje z API i instaluje ja

---

## Sources used

- mcp-rag: ModConfiguration model, ConfigManager, ToU config system, backend API routes, ModVersionService
- Local files: 12-voice-chat-integration.md, 10-lobby-code-sharing.md, 00-decyzje-negatywne.md, README.md (frontend-ideas)
- CLAUDE.md: Architecture, data layer, appsettings.json structure
- .opencode/instructions/susmodder-data-layer.md: SQLite migration patterns