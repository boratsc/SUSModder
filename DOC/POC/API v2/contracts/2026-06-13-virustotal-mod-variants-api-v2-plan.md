# Plan: VirusTotal dla wariantów modów w API v2

Data: 2026-06-13  
Zakres: `susmodder-api`, API v2, `mod_variants`

## Kontekst

`VirusTotal` już istnieje w `susmodder-api`:

- `GET /virustotal/report` w `susmodder-api/routes/virustotal.js`
  - skanuje publikowane binarki SUSModdera,
  - używa `VIRUSTOTAL_API_KEY`,
  - odświeża cache codziennie o 03:00 Europe/Warsaw.
- `GET /api/v2/virustotal/report` w `susmodder-api/routes/v2/virustotal.js`
  - opakowuje cache z v1 i dodaje ETag/cache headers.
- Modpack DLL mają osobny flow VirusTotal:
  - `routes/modPacksCleanup.js`,
  - `routes/modPacksDll.js`,
  - `utils/vtScanDll.js`.

Brakuje natomiast skanowania i raportu VirusTotal per wariant moda z tabeli `mod_variants`, używanej przez:

```http
GET /api/v2/downloads/mod/{id}/{version}?platform=steam&arch=x64
```

## 1. Goal and non-goals

### Goal

- Rozszerzyć istniejącą integrację VirusTotal na pliki z `mod_variants` jako mechanizm best-effort.
- Przy dodaniu lub aktualizacji wariantu moda opcjonalnie uruchamiać skan/lookup VirusTotal po SHA256.
- Zapisać wynik skanu przy wariancie w DB, jeśli raport uda się pobrać.
- Przeskanować/backfillować obecną bazę `mod_variants`, żeby istniejące pliki też dostały raporty tam, gdzie to możliwe.
- Dodać publiczny endpoint API v2 zwracający raport VT dla konkretnego wariantu:

```http
GET /api/v2/downloads/mod/{id}/{version}/virustotal?platform=steam&arch=x64
```

- Rozszerzyć endpoint downloadu v2 o opcjonalne nagłówki informujące o statusie skanu, jeśli raport istnieje:

```http
X-SUSModder-Scan-Status: clean
X-SUSModder-VT-Permalink: https://www.virustotal.com/gui/file/...
X-SUSModder-VT-Last-Analysis: ...
```

### Non-goals

- Nie dodawać VirusTotal od zera — istniejący kod ma zostać wykorzystany lub ujednolicony.
- Nie zmieniać istniejącego `/virustotal/report` dla binarek aplikacji SUSModder.
- Nie przebudowywać legacy downloadu jako głównego mechanizmu — API v2 pozostaje docelowe. Legacy endpoint `/mod-download/:id/:version` może jednak dostać minimalne zabezpieczenia/nagłówki VT, bo API v2 jest jeszcze w trakcie wdrożenia, a legacy to obecne API v1.
- Nie blokować downloadów modów przy `pending`/`error` w pierwszym wdrożeniu.
- Nie dodawać UI w `susadmin` w tym zadaniu.
- Nie robić roadmapy dalszych zabezpieczeń ani pełnego systemu moderacji plików.
- Nie traktować braku raportu VirusTotal jako błędu krytycznego. Dostępność i poprawność wersji modów jest ważniejsza niż raport VT.

## 2. Service ownership

### `susmodder-api` — właściciel zmian

Zmiany w:

- `susmodder-api/routes/v2/downloads.js`
- `susmodder-api/routes/v2/admin/mods.js`
- `susmodder-api/routes/githubUpdatesHelpers.js`
- `susmodder-api/utils/vtScanDll.js` albo nowy wspólny serwis `susmodder-api/services/virustotalService.js`
- `susmodder-api/.env.example`
- `migrations/018_add_virustotal_to_mod_variants.sql`
- Swagger JSDoc dla nowego endpointu i nowych nagłówków

### `susadmin`

Bez zmian w tym etapie.

### `discord-bots`, `github-monitor`, legacy PHP

