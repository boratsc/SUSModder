# SUSModder API — audyt E2E (produkcja)

> **Data audytu:** 2026-06-07  
> **Status:** ✅ **REMEDIATED** (backend wdrożony 2026-06-07, zweryfikowane ponownie)  
> **Środowisko:** `https://api.susmodder-cdn.ovh/v2`, legacy `https://susmodder.app`, CDN `https://susmodder-cdn.ovh`  
> **Skrypt testów:** `SKRYPTY/Test/test-api-e2e.ps1`  
> **Wyniki JSON (przed fixem):** `SKRYPTY/Test/api-e2e-results-2026-06-07-0916.json`

---

## Podsumowanie wykonawcze (po remediacji)

| Kategoria | Status | Uwagi |
|-----------|--------|-------|
| **Pobrania modów (v1 + v2)** | ✅ 20/20 → HTTP 200 | CDN URL bez prefiksu `/mods/` |
| **`iconUrl` w katalogu** | ✅ | `/api/v2/icons/{filename}` → 302 → `susmodder.app/icons/` |
| **Warianty brakujące** | ✅ | 12 nowych wariantów (cdn-bulk-upload) |
| **`versions/2025-3-31`** | ✅ | INSERT do `among_us_versions` |
| **Legacy `/api/mod-download/`** | ✅ | Reguła nginx na produkcji |
| **Lobby GET** | ⚠️ | Wymaga `Authorization: Bearer` (intencjonalne) |

---

## Podsumowanie wykonawcze (stan przed fixem — archiwum)

| Kategoria | Liczba | Wpływ na klienta |
|-----------|--------|------------------|
| **Krytyczne (blokują instalację)** | 1 | Wszystkie pobrania modów → 404 na CDN |
| **Brak danych w API** | 2 | 8 modów bez wariantów; `iconUrl` = null wszędzie |
| **Legacy v1 usunięte z susmodder.app** | 9 | OK jeśli klient używa v2 (częściowo tak) |
| **Działające endpointy v2** | ~20 GET + heartbeat | Katalog, role, Discord, updater, compat |
| **Wymagają auth (nie testowane w pełni)** | lobby, admin, sustats write | Zależą od tokena |

---

## Krytyczne — blokują instalację modów

### `GET /v2/downloads/mod/:id/:version?platform=steam&arch=x86`

**Status:** API działa (302 + nagłówki `X-SUSModder-SHA256`, `X-SUSModder-File-Size`), **CDN nie ma plików**.

Przetestowano wszystkie 12 modów z wariantami w katalogu — **każdy** kończy się `FINAL:404`:

| Mod ID | Nazwa | Wersja | API | CDN po redirect |
|--------|-------|--------|-----|-----------------|
| 1 | Town of Us | 5.3.1 | 302 | **404** |
| 2 | ToU - Wygon | 2.0.0 | 302 | **404** |
| 4 | The Other Roles | 4.8.0 | 302 | **404** |
| 6 | ToH Enhanced | 2.4.1 | 302 | **404** |
| 8 | AUnlocker (DLL) | 1.3.0 | 302 | **404** |
| 10 | CrowdedMod (DLL) | 2.10.0 | 302 | **404** |
| 11 | Town Of Us Edited | 1.1.5 | 302 | **404** |
| 12 | Endless Host Roles | 7.5.1 | 302 | **404** |
| 13 | Town of Us Mira | 1.6.2 | 302 | **404** |
| 15 | Stellar Roles | 2026.6.2 | 302 | **404** |
| 16 | All The Roles | 0.13.0 | 302 | **404** |
| 19 | Town of Host | 5.1.14 | 302 | **404** |

**Przyczyna:** Metadane wariantów (SHA256, `downloadUrl`) są w DB, ale artefakty **nie zostały wrzucone na CDN** (`susmodder-cdn.ovh/mods/...`). Endpoint `POST /admin/mods/:id/download-to-cdn` istnieje, ale nie został uruchomiony dla tych modów.

**Epic:** `GET .../downloads/mod/1/5.3.1?platform=epic` → `VARIANT_NOT_FOUND` + `legacyFallbackAvailable: true` — brak wariantu epic w DB.

---

