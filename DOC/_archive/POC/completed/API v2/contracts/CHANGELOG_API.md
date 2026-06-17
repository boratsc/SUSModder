# SUSModder Changelog API – Full Reference

> **Status:** ✅ Wdrożone (2026-06-11)  
> **Właściciel ingestu:** `github-monitor` (cron + CLI backfill)  
> **Właściciel API:** `susmodder-api` (Express 5, read-only z DB)  
> **Tłumaczenie:** `ai-provider` (OpenAI, EN → PL)

---

## 1. Architektura

```
GitHub API ──→ github-monitor (cron co 6h) ──→ mod_changelogs (PostgreSQL)
                       │                              │
                       └── ai-provider:3010 (tłumaczenie EN→PL)
                                                      │
                    susmodder-api ←───────────────────┘
                         │
            ┌────────────┼────────────┐
            ▼            ▼            ▼
    GET /catalog/:id   GET /admin/   GET /admin/mods/:id
    /changelog          /changelogs  /changelogs
    (PUBLIC)            (AUTH)       (AUTH)
```

**Kluczowa zasada:** Klient API nigdy nie czeka na GitHub ani AI. Wszystkie changelogi są pre-fetched do bazy. Tłumaczenie dzieje się w tle, a API zwraca fallback EN gdy PL niegotowy.

---

## 2. Publiczny endpoint

### `GET /api/v2/catalog/:id/changelog`

```
Metoda:   GET
Auth:     Brak (publiczny)
Cache:    ETag + Cache-Control: public, max-age=120, stale-while-revalidate=300
          In-memory TTL: 120s (klucz: v2:catalog:changelog:{modId}:{lang}:{limit})
```

**Dostępność zewnętrzna:**

| Domena | Routing | Uwagi |
|--------|---------|-------|
| `susmodder.app` | `location /api/v2/` → proxy → `susmodder-api:3001` | ETag/304. `client_max_body_size 11m`. |
| `api.susmodder-cdn.ovh` | `location /api/` → proxy → `susmodder-api:3001` | Poprzez Cloudflare. Admin bypass cache. |

**Parametry zapytania (Joi walidacja):**

| Param | Typ | Domyślnie | Ograniczenia |
|-------|-----|-----------|-------------|
| `lang` | `enum('pl','en')` | `pl` | Joi |
| `limit` | `integer` | `5` | `min(1), max(20)` |

**Nagłówki odpowiedzi:**

| Nagłówek | Wartość |
|----------|---------|
| `ETag` | `"catalog-changelog-{modId}-{lang}-{limit}-{revision}"` |
| `Cache-Control` | `public, max-age=120, stale-while-revalidate=300` |
| `X-Cache` | `HIT` lub `MISS` (in-memory cache) |

### Response `200 OK`

```json
{
  "data": [
    {
      "id": 42,
      "modId": 1,
      "version": "v2.1.0",
      "releaseName": "Version 2.1.0 – Lobby fixes",
      "body": "Naprawiono rozłączanie w lobby…",
      "language": "pl",
      "requestedLanguage": "pl",
      "fallbackLanguage": null,
      "translationStatus": "translated",
      "translationProvider": "openai",
      "translationModel": "gpt-5.4-mini",
      "releaseUrl": "https://github.com/owner/repo/releases/tag/v2.1.0",
      "githubRepo": "owner/repo",
      "source": "github",
      "publishedAt": "2026-06-01T12:00:00.000Z",
      "fetchedAt": "2026-06-01T12:05:00.000Z",
      "translatedAt": "2026-06-01T12:05:02.000Z",
      "updatedAt": "2026-06-01T12:05:02.000Z"
    }
  ],
  "meta": {
    "modId": 1,
    "modName": "Super Mod",
    "lang": "pl",
    "limit": 5,
    "total": 3,
    "revision": "2026-06-01T12:05:02.000Z",
    "fallbackCount": 1
  }
}
```

### Logika wyboru języka (`selectPublicBody`)

| `lang` | `body_pl` istnieje? | `translation_status` | Zwracane `body` | `language` | `fallbackLanguage` |
|--------|---------------------|-----------------------|-----------------|------------|---------------------|
| `en` | – | – | `body_en` | `en` | `null` |
| `pl` | ✅ + `translated`/`manual` | – | `body_pl` | `pl` | `null` |
| `pl` | ❌ lub inny status | – | `body_en` | `en` | `en` |

