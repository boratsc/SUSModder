# Mod Pack Sharing – API Documentation

**Service:** `susmodder-api`  
**Base path:** `/mod-packs`  
**External prefix:** `/api/mod-packs` (via nginx proxy)  
**Web fallback:** `https://susmodder.app/pack/{packCode}`  

---

## Authentication

| Endpoint | Auth | Uwagi |
|----------|------|-------|
| `POST /mod-packs` | `Authorization: Bearer {HTTP_TOKEN}` | Tworzenie paczki |
| `GET /mod-packs` | `Authorization: Bearer {HTTP_TOKEN}` | Lista własnych paczek |
| `GET /mod-packs/:packCode` | Brak | Publiczny podgląd paczki |
| `GET /mod-packs/web/:packCode` | Brak | Web fallback (HTML) |
| `POST /mod-packs/:packCode/external-dll` | `Authorization: Bearer {HTTP_TOKEN}` | Upload DLL |
| `GET /mod-packs/:packCode/dlls/:sha256` | Brak | Pobranie DLL (302 → CDN) |
| `DELETE /mod-packs/:packCode` | `Authorization: Bearer {HTTP_TOKEN}` | Usunięcie paczki |

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
├── ABCD-EFGH-IJKL/
│   └── {sha256}.dll
├── XXXX-YYYY-ZZZZ/
│   └── {sha256}.dll
```

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
