# Plan wdrożenia: susmodder-backend/API — config i compatibility sync

**Data:** 2026-06-04  
**Status:** Do przekazania do wdrożenia w `susmodder-backend`  
**Priorytet:** P1  
**Repo docelowe:** `susmodder-backend` / `susmodder-api`  
**Powiązany POC:** [`DOC/POC/2026-06-04-api-config-compatibility-sync-poc.md`](../POC/2026-06-04-api-config-compatibility-sync-poc.md)  
**Client plan:** [`2026-06-04-susmodder-client-api-sync-plan.md`](2026-06-04-susmodder-client-api-sync-plan.md)

---

## 1. Cel backendu

Wdrożyć addytywne zmiany API, które pozwolą klientowi SUSModder pobierać katalog modów i kompatybilność rzadziej, dokładniej i bez dużych odpowiedzi, gdy dane się nie zmieniły.

Wymagania nadrzędne:

- nie złamać obecnego `/api/susmodder-config`, który zwraca legacy JSON array,
- nie złamać obecnego `/api/compatibility`, parametrów ani odpowiedzi,
- dodać standardowe HTTP caching (`ETag`, `If-None-Match`, `304`),
- dodać lekki endpoint meta/health dla status baru,
- dodać publiczny snapshot aktualnej macierzy kompatybilności,
- zwracać kanoniczny link do GitHuba/projektu moda jako osobne pole, niezależnie od linku pobierania,
- zachować rozsądny cache po stronie API/Redis i nie zwiększyć obciążenia DB.

---

## 2. Obecny stan backendu

### `susmodder-api/routes/config.js`

- `GET /susmodder-config`:
  - zwraca JSON array z tabeli `config`,
  - ma cache TTL 60 s,
  - ustawia `Content-Type` i `X-Cache`,
  - nie ustawia `ETag` / `Last-Modified`,
  - nie obsługuje `If-None-Match`,
  - dla FULL modów nadpisuje `GitHubRepoOrLink` dynamicznym URL-em `/api/mod-download/...`, więc klient nie dostaje już stabilnego linku do repo/projektu,
  - request trackuje online usera.

- `GET /susmodder-config-versions`:
  - istnieje i zwraca historię wersji,
  - nie jest głównym elementem tej zmiany.

### `susmodder-api/routes/compatibility.js`

- `GET /compatibility`:
  - obsługuje `fullModId`, `dllModId`, `fullModVersion`, `dllModVersion`, `status`, `includeUntested`,
  - gdy wersja nie jest podana, używa aktualnej z tabeli `config`,
  - ma cache TTL 120 s,
  - nie ma `ETag` / `304`.

- `GET /compatibility/matrix`:
  - wymaga auth,
  - jest bardziej adminowe i nie powinno być używane jako publiczny snapshot desktopa bez osobnej decyzji.

---

## 3. Zasady kompatybilności wstecznej

1. `GET /api/susmodder-config` bez conditional headers nadal zwraca `200 OK` i identyczny JSON array.
2. `GET /api/compatibility` bez conditional headers nadal zwraca `200 OK` i obecny JSON object.
3. Nowe nagłówki są addytywne.
4. `304 Not Modified` pojawia się tylko wtedy, gdy klient sam wyśle poprawne `If-None-Match`.
5. Nowe endpointy są opcjonalne dla starych klientów.
6. Nie zmieniać nazw istniejących pól JSON ani semantyki `success`, `count`, `compatibilities`.
7. Nowy link do projektu ma być osobnym polem addytywnym; nie zmieniać istniejącej semantyki `GitHubRepoOrLink` jako linku pobierania/dynamicznego download endpointu.

---

## 4. Faza 1 — ETag/304 dla `/susmodder-config`

### Zadania

