# Mod Pack Sharing – API Documentation

**Service:** `susmodder-api`  
**Base path:** `/mod-packs`  
**External prefix:** `/api/mod-packs` (via nginx proxy)  
**Web fallback:** `https://susmodder.app/pack/{packCode}`  

---

## Authentication

**v1 (legacy, `/mod-packs` z myślnikiem):** wymaga `Authorization: Bearer {HTTP_TOKEN}` na write/read własnych endpointach.

**v2 (zalecane, `/api/v2/modpacks` bez myślnika):** używa **soft identity** – `creatorHash` w body/query, bez Bearer tokena. Publiczne endpointy GET są bez autoryzacji.

| Endpoint (v1) | Auth | Uwagi |
|----------|------|-------|
| `POST /mod-packs` | `Authorization: Bearer {HTTP_TOKEN}` | Tworzenie paczki |
| `GET /mod-packs` | `Authorization: Bearer {HTTP_TOKEN}` | Lista własnych paczek |
| `GET /mod-packs/:packCode` | Brak | Publiczny podgląd paczki |
| `GET /mod-packs/web/:packCode` | Brak | Web fallback (HTML) |
| `POST /mod-packs/:packCode/external-dll` | `Authorization: Bearer {HTTP_TOKEN}` | Upload DLL (legacy v1) |
| `GET /mod-packs/:packCode/dlls/:sha256` | Brak | Pobranie DLL (302 → CDN) |
| `DELETE /mod-packs/:packCode` | `Authorization: Bearer {HTTP_TOKEN}` | Usunięcie paczki |
| `POST /api/v2/modpacks/:code/dlls` | `creatorHash` w body | Upload DLL (v2, multipart) |

**Uwaga:** `creatorHash` w body requestów chronionych identyfikuje właściciela paczki (SHA256 HWID). Token `HTTP_TOKEN` autoryzuje aplikację SUSModder – nie jest powiązany 1:1 z użytkownikiem. Wzorzec zgodny z istniejącym `lobbyBoard` (`X-User-Hash`).

---

## Pack Code Format

- Format: `XXXX-XXXX-XXXX` (3 grupy po 4 znaki, separator `-`)
- Alfabet: `A-Z` (bez `I`, `O`) + `2-9` (bez `0`, `1`) = 28 znaków
- Generator: `crypto.randomInt()` (CSPRNG), retry do 5 razy na UNIQUE collision
- Entropia: ~57 bitów

---

## TTL (Time To Live)

| Wartość | Opis |
|---------|------|
| `7` | 7 dni |
| `30` | 30 dni (domyślnie) |
| `90` | 90 dni |

Po wygaśnięciu:
- Soft-delete (cron co 10 min): `is_deleted = TRUE`
- Hard-delete (cron co 24h, 3:30 AM): usunięcie z DB + plików CDN
- HTTP 410 Gone dla wszystkich requestów

---

## Limit paczek

- **10 aktywnych** paczek na `creatorHash`
- Liczone: `is_deleted = FALSE AND expires_at > NOW()`
- Przekroczenie: HTTP 429 `PACK_LIMIT_REACHED`

---

## Endpointy

### 1. POST /mod-packs – tworzenie paczki

Tworzy nową paczkę z zestawem modów. Zwraca kod paczki, URL do udostępniania i deep link.

**Request:**
```json
{
  "creatorHash": "string (64 hex, wymagane)",
  "creatorName": "string (max 100, opcjonalne, default null)",
  "fullModId": "integer (wymagane)",
  "fullModVersion": "string (max 50, wymagane, np. 'latest' lub '5.4.0')",
  "modName": "string (max 255, opcjonalne)",
  "discordInvite": "string (max 255, opcjonalne, akceptuje discord.gg/XXX z/bez https://)",
  "includeIntegrationDll": "boolean (opcjonalne, default false)",
  "ttlDays": "integer (7|30|90, opcjonalne, default 30)",
  "dllMods": [
    {
      "dllModId": "integer (wymagane)",
      "dllModVersion": "string (max 50, wymagane)"
    }
  ],
  "touConfig": "object (opcjonalne, pełny JSON configu ToU)",
  "externalDlls": [
    {
      "fileName": "string (max 255, wymagane)",
      "fileSha256": "string (64 hex, wymagane)",
      "fileSize": "integer (max 10485760 = 10MB, wymagane)"
    }
  ]
}
```

**Response 201:**
```json
{
  "success": true,
  "packId": "uuid",
  "packCode": "ABCD-EFGH-IJKL",
  "shareUrl": "https://susmodder.app/pack/ABCD-EFGH-IJKL",
  "deepLink": "susmodder://pack/ABCD-EFGH-IJKL",
  "expiresAt": "2026-08-28T12:00:00.000Z"
}
```

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | (Joi message) | Walidacja nie przeszła |
| 400 | `INVALID_CREATOR_HASH` | Nieprawidłowy format creatorHash |
| 401 | — | Brak tokenu |
| 429 | `PACK_LIMIT_REACHED` | Przekroczono limit 10 paczek |
| 500 | `PACK_CODE_COLLISION` | Nie udało się wygenerować unikalnego kodu (edge case) |
| 500 | `Internal server error` | Błąd serwera |

