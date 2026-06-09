# Bug: `POST /v2/modpacks` → 500 INTERNAL_ERROR

**Status:** ✅ **NAPRAWIONE** (wdrożone na produkcję, 2026-06-07)  
**Data zgłoszenia:** 2026-06-07  
**Środowisko:** produkcja `https://api.susmodder-cdn.ovh/v2`  
**Powiązane:** [`MODPACK_API.md`](../../PLAN/MODPACK_API.md) (v1), [`consumer-susmodder-3x.md`](consumer-susmodder-3x.md)

### Stan po fixie

| Endpoint | Przed | Po |
|----------|-------|-----|
| `POST /v2/modpacks` | ❌ 500 `INTERNAL_ERROR` | ✅ 201 `{ data: { packCode, shareUrl, deepLink, expiresAt } }` |
| `GET /v2/modpacks/:code` | ⚠️ duplikaty PascalCase, niespójny `shareUrl` | ✅ czysty camelCase, `shareUrl` → `/pack/` |
| `DELETE /v2/modpacks/:code` | ❌ 500 (brak kolumny `deleted_at`) | ✅ 200 |

Klient SUSModder 3.x: workaround legacy `/api/mod-packs` **usunięty** — zapis wyłącznie przez v2.

---

## Streszczenie (historyczne)

Endpoint **`POST /v2/modpacks`** przechodzi walidację Joi (body jest poprawne), ale **zawsze kończy się HTTP 500** z generycznym `INTERNAL_ERROR`. Ten sam payload na legacy **`POST /api/mod-packs`** działa (201 + `packCode`).

Odczyt paczki przez v2 (`GET /v2/modpacks/:code`) **działa** — także dla paczek utworzonych przez v1.

Problem leży wyłącznie w **ścieżce tworzenia** w implementacji v2 (handler → serwis → DB / response), nie w kliencie ani w formacie danych wejściowych.

---

## Reprodukcja

### Request (minimalny, zawsze 500 na v2)

```http
POST /v2/modpacks HTTP/1.1
Host: api.susmodder-cdn.ovh
Authorization: Bearer <HTTP_TOKEN>
X-User-Hash: a0516c62cae89f455520ec5f5355086854eef12ebec970a8634287d1849dd348
Content-Type: application/json

{
  "creatorHash": "a0516c62cae89f455520ec5f5355086854eef12ebec970a8634287d1849dd348",
  "fullModId": 1,
  "fullModVersion": "5.3.1",
  "ttlDays": 30,
  "dllMods": []
}
```

### Odpowiedź v2 (błąd)

```json
HTTP/1.1 500 Internal Server Error

{
  "error": {
    "code": "INTERNAL_ERROR",
    "message": "An unexpected error occurred"
  }
}
```

### Ten sam request na v1 (działa)

```http
POST /api/mod-packs HTTP/1.1
Host: susmodder.app
Authorization: Bearer <HTTP_TOKEN>
X-User-Hash: <ten sam hash>
Content-Type: application/json

{ ...identyczne body... }
```

```json
HTTP/1.1 201 Created

{
  "success": true,
  "packId": "8350d99d-894c-4877-ae99-3f9b61082c64",
  "packCode": "VT5R-H5UU-Y3YT",
  "shareUrl": "https://susmodder.app/pack/VT5R-H5UU-Y3YT",
  "deepLink": "susmodder://pack/VT5R-H5UU-Y3YT",
  "expiresAt": "2026-07-07T10:38:59.790Z"
}
```

### PowerShell (szybki test)

```powershell
$token = "<HTTP_TOKEN>"
$userHash = "a0516c62cae89f455520ec5f5355086854eef12ebec970a8634287d1849dd348"
$body = @{
  creatorHash = $userHash
  fullModId = 1
  fullModVersion = "5.3.1"
  modName = "test"
  includeIntegrationDll = $false
  ttlDays = 30
  dllMods = @()
} | ConvertTo-Json -Compress

$h = @{ Authorization = "Bearer $token"; "X-User-Hash" = $userHash }

# v2 → 500
Invoke-RestMethod "https://api.susmodder-cdn.ovh/v2/modpacks" -Method POST -Headers $h -ContentType "application/json" -Body $body

# v1 → 201
Invoke-RestMethod "https://susmodder.app/api/mod-packs" -Method POST -Headers $h -ContentType "application/json" -Body $body
```

---

## Co działa / co nie (macierz)

| Endpoint | Metoda | Status | Uwagi |
|----------|--------|--------|-------|
| `/v2/modpacks` | POST | ❌ **500** | Walidacja OK, crash w logice |
| `/api/mod-packs` | POST | ✅ 201 | Ta sama logika biznesowa powinna działać |
| `/v2/modpacks/:code` | GET | ✅ 200 | Zwraca `{ data: { ... } }`, w tym paczki z v1 |
| `/v2/modpacks` | POST bez `creatorHash` | ✅ 400 | `"creatorHash" is required` — Joi działa |
| `/v2/modpacks` | POST z `externalDlls` | ✅ 400 | `"externalDlls" is not allowed` — schema v2 |
| `/v2/modpacks` | POST z nieznanym polem | ✅ 400 | `"foo" is not allowed` — strict schema |

**Wniosek:** middleware walidacji v2 jest podłączony; **wyjątek leci dopiero po walidacji** (handler, serwis, transakcja DB, formatowanie odpowiedzi).

---

## Schemat wejścia v2 (potwierdzony empirycznie)

Różnice względem v1 (`MODPACK_API.md`):