- [ ] Dodać funkcję wyliczania rewizji configu.
- [ ] Dodać `ETag`, `Last-Modified`, `Cache-Control` do odpowiedzi `200 OK`.
- [ ] Obsłużyć `If-None-Match` i zwrócić `304 Not Modified`, gdy rewizja się zgadza.
- [ ] Zachować obecny cache JSON response, ale cache key powinien uwzględniać rewizję albo być invalidowany po zmianie danych.
- [ ] Nie wykonywać ciężkiego `SELECT * FROM config`, jeśli request może zostać zakończony na etapie rewizji i `304`.

### Rewizja configu

Preferowana opcja:

```text
catalog_revision.config_revision
```

Tabela/klucz monotoniczny aktualizowany przy zapisie przez panel admina/import. Jeśli to za dużo na pierwszy krok, użyć:

```sql
SELECT MAX(COALESCE(UpdatedAt, LastUpdated, '1970-01-01')) AS revision FROM config;
```

Jeśli kolumny dat są niespójne, fallback:

- hash z `Id`, `ModVersion`, `AmongVersion`, linków, `ModType`, `PngFileName`, `Description`,
- cache hash w Redis/node-cache na 60 s, żeby nie liczyć go dla każdego requestu.

### Nagłówki

Przykład:

```http
ETag: "susmodder-config-2026-06-04T10:15:00.000Z"
Last-Modified: Thu, 04 Jun 2026 10:15:00 GMT
Cache-Control: public, max-age=60, stale-while-revalidate=300
```

### Response `304`

```http
HTTP/1.1 304 Not Modified
ETag: "susmodder-config-..."
Cache-Control: public, max-age=60, stale-while-revalidate=300
```

Bez body.

---

## 5. Faza 2 — lekki endpoint meta/health

### Endpoint

```http
GET /api/catalog-meta
```

### Cel

Zastąpić pobieranie pełnego `/api/susmodder-config` jako ping/status bar w aplikacji desktop.

### Response

```json
{
  "success": true,
  "configRevision": "2026-06-04T10:15:00.000Z",
  "compatibilityRevision": "2026-06-04T10:17:00.000Z",
  "serverTimeUtc": "2026-06-04T10:20:00.000Z"
}
```

### Wymagania

- [ ] Endpoint publiczny, bez auth.
- [ ] Cache 30-60 s.
- [ ] Minimalne zapytania DB albo odczyt rewizji z cache.
- [ ] Zwraca `200` szybko; jeśli nie da się pobrać compatibility revision, nadal może zwrócić `success: true` z `compatibilityRevision: null` i logiem warning.
- [ ] Opcjonalnie ustawia `ETag` dla meta response.

### Alternatywa

Można dodać `HEAD /api/susmodder-config`, ale rekomendowany jest osobny `catalog-meta`, bo jednym requestem obsłuży config + compatibility.

---

## 6. Faza 3 — ETag/304 dla `/compatibility`

### Zadania

- [ ] Wyliczać rewizję kompatybilności zależną od danych `compatibility_matrix` i aktualnych wersji modów.
- [ ] Dodać `ETag`, `Last-Modified`, `Cache-Control` do `GET /compatibility`.
- [ ] Cache key musi uwzględniać wszystkie query parametry:
  - `fullModId`, `dllModId`,
  - `fullModVersion`, `dllModVersion`,
  - `status`, `includeUntested`.
- [ ] `If-None-Match` zwraca `304`, gdy rewizja dla danego query się nie zmieniła.

### Uwaga o wersjach

Endpoint już obsługuje wersje. Nie zmieniać tej semantyki. Klient po swojej stronie zacznie przekazywać wersje dokładniej.

---

## 7. Faza 4 — publiczny snapshot kompatybilności

### Endpoint

```http
GET /api/compatibility/snapshot?onlyCurrentVersions=true
```

### Cel

Jednym, cache'owanym requestem dostarczyć desktopowi aktualną macierz kompatybilności do lokalnego SQLite cache. To ograniczy requesty per otwarcie inspektora/managera DLL.

### Query params

| Parametr | Typ | Default | Opis |
|---|---:|---:|---|
| `onlyCurrentVersions` | boolean | `true` | Zwraca tylko kombinacje zgodne z aktualnymi wersjami modów. |
| `includeUntested` | boolean | `true` | Czy zawierać `NT`. |
| `status` | string | brak | Opcjonalny filtr `F,W,NT,NW`. |