---

### 2. GET /mod-packs – lista paczek użytkownika

Zwraca listę wszystkich aktywnych paczek dla danego `creatorHash`.

**Query params:**
| Param | Typ | Wymagane | Opis |
|-------|-----|----------|------|
| `creatorHash` | string (64 hex) | Tak | SHA256 HWID twórcy |

**Response 200:**
```json
{
  "success": true,
  "packs": [
    {
      "packId": "uuid",
      "packCode": "ABCD-EFGH-IJKL",
      "modName": "Town of Us",
      "fullModId": 1,
      "fullModVersion": "latest",
      "ttlDays": 30,
      "vtStatus": "clean",
      "dllCount": 3,
      "externalDllCount": 1,
      "createdAt": "2026-05-29T12:00:00.000Z",
      "expiresAt": "2026-06-28T12:00:00.000Z",
      "active": true
    }
  ],
  "activeCount": 5,
  "maxAllowed": 10
}
```

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | `INVALID_CREATOR_HASH` | Brak lub nieprawidłowy creatorHash |
| 401 | — | Brak tokenu |

---

### 3. GET /mod-packs/:packCode – podgląd paczki (publiczny)

Zwraca pełne dane paczki. **Bez autoryzacji.**

**Response 200:**
```json
{
  "success": true,
  "pack": {
    "packId": "uuid",
    "packCode": "ABCD-EFGH-IJKL",
    "creatorName": "OptionalName",
    "fullMod": {
      "id": 1,
      "version": "5.4.0"
    },
    "modName": "Town of Us",
    "discordInvite": "https://discord.gg/XXXX",
    "includeIntegrationDll": false,
    "ttlDays": 30,
    "vtStatus": "clean",
    "metadata": {},
    "createdAt": "2026-05-29T12:00:00.000Z",
    "expiresAt": "2026-06-28T12:00:00.000Z",
    "dllMods": [
      {
        "dllModId": 12,
        "dllModVersion": "2.1.0"
      }
    ],
    "externalDlls": [
      {
        "id": 5,
        "fileName": "custom.dll",
        "sha256": "abc123...",
        "fileSize": 5242880,
        "vtStatus": "clean",
        "vtPermalink": "https://www.virustotal.com/gui/file/abc123...",
        "downloadUrl": "https://susmodder-cdn.ovh/modpacks/ABCD-EFGH-IJKL/abc123...dll"
      }
    ],
    "touConfig": { "role": "Crewmate" }
  }
}
```

**Uwagi:**
- Tylko DLL z `cdn_path IS NOT NULL` (faktycznie uploadowane) są zwracane w `externalDlls`
- Pre-deklarowane DLL bez uploadu nie pojawiają się w odpowiedzi
- `downloadUrl` → `null` jeśli brak `fileSha256`

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | `INVALID_PACK_CODE` | Nieprawidłowy format kodu |
| 404 | `PACK_NOT_FOUND` | Paczka nie istnieje |
| 410 | `PACK_EXPIRED` | Paczka wygasła lub została usunięta |

---

### 4. GET /mod-packs/web/:packCode – web fallback (HTML)

Zwraca stronę HTML z meta tagami OpenGraph, deep link redirectem i przyciskiem "Pobierz SUSModder". **Bez autoryzacji.**

**Response 200:** `text/html; charset=utf-8`

Struktura HTML:
- `<meta property="og:title">` – nazwa paczki
- `<meta property="og:description">` – liczba modów, twórca
- `<meta http-equiv="refresh">` – redirect do `susmodder://pack/{code}`
- `<script>window.location.href='susmodder://...'</script>` – JS fallback redirect
- Przycisk "Pobierz SUSModder" pokazuje się po 1.5s (jeśli redirect nie zadziałał)
- Ostrzeżenie dla paczek z external DLL lub VT `suspicious`

**Routing nginx:**
```nginx
location /pack/ {
    rewrite ^/pack/(.+)$ /mod-packs/web/$1 break;
    proxy_pass http://susmodder-api:3001;
}
```

**Error codes:**
| HTTP | Opis |
|------|------|
| 200 | Strona HTML z przekierowaniem |
| 400 | Nieprawidłowy format kodu (HTML error page) |
| 404 | Paczka nie znaleziona (HTML error page) |
| 410 | Paczka wygasła (HTML error page) |

---

### 5. POST /mod-packs/:packCode/external-dll – upload zewnętrznego DLL

Upload pliku `.dll` do paczki. Wymaga autoryzacji i własności paczki.

**Content-Type:** `multipart/form-data`

| Field | Typ | Wymagane | Opis |
|-------|-----|----------|------|
| `file` | binary | Tak | Plik `.dll`, max 10 MB |
| `creatorHash` | string (64 hex) | Tak | SHA256 HWID właściciela |

**Ograniczenia:**
- Tylko pliki `.dll` (sprawdzane po rozszerzeniu)
- Max 10 MB (`LIMIT_FILE_SIZE`)
- Max 3 zewnętrzne DLL na paczkę
- Plik zapisywany na CDN: `modpacks/{packCode}/{sha256}.dll`
- Jeśli istnieje pre-deklarowany wpis DLL (z `externalDlls` przy tworzeniu) – jest aktualizowany zamiast tworzenia nowego wiersza