### Kody błędów

| Status | Warunek | Body |
|--------|---------|------|
| `304` | `If-None-Match` pasuje do ETag | (puste) |
| `400` | Nieprawidłowy `id` lub parametry | `{ "error": { "code": "VALIDATION_ERROR", "message": "…" } }` |
| `404` | Mod nie istnieje w `config` | `{ "error": { "code": "NOT_FOUND", "message": "Mod with id 999 not found" } }` |
| `500` | Błąd DB / wyjątek | `{ "error": { "code": "INTERNAL_ERROR", "message": "An unexpected error occurred" } }` |

---

## 3. Admin endpointy

**Wszystkie endpointy admin wymagają podwójnego auth:**
1. `Authorization: Bearer <HTTP_TOKEN>` (middleware `requireAuthToken`)
2. `X-Admin-API-Secret: <ADMIN_API_SECRET>` (middleware `requireAdminApiSecret`)

**Nginx IP allowlist** (tylko na `susmodder.app`, NIE na Cloudflare):
```
allow 10.0.0.0/8;  allow 172.16.0.0/12;  allow 127.0.0.1;  deny all;
```

### `GET /api/v2/admin/changelogs`

Operacyjna lista changelogów z możliwością filtrowania.

| Param | Typ | Domyślnie | Ograniczenia |
|-------|-----|-----------|-------------|
| `modId` | `integer` | – | Opcjonalny filtr |
| `status` | `enum('pending','translated','failed','skipped_empty','skipped_non_english','manual')` | – | Opcjonalny filtr |
| `limit` | `integer` | `50` | `min(1), max(100)` |
| `offset` | `integer` | `0` | `min(0)` |

```json
{
  "data": [
    {
      "id": 42,
      "modId": 1,
      "modName": "Super Mod",
      "version": "v2.1.0",
      "releaseName": "Version 2.1.0",
      "bodyEn": "Fixed lobby disconnect…",
      "bodyPl": "Naprawiono rozłączanie w lobby…",
      "publishedAt": "2026-06-01T12:00:00.000Z",
      "releaseUrl": "https://github.com/owner/repo/releases/tag/v2.1.0",
      "githubRepo": "owner/repo",
      "source": "github",
      "translationStatus": "translated",
      "translationProvider": "openai",
      "translationModel": "gpt-5.4-mini",
      "translationError": null,
      "fetchedAt": "2026-06-01T12:05:00.000Z",
      "translatedAt": "2026-06-01T12:05:02.000Z",
      "updatedAt": "2026-06-01T12:05:02.000Z"
    }
  ],
  "meta": { "total": 1, "limit": 50, "offset": 0 }
}
```

### `GET /api/v2/admin/mods/:id/changelogs`

Changelogi dla pojedynczego moda (z pełnym `bodyEn`/`bodyPl`).

| Param | Typ | Domyślnie | Ograniczenia |
|-------|-----|-----------|-------------|
| `limit` | `integer` | `20` | `min(1), max(100)` |

```json
{
  "data": [ { "…": "…" } ],
  "meta": { "total": 3, "modId": 1, "modName": "Super Mod", "limit": 20 }
}
```

`404` gdy mod nie istnieje.

---

## 4. Model danych: `mod_changelogs`