| Pole | v1 POST | v2 POST |
|------|---------|---------|
| `creatorHash` | wymagane | wymagane |
| `fullModId`, `fullModVersion`, `dllMods`, … | OK | OK |
| `externalDlls` | dozwolone (pre-deklaracja) | **zabronione** — upload osobno przez `POST /modpacks/:code/dlls` |
| `touConfig` | dozwolone | nie testowane (klient na razie nie wysyła) |

Nagłówki (zgodnie z innymi endpointami v2):

- `Authorization: Bearer <HTTP_TOKEN>` — token aplikacji
- `X-User-Hash: <64 hex>` — rate limit / ownership (10 req/min wg dokumentacji)

---

## Oczekiwana odpowiedź v2 (kontrakt)

Zgodnie z formatem v2 (`{ data, meta }`) i v1 (pola biznesowe):

```json
HTTP/1.1 201 Created

{
  "data": {
    "packId": "uuid",
    "packCode": "XXXX-XXXX-XXXX",
    "shareUrl": "https://susmodder.app/pack/XXXX-XXXX-XXXX",
    "deepLink": "susmodder://pack/XXXX-XXXX-XXXX",
    "expiresAt": "2026-07-07T10:38:59.790Z"
  }
}
```

Klient SUSModder 3.x już parsuje envelope `{ data: { packCode, ... } }` oraz legacy `{ success, packCode, ... }`.

---

## Hipotezy dla backendu (gdzie szukać)

Ponieważ **v1 POST działa**, a **v2 GET czyta te same rekordy**, najpewniej v2 POST to **osobna, niedokończona ścieżka** zamiast reuse v1 service:

1. **Handler v2 nie wywołuje wspólnego `createModPack()`** — pusta implementacja, zły import, lub `await` na undefined.
2. **Format odpowiedzi** — próba zbudowania `{ data }` na obiekcie Sequelize z cyklicznymi referencjami → `JSON.stringify` / mapper rzuca wyjątek.
3. **Insert `dll_mods`** — pętla po `dllMods` z błędną nazwą kolumny / FK tylko w ścieżce v2.
4. **Generowanie `packCode`** — inna funkcja w v2 (np. zwraca format niezgodny z DB constraint) → uncaught DB error zamiast `PACK_CODE_COLLISION`.
5. **Brak `shareUrl` / `deepLink` w mapperze v2** — odczyt undefined property przy budowaniu response.
6. **Transakcja** — rollback bez mapowania błędu SQL na 4xx (np. FK `full_mod_id` → 500 zamiast `MOD_NOT_IN_CATALOG`). *Uwaga: v1 z tym samym `fullModId=1` działa, więc FK raczej OK — chyba że v2 używa innej tabeli.*

### Gdzie patrzeć w logach serwera

Przy repro z powyższym body szukać stack trace tuż po `POST /v2/modpacks`:

- `routes/v2/modpacks.js` (lub odpowiednik)
- `services/modPackService.create`
- `modpackResponseMapper` / `toV2Envelope`
- Sequelize: `mod_packs`, `mod_pack_dll_mods` insert

---

## Dodatkowy bug (niski priorytet): duplikaty pól w GET

`GET /v2/modpacks/:code` zwraca **jednocześnie camelCase i PascalCase** na tym samym obiekcie:

```json
{
  "data": {
    "id": "...",
    "packCode": "VT5R-H5UU-Y3YT",
    "Id": "...",
    "PackCode": "VT5R-H5UU-Y3YT",
    ...
  }
}
```

To wygląda na `Object.assign(row, row.dataValues)` lub podwójny mapper. Nie blokuje klienta (case-insensitive), ale warto posprzątać przy okazji POST.

---

## Niespójność `shareUrl` (niski priorytet)

| Źródło | `shareUrl` |
|--------|------------|
| v1 POST response | `https://susmodder.app/pack/{code}` |
| v2 GET `data.shareUrl` | `https://susmodder.app/modpacks/{code}` |

Web fallback w nginx (`/pack/`) wskazuje na format **`/pack/`**. GET v2 powinien zwracać ten sam URL co create.

---

## Kryteria akceptacji fixa

1. `POST /v2/modpacks` z minimalnym poprawnym body → **201** + `{ data: { packCode, shareUrl, deepLink, expiresAt } }`.
2. `POST` z `dllMods: [{ dllModId, dllModVersion }]` → 201, GET zwraca te same DLL.
3. `POST` z `externalDlls` → **400** `VALIDATION_ERROR` (bez zmiany kontraktu).
4. `POST` bez `creatorHash` → **400** (bez zmiany).
5. Przekroczenie limitu 10 paczek → **429** `PACK_LIMIT_REACHED` (nie 500).
6. Po fixie: ten sam test E2E co v1 (curl powyżej) na `/v2/modpacks` daje 201.

---

## Kontekst klienta (SUSModder 3.x)

- Klient wysyłał pierwotnie `externalDlls: []` → v2 słusznie odrzucało (**400**, naprawione po stronie klienta).
- Po usunięciu `externalDlls` z body → **500** — to jest ten bug.
- Tymczasowy workaround w kliencie (legacy POST) **nie powinien zostać na stałe** — docelowo cały zapis modpacków przez `/v2/modpacks`.

---

## Historia odkrycia

1. Użytkownik: tworzenie zestawu → `400 "externalDlls" is not allowed`.
2. Fix klienta: osobny serializer bez `externalDlls` dla v2.
3. Użytkownik: tworzenie zestawu → `500 INTERNAL_ERROR`.
4. Testy E2E z repo: v2 POST zawsze 500; v1 POST z identycznym body → 201; v2 GET paczki z v1 → 200.