**Response 201:**
```json
{
  "success": true,
  "dllEntry": {
    "id": 5,
    "fileName": "custom.dll",
    "sha256": "abc123def456...",
    "fileSize": 5242880,
    "vtStatus": "pending",
    "vtPermalink": null
  }
}
```

**VirusTotal flow:**
1. Upload → `vtStatus: "pending"` (plik jeszcze nie przeskanowany)
2. Cron co 5 min → VT API check (`getFileReport` po SHA256)
3. Jeśli brak w cache → upload do VT + wait for analysis
4. Wynik: `malicious >= 3` → `suspicious`, w przeciwnym razie → `clean`
5. `suspicious` DLL aktualizuje `vt_status` całej paczki

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | `INVALID_PACK_CODE` | Nieprawidłowy format kodu |
| 400 | `INVALID_FILE_TYPE` | Plik nie jest `.dll` |
| 400 | `FILE_REQUIRED` | Brak pliku |
| 400 | `INVALID_CREATOR_HASH` | Nieprawidłowy creatorHash |
| 401 | — | Brak tokenu |
| 403 | `NOT_PACK_OWNER` | CreatorHash nie pasuje do właściciela paczki |
| 404 | `PACK_NOT_FOUND` | Paczka nie istnieje |
| 410 | `PACK_EXPIRED` | Paczka wygasła |
| 413 | `FILE_TOO_LARGE` | Plik > 10 MB |
| 429 | `EXTERNAL_DLL_LIMIT` | Osiągnięto limit 3 DLL na paczkę |

---

### 6. GET /mod-packs/:packCode/dlls/:sha256 – pobranie DLL (publiczne)

Przekierowuje (302) na URL CDN. **Bez autoryzacji.** Blokuje podejrzane i nieskanowane pliki.

**Response:**
| Status | Warunek |
|--------|---------|
| 302 → CDN URL | `vtStatus == "clean"` |
| 425 `DLL_SCAN_PENDING` | `vtStatus` to `"pending"` lub `"unknown"` |
| 451 `DLL_SUSPICIOUS` | `vtStatus == "suspicious"` |
| 404 `DLL_NOT_FOUND` | DLL nie znaleziony w paczce |
| 410 `PACK_EXPIRED` | Paczka wygasła |

**CDN URL format:** `https://susmodder-cdn.ovh/modpacks/{packCode}/{sha256}.dll`

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | `INVALID_PARAMS` | Nieprawidłowy format packCode lub SHA256 |
| 404 | `DLL_NOT_FOUND` | DLL nie istnieje |
| 410 | `PACK_EXPIRED` | Paczka wygasła |
| 425 | `DLL_SCAN_PENDING` | DLL nie został jeszcze przeskanowany |
| 451 | `DLL_SUSPICIOUS` | DLL oznaczony jako podejrzany przez VirusTotal |

---

### 7. DELETE /mod-packs/:packCode – usunięcie paczki

Soft-delete paczki. Wymaga autoryzacji i własności. Pliki CDN nie są usuwane natychmiast – hard-delete cron usunie je po 24h.

**Request:**
```json
{
  "creatorHash": "string (64 hex, wymagane)"
}
```

**Response 200:**
```json
{
  "success": true
}
```

**Error codes:**
| HTTP | errorCode | Opis |
|------|-----------|------|
| 400 | `INVALID_PACK_CODE` | Nieprawidłowy format kodu |
| 400 | `INVALID_CREATOR_HASH` | Nieprawidłowy creatorHash |
| 401 | — | Brak tokenu |
| 403 | `NOT_PACK_OWNER` | CreatorHash nie pasuje do właściciela paczki |
| 404 | `PACK_NOT_FOUND` | Paczka nie istnieje |

---

## VirusTotal Integration

### Flow

```
Upload DLL → vtStatus = "pending"
                ↓
    Cron co 5 min (pollPendingVtScans)
                ↓
    GET /files/{sha256} (VT cache check)
       ↙              ↘
  znaleziony        brak w cache
       ↓                ↓
  ocena stats      POST /files (submit)
       ↓                ↓
  malicious>=3?    waitForAnalysis (max 90s)
    ↙      ↘           ↓
 suspicious  clean   ocena stats
    ↓         ↓        ↓
  update DB  update DB + permalink
    ↓
  update pack vt_status = "suspicious"
```

### Stany VT

| vtStatus | Znaczenie | Downloadable? |
|----------|-----------|---------------|
| `unknown` | Nie dotyczy / plik nie istnieje | ❌ 425 |
| `pending` | Oczekuje na skanowanie | ❌ 425 |
| `clean` | Przeskanowany, < 3 detekcji | ✅ 302 |
| `suspicious` | ≥ 3 detekcji malicious | ❌ 451 |

### Próg detekcji

- `malicious >= 3` → `suspicious` (VT ma ~70 skanerów)
- Możliwy false positive przy niskim progu – do dostrojenia w produkcji

---

## Cron Jobs