Bez zmian, chyba że któryś proces dodaje warianty poza API v2. Jeśli tak, powinien używać tego samego serwisu/skryptu skanowania albo dostać osobne zadanie backfill/rescan.

## 3. Workflow impact

### Docker compose

Brak nowych portów i brak nowych kontenerów.

Istniejące `VIRUSTOTAL_API_KEY` już jest w `.env.example`, ale warto dodać przełączniki sterujące zachowaniem wariantów:

```env
VIRUSTOTAL_SCAN_ON_VARIANT_WRITE=true
VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS=false
VIRUSTOTAL_MAX_UPLOAD_MB=32
```

### Migrations

Wymagana migracja PostgreSQL dla `mod_variants`.

Proponowane kolumny:

```sql
ALTER TABLE mod_variants
  ADD COLUMN IF NOT EXISTS vt_analysis_id TEXT,
  ADD COLUMN IF NOT EXISTS vt_permalink TEXT,
  ADD COLUMN IF NOT EXISTS vt_last_analysis_date TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS vt_last_submitted_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS vt_last_checked_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS vt_stats JSONB,
  ADD COLUMN IF NOT EXISTS vt_error TEXT;
```

Istniejące `scan_status` zostaje użyte jako status skanu:

- `pending`
- `scanning`
- `clean`
- `suspicious`
- `malicious`
- `error`
- `unknown`

Indeksy:

```sql
CREATE INDEX IF NOT EXISTS idx_mod_variants_scan_status
  ON mod_variants (scan_status);

CREATE INDEX IF NOT EXISTS idx_mod_variants_sha256
  ON mod_variants (sha256);
```

### Nginx

Bez zmian w konfiguracji nginx.

Po deployu produkcyjnym `susmodder-api`, jeśli kontener zostanie odtworzony, wymagany reload nginx:

```bash
docker compose -f docker-compose.prod.yml up -d --build susmodder-api
docker exec nginx nginx -s reload
```

## 4. Security/deployment considerations

- `VIRUSTOTAL_API_KEY` jest sekretem i nie może być logowany ani zwracany z API.
- Publiczny endpoint raportu wariantu ma zwracać tylko bezpieczny subset danych:
  - `sha256`,
  - `scanStatus`,
  - `vtPermalink`,
  - `lastAnalysisStats`,
  - timestamps,
  - liczby detekcji.
- Nie zwracać pełnej surowej odpowiedzi VirusTotal.
- Najpierw zawsze robić lookup po SHA256 przez `/files/{sha256}`.
- Uploadować plik do VT tylko jeśli raport nie istnieje.
- Respektować limity VirusTotal API, szczególnie przy backfillu.
- Przy braku `VIRUSTOTAL_API_KEY` API nie może crashować; wariant może dostać status `unknown` albo pozostać bez raportu.
- Każdy błąd VT, timeout, rate limit, brak pliku do uploadu albo problem modeli AI ma być traktowany jako best-effort failure: zalogować bez sekretów, zapisać opcjonalny `vt_error`, ale nie przerywać dodawania wersji ani aktualizacji moda.
- W pierwszym etapie nie blokować publicznego downloadu modów, żeby nie zepsuć istniejącego API v2.
- Ewentualne blokowanie `suspicious`/`malicious` downloadów zostawić za flagą, domyślnie wyłączoną:

```env
VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS=false
```

### False positive verification with independent AI models

Mody i narzędzia modderskie często generują false positive w antywirusach. Jeśli VirusTotal wykryje coś podejrzanego, wynik nie powinien automatycznie oznaczać moda jako definitywnie złośliwego bez dodatkowej weryfikacji.

Dodać osobny status procesu weryfikacji, np.:

- `ai_review_not_needed` — brak detekcji albo status `clean`,
- `ai_review_pending` — VT wykrył `suspicious`/`malicious`, czeka na analizę,
- `ai_review_false_positive_likely` — niezależne modele uznały, że to najpewniej false positive,
- `ai_review_risk_confirmed` — modele potwierdziły realne ryzyko,
- `ai_review_inconclusive` — brak zgodności modeli albo za mało danych.