## Brak danych w API v2 (nie endpoint, ale dane)

### 1. Puste `variants[]` — modów nie da się pobrać w ogóle

| ID | Mod | Wersja |
|----|-----|--------|
| 5 | AleLuduMod | 1.1.2 |
| 7 | Syzyfowy ToU | 7.2.0-pr3-fix |
| 9 | LevelImposter | 0.21.2 |
| 14 | Syzyfowa Beta | 7.2.0-pr3-fix |
| 17 | Mira - Stats Exporter | 1.0.4 |
| 18 | Chaos Tokens | 1.2.0 |
| 20 | Mega Chujowe Perfect Comms | 1.0.5.1 |
| 21 | TownOfUsMegaChujoweExtension | 1.3.2 |

API zwraca `404 VARIANT_NOT_FOUND` — to nie błąd routingu, tylko **brak wpisu wariantu z SHA256**.

### 2. `iconUrl: null` we wszystkich 20 modach w `/catalog`

Ikony istnieją tylko w legacy `GET /api/susmodder-config` (`PngFileName: "10.png"` itd.) i są serwowane z **`https://susmodder.app/icons/`** (200 OK), **nie** z CDN.

| URL | Status |
|-----|--------|
| `susmodder.app/icons/10.png` | 200 |
| `susmodder.app/icons/syzyf-beta.png` | 200 |
| `susmodder-cdn.ovh/icons/*` | 404 |

### 3. Niespójność wersji Among Us

- Katalog mod 1: `amongVersion.dbValue = "2025-3-31"`
- `GET /v2/versions/2025-3-31` → **404** `"Version not found"`
- `GET /v2/versions/2026-3-31` → 200

Mod odnosi się do wersji AU, której **nie ma w tabeli `versions`**.

### 4. Niespójność nazwy pliku DLL w wariancie

Mod 8 (AUnlocker): wariant `version: "1.3.0"`, ale `downloadUrl` wskazuje `AUnlocker_v1.2.2.dll` — potencjalny błąd migracji danych.

---

## Legacy v1 na `susmodder.app` — usunięte / niedostępne

Te endpointy zwracają **404** na głównym nginxie. Klient 3.x powinien używać odpowiedników v2:

| Legacy (404) | Zamiennik v2 | v2 status |
|--------------|--------------|-----------|
| `/api/mod-download/:id/:ver` | `/v2/downloads/mod/:id/:ver` | route OK, CDN 404 |
| `/api/releases?channel=` | `/v2/releases?channel=` | 200 |
| `/api/lobby-board` | `/v2/lobby` | 401 bez tokena |
| `/api/roles-modifiers` | `/v2/roles` | 200 |
| `/api/susmodder-discordfavs` | `/v2/discord/favs/public` | 200 |
| `/api/public/discord-server-counts` | `/v2/discord/server-counts` | 200 |
| `/api/compatibility` | `/v2/compatibility` | 200 (`fullModId`, nie `modId`) |
| `/api/mod-packs` | `/v2/modpacks` | route istnieje |
| `/api/among-tokens` | brak w v2 | 404 (SUStats legacy) |

**Nadal działające legacy:**

| Endpoint | Status | Uwagi |
|----------|--------|-------|
| `GET /api/susmodder-config` | 200 | Używane przez klienta jako fallback ikon |
| `GET /api/online-users` | 200 | Zastąpione przez `/v2/online` |
| `GET /releases/release/*.nupkg` | 200 | Pliki Velopack na susmodder.app |

---

## Działające endpointy API v2

### Faza 1 — Core MVP

| Endpoint | HTTP | Uwagi |
|----------|------|-------|
| `GET /catalog` | 200 | ETag/304 OK |
| `GET /catalog/:id` | 200 | Warianty w detail |
| `GET /catalog/:id/versions` | 200 | |
| `GET /catalog-meta` | 200 | 4 rewizje |
| `GET /versions` | 200 | 22 wersje AU |
| `GET /versions/:dbValue` | 200/404 | 404 gdy brak w DB |
| `GET /versions/:dbValue/steam` | 200 | Manifest Steam |
| `GET /versions/:dbValue/epic` | 200 | Manifest Epic |

### Faza 2 — Compatibility + Roles