| Cron | Interwał | Akcja |
|------|----------|-------|
| Soft-delete | `*/10 * * * *` | `UPDATE mod_packs SET is_deleted = TRUE WHERE expires_at < NOW()` |
| Hard-delete | `30 3 * * *` | Usunięcie paczek starszych niż 24h po wygaśnięciu + pliki CDN + CASCADE |
| VT poll | `*/5 * * * *` | Skanowanie `pending` DLL (max 10 na cykl) |

---

## CDN

### Struktura katalogów

```
/var/www/html/susmodder-cdn/modpacks/
├── ABCD-EFGH-IJKL/          # External DLL (v1) – per-pack
│   └── {sha256}.dll
├── XXXX-YYYY-ZZZZ/
│   └── {sha256}.dll
├── artifacts/               # Custom artifacts (v2) – deduplikowane po SHA256
│   ├── {sha256_1}/
│   │   └── TownOfUs.dll
│   └── {sha256_2}/
│       └── CustomMod.dll
```

**Deduplikacja:** Custom artifacty są przechowywane pod `artifacts/{sha256}/{nazwa}` – ten sam plik (identyczny SHA256) jest współdzielony między wszystkimi paczkami. Worker pomija zapis, jeśli plik już istnieje (`fs.access`).

### Nginx CDN config (`susmodder-cdn.ovh.conf`)

```nginx
location /modpacks/ {
    alias /var/www/html/susmodder-cdn/modpacks/;
    limit_except GET HEAD { deny all; }
    location ~* \.dll$ {
        add_header Cache-Control "public, max-age=604800" always;
        add_header X-Content-Type-Options "nosniff" always;
    }
    location ~* \.(?!dll$) { return 404; }
    location ~ /\. { deny all; return 404; }
}
```

### Zmienne środowiskowe

| Zmienna | Domyślnie | Opis |
|---------|-----------|------|
| `MODPACKS_CDN_DIR` | `/usr/src/app/susmodder-cdn/modpacks` | Ścieżka do katalogu CDN (wewnątrz kontenera) |
| `MODPACKS_CDN_BASE_URL` | `https://susmodder-cdn.ovh/modpacks` | Publiczny URL baza CDN |
| `MODPACKS_MAX_PER_CREATOR` | `10` | Maksymalna liczba aktywnych paczek na twórcę |

---

## PostgreSQL Schema

### Tabele

| Tabela | Klucz | Opis |
|--------|-------|------|
| `mod_packs` | `id UUID PK` | Metadane paczki, TTL, creator_hash |
| `mod_pack_dlls` | `id SERIAL PK` | DLL katalogowe + zewnętrzne, VT status |
| `mod_pack_tou_configs` | `id SERIAL PK` | Pełny config ToU (JSONB) |

### Relacje

```
mod_packs 1───* mod_pack_dlls        (ON DELETE CASCADE)
mod_packs 1───1 mod_pack_tou_configs  (ON DELETE CASCADE)
```

### Deployment – migracja

```bash
# Na produkcji:
cd /srv/synapsekit-boracik
docker exec -i susadmin-db psql "$DATABASE_URL" < migrations/008_create_mod_packs.sql
```

### Nginx routing (produkcja)