```sql
CREATE TABLE mod_changelogs (
    id                  BIGSERIAL PRIMARY KEY,
    mod_id              INTEGER NOT NULL REFERENCES config(id) ON DELETE CASCADE,
    version             VARCHAR(100) NOT NULL,       -- GitHub release tag_name
    release_name        TEXT,                         -- GitHub release display name
    body_en             TEXT NOT NULL DEFAULT '',     -- Oryginalny changelog EN
    body_pl             TEXT,                         -- Przetłumaczony changelog PL (NULL = brak)
    published_at        TIMESTAMPTZ NOT NULL,        -- Data publikacji release na GitHub
    release_url         TEXT NOT NULL,               -- URL do release na GitHub
    github_repo         TEXT,                         -- Sparsowane owner/repo
    source              VARCHAR(50) NOT NULL DEFAULT 'github',
    translation_provider VARCHAR(50),                -- 'openai' lub null
    translation_model   VARCHAR(200),                -- 'gpt-5.4-mini' lub null
    translation_status  VARCHAR(30) NOT NULL DEFAULT 'pending',
    translation_error   TEXT,                         -- Ostatni błąd (<2000 znaków, bez sekretów)
    fetched_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    translated_at       TIMESTAMPTZ,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_mod_changelogs_mod_version UNIQUE (mod_id, version),
    CONSTRAINT chk_mod_changelogs_translation_status CHECK (
        translation_status IN ('pending','translated','failed','skipped_empty','skipped_non_english','manual')
    )
);

CREATE INDEX idx_mod_changelogs_mod_published
    ON mod_changelogs (mod_id, published_at DESC);
CREATE INDEX idx_mod_changelogs_translation_status
    ON mod_changelogs (translation_status);
CREATE INDEX idx_mod_changelogs_release_url
    ON mod_changelogs (release_url);
CREATE INDEX idx_mod_changelogs_updated_at
    ON mod_changelogs (updated_at DESC);
```

**Statusy tłumaczenia:**

| Status | Znaczenie | Ustawiany przez |
|--------|-----------|----------------|
| `pending` | EN zapisany, oczekuje tłumaczenia | `upsertChangelog()` |
| `translated` | PL gotowy | `markChangelogTranslated()` |
| `failed` | Tłumaczenie nie powiodło się | `markChangelogFailed()` |
| `skipped_empty` | Pusty changelog | `upsertChangelog()` |
| `skipped_non_english` | Zarezerwowany (nieużywany) | – |
| `manual` | Zarezerwowany (ręczna korekta) | – |

**Deduplikacja:** `UNIQUE(mod_id, version)` — drugi insert z tą samą parą robi UPDATE.

**Ochrona istniejącego PL:** Jeśli `body_pl` jest już niepuste, update NIE resetuje `translation_status` ani `translation_error`.

---

## 5. Flow ingestu (github-monitor)

```
Cron (co 6h) → checkAllUpdates()
  ├── getMonitoredMods()                        # config WHERE githubrepoorlink IS NOT NULL
  ├── Dla każdego moda:
  │     ├── getLatestRelease(github_repo)        # GitHub API: /repos/{owner}/{repo}/releases/latest
  │     ├── ingestReleaseChangelog(mod, release)
  │     │     ├── upsertChangelog()              # INSERT … ON CONFLICT DO UPDATE
  │     │     │   Status: 'pending' / 'skipped_empty'
  │     │     │   NIE nadpisuje istniejącego body_pl
  │     │     ├── Jeśli już ma PL → skip
  │     │     ├── Jeśli translation disabled → log
  │     │     └── Jeśli translation enabled:
  │     │           ├── POST ai-provider:3010/v1/translate
  │     │           ├── 200 → markChangelogTranslated()
  │     │           └── err → markChangelogFailed()
  │     └── (reszta: wersje, pending updates, auto-apply, webhooks)
  └── sleep(CHECK_DELAY_MS)
```

- Błąd tłumaczenia **nie blokuje** wykrywania release'ów.
- `ingestReleaseChangelog` wołane ZAWSZE, niezależnie od zmiany wersji.
- Backfill to osobny CLI, nie część crona.

---

## 6. Backfill CLI

```bash
cd github-monitor
npm run backfill:changelogs -- [opcje]
```

| Opcja | Domyślnie | Opis |
|-------|-----------|------|
| `--mod-id=N` | wszystkie | Ogranicz do jednego moda |
| `--limit=N` | `CHANGELOG_BACKFILL_MAX_RELEASES_PER_MOD` (20) | Release'ów per mod (max 100) |
| `--translate=false` | `true` | Wyłącza tłumaczenie |
| `--dry-run` | `false` | Tylko loguje |

**Flow dla pojedynczego moda:**

```
resolveRepoCandidate(mod)
  ├── Priorytet: 1) githubprojecturl  2) githubrepoorlink  3) epicgithubrepoorlink
  ├── Każdy URL → parseRepo() → owner/repo
  └── Jeśli nic nie parsowalne → skip

getReleases("owner/repo", limit)
  └── GET /repos/{owner}/{repo}/releases?per_page={limit}&page=1

Dla każdego release:
  ├── upsertChangelog() → INSERT ON CONFLICT
  └── translateIfNeeded() → tylko jeśli brak PL i translate=true
```