| Endpoint | HTTP | Uwagi |
|----------|------|-------|
| `GET /compatibility?fullModId=1` | 200 | Parametr `modId` → **400** |
| `GET /compatibility/snapshot` | 200 | Bez `amongVersion` w query |
| `GET /roles` | 200 | 208 encji |

### Faza 3 — Public

| Endpoint | HTTP | Uwagi |
|----------|------|-------|
| `GET /releases?channel=release` | 200 | latest 2.6.1 |
| `GET /releases?channel=beta` | 200 | latest 2.7.1-beta |
| `GET /telemetry/stats` | 200 | |
| `GET /telemetry/health` | 200 | Redis connected |
| `POST /telemetry/heartbeat` | 200 | Wymaga poprawnego semver (`1.0.0` OK, `3.0.0` → 400) |
| `GET /discord/favs/public` | 200 | 8 serwerów |
| `GET /discord/server-counts` | 200 | Live counts |
| `GET /online` | 200 | |
| `GET /virustotal/report` | 200 | |

### Faza 4 — Lobby / ModPacks / Sustats

| Endpoint | HTTP | Uwagi |
|----------|------|-------|
| `GET /lobby` | **401** | Wymaga `Authorization` (bez tokena: `"No token provided"`) |
| `POST /lobby` | nie test. | Wymaga `X-User-Hash` |
| `GET /modpacks/:code` | 400 | Route istnieje (walidacja kodu) |
| `GET /sustats/games` | 400 | Route istnieje (brak parametrów) |
| `GET /sustats/drafts/stats` | 200 | `degraded: true`, storage niedostępny |

### Faza 5 — Admin (51 endpointów)

Wszystkie zwracają **401** bez `Authorization` + `X-Admin-API-Secret` — routing działa, nie testowano z sekretami.

---

## Zewnętrzne (poza susmodder-api)

| Usługa | Endpoint | Status |
|--------|----------|--------|
| Clair | `GET clairbot.app/api/susmodder/config` | 200 |
| Alternate host | `api.susmodder.app/api/v2/catalog` | 200 |
| Swagger | `susmodder.app/api-docs` | dostępny |

---

## Mapowanie: co klient 3.x faktycznie woła

| Funkcja w aplikacji | Endpoint | Backend OK? |
|---------------------|----------|-------------|
| Lista modów | `/v2/catalog` | tak |
| Instalacja FULL/DLL | `/v2/downloads/mod/...` | **nie** — CDN 404 |
| Role | `/v2/roles` | tak |
| Discord ulubione | `/v2/discord/favs/public` | tak |
| Kompatybilność DLL | `/v2/compatibility` | tak |
| Velopack updater | `/v2/releases` | tak (+ pliki .nupkg na susmodder.app) |
| Telemetria | `/v2/telemetry/heartbeat` | tak |
| Ikony modów | legacy config + `susmodder.app/icons/` | częściowo — v2 nie ma `iconUrl` |
| Lobby board | `/v2/lobby` | wymaga tokena; format `data: [...]` |
| Mod packs | `/v2/modpacks` | nie test. z prawdziwym kodem |
| SUStats serwery | `/api/among-tokens` (legacy) | **nie** — 404, migracja na Clair OAuth |
| Among Us manifesty | `/v2/versions/:dbValue/steam` | tak (jeśli dbValue istnieje) |

---

## Co backend musi naprawić (priorytet)

1. **Upload wszystkich artefaktów na CDN** — `POST /admin/mods/:id/download-to-cdn` dla każdego modu z wariantem, albo ręczny upload do `susmodder-cdn.ovh/mods/{id}/{version}/`.
2. **Uzupełnić warianty** dla 8 modów bez `variants[]` (szczególnie 7 Syzyfowy ToU, 14 Syzyfowa Beta).
3. **Zmigrować `iconUrl`** do katalogu v2 (`/icons/{filename}` na susmodder.app).
4. **Dodać `2025-3-31` do `/versions`** albo zaktualizować `amongVersion` w katalogu modów.
5. **Lobby `GET /lobby`** — upewnić się, że token aplikacji (`Authorization`) jest akceptowany (nie tylko admin); odpowiedź jako tablica w `data` jest OK po poprawce klienta.
6. **Opcjonalnie:** przywrócić `/api/among-tokens` na susmodder.app dla starych klientów SUStats, albo dokończyć migrację na Clair.