```nginx
# susmodder.app.conf – dodane:
location /pack/ {
    rewrite ^/pack/(.+)$ /mod-packs/web/$1 break;
    proxy_pass http://susmodder-api:3001;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

---

## Kody błędów – pełna lista

| HTTP | errorCode | Endpoint(y) | Opis |
|------|-----------|-------------|------|
| 400 | (Joi message) | POST /mod-packs | Walidacja Joi nie przeszła |
| 400 | `INVALID_CREATOR_HASH` | POST, GET list, DELETE, POST DLL | Nieprawidłowy format creatorHash (nie 64 hex) |
| 400 | `INVALID_PACK_CODE` | GET, DELETE, POST DLL, GET DLL | Nieprawidłowy format packCode |
| 400 | `INVALID_PARAMS` | GET DLL | Nieprawidłowy format packCode lub SHA256 |
| 400 | `INVALID_FILE_TYPE` | POST DLL | Plik nie ma rozszerzenia `.dll` |
| 400 | `FILE_REQUIRED` | POST DLL | Brak pliku w requeście |
| 401 | — | POST, GET list, DELETE, POST DLL | Brak lub nieprawidłowy Bearer token |
| 403 | `NOT_PACK_OWNER` | DELETE, POST DLL | CreatorHash nie pasuje do właściciela paczki |
| 404 | `PACK_NOT_FOUND` | GET, DELETE, POST DLL, GET DLL | Paczka nie istnieje |
| 404 | `DLL_NOT_FOUND` | GET DLL | DLL nie znaleziony w paczce |
| 410 | `PACK_EXPIRED` | GET, GET web, GET DLL | Paczka wygasła lub usunięta |
| 413 | `FILE_TOO_LARGE` | POST DLL | Plik DLL > 10 MB |
| 425 | `DLL_SCAN_PENDING` | GET DLL | DLL jeszcze nie przeskanowany przez VT |
| 429 | `PACK_LIMIT_REACHED` | POST /mod-packs | Przekroczono limit 10 paczek |
| 429 | `EXTERNAL_DLL_LIMIT` | POST DLL | Przekroczono limit 3 DLL na paczkę |
| 451 | `DLL_SUSPICIOUS` | GET DLL | DLL oznaczony jako podejrzany przez VT |
| 500 | `PACK_CODE_COLLISION` | POST /mod-packs | Nie udało się wygenerować unikalnego kodu |
| 500 | `Internal server error` | Wszystkie | Nieoczekiwany błąd serwera |

---

## API v2 – Custom Content (GitHub DLL/FULL)

**Base path:** `/api/v2/modpacks`  
**Status:** ✅ Production (2026-06-14)  
**Auth:** Soft identity (`creatorHash` w body lub query) – brak `Authorization: Bearer`  

### Authentication

Endpointy v2 używają **soft identity** przez `creatorHash`. Żaden z endpointów v2 nie wymaga `Authorization: Bearer`. Weryfikacja: `creatorHash` z body/query musi być zgodny z `creator_hash` właściciela paczki.

**Uwaga:** v1 ścieżki (`/mod-packs` z myślnikiem) dalej istnieją i wymagają `Authorization: Bearer HTTP_TOKEN`. Nowe klienty powinny używać v2 (`/api/v2/modpacks` bez myślnika).

### Przegląd endpointów v2

| Endpoint | Auth | Opis |
|----------|------|------|
| `POST /api/v2/modpacks` | `creatorHash` w body | Tworzenie paczki |
| `GET /api/v2/modpacks?creatorHash=…` | `creatorHash` w query | Lista paczek użytkownika |
| `GET /api/v2/modpacks/:code` | Brak (publiczny) | Podgląd paczki |
| `DELETE /api/v2/modpacks/:code` | `creatorHash` w body | Usunięcie paczki |
| `POST /api/v2/modpacks/:code/dlls` | `creatorHash` w body | Upload DLL (multipart) |
| `POST /api/v2/modpacks/:code/custom-github-mods` | `creatorHash` w body | Deklaracja GitHub artifactu |
| `POST /api/v2/modpacks/:code/finalize` | `creatorHash` w body | Finalizacja paczki |
| `GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/status` | Brak (publiczny) | Status artefaktu |
| `GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/download` | Brak (publiczny) | Download artefaktu |
| `GET /api/v2/modpacks/:code/web` | Brak (publiczny) | Web fallback (JSON/HTML) |

### Modele danych

#### Pack status

| Status | Znaczenie | `installable` |
|--------|-----------|---------------|
| `draft` | Paczka utworzona, brak custom content | `false` |
| `scanning` | Artefakty w trakcie walidacji/VT scan | `false` |
| `ready` | Wszystkie artefakty `clean`, paczka gotowa | `true` |
| `blocked` | Co najmniej 1 artefakt `suspicious`/`rejected` | `false` |
| `expired` | TTL minął | `false` |

Paczki **bez custom content** mają domyślnie `status: ready`, `installable: true` – kompatybilność zachowana.

#### Artifact status

| Status | Znaczenie | Download? |
|--------|-----------|-----------|
| `pending` | Rekord utworzony, worker jeszcze nie przetworzył | ❌ 425 |
| `scanning` | Worker pobiera/waliduje/skanuje | ❌ 425 |
| `clean` | Walidacja struktury OK + VT clean | ✅ 302 |
| `suspicious` | VT przekroczył próg (`malicious >= 3`) | ❌ 451 |
| `rejected` | Walidacja URL/struktury/rozmiaru nie przeszła | ❌ 451 |
| `expired` | TTL paczki/artefaktu minął | ❌ 410 |

---

### 8. GET /api/v2/modpacks – lista paczek użytkownika (v2)

Zwraca listę wszystkich aktywnych paczek dla danego `creatorHash`. **Soft identity** – w query param, bez Bearer tokena.

**Query params:**
| Param | Typ | Wymagane | Opis |
|-------|-----|----------|------|
| `creatorHash` | string (64 hex) | Tak | SHA256 HWID twórcy |

**Response 200:**
```json
{
  "data": {
    "packs": [
      {
        "packId": "uuid",
        "packCode": "ABCD-EFGH-IJKL",
        "modName": "Town of Us",
        "fullModId": 1,
        "fullModVersion": "latest",
        "ttlDays": 30,
        "vtStatus": "clean",
        "status": "ready",
        "installable": true,
        "dllCount": 3,
        "externalDllCount": 1,
        "createdAt": "2026-05-29T12:00:00.000Z",
        "expiresAt": "2026-06-28T12:00:00.000Z",
        "active": true
      }
    ],
    "activeCount": 5,
    "maxAllowed": 10
  }
}
```

**Error codes:**
| HTTP | code | Opis |
|------|------|------|
| 400 | `VALIDATION_ERROR` | Brak lub nieprawidłowy creatorHash |

---

### 9. POST /api/v2/modpacks/:code/custom-github-mods – deklaracja GitHub artifactu

Deklaruje custom DLL lub FULL mod z publicznego linku GitHub release asset. Backend tworzy rekord `pending` i worker asynchronicznie pobiera, waliduje i skanuje artefakt.

**Request:**
```json
{
  "creatorHash": "string (64 hex, wymagane)",
  "sourceKind": "github_dll | github_full (wymagane)",
  "modType": "dll | full (wymagane, musi zgadzać się z sourceKind)",
  "displayName": "string (1-100 znaków, wymagane, bez control chars)",
  "version": "string (max 50, opcjonalne)",
  "githubUrl": "string (wymagane, tylko https://github.com/owner/repo/releases/download/tag/asset)",
  "dllInstallPath": "string (max 500, opcjonalne, tylko dla DLL, default: BepInEx/plugins)"
}
```

**GitHub URL – reguły walidacji:**
- ✅ `https://github.com/{owner}/{repo}/releases/download/{tag}/{asset}`
- ❌ `http://`, host inny niż `github.com`
- ❌ Branch linki (`/tree/`, `/blob/`)
- ❌ Archive linki (`/archive/refs/heads/`)
- ❌ Repo homepage bez release assetu
- ❌ URL z credentials (`user:pass@`)
- ❌ Non-standard port