---

## 7. Kontrakt z ai-provider

`github-monitor/src/aiProvider.js` → `POST http://ai-provider:3010/v1/translate`

**Request:**
```json
{
  "text": "Fixed lobby disconnect when host leaves…",
  "sourceLang": "en",
  "targetLang": "pl",
  "context": "changelog",
  "provider": "openai",
  "model": "gpt-5.4-mini"
}
```

- `model` — opcjonalny (domyślny model providera)
- `context: "changelog"` — aktywuje prompt changelogowy w `TranslationService`

**Response `200 OK`:**
```json
{
  "data": {
    "translatedText": "Naprawiono rozłączanie w lobby gdy host wychodzi…",
    "provider": "openai",
    "model": "gpt-5.4-mini"
  }
}
```

**Timeout:** `CHANGELOG_TRANSLATION_TIMEOUT_MS` (domyślnie 30000ms)  
**Token:** `AI_PROVIDER_TOKEN` (fallback → `HTTP_TOKEN`)  
**SSRF:** `redirect: 'error'` (nie followuje redirectów)

---

## 8. Zmienne środowiskowe

Wszystkie zmienne w `github-monitor/.env`:

| Zmienna | Wymagane | Domyślnie | Opis |
|---------|----------|-----------|------|
| `DATABASE_URL` | Tak | – | PostgreSQL connection string |
| `GITHUB_TOKEN` | Nie | – | Zwiększa rate limit |
| `AI_PROVIDER_BASE_URL` | Nie | `http://ai-provider:3010` | URL wewnętrznego AI providera |
| `AI_PROVIDER_TOKEN` | Nie | `HTTP_TOKEN` | Bearer token do ai-provider |
| `CHANGELOG_TRANSLATION_ENABLED` | Nie | `false` | Włącza tłumaczenie EN→PL |
| `CHANGELOG_TRANSLATION_PROVIDER` | Nie | `openai` | Provider do `/v1/translate` |
| `CHANGELOG_TRANSLATION_MODEL` | Nie | provider default | Wymuszony model |
| `CHANGELOG_TRANSLATION_TIMEOUT_MS` | Nie | `30000` | Timeout requestu AI |
| `CHANGELOG_BACKFILL_MAX_RELEASES_PER_MOD` | Nie | `20` | Limit backfill CLI |
| `CHANGELOG_FETCH_DELAY_MS` | Nie | `1500` | Odstęp między modami |

---

## 9. Exposure matrix

| Endpoint | Auth | Domain (`susmodder.app`) | Domain (`api.susmodder-cdn.ovh`) | Cache |
|----------|------|--------------------------|----------------------------------|-------|
| `GET /api/v2/catalog/:id/changelog` | ❌ Brak | Publiczny, ETag/304 | Publiczny przez Cloudflare | ETag + 120s mem |
| `GET /api/v2/admin/changelogs` | ✅ Bearer + Secret | IP-locked (10.x, 172.x, 127.x) | Tylko auth middleware | Brak |
| `GET /api/v2/admin/mods/:id/changelogs` | ✅ Bearer + Secret | IP-locked | Tylko auth middleware | Brak |

**Uwaga bezpieczeństwa:** Na `api.susmodder-cdn.ovh` (Cloudflare) admin endpointy NIE mają nginx IP allowlist — Cloudflare maskuje IP klienta. Ochrona opiera się **wyłącznie** na middleware `requireAuthToken` + `requireAdminApiSecret`.

---

## 10. Powiązana dokumentacja

- `DOC/services/SUSMODDER_API.md` — full API reference (sekcja 5.8 GitHub Updates)
- `DOC/services/GITHUB_MONITOR.md` — github-monitor service doc (sekcja 4: `mod_changelogs`, sekcja 11: backfill)
- `DOC/services/ai-provider/README.md` — ai-provider kontrakt `/v1/translate`
- `DOC/plans/2026-06-09-changelog-translation-deployment-plan.md` — plan wdrożenia
- `DOC/poc/susmodder-api/2026-06-05-changelog-translation-poc.md` — oryginalny POC
- `migrations/014_create_mod_changelogs.sql` — migracja DB
- `github-monitor/.env.example` — wzór konfiguracji