---

## Uruchamianie testów ponownie

```powershell
.\SKRYPTY\Test\test-api-e2e.ps1

# Z tokenem (lobby, admin):
.\SKRYPTY\Test\test-api-e2e.ps1 -AuthToken "Bearer <HTTP_TOKEN>" -AdminSecret "<ADMIN_API_SECRET>"
```

Dodatkowy skrypt testujący łańcuch redirectów pobierań:

```powershell
# Wymaga wcześniejszego pobrania katalogu:
curl -s "https://api.susmodder-cdn.ovh/v2/catalog?limit=50" -o SKRYPTY\Test\_catalog.json
```

Plik `SKRYPTY/Test/test-downloads.py` — test CDN dla wszystkich modów z wariantami (wymaga curl/User-Agent; PowerShell loop w skrypcie głównym).

---

## E2E Audit Remediation — 2026-06-07

### Root causes

| Problem | Root cause | Fix |
|---------|-----------|-----|
| CDN 404 na wszystkie pobrania v2 | `mod_variants.download_url` miał prefix `/mods/` niepasujący do nginx `root` | UPDATE 135 wierszy — usunięcie `/mods/` z URL |
| V1 download 404 | Brak reguły nginx dla `/api/mod-download/` — prefix `/api/` nie był stripowany | Dodano `location /api/mod-download/` → `proxy_pass /mod-download/` |
| `iconUrl: null` w katalogu v2 | View zwraca kolumnę `icon_file_name`, a `normalizeCatalogRow` szukał tylko `pngfilename` | Fix w `_helpers.js` + nowy route `GET /v2/icons/:filename` |
| `2025-3-31` → 404 w `/versions` | Brak wiersza w `among_us_versions` | INSERT |
| 8 modów bez `variants[]` | Brak wpisów w `mod_variants` dla obecnych wersji | `cdn-bulk-upload.js` — 12 wariantów ściągniętych z GitHuba |
| Lobby GET 401 | Endpoint wymaga `Authorization: Bearer` tokena (intencjonalne) | Token w `HTTP_TOKEN` env (nie commitować) |

### Commity (susmodder-api)

```
567aae4 fix(cdn): x32 filename should map to x86 architecture, not x64
69f3d5b fix(cdn): allow release-assets.githubusercontent.com in GitHub host validation
7c07034 fix(cdn): swap filename/arch detection order in cdn-bulk-upload
3af7a98 fix(cdn): remove /mods/ prefix from all download URL generation
f6e95c7 fix(migration): remove added_by column, production schema
cedd5fb fix(e2e): iconUrl null, missing 2025-3-31 version, CDN bulk upload tool
```

### Zmienione pliki (backend)

| Plik | Zmiana |
|------|--------|
| `susmodder-api/routes/v2/icons.js` | **Nowy** — `GET /api/v2/icons/:filename` → 302 do susmodder.app |
| `susmodder-api/routes/v2/_helpers.js` | Fix `normalizeCatalogRow` — resolve `icon_file_name` z views |
| `susmodder-api/routes/v2/_router.js` | Rejestracja route icons |
| `susmodder-api/routes/v2/admin/mods.js` | Usunięcie `/mods/` z generowania URL w `download-to-cdn` |
| `susmodder-api/scripts/cdn-bulk-upload.js` | **Nowy** — 3-fazowy upload na CDN |
| `susmodder-api/scripts/migrate-mod-variants.js` | Usunięcie `/mods/` z `CDN_URL_PREFIX` |
| `migrations/013_e2e_audit_fixes.sql` | **Nowy** — INSERT `2025-3-31`, zapytania diagnostyczne CDN |
| `nginx/conf.d/susmodder.app.conf` | **Produkcja** — `location /api/mod-download/` |

### Zmiany w bazie (produkcja)