**DLL Install Path – reguły:**
- ✅ `BepInEx/plugins`
- ✅ `BepInEx/plugins/SubFolder`
- ❌ `..` (path traversal)
- ❌ Absolutne ścieżki (`/`, `C:\`)
- ❌ ADS (`:`)
- ❌ Ścieżki spoza whitelisty

**Response 202:**
```json
{
  "data": {
    "customArtifact": {
      "artifactId": "uuid",
      "sourceKind": "github_dll",
      "modType": "dll",
      "displayName": "My Custom DLL",
      "version": "v1.2.3",
      "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
      "fileName": "MyMod.dll",
      "sha256": "",
      "fileSize": 0,
      "status": "pending",
      "vtPermalink": null,
      "downloadUrl": null,
      "dllInstallPath": "BepInEx/plugins",
      "structureWarnings": []
    }
  }
}
```

**Error codes:**
| HTTP | code | Opis |
|------|------|------|
| 400 | `VALIDATION_ERROR` | Walidacja Joi nie przeszła |
| 400 | `GITHUB_URL_NOT_ALLOWED` | URL nie spełnia wymagań (host/protocol/credentials) |
| 400 | `GITHUB_RELEASE_ASSET_REQUIRED` | URL nie wskazuje release assetu |
| 400 | `DLL_INSTALL_PATH_INVALID` | Nieprawidłowa ścieżka instalacji DLL |
| 403 | `FORBIDDEN` | CreatorHash nie pasuje do właściciela paczki |
| 404 | `NOT_FOUND` | Paczka nie istnieje |
| 410 | `PACK_EXPIRED` | Paczka wygasła |

---

### 10. GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/status – status artefaktu

Publiczny endpoint do pollingu statusu walidacji/skanu artefaktu.

**Response 200:**
```json
{
  "data": {
    "status": "scanning",
    "downloadAvailable": false,
    "customArtifact": {
      "artifactId": "uuid",
      "sourceKind": "github_dll",
      "modType": "dll",
      "displayName": "My Custom DLL",
      "version": "v1.2.3",
      "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
      "fileName": "MyMod.dll",
      "sha256": "abc123...",
      "fileSize": 123456,
      "status": "scanning",
      "vtPermalink": null,
      "downloadUrl": null,
      "dllInstallPath": "BepInEx/plugins",
      "structureWarnings": [],
      "normalizedGithubUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
      "githubOwner": "owner",
      "githubRepo": "repo",
      "githubTag": "v1.2.3",
      "githubAssetName": "MyMod.dll",
      "errorCode": null,
      "errorMessage": null,
      "createdAt": "2026-06-14T20:40:34.100Z",
      "updatedAt": "2026-06-14T20:40:34.450Z"
    }
  }
}
```

---

### 11. POST /api/v2/modpacks/:code/finalize – finalizacja paczki

Ustawia status paczki na podstawie stanu wszystkich custom artefaktów.

**Request:**
```json
{
  "creatorHash": "string (64 hex, wymagane)"
}
```

**Response 200 (ready):**
```json
{
  "data": {
    "status": "ready",
    "installable": true,
    "shareUrl": "https://susmodder.app/pack/ABCD-EFGH-JKLM",
    "deepLink": "susmodder://pack/ABCD-EFGH-JKLM"
  }
}
```

**Response 425 (pending scan):**
```json
{
  "error": {
    "code": "PACK_NOT_READY",
    "message": "Custom content is still being scanned",
    "details": {
      "pendingCount": 1,
      "scanningCount": 2
    }
  }
}
```

**Response 409 (blocked):**
```json
{
  "error": {
    "code": "PACK_NOT_READY",
    "message": "Custom content has been flagged as suspicious or rejected",
    "details": {
      "suspiciousCount": 1,
      "rejectedCount": 0
    }
  }
}
```

**Logika finalizacji:**
| Warunek | Status paczki | installable | HTTP |
|---------|---------------|-------------|------|
| Brak custom artefaktów | `ready` | `true` | 200 |
| Wszystkie artefakty `clean` | `ready` | `true` | 200 |
| Jakikolwiek `pending` lub `scanning` | `scanning` | `false` | 425 |
| Jakikolwiek `suspicious` lub `rejected` | `blocked` | `false` | 409 |

---

### 12. GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/download – download artefaktu

Przekierowuje (302) na CDN. Dozwolone tylko dla `status: clean`.

**Response:**
| HTTP | code | Warunek |
|------|------|---------|
| 302 → CDN | — | `status == clean`, nagłówki: `X-SUSModder-SHA256`, `X-SUSModder-File-Size` |
| 404 | `NOT_FOUND` | Artefakt nie istnieje |
| 410 | `PACK_EXPIRED` | Paczka wygasła |
| 425 | `CUSTOM_CONTENT_PENDING_SCAN` | `status ∈ {pending, scanning}` |
| 451 | `CUSTOM_CONTENT_SUSPICIOUS` | `status == suspicious` |
| 451 | `CUSTOM_CONTENT_REJECTED` | `status == rejected` |

---

### 13. GET /api/v2/modpacks/:code – rozszerzona odpowiedź

Endpoint `GET /api/v2/modpacks/:code` (opisany wcześniej dla v2) został rozszerzony o nowe pola:

```json
{
  "data": {
    "packCode": "ABCD-EFGH-JKLM",
    "status": "ready",
    "installable": true,
    "fullModId": 1,
    "fullModVersion": "5.4.0",
    "dllMods": [ ... ],
    "externalDlls": [ ... ],
    "customArtifacts": [
      {
        "artifactId": "uuid",
        "sourceKind": "github_dll",
        "modType": "dll",
        "displayName": "My Custom DLL",
        "version": "v1.2.3",
        "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
        "fileName": "MyMod.dll",
        "sha256": "abc123...",
        "fileSize": 123456,
        "status": "clean",
        "vtPermalink": "https://www.virustotal.com/gui/file/abc123...",
        "downloadUrl": "https://susmodder-cdn.ovh/modpacks/artifacts/uuid",
        "dllInstallPath": "BepInEx/plugins",
        "structureWarnings": [],
        "errorCode": null,
        "errorMessage": null,
        "createdAt": "...",
        "updatedAt": "..."
      }
    ],
    "touConfig": null,
    "shareUrl": "https://susmodder.app/pack/ABCD-EFGH-JKLM",
    "deepLink": "susmodder://pack/ABCD-EFGH-JKLM"
  }
}
```

**Kompatybilność:** `customArtifacts` jest opcjonalne – stare klienty które nie parsują tego pola działają bez zmian. Dla paczek bez custom content `customArtifacts` to pusta tablica `[]`.

---

## Custom Artifact Worker (async processing)

### Flow

```
POST /custom-github-mods
  → INSERT mod_pack_custom_artifacts (status = 'pending')
  → Worker (co 10s, batch 3):
      1. SELECT ... FOR UPDATE SKIP LOCKED (status = 'pending')
      2. SET status = 'scanning'
      3. Download asset z GitHub (verify redirect host)
      4. Validate structure (DLL: MZ header, .dll extension, size ≤ 10MB)
      5. Compute SHA256
      6. Check file_scan_cache (SHA256 → cached VT result)
      7. VirusTotal: GET /files/{sha256} → jeśli brak → POST /files → poll analysis
      8. Jeśli clean → save to CDN (artifacts/{sha256}/{fileName}, skip jeśli już istnieje) → SET status = 'clean'
      9. Jeśli suspicious → SET status = 'suspicious'
      10. Jeśli błąd → SET status = 'rejected' + error_code
      11. UPDATE mod_packs.pack_status + installable
      12. Invalidate cache (v2:modpack:{code})
```

### Recovery

- Worker przy starcie podnosi artefakty `scanning` starsze niż 30 min
- `FOR UPDATE SKIP LOCKED` zapobiega race condition przy wielu workerach

### Deduplikacja CDN

- Pliki przechowywane pod `artifacts/{sha256}/{safeFileName}` – współdzielone między paczkami
- Worker nie powiela pliku – `fs.access()` sprawdza czy już istnieje przed zapisem
- `file_scan_cache` (SHA256 → VT result) zapobiega ponownemu skanowaniu VirusTotal
- Unikalny constraint `(pack_id, source_kind, sha256)` zapobiega duplikacji deklaracji w tej samej paczce
- 5 użytkowników wrzuca identyczny DLL → 1 plik na dysku, 1 skan VT, 5 osobnych rekordów w DB

### Konfiguracja (env vars)

| Zmienna | Domyślnie | Opis |
|---------|-----------|------|
| `CUSTOM_ARTIFACT_WORKER_INTERVAL` | `10000` | Interwał workera w ms |
| `CUSTOM_ARTIFACT_BATCH_SIZE` | `3` | Max artefaktów na cykl |
| `CUSTOM_ARTIFACT_STALE_MINUTES` | `30` | Po ilu minutach `scanning` uznajemy za stale |
| `VIRUSTOTAL_API_KEY` | — | Klucz VT (bez niego artefakty zostają w `scanning`) |
| `VT_MALICIOUS_THRESHOLD` | `3` | Próg malicious detections → `suspicious` |
| `MODPACKS_CDN_DIR` | `/usr/src/app/susmodder-cdn/modpacks` | Katalog CDN |
| `MODPACKS_CDN_BASE_URL` | `https://susmodder-cdn.ovh/modpacks` | URL baza CDN |

---

## PostgreSQL Schema – v2 Custom Content

### Nowe tabele

| Tabela | Klucz | Opis |
|--------|-------|------|
| `mod_pack_custom_artifacts` | `id UUID PK` | Deklaracje custom modów (GitHub DLL/FULL, uploaded DLL) |
| `mod_pack_artifact_scans` | `id UUID PK` | Per-file skany wewnątrz ZIP/FULL artefaktów |
| `file_scan_cache` | `sha256 CHAR(64) PK` | Cache wyników VirusTotal po SHA256 |

### Nowe kolumny w `mod_packs`

| Kolumna | Typ | Domyślnie | Opis |
|---------|-----|-----------|------|
| `pack_status` | `TEXT NOT NULL` | `'ready'` | Status paczki: `draft`, `scanning`, `ready`, `blocked`, `expired` |
| `installable` | `BOOLEAN NOT NULL` | `TRUE` | Czy paczka jest gotowa do instalacji |

### Relacje

```
mod_packs 1───* mod_pack_custom_artifacts  (ON DELETE CASCADE)
mod_pack_custom_artifacts 1───* mod_pack_artifact_scans (ON DELETE CASCADE)
```

### Indeksy

- `(pack_id)` – lookup artefaktów per paczka
- `(sha256)` – deduplikacja i cache lookup
- `(status)` – worker query (`pending`/`scanning`)
- `(pack_id, source_kind, sha256) UNIQUE` – deduplikacja per paczka
- `(status, created_at)` – zoptymalizowany worker query

### Funkcje pomocnicze

- `custom_artifact_status_counts(pack_id UUID)` – zwraca liczniki artefaktów per status (używane przez `POST /finalize`)
- `update_custom_artifact_updated_at()` – trigger BEFORE UPDATE aktualizujący `updated_at`

### Deployment – migracja

```bash
# Na produkcji:
cd /srv/synapsekit-boracik
docker exec -i susadmin-db psql -U susadmin -d susmodder < migrations/019_create_mod_pack_custom_artifacts.sql
```

---

## Nowe kody błędów (v2 custom content)

| HTTP | code | Endpoint(y) | Opis |
|------|------|-------------|------|
| 400 | `GITHUB_URL_REQUIRED` | POST custom-github-mods | Brak URL |
| 400 | `GITHUB_URL_NOT_ALLOWED` | POST custom-github-mods | URL nie spełnia reguł (host, protocol, credentials, port) |
| 400 | `GITHUB_RELEASE_ASSET_REQUIRED` | POST custom-github-mods | URL nie wskazuje release assetu |
| 400 | `DLL_INSTALL_PATH_INVALID` | POST custom-github-mods | Ścieżka instalacji DLL poza whitelistą |
| 400 | `VALIDATION_ERROR` | Wszystkie v2 custom | Walidacja Joi nie przeszła |
| 403 | `FORBIDDEN` | POST custom-github-mods, finalize | CreatorHash ≠ właściciel paczki |
| 404 | `NOT_FOUND` | Wszystkie v2 custom | Paczka lub artefakt nie istnieje |
| 410 | `PACK_EXPIRED` | Wszystkie v2 custom | Paczka wygasła |
| 425 | `PACK_NOT_READY` | POST finalize | Artefakty wciąż `pending`/`scanning` |
| 425 | `CUSTOM_CONTENT_PENDING_SCAN` | GET download | Artefakt `pending`/`scanning` |
| 409 | `PACK_NOT_READY` | POST finalize | Artefakty `suspicious`/`rejected` |
| 451 | `CUSTOM_CONTENT_SUSPICIOUS` | GET download | Artefakt oznaczony jako podejrzany |
| 451 | `CUSTOM_CONTENT_REJECTED` | GET download | Artefakt odrzucony podczas walidacji |
| 410 | `CUSTOM_CONTENT_EXPIRED` | GET download | Artefakt wygasł |

---

## Client Integration (SUSModder 3.x)

SUSModder Core ma przygotowane metody do integracji z powyższym API:

```csharp
// Lista paczek użytkownika (v2, soft identity)
await ListMyPacksAsync(creatorHash, ct);
// → GET /api/v2/modpacks?creatorHash={hash}
// Odpowiedź: { data: { packs: [...], activeCount: N, maxAllowed: 10 } }

// Deklaracja GitHub custom moda
await DeclareGitHubCustomModAsync(packCode, request, ct);

// Polling statusu
await GetCustomArtifactStatusAsync(packCode, artifactId, ct);

// Finalizacja paczki
await FinalizePackAsync(packCode, ct);

// Parsowanie preview z customArtifacts[]
// Model: ModPackCustomArtifact (ArtifactId, SourceKind, ModType, Status, Sha256, DownloadUrl, ...)
```

Instalator klienta:
- Pobiera artefakty tylko przez API/CDN (nie bezpośrednio z GitHuba)
- Weryfikuje SHA256 przed zapisem
- Instaluje DLL do `DllInstallPath` (tylko whitelisted ścieżki)
- Blokuje instalację dla `status != clean`
- Wymaga checkboxa zgody użytkownika dla custom content