### Response

```json
{
  "success": true,
  "revision": "compat-2026-06-04T10:17:00.000Z",
  "generatedAtUtc": "2026-06-04T10:20:00.000Z",
  "count": 123,
  "entries": [
    {
      "id": 77,
      "fullModId": 1,
      "fullModVersion": "5.4.0",
      "fullModCurrentVersion": "5.4.0",
      "dllModId": 5,
      "dllModVersion": "1.2.0",
      "dllModCurrentVersion": "1.2.0",
      "status": "W",
      "isExactVersion": true,
      "warning": null
    }
  ]
}
```

### Semantyka `isExactVersion`

- `true`: wpis dotyczy aktualnych wersji obu modów albo wersji dokładnie wskazanych w query.
- `false`: wpis historyczny; desktop nie powinien traktować historycznego `F/W` jako aktualnej kompatybilności.

### ETag

Snapshot musi obsługiwać:

```http
If-None-Match: "compat-..."
```

Przy braku zmian:

```http
304 Not Modified
```

### SQL — sugerowany kierunek

Bazować na `compatibility_matrix` + join do `config` dla aktualnych wersji:

```sql
SELECT
  cm.Id,
  cm.FullModId,
  cm.FullModVersion,
  fm.ModVersion AS FullModCurrentVersion,
  cm.DllModId,
  cm.DllModVersion,
  dm.ModVersion AS DllModCurrentVersion,
  cm.CompatibilityStatus,
  CASE
    WHEN cm.FullModVersion = fm.ModVersion AND cm.DllModVersion = dm.ModVersion THEN TRUE
    ELSE FALSE
  END AS IsExactVersion
FROM compatibility_matrix cm
JOIN config fm ON cm.FullModId = fm.Id AND fm.ModType = 'full'
JOIN config dm ON cm.DllModId = dm.Id AND dm.ModType = 'dll'
WHERE ...
```

Jeśli `onlyCurrentVersions=true`, dodać warunek:

```sql
cm.FullModVersion = fm.ModVersion AND cm.DllModVersion = dm.ModVersion
```

---

## 8. Faza 5 — invalidacja cache po zmianach admina

### Zadania

- [ ] Po każdej zmianie tabeli `config` invalidować:
  - cache `susmodder-config`,
  - `catalog-meta`,
  - rewizję configu.
- [ ] Po każdej zmianie `compatibility_matrix` invalidować:
  - cache `compat-*`,
  - `compatibility-snapshot-*`,
  - rewizję kompatybilności.
- [ ] Jeśli istnieje panel admina/PHP legacy zapisujący config poza Node API, dopilnować invalidacji albo oprzeć rewizję o DB timestamp/hash, żeby nie wymagać pełnej kontroli nad write path.

---

## 9. Faza 6 — kanoniczny link do GitHuba/projektu moda

### Cel

Klient SUSModder będzie potrzebował w przyszłej funkcji linku do strony projektu/repozytorium moda. Obecnie `GitHubRepoOrLink` nie jest dobrym źródłem dla UI/funkcji projektowych, bo:

- dla FULL modów backend zamienia je na dynamiczny URL pobierania `/api/mod-download/{id}/{version}?platform=...`,
- dla DLL może to być bezpośredni link do pliku/release assetu,
- link pobierania i link do projektu to różne pojęcia.

### Rekomendowane pole

Dodać osobne pole w API:

```json
"GitHubProjectUrl": "https://github.com/org/repository"
```

Właściwość:

- nullable, jeśli projekt nie ma GitHuba albo nie jest jeszcze uzupełniony,
- kanoniczny URL repo/projektu, bez linku do konkretnego assetu,
- nie używać go jako download URL,
- nie nadpisywać nim `GitHubRepoOrLink` ani `EpicGitHubRepoOrLink`.

### Źródło danych

Preferowana opcja:

```sql
ALTER TABLE config ADD COLUMN GitHubProjectUrl TEXT NULL;
```

Jeśli baza/driver używa MySQL:

```sql
ALTER TABLE config ADD COLUMN GitHubProjectUrl VARCHAR(500) NULL;
```

Fallback na start, jeśli kolumna nie jest jeszcze wypełniona:

- jeśli `GitHubRepoOrLink` jest URL-em GitHub, spróbować wyprowadzić `https://github.com/{owner}/{repo}`,
- jeśli `EpicGitHubRepoOrLink` jest GitHub URL-em i Steam link nie jest, analogicznie,
- jeśli nie da się wyprowadzić — `null`.

Uwaga: fallback ma być best-effort. Docelowo susadmin powinien mieć możliwość ręcznego ustawienia `GitHubProjectUrl`.

### Endpointy, w których zwracać pole

- [ ] `GET /api/susmodder-config` — dodać `GitHubProjectUrl` do każdego moda.
- [ ] `GET /api/susmodder-config-versions` — dodać `GitHubProjectUrl`, jeśli historia wersji ma pokazywać link projektu.
- [ ] `GET /api/catalog-meta` — nie musi zwracać linków, tylko rewizje.
- [ ] `GET /api/compatibility/snapshot` — opcjonalnie dodać `fullModGitHubProjectUrl` i `dllModGitHubProjectUrl`, jeśli klient ma używać snapshotu do UI bez dodatkowego lookupu katalogu. Jeśli klient zawsze łączy snapshot z lokalnym katalogiem po ID, można pominąć.

### Wpływ na rewizję/cache

- `GitHubProjectUrl` musi wchodzić do rewizji/ETag configu.
- Zmiana `GitHubProjectUrl` musi invalidować cache `/susmodder-config` i `catalog-meta`.
- Jeśli snapshot compatibility zwraca te linki, zmiana `GitHubProjectUrl` musi invalidować także snapshot.

### Walidacja

- Akceptować tylko `https://github.com/{owner}/{repo}` albo `https://github.com/{owner}/{repo}/...`, normalizując do repo root.
- Nie zwracać tokenów, query stringów ani prywatnych URL-i.
- Jeśli admin poda nie-GitHub homepage w przyszłości, rozważyć osobne pole `ProjectUrl`; w tym planie wymagany jest GitHub, więc pole nazywa się `GitHubProjectUrl`.

### Testy

- [ ] `/susmodder-config` zwraca `GitHubProjectUrl` jako osobne pole i nadal zachowuje dotychczasowy `GitHubRepoOrLink`.
- [ ] Dla FULL modów `GitHubRepoOrLink` nadal jest dynamicznym download URL-em, a `GitHubProjectUrl` jest repo URL-em.
- [ ] Jeśli `GitHubProjectUrl` jest null, response nadal jest poprawny.
- [ ] Zmiana `GitHubProjectUrl` zmienia `ETag` configu.

---

## 10. Rate limiting i cache

### Rekomendowane TTL

| Endpoint | Cache server-side | HTTP cache |
|---|---:|---:|
| `/susmodder-config` | 60 s | `max-age=60, stale-while-revalidate=300` |
| `/catalog-meta` | 30 s | `max-age=30` |
| `/compatibility` | 120 s | `max-age=120, stale-while-revalidate=300` |
| `/compatibility/snapshot` | 120-300 s | `max-age=120, stale-while-revalidate=600` |

### Rate limit

- Publiczne GET mogą zostać na obecnych limitach.
- Snapshot może mieć osobny limit, np. 30 req/min/IP, bo response jest większy.
- `304` nadal liczy się jako request, ale ma minimalny transfer i koszt DB przy dobrej rewizji/cache.

---

## 11. Testy backendu

### Unit/integration