```sql
-- 135 rows: remove /mods/ prefix from download_url
UPDATE mod_variants
SET download_url = REPLACE(download_url, 'https://susmodder-cdn.ovh/mods/', 'https://susmodder-cdn.ovh/'),
    updated_at = NOW()
WHERE download_url LIKE 'https://susmodder-cdn.ovh/mods/%';

-- 1 row: add missing Among Us version
INSERT INTO among_us_versions (db_value, release_date, has_steam_pkg)
VALUES ('2025-3-31', '2025-03-31', FALSE);

-- 2 rows: fix architecture for Syzyfowy mods (x32 → x86)
UPDATE mod_variants SET architecture='x86', updated_at=NOW()
WHERE mod_id IN (7,14) AND mod_version='7.2.0-pr3-fix' AND download_url LIKE '%x32.zip';

-- 12 rows: new variants created by cdn-bulk-upload.js (mods 5,7,9,14,17,18,20,21)
```

### Wyniki E2E po remediacji (klient repo, 2026-06-07)

**Skrypt weryfikacji klienta:** `SKRYPTY/Test/test-api-v2-client.ps1`  
**Ostatni run:** 2026-06-07 — **24/24 OK** (+ 1 EXPECTED: lobby 401 bez tokena)

| Obszar | Test | Wynik |
|--------|------|-------|
| Katalog | `/catalog`, `/catalog/:id`, `/catalog/:id/versions`, `/catalog-meta` | ✅ |
| Ikony | `iconUrl` 20/20, `GET /v2/icons/10.png` | ✅ |
| Wersje AU | `/versions`, `/versions/2025-3-31`, steam/epic manifesty | ✅ |
| Pobrania | 20/20 modów steam x86 → HTTP 200 | ✅ |
| Kompatybilność | `/compatibility?fullModId&dllModId`, `/compatibility/snapshot` | ✅ |
| Role / Discord | `/roles`, `/discord/favs/public`, `/discord/server-counts` | ✅ |
| Public | `/releases`, `/telemetry/*`, `/online`, `/virustotal/report` | ✅ |
| Telemetria write | `POST /telemetry/heartbeat` | ✅ (429 = rate limit OK) |
| Lobby | `GET /lobby` bez tokena | ⚠️ 401 (oczekiwane) |
| Modpacks | route `/modpacks/:code` | ✅ (400 walidacja) |
| Unit testy Core | `SUSModder.Core.Tests` | ✅ 97/97 |

**Pobrania v2 (wszystkie mody 1–21): 20/20 → HTTP 200 po redirect**

```
GET /v2/downloads/mod/1/5.3.1?platform=steam → 302 → susmodder-cdn.ovh/1/5.3.1/ToU.v5.3.1.zip → 200
GET /v2/catalog/7 → variants: 2 (wcześniej 0)
GET /v2/versions/2025-3-31 → 200
GET /v2/catalog → iconUrl: "/api/v2/icons/10.png" (nie null)
GET /v2/icons/10.png → 302 → susmodder.app/icons/10.png
GET /api/mod-download/1/5.3.1?platform=steam (v1) → 302 → CDN 200
```

### Zmiany po stronie klienta (SUSModder)

| Plik | Zmiana |
|------|--------|
| `SUSModder.Core/Api/CdnAssetUrlResolver.cs` | Rozwiązywanie `/api/v2/icons/...` względem `ApiV2BaseUrl` |
| `SUSModder.Core/Utilities/ModDownloadUrlBuilder.cs` | Zawsze URL przez `/v2/downloads/mod/...` (nie bezpośredni CDN) |

### Zmiany serwerowe (poza git)

- `nginx/conf.d/susmodder.app.conf`: reguła `location /api/mod-download/`
- Pliki CDN dla modów 5, 7, 9, 14, 17, 18, 20, 21 (~400 MB, 12 plików)

### Notatki operacyjne

- **cdn-bulk-upload.js** — zawsze `--dry-run` przed realnym uploadem.
- Allowlist GitHub: `github.com`, `*.github.com`, `objects.githubusercontent.com`, `github-releases.githubusercontent.com`, `release-assets.githubusercontent.com`.
- **Lobby**: klient musi wysyłać `Authorization: Bearer <HTTP_TOKEN>`. Token tylko w env / `SecretProvider`, **nigdy w repo**.
- **Cache**: TTL 60–120 s; ręczne czyszczenie: `POST /v2/admin/cache/clear`.