Weryfikacja powinna używać 2–3 niezależnych modeli, najlepiej przez istniejącą konfigurację OpenRouter/AI, ale bez wysyłania całych binarek do modeli. Modele powinny dostać tylko bezpieczne metadane:

- nazwę moda i wariantu,
- SHA256, rozmiar, typ artefaktu,
- publiczny link VT,
- `last_analysis_stats`,
- nazwy silników, które wykryły problem,
- kategorie detekcji/nazwy malware z VT,
- kontekst, że jest to mod do gry i możliwe są false positive.

Nie wysyłać tokenów, sekretów, prywatnych URL-i ani pełnej binarki do modeli.

Decyzja automatyczna powinna być konserwatywna:

- jeśli 2 z 3 modeli wskazują `false_positive_likely`, można pokazać status jako „wymaga ostrożności / prawdopodobny false positive”, ale nadal zachować dane VT,
- jeśli 2 z 3 modeli wskazują realne ryzyko, oznaczyć wariant jako `ai_review_risk_confirmed`,
- przy braku większości ustawić `ai_review_inconclusive` i wymagać ręcznej decyzji administratora.

Ten proces ma wspierać decyzję admina, nie ukrywać wyniku VirusTotal przed użytkownikiem. Publiczny raport powinien jasno pokazywać zarówno wynik VT, jak i status dodatkowej weryfikacji.

## 5. Verification plan

### Database

- Uruchomić migrację lokalnie.
- Sprawdzić, że `mod_variants` ma nowe kolumny.
- Sprawdzić idempotencję migracji przez ponowne uruchomienie.

### Unit/helper tests

Dodać testy dla mapowania statusów:

- brak statystyk VT → `unknown`,
- `malicious = 0` → `clean`,
- mała liczba podejrzanych/malicious → zgodnie z przyjętym thresholdem,
- wysoka liczba detekcji → `suspicious` albo `malicious`.

### API tests

1. Dodanie wariantu:

```http
POST /api/v2/admin/mods/{id}/variants
```

Oczekiwane:

- wariant zapisany,
- jeśli VT jest skonfigurowany: `scan_status` ustawiony na `pending` albo `scanning`,
- jeśli VT nie jest skonfigurowany albo zawiedzie: wariant nadal zapisany, bez błędu dla flow wersji,
- skan uruchomiony asynchronicznie/best-effort.

2. Aktualizacja wariantu:

```http
PUT /api/v2/admin/mods/{id}/variants/{variantId}
```

Jeśli zmienia się `sha256`, `downloadUrl` lub `fileSizeBytes`:

- stare dane VT są czyszczone albo zastępowane,
- `scan_status = pending`,
- skan uruchomiony ponownie.

3. Download v2:

```http
GET /api/v2/downloads/mod/{id}/{version}?platform=steam&arch=x64
```

Oczekiwane:

- nadal `302`,
- istnieją dotychczasowe nagłówki `X-SUSModder-SHA256`, `X-SUSModder-File-Size`,
- jeśli raport/status istnieje, dochodzi `X-SUSModder-Scan-Status`,
- jeśli dostępny, dochodzi `X-SUSModder-VT-Permalink`,
- brak raportu VT nie zmienia wyniku downloadu.

4. Raport wariantu:

```http
GET /api/v2/downloads/mod/{id}/{version}/virustotal?platform=steam&arch=x64
```

Oczekiwane `200`:

```json
{
  "success": true,
  "data": {
    "modId": 12,
    "modVersion": "7.0.0",
    "platform": "steam",
    "architecture": "x64",
    "sha256": "...",
    "scanStatus": "clean",
    "vtPermalink": "https://www.virustotal.com/gui/file/...",
    "lastAnalysisStats": {
      "malicious": 0,
      "suspicious": 0,
      "undetected": 60
    }
  }
}
```

5. Brak wariantu:

```http
GET /api/v2/downloads/mod/999/1.0.0/virustotal?platform=steam&arch=x64
```