- [ ] `/susmodder-config` bez `If-None-Match` -> `200`, JSON array, `ETag`.
- [ ] `/susmodder-config` z pasującym `If-None-Match` -> `304`, bez body.
- [ ] `/susmodder-config` po zmianie rewizji -> `200`, nowy `ETag`.
- [ ] `/catalog-meta` -> `200`, zawiera `configRevision`, `compatibilityRevision`, `serverTimeUtc`.
- [ ] `/compatibility` bez conditional headers zachowuje obecny kontrakt.
- [ ] `/compatibility` z pasującym `If-None-Match` -> `304`.
- [ ] `/compatibility/snapshot` zwraca entries z `fullModVersion`, `dllModVersion`, `status`, `isExactVersion`.
- [ ] `onlyCurrentVersions=true` nie zwraca historycznych kombinacji jako aktualnych.
- [ ] Filtry `status` i `includeUntested` działają w snapshotcie.
- [ ] `/susmodder-config` zwraca addytywne pole `GitHubProjectUrl` bez zmiany dotychczasowych linków pobierania.
- [ ] `GitHubProjectUrl` jest uwzględniony w rewizji/ETag configu.

### Manual smoke test

```bash
curl -i https://susmodder.app/api/susmodder-config
curl -i -H 'If-None-Match: "ETAG_Z_POPRZEDNIEGO_REQUESTU"' https://susmodder.app/api/susmodder-config
curl -i https://susmodder.app/api/catalog-meta
curl -i 'https://susmodder.app/api/compatibility?fullModId=1'
curl -i 'https://susmodder.app/api/compatibility/snapshot?onlyCurrentVersions=true'
```

### Weryfikacja kompatybilności legacy

- Stary SUSModder bez `If-None-Match` nadal pobiera config.
- Zewnętrzne integracje używające `/api/compatibility?dllModId=...` dostają ten sam kształt response.

---

## 12. Deployment i rollback

### Deployment

1. Wdrożyć backend z ETag dla configu.
2. Sprawdzić legacy client i curl smoke test.
3. Wdrożyć `catalog-meta`.
4. Wdrożyć ETag dla compatibility.
5. Wdrożyć snapshot compatibility.
6. Dodać `GitHubProjectUrl` do configu i odpowiedzi API.
7. Dopiero potem włączyć pełną integrację po stronie desktopa.

### Rollback

- Jeśli problem dotyczy tylko `304`, można tymczasowo ignorować `If-None-Match` i zawsze zwracać `200`.
- Jeśli problem dotyczy snapshotu, wyłączyć/ukryć tylko `/compatibility/snapshot`; obecny `/compatibility` zostaje fallbackiem.
- Jeśli problem dotyczy `GitHubProjectUrl`, można tymczasowo zwracać `null`; nie wpływa to na legacy download flow.
- Nie usuwać istniejących endpointów ani pól.

---

## 13. Checklist do przekazania backendowi

- [ ] Additive only — brak breaking changes w `/susmodder-config` i `/compatibility`.
- [ ] `ETag` i `304` dla configu.
- [ ] Lekki `GET /api/catalog-meta`.
- [ ] `ETag` i `304` dla compatibility.
- [ ] Publiczny `GET /api/compatibility/snapshot` z `ETag`.
- [ ] Addytywne `GitHubProjectUrl` w `/susmodder-config` i opcjonalnie w snapshotcie.
- [ ] Cache invalidation/revision po zmianach admina.
- [ ] Testy kontraktu i curl smoke test.
- [ ] Aktualizacja Swagger/API docs.

---

## 14. Uwagi dla implementatora backendu

- Nie zmieniać `MOD_DOWNLOAD_BASE_URL` ani transformacji linków w configu bez osobnej decyzji.
- Nie używać `GitHubProjectUrl` jako linku pobierania; to link informacyjny/projektowy.
- Nie wymagać auth dla nowych read-only endpointów, chyba że pojawi się osobna decyzja produktowa.
- Nie ujawniać danych administracyjnych w snapshotcie; tylko ID, wersje, status i warning.
- Jeśli reverse proxy ma gzip/brotli, upewnić się, że snapshot korzysta z kompresji.
- Logować liczbę `200` vs `304`, żeby potwierdzić spadek transferu po wdrożeniu klienta.