Oczekiwane:

- `404 VARIANT_NOT_FOUND`.

### Docker verification

```bash
docker compose up --build susmodder-api
curl http://localhost:3001/health
curl http://localhost:3001/api-docs
```

## 6. Implementation order

### Step 1 — migracja DB

Dodać:

```text
migrations/018_add_virustotal_to_mod_variants.sql
```

Zakres:

- kolumny VT do `mod_variants`,
- indeks po `scan_status`,
- indeks po `sha256`,
- pełna idempotencja.

### Step 2 — ujednolicić helper VirusTotal

Obecnie istnieje:

```text
susmodder-api/utils/vtScanDll.js
```

Opcje:

1. Rozszerzyć `utils/vtScanDll.js` tak, żeby nie był DLL-specific.
2. Lepsza opcja: dodać nowy serwis:

```text
susmodder-api/services/virustotalService.js
```

Funkcje:

- `getFileReport(sha256, apiKey)`,
- `submitFileForAnalysis(fileBuffer, fileName, apiKey)`,
- `waitForAnalysis(analysisId, apiKey)`,
- `determineStatus(stats)`,
- `scanBuffer(fileBuffer, fileName)`,
- `refreshVariantReport(variantId)`.

Potem modpacki można zostawić bez zmian albo przepiąć w osobnym cleanupie.

### Step 3 — best-effort scan po dodaniu/aktualizacji wariantu

W `routes/v2/admin/mods.js`:

- `POST /mods/:id/variants`
  - po insercie, jeśli VT jest włączony, ustawić `scan_status = pending`,
  - uruchomić scan asynchronicznie,
  - odpowiedź admina nie czeka na VT,
  - błąd VT nie przerywa dodania wariantu ani wersji.

- `PUT /mods/:id/variants/:variantId`
  - jeśli zmienia się plik lub hash, wyczyścić pola `vt_*`,
  - ustawić `scan_status = pending`,
  - uruchomić scan ponownie.

- `download-to-cdn`
  - po compute SHA256 i upsercie wariantu uruchomić scan wariantu.

### Step 4 — GitHub update helper

W `routes/githubUpdatesHelpers.js`:

- po utworzeniu `mod_variants` dla pobranych release assets odpalić skan dla każdego nowego wariantu,
- błąd VT nie powinien rollbackować aktualizacji moda,
- przy błędzie opcjonalnie zapisać `scan_status = error` i `vt_error`, ale priorytetem jest poprawne dodanie wersji i wariantu.

### Step 5 — endpoint raportu per wariant

W `routes/v2/downloads.js` dodać:

```http
GET /downloads/mod/:id/:version/virustotal
```

Query params takie jak download:

- `platform`: `steam | epic | msstore | itchio`, default `steam`,
- `arch`: `x64 | x86`, default `x64`.

Logika:

- walidacja `id`, `version`, `platform`, `arch`,
- lookup dokładnego wariantu,
- `404 VARIANT_NOT_FOUND` jeśli brak,
- zwrócić zapisany raport z DB,
- opcjonalnie triggerować refresh w tle, jeśli status jest `pending` albo raport jest stary.

### Step 6 — nagłówki VT w downloadzie v2 i minimalny parity dla v1 legacy

W istniejącym endpointcie v2:

```http
GET /api/v2/downloads/mod/:id/:version
```

rozszerzyć SELECT o:

```sql
scan_status, vt_permalink, vt_last_analysis_date
```

Dodać nagłówki:

```http
X-SUSModder-Scan-Status
X-SUSModder-VT-Permalink
X-SUSModder-VT-Last-Analysis
```

Ponieważ API v2 jest jeszcze w trakcie wdrożenia, a `/mod-download/:id/:version` pełni rolę legacy API v1, można też zmienić legacy endpoint w minimalnym zakresie:

- jeśli legacy request da się zmapować na rekord `mod_variants`, zwrócić te same nagłówki VT co v2,
- nie zmieniać kontraktu redirectu `302`,
- nie usuwać obecnego fallbacku CDN/GitHub bez osobnej decyzji,
- jeśli brak dopasowanego `mod_variants`, zachować obecne zachowanie legacy endpointu,
- ewentualne blokowanie `suspicious`/`malicious` stosować tylko za flagą `VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS=true`.

Dzięki temu obecni klienci v1 dostają sygnał bezpieczeństwa, ale migracja do v2 nadal pozostaje docelowym kierunkiem.

### Step 7 — Swagger i env example

- Dodać Swagger JSDoc dla nowego endpointu.
- Uzupełnić Swagger dla nowych nagłówków w endpointcie downloadu.
- Dopisać do `.env.example`:

```env
VIRUSTOTAL_SCAN_ON_VARIANT_WRITE=true
VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS=false
VIRUSTOTAL_MAX_UPLOAD_MB=32
```

### Step 8 — AI verification dla podejrzanych wyników

Dodać mechanizm uruchamiany po skanie VT, gdy status wariantu jest `suspicious` albo `malicious`.

Zakres minimalny:

- dodać kolumny albo osobną tabelę na wynik weryfikacji AI, np. `vt_ai_review_status`, `vt_ai_review_summary`, `vt_ai_reviewed_at`, `vt_ai_model_votes`,
- przygotować prompt, który dostaje wyłącznie metadane VT i kontekst modderski,
- odpalić 2–3 niezależne modele,
- zapisać głosy modeli i wynik większościowy,
- nie zmieniać surowego `scan_status` z VT — AI review jest dodatkową warstwą interpretacji.

Publiczny endpoint raportu per wariant powinien zwracać oba poziomy informacji:

```json
{
  "scanStatus": "suspicious",
  "aiReviewStatus": "false_positive_likely",
  "aiReviewSummary": "Detekcje wyglądają na heurystyczne i typowe dla mod loaderów; zalecana ostrożność.",
  "aiModelVotes": {
    "falsePositiveLikely": 2,
    "riskConfirmed": 0,
    "inconclusive": 1
  }
}
```

### Step 9 — backfill / przeskanowanie obecnej bazy wariantów

To jest wymagany element wdrożenia, ale nadal best-effort: skrypt ma przejść po obecnej bazie i uzupełnić raporty tam, gdzie jest to możliwe, bez blokowania produkcyjnego działania API.

Dodać skrypt:

```text
susmodder-api/scripts/scan-existing-mod-variants.js
```

Tryby:

```bash
node scripts/scan-existing-mod-variants.js --dry-run
node scripts/scan-existing-mod-variants.js --limit=20
node scripts/scan-existing-mod-variants.js --status=pending
```

Zasady:

- skanować tylko warianty z `sha256`,
- najpierw lookup po SHA256,
- upload pliku tylko jeśli raportu nie ma i plik jest dostępny lokalnie/CDN,
- jeśli pliku nie da się pobrać albo VT zwróci limit/błąd, zapisać stan best-effort i iść dalej,
- rate limit między requestami,
- możliwość wznawiania po statusie i limicie,
- bezpieczne logi bez tokenów,
- po backfillu wypisać podsumowanie: ile `clean`, `suspicious`, `malicious`, `unknown/error`, ile pominięto.

## 7. Mandatory reviewers

### Security review — required

Powód:

- praca z `VIRUSTOTAL_API_KEY`,
- wysyłanie plików do zewnętrznego API,
- publiczny raport bezpieczeństwa artefaktów,
- potencjalne blokowanie downloadu,
- dodatkowa weryfikacja false positive przez modele AI i ryzyko błędnej klasyfikacji.

### Senior review — required

Powód:

- migracja DB,
- zmiana flow publikowania wariantów,
- wpływ na API v2 download,
- deployment produkcyjny `susmodder-api`.

### Quality review — required

Powód:

- średnia/duża zmiana backendowa,
- nowy endpoint,
- async workflow,
- Swagger i testy.

Rekomendowana kolejność:

1. Quality review,
2. Security review,
3. Senior review przed deployem produkcyjnym.
