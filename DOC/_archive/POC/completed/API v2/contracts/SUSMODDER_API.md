# SUSModder API – Full Reference

> **Version:** 1.0.0  
> **Runtime:** Node.js 22 LTS (CommonJS)  
> **Framework:** Express 5.1.0  
> **Database:** PostgreSQL (via mysql2-compatible shim)  
> **Swagger:** `/api-docs` (auto-generated from JSDoc)

---

## 1. Architecture Overview

```
susmodder-api/
├── server.js              # Entry point – Express app, route registration, Redis init
├── swagger.js             # OpenAPI 3.0 spec definition
├── config/
│   ├── database.js        # PostgreSQL pool wrapped with mysql2-like interface
│   ├── cache.js           # In-memory TTL Map cache
│   └── redis.js           # Redis client (optional, telemetry only)
├── middleware/
│   ├── auth.js            # Bearer token validation for admin/write routes
│   └── clairbot-auth.js   # ClairBot webhook secret validation
├── routes/
│   ├── admin.js           # Mod CRUD, entity CRUD, CDN download, icons list
│   ├── amongUsVersions.js # Among Us version validation
│   ├── compatibility.js   # DLL↔FULL mod compatibility matrix
│   ├── config.js          # Public config endpoints (/susmodder-config)
│   ├── download.js        # File download (legacy)
│   ├── githubUpdates.js   # GitHub pending updates + webhooks
│   ├── githubUpdatesHelpers.js # CDN download, webhook dispatch, inline check
│   ├── modDownload.js     # Mod binary download with platform detection
│   ├── onlineUsers.js     # Online user tracking (Redis-backed)
│   ├── releases.js        # Velopack update manifest serving
│   ├── rolesModifiers.js  # Role/modifier catalog
│   ├── serverConfig.js    # Among Us API config + ClairBot sync
│   ├── sustats.js         # Game data (POST /among-data, GET /among-games)
│   ├── telemetry.js       # Heartbeat collection + stats aggregation
│   ├── upload.js          # File upload (Multer)
│   └── versions.js        # Version management (legacy)
├── utils/
│   └── psychopatycznyApiClient.js # ClairBot game result forwarding
└── test/
    └── test-channel-normalization.js
```

---

## 2. Database Layer

### PostgreSQL Shim (`config/database.js`)

The codebase was originally MySQL. The shim wraps `pg.Pool` to expose a mysql2-compatible API:

| Method | Description |
|--------|-------------|
| `pool.execute(sql, params)` | Returns `[rows, meta]` like mysql2 |
| `pool.query(sql, params)` | Alias for `execute()` |
| `pool.getConnection()` | Returns `PooledConnection` with transaction support |

**Key transformations:**
- **Placeholder conversion:** `?` → `$1, $2, ...` (positional parameters)
- **Column name normalization:** Lowercase PG column names → PascalCase via `ALIAS_MAP`
- **Transaction support:** `beginTransaction()`, `commit()`, `rollback()` on connections

**Connection string resolution (priority):**
1. `process.env.DATABASE_URL`
2. Manual: `PG_HOST/PG_PORT/PG_USER/PG_PASSWORD/PG_DATABASE`
3. Fallback: `DB_HOST/DB_PORT/DB_USER/DB_PASSWORD/DB_NAME`

**Pool config:** max 10 connections, no idle exit

### In-Memory Cache (`config/cache.js`)

Simple TTL Map cache used in:
- `config.js` – mod catalog (60s TTL), version history (60s)
- `compatibility.js` – compatibility matrix (120s TTL)
- `rolesModifiers.js` – role catalog (120s TTL)

API: `cache.get(key)`, `cache.set(key, value, ttlSeconds)`, `cache.delete(key)`, `cache.clear()`

### Redis (`config/redis.js`)

Optional – used only for telemetry. Connects on startup in non-blocking `try/catch`. All telemetry endpoints degrade gracefully when Redis is unavailable.

**Uses Redis:**
- `/telemetry/heartbeat` – heartbeat storage, daily stats, rate limiting
- `/telemetry/stats` – aggregated statistics queries
- `/online-users` – real-time user tracking

---

## 3. Authentication

### Bearer Token Auth (`middleware/auth.js`)

**Used by:** All admin/write routes
**Token source:** `HTTP_TOKEN` env var (comma-separated list)
**Header:** `Authorization: Bearer <TOKEN>` or `Authorization: <TOKEN>`

```js
// Routes protected by requireAuthToken:
/admin/mods/*        (CRUD)
/admin/entities/*    (CRUD)
/admin/compatibility (PUT)
/admin/github-updates/*
/admin/webhooks/*
/admin/icons         (GET)
/admin/configs       (GET)
/upload              (POST)
/among-api-configs   (GET)
/among-api-add-discord-fav (POST)
/among-tokens        (GET)
/susmodder-discordfavs (GET)
/compatibility/matrix (GET)
```

### ClairBot Auth (`middleware/clairbot-auth.js`)

**Used by:** `/sync-server-config` (dual route in `sustats.js` and `serverConfig.js`)
**Token source:** `CLAIRBOT_SYNC_SECRET` env var (single token)
**Header:** `Authorization: Bearer <SECRET>`

---

## 4. Common Conventions

### Response Format

```json
// Success
{ "success": true, "data": [...], "count": 10 }

// Error
{ "error": "Database error while fetching mods", "success": false }

// Auth error
{ "error": "Unauthorized - Invalid token" }
```

### Error Handling Pattern

Every route follows:
```js
router.get('/endpoint', async (req, res) => {
  try {
    const [rows] = await pool.execute('SELECT ...', [param]);
    res.json({ success: true, data: rows });
  } catch (err) {
    console.error('Error:', err);
    res.status(500).json({ error: 'Database error' });
  }
});
```

### CORS

All origins allowed (`Access-Control-Allow-Origin: *`). Methods: GET, POST, PUT, DELETE, OPTIONS.

### Rate Limiting

| Endpoint | Limit | Window |
|----------|-------|--------|
| `/among-draft-data` | 3 requests | 1 minute |
| `/sync-server-config` | 60 requests | 1 minute |
| Susadmin login | 10 attempts | 15 minutes |

---

## 5. Complete API Reference

### 5.1 Admin Routes (`routes/admin.js`)

All admin routes require `Authorization: Bearer <HTTP_TOKEN>`.

#### `GET /admin/mods`
List all mods (Id, ModName, ModVersion, ModType).

#### `GET /admin/mods/:id`
Get single mod details.

#### `POST /admin/mods`
Create new mod. Required: `ModName`, `ModType`. Validates `AmongVersion` against allowed versions. Auto-assigns `Id = MAX(Id) + 1`. Creates initial `config_versions` entry. Uses transaction.

#### `PUT /admin/mods/:id`
Update mod. Detects version changes and creates `config_versions` history entries. Uses transaction.

#### `DELETE /admin/mods/:id`
Delete mod + associated `config_versions` entries. Uses transaction.

#### `POST /admin/mods/:id/download-to-cdn`
Download mod files from GitHub to CDN. Supports Steam and Epic platforms. Downloads only if file doesn't exist. Returns per-file download status.

#### `GET /admin/icons`
List distinct icon filenames from config table.

#### `GET /admin/configs`
List full mods (ModType = 'full') for entity association.

#### `GET /admin/entities`
List all entities with associated mods and abilities (JOIN across `entity`, `entity_config`, `entity_ability`, `ability`).

#### `GET /admin/entities/:id`
Get single entity with mods and abilities.

#### `POST /admin/entities`
Create entity with optional mods and abilities. Validates Type ∈ {Role, Modifier}. Uses `RETURNING Id` (PostgreSQL). Creates/updates abilities as needed. Uses transaction.

#### `PUT /admin/entities/:id`
Update entity – deletes and recreates all associations (entity_config, entity_ability). Uses transaction.

#### `DELETE /admin/entities/:id`
Delete entity. Cascading deletes handled by FK constraints.

#### `PUT /admin/compatibility`
Set compatibility status between FullMod and DllMod. Status values: `F` (Full compatible), `W` (Works with issues), `NT` (Not tested), `NW` (Not working). Uses `ON CONFLICT ... DO UPDATE`.

---

### 5.2 Config Routes (`routes/config.js`)

#### `GET /susmodder-config` (PUBLIC)
Get full mod catalog. Transforms GitHub URLs to dynamic `mod-download` API URLs. Cached 60 seconds.

**Response:** Array of mod objects with modDownload URLs:
```json
[{
  "Id": 1,
  "ModName": "Town of Us",
  "ModVersion": "5.4.0",
  "ModType": "full",
  "GitHubRepoOrLink": "https://susmodder.app/api/mod-download/1/5.4.0?platform=steam",
  "EpicGitHubRepoOrLink": "https://susmodder.app/api/mod-download/1/5.4.0?platform=epic"
}]
```

#### `GET /susmodder-config-versions` (PUBLIC)
Get version history. Optional `?modId=` filter. Dynamically builds mod-download URLs. Cached 60 seconds.

#### `GET /susmodder-discordfavs` (AUTH REQUIRED)
Get Discord favorites list (with descriptions, active status). Cached 60 seconds.

#### `GET /public/discord-favs` (PUBLIC)
Public Discord favorites (limited fields – no is_active). Cached 5 minutes.

#### `GET /public/discord-server-counts` (PUBLIC)
Fetch live Discord server member counts via Discord API invites. Cached 30 minutes.

---

### 5.3 Compatibility Routes (`routes/compatibility.js`)

#### `GET /compatibility` (PUBLIC)
Query compatibility between DLL and FULL mods.

**Parameters:**
- `dllModId` / `fullModId` – one required
- `dllModVersion` / `fullModVersion` – optional (defaults to current)
- `status` – filter: `F,W,NT,NW` (comma-separated)
- `includeUntested` – boolean (default true)

**Response includes:**
- `query.modId`, `query.modName`, `query.modVersion`
- `compatibilities[]` with status, isCurrentVersion flag
- Warning if tested version differs from current version

#### `GET /compatibility/matrix` (AUTH REQUIRED)
Full matrix view. Joins all FULL×DLL combinations. Uses `vw_compatibility_matrix_full` view.

---

### 5.4 Releases Route (`routes/releases.js`)

#### `GET /releases` (PUBLIC)
Serve Velopack update manifest for SUSModder auto-update.

**Parameters:**
- `channel` – `release` (default) or `beta`. Legacy aliases: `win` → `release`, `win-beta` → `beta`
- `arch` – `x64` (default)

**Manifest resolution chain:**
1. `VELOPACK_RELEASES_DIR` env var
2. `versions/` directory
3. `nginx/html/susmodder-velopack/`
4. `nginx/html/susmodder-versions/`

**Features:**
- ETag-based caching (304 Not Modified)
- Cache-Control: `public, max-age=300, stale-while-revalidate=120`
- Transforms Velopack native `Assets` format to custom `Releases` format
- Supports both `releases.{channel}.json` and legacy formats

---

### 5.5 Telemetry Routes (`routes/telemetry.js`)

#### `POST /telemetry/heartbeat` (PUBLIC)
Collect anonymous usage telemetry. Validated with Joi.

**Request body:**
```json
{
  "userHash": "a1b2c3... (64 hex chars SHA256)",
  "appVersion": "2.0.0",
  "platform": "steam",
  "language": "pl",
  "installedModIds": [1, 3, 7],
  "sessionTimeSeconds": 1234,
  "timestamp": "2025-10-27T12:34:56.789Z"
}
```

**Validation rules:**
- `userHash`: 64-char hex string (SHA256)
- `appVersion`: semver format
- `platform`: `steam` or `epic`
- `language`: 2-char ISO code
- `installedModIds`: max 100 items
- `sessionTimeSeconds`: 0–86400 (24h)
- `timestamp`: ISO 8601 UTC

**Redis operations:**
- Rate limit: 10 minutes per userHash
- Heartbeat storage: 90-day TTL
- Daily unique users: Sorted set, 1-year TTL
- Daily stats: Hash (platform, version, language, session counters)
- Mod popularity: Sorted set, 90-day TTL

#### `GET /telemetry/stats` (PUBLIC)
Aggregated statistics.

**Parameters:** `from`, `to` (YYYY-MM-DD, max 365 days)

**Response:**
- `onlineUsers` – real-time count (last 10 min)
- `range` – aggregated: uniqueUsers, sessionCount, totalSessionTime, platforms, versions, languages, topMods (top 20)
- `last7Days`, `last30Days` – precomputed card metrics
- `timeline` – per-day breakdown

#### `GET /telemetry/health` (PUBLIC)
Check Redis connectivity for telemetry.

---

### 5.6 Sustats Routes (`routes/sustats.js` + `routes/serverConfig.js`)

> **Note:** `/sync-server-config` is defined identically in both `sustats.js` and `serverConfig.js`. The `serverConfig.js` version (loaded second) takes precedence due to Express middleware order.

#### `POST /among-data` (PUBLIC, token+secret auth)
Save Among Us game results.

**Request body:**
```json
{
  "token": "<guild_id>",
  "secret": "<server_secret>",
  "gameInfo": {
    "gameId": "...",
    "lobbyCode": "...",
    "gameMode": "...",
    "duration": 600,
    "map": "Skeld",
    "timestamp": "2025-01-01T00:00:00Z"
  },
  "players": [{ "playerId": "...", "playerName": "...", "role": "Impostor", ... }],
  "gameResult": { "winningTeam": "Impostors", "specialWinners": [...] }
}
```

**Processing:**
1. Validates token+secret against `among_tokens`
2. Checks for duplicate gameId (409) and lobby code spam (429, 15s window)
3. Translates roles from Polish to English via `roles_dict`
4. Validates and auto-corrects role vs roles array consistency
5. Inserts into `among_games`, `among_players`, `among_player_modifiers`, `among_special_winners` (transaction)
6. **Forwards** game result to Psychopatyczny-API (fire-and-forget, non-blocking)

#### `GET /among-games` (PUBLIC, token+secret auth)
Query game data and player statistics.

**Parameters:**
- `token`, `secret` – required for auth
- `type` – `games` (default), `player_stats`, `player_stats_monthly`, `player_stats_weekly`
- `dzien`, `gracz`, `game`, `code` – filters

#### `POST /sync-server-config` (ClairBot auth required)
Synchronize Discord server configuration from ClairBot.

**Request body:**
```json
{
  "guild_id": "1372226857294106644",
  "server_name": "Serwer DEV",
  "token": "<token>",
  "secret": "<secret>",
  "endpoint": "https://clairbot.app/api/among-data",
  "is_active": true,
  "notification_channel_id": "1376333200753823774"
}
```

**Behavior:**
- `is_active: true` → UPSERT into `among_tokens` and `among_server_config`
- `is_active: false` → DELETE from both tables
- Uses transaction

#### `POST /among-draft-data` (PUBLIC, rate limited: 3/min)
Add role draft data. Translates role PL→EN before storage.

#### `GET /among-draft-occurances` (PUBLIC)
Get role occurrence statistics. Optional `?max_roles=` limit.

#### `GET /among-tokens` (AUTH REQUIRED)
List Among Us API tokens with server names.

#### `GET /among-api-configs` (AUTH REQUIRED)
List Among Us API configurations (token, secret, endpoint, server_name).

#### `POST /among-api-add-discord-fav` (AUTH REQUIRED)
Add or update Discord favorite server.

---

### 5.7 Roles & Modifiers Route (`routes/rolesModifiers.js`)

#### `GET /roles-modifiers` (PUBLIC)
Get role and modifier catalog.

**Parameters:** `Id` (ConfigId), `Name` (EntityName) – optional filters.

**Returns:** Entities with their types, categories, descriptions, abilities, and associated mod names. Sorted: Roles first, then alphabetically. Cached 120 seconds.

---

### 5.8 GitHub Updates Routes (`routes/githubUpdates.js`)

#### `GET /github-updates/pending` (PUBLIC)
Public list of pending GitHub updates (limited fields: detected_version, mod_name, mod_type).

#### `GET /admin/github-updates` (AUTH REQUIRED)
Admin list with all fields. Filter by `?status=pending|dismissed|updated|all`. v2 admin responses include a `changelog` summary with `translationStatus`, EN/PL availability, provider/model, and last translation error for susadmin badges/previews.

#### `GET /admin/github-updates/:id` (AUTH REQUIRED)
Get single pending update details.

#### `PUT /admin/github-updates/:id/dismiss` (AUTH REQUIRED)
Dismiss a pending update.

#### `PUT /admin/github-updates/:id/apply` (AUTH REQUIRED)
Apply an update:
1. Updates `config.modversion`
2. Creates `config_versions` history entry
3. Marks update as resolved
4. Triggers CDN download (for full mods, non-blocking)
5. Dispatches `mod_updated` webhooks (non-blocking)

#### `POST /admin/github-updates/check-now` (AUTH REQUIRED)
Trigger immediate update check.

#### `GET /api/v2/catalog/:id/changelog` (PUBLIC)
Read pre-fetched release changelogs for a mod. Query params:
- `lang=pl|en` (default `pl`)
- `limit=1..20` (default `5`)

For `lang=pl`, the endpoint returns the Polish translation when ready and falls back to the original English body while exposing `translationStatus` and `fallbackLanguage`. The endpoint is cacheable (`ETag`, `Cache-Control`) and never calls GitHub or AI during the request.

📘 **Full changelog contracts:** `DOC/services/CHANGELOG_API.md` — includes response schemas, error codes, language selection logic, data model, ingestion flow, backfill CLI, and exposure matrix.

#### `GET /api/v2/admin/changelogs` (AUTH REQUIRED)
Operational list of stored changelogs. Query params: `modId`, `status`, `limit`, `offset`. Full details in `CHANGELOG_API.md`.

#### `GET /api/v2/admin/mods/:id/changelogs` (AUTH REQUIRED)
Admin list of changelogs for one mod, including full EN/PL bodies and translation metadata. Full details in `CHANGELOG_API.md`.

#### Webhooks CRUD:
- `GET /admin/webhooks` – List all webhook configs
- `POST /admin/webhooks` – Create webhook (events: `release_detected`, `mod_updated`)
- `PUT /admin/webhooks/:id` – Update webhook
- `DELETE /admin/webhooks/:id` – Delete webhook
- `POST /admin/webhooks/:id/test` – Send test notification (Discord embed)

---

### 5.9 Mod Download Route (`routes/modDownload.js`)

#### `GET /api/mod-download/:modId/:version` (PUBLIC)
Download mod binaries with platform-aware file selection.

**Parameters:** `?platform=steam|epic`

**Platform detection logic:**
1. Explicit platform markers in filenames (steam/itchie/epic)
2. Architecture fallback: x32/x86 → Steam, x64 → Epic
3. Default to first matching file if no platform markers

**CDN paths:**
- Primary: `/usr/src/app/susmodder-cdn/{modId}/{version}/`
- Fallback: `https://susmodder-cdn.ovh/{modId}/{version}/`

---

### 5.10 Upload Route (`routes/upload.js`)

#### `POST /upload` (AUTH REQUIRED)
Upload files using Multer. Files saved to `/usr/src/app/configs/`. Volume-mounted from `nginx/html/susmodder-backend/usr-configs/`.

---

### 5.11 Online Users Route (`routes/onlineUsers.js`)

#### Online user tracking
Tracks users via IP in Redis sorted set (`susmodder:online`). Triggered on `/susmodder-config` access (fire-and-forget). Uses 10-minute sliding window.

---

### 5.12 Health Check

#### `GET /health` (PUBLIC)
```json
{
  "status": "ok",
  "timestamp": "2026-05-24T12:00:00.000Z",
  "service": "susmodder-api"
}
```

---

### 5.13 AI Support Assistant (`routes/v2/support.js`) — NEW v2

> **Full contract:** [`AI_SUPPORT_API.md`](AI_SUPPORT_API.md)  
> **Base:** `/api/v2/support`

Knowledge-base-first support assistant. Returns structured repair steps from a curated, PostgreSQL-backed KB with optional OpenRouter free-model fallback (disabled by default).

#### `GET /api/v2/support/knowledge/meta` (PUBLIC)
Returns KB version, available locales, categories, and article count. Cacheable.

#### `POST /api/v2/support/query` (PUBLIC, rate limited: 10/min/IP)
Main endpoint. Accepts user problem description + diagnostic context, returns scored KB matches with `actionCode`-mapped repair steps. Response includes `supportSessionId` for feedback tracking.
- **Headers:** `X-User-Hash` (optional, soft identity)
- **Source determination:** `knowledge_base` (KB match), `fallback` (no match), `llm` (only when `AI_SUPPORT_LLM_ENABLED=true`)
- **Confidence:** `high` (score ≥15), `medium` (score ≥5), `low`

#### `POST /api/v2/support/feedback` (PUBLIC, rate limited: 30/min/IP)
Record `helped`/`not_helped`/`report_generated`/`discord_clicked`. Max 5 entries per session. Comment is PII-redacted.

#### `POST /api/v2/support/report-metadata` (PUBLIC)
Track diagnostic report generation (no file upload). `X-User-Hash` for daily limits.

#### `GET /api/v2/support/health` (PUBLIC)
KB status, article count, LLM enabled flag, ai-provider reachability. No secrets.

**Search scoring weights:**
| Match type | Points |
|------------|--------|
| Exact diagnosis code | 10 |
| Partial diagnosis code | 5 |
| Tag match | 3 |
| Symptom keyword | 2 |
| Title/search_text | 1 |
| Category hint bonus | 2 |
| Platform mode bonus | 1 |
| Mod type bonus | 1 |

**Action codes (allowlist):**
`none`, `open_logs`, `open_mod_folder`, `open_firewall_repair`, `open_defender_instructions`, `generate_report`, `open_discord`

**KB tables (PostgreSQL):**
- `support_kb_articles` — article metadata (slug, category, diagnosis_codes, tags)
- `support_kb_article_translations` — PL/EN localized content (title, symptoms, solution_steps)
- `support_kb_article_revisions` — version history
- `support_sessions` — request metadata (no raw problem text)
- `support_feedback` — user feedback
- `support_report_metadata` — report generation tracking

**Environment variables:**
| Variable | Default | Description |
|----------|---------|-------------|
| `AI_PROVIDER_URL` | `http://ai-provider:3010` | ai-provider microservice URL |
| `AI_PROVIDER_ENABLED` | `true` | Enable ai-provider dependency |
| `AI_SUPPORT_LLM_ENABLED` | `false` | Enable LLM fallback (start disabled!) |
| `AI_SUPPORT_LLM_PROVIDER` | `openrouter` | Provider ID for ai-provider |
| `AI_SUPPORT_LLM_TIMEOUT_MS` | `12000` | LLM call timeout |
| `AI_SUPPORT_KB_CACHE_TTL_SECONDS` | `300` | KB in-memory cache TTL |
| `AI_SUPPORT_TRANSLATE_FALLBACK` | `false` | Live EN translation fallback |

---

### 5.14 VirusTotal for Mod Variants (`routes/v2/downloads.js` + `services/virustotalService.js`) — NEW

> **Base:** `/api/v2/downloads`

Best-effort VirusTotal scanning for mod variant files. Every `mod_variants` row can carry a VT report. Scanning runs asynchronously and never blocks mod operations (variant creation, download, etc.).

#### Architecture

```
┌──────────────────┐     ┌─────────────────┐     ┌──────────────┐
│  Admin/GitHub    │────▶│ virustotalService│────▶│ VirusTotal   │
│  variant write   │     │ (async, best-    │     │ API v3       │
│  (POST/PUT/cdn)  │     │  effort)         │     │              │
└──────────────────┘     └────────┬────────┘     └──────────────┘
                                  │
                                  ▼
                         ┌─────────────────┐
                         │  mod_variants   │
                         │  scan_status    │
                         │  vt_permalink   │◀── AI review
                         │  vt_stats (JSON)│    (gpt-5.4-mini
                         │  vt_ai_review_* │     via ai-provider)
                         └─────────────────┘
```

#### Data stored per variant

| Column | Type | Description |
|--------|------|-------------|
| `scan_status` | text | `pending` → `scanning` → `clean` / `suspicious` / `malicious` / `error` / `unknown` |
| `vt_permalink` | text | Link to full VirusTotal report |
| `vt_last_analysis_date` | timestamptz | When VT last analyzed the file |
| `vt_last_checked_at` | timestamptz | When our API last checked VT |
| `vt_stats` | jsonb | Raw `last_analysis_stats` (malicious, suspicious, undetected, etc.) |
| `vt_ai_review_status` | text | AI false-positive verdict (see below) |
| `vt_ai_review_summary` | text | Human-readable AI verdict summary |

#### `GET /api/v2/downloads/mod/:id/:version/virustotal` — VirusTotal report (PUBLIC)

Returns the VT scan report for a specific mod variant. Report is best-effort — may show `scanStatus: "unknown"` if the variant hasn't been scanned yet.

**Query params:** `platform` (steam|epic|msstore|itchio, default `steam`), `arch` (x64|x86, default `x64`)

**Response `200`:**
```json
{
  "data": {
    "modId": 12,
    "modVersion": "7.0.0",
    "platform": "steam",
    "architecture": "x64",
    "sha256": "f96de017ba781bdd32a40fad1cd7123...",
    "scanStatus": "clean",
    "vtPermalink": "https://www.virustotal.com/gui/file/...",
    "vtLastAnalysisDate": "2026-03-12T17:46:50.000Z",
    "vtLastCheckedAt": "2026-06-13T13:26:23.373Z",
    "lastAnalysisStats": {
      "malicious": 0,
      "suspicious": 0,
      "undetected": 60,
      "harmless": 0,
      "timeout": 0
    },
    "aiReviewStatus": "ai_review_not_needed",
    "aiReviewSummary": null
  }
}
```

**Response `404`:**
```json
{
  "error": {
    "code": "VARIANT_NOT_FOUND",
    "message": "No variant found for mod ..."
  }
}
```

Triggers a background VT refresh if `scanStatus` is `pending` or `unknown`.

#### `GET /api/v2/downloads/mod/:id/:version` — Download with VT headers (PUBLIC)

The existing download endpoint now includes optional VT response headers when data is available:

```http
X-SUSModder-Scan-Status: clean
X-SUSModder-VT-Permalink: https://www.virustotal.com/gui/file/...
X-SUSModder-VT-Last-Analysis: 2026-03-12T17:46:50.000Z
```

Headers are absent or skipped when no VT data exists. Download behavior (302 redirect) is never blocked by VT unless `VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS=true`.

#### AI False-Positive Review

When a variant is flagged as `suspicious` or `malicious`, the service triggers an automatic AI review using 2–3 models (currently `gpt-5.4-mini` × 2 + `gpt-4.1-nano`, called through the internal ai-provider). Models receive only VT metadata (SHA256, detection stats, engine names, mod context) — **no binary data or private URLs are sent**.

Voting logic:
- ≥2 models vote `false_positive_likely` → `ai_review_false_positive_likely`
- ≥2 models vote `risk_confirmed` → `ai_review_risk_confirmed`
- Otherwise → `ai_review_inconclusive`

AI review is fire-and-forget: triggered after VT scan completes but never blocks the scan result.

#### Backfill script

```bash
# Scan existing variants (best-effort, respects VT rate limits)
node scripts/scan-existing-mod-variants.js --dry-run
node scripts/scan-existing-mod-variants.js --limit=20
node scripts/scan-existing-mod-variants.js --status=unknown
```

Strategies tried in order:
1. VT SHA256 lookup (instant if VT has seen the file before)
2. File upload (for files ≤32MB available on CDN)
3. URL-based scan (VT downloads the file itself — no size limit)

#### Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VIRUSTOTAL_API_KEY` | – | VT API v3 key (required for scanning) |
| `VIRUSTOTAL_SCAN_ON_VARIANT_WRITE` | `true` | Auto-scan when variants are created/updated |
| `VIRUSTOTAL_BLOCK_SUSPICIOUS_DOWNLOADS` | `false` | Block downloads of suspicious/malicious variants |
| `VIRUSTOTAL_MAX_UPLOAD_MB` | `32` | Max file size for binary VT upload (URL scanning has no limit) |
| `VIRUSTOTAL_MALICIOUS_THRESHOLD` | `3` | Number of engines to mark as `malicious` |
| `VIRUSTOTAL_CDN_BASE_PATH` | `/usr/src/app/susmodder-cdn` | CDN path for self-hosted files |
| `VT_AI_REVIEW_TIMEOUT_MS` | `30000` | AI model call timeout |

---

## 6. Environment Variables Reference

See `susmodder-api/.env.example`:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `PORT` | No | `3001` | API port |
| `DATABASE_URL` | Yes | – | PostgreSQL connection string |
| `HTTP_TOKEN` | Yes | – | Comma-separated Bearer tokens |
| `CLAIRBOT_SYNC_SECRET` | Yes | – | ClairBot webhook secret |
| `REDIS_HOST` | No | – | Redis host for telemetry |
| `REDIS_PORT` | No | `6379` | Redis port |
| `REDIS_PASSWORD` | No | – | Redis auth password |
| `REDIS_DB` | No | `5` | Redis DB number |
| `VELOPACK_RELEASES_DIR` | No | – | Velopack manifests directory |
| `VELOPACK_DEFAULT_CHANNEL` | No | `release` | Default update channel |
| `VELOPACK_CHANNELS` | No | `release,beta` | Allowed channels |
| `VELOPACK_DEFAULT_ARCH` | No | `x64` | Default architecture |
| `VELOPACK_ARCHES` | No | `x64` | Allowed architectures |
| `VELOPACK_CACHE_CONTROL` | No | `public, max-age=300, stale-while-revalidate=120` | Cache header |
| `VELOPACK_DOWNLOAD_BASE_URL` | No | – | External download URL |
| `AI_PROVIDER_URL` | No | `http://ai-provider:3010` | AI microservice URL |
| `AI_SUPPORT_LLM_ENABLED` | No | `false` | Enable LLM fallback for support |
| `AI_SUPPORT_LLM_TIMEOUT_MS` | No | `12000` | LLM call timeout (ms) |
| `AI_SUPPORT_KB_CACHE_TTL_SECONDS` | No | `300` | KB cache TTL (seconds) |

---

## 7. Swagger / OpenAPI

Documentation auto-generated at `/api-docs` from JSDoc `@swagger` annotations in route files.

**Configuration** (`swagger.js`):
```js
{
  openapi: '3.0.0',
  info: { title: 'Susmodder API', version: '1.0.0' },
  components: {
    securitySchemes: {
      bearerAuth: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' }
    }
  },
  apis: ['./routes/*.js']
}
```

**Routes with Swagger annotations:**
- `config.js` – `/susmodder-discordfavs`, `/susmodder-config-versions`, `/public/discord-favs`, `/public/discord-server-counts`
- `compatibility.js` – `/compatibility`, `/compatibility/matrix`
- `telemetry.js` – `/telemetry/heartbeat`, `/telemetry/health`, `/telemetry/stats`
- `releases.js` – `/releases`
- `rolesModifiers.js` – `/roles-modifiers`
- `sustats.js` – `/among-api-configs`, `/among-api-add-discord-fav`, `/among-games`, `/among-data`, `/among-draft-data`, `/among-draft-occurances`, `/among-tokens`, `/sync-server-config`
- `upload.js` – `/upload`

**v2 routes with Swagger annotations:**
- `v2/downloads.js` – `/api/v2/downloads/mod/:id/:version` (download + VT report)
- `v2/virustotal.js` – `/api/v2/virustotal/report` (global SUSModder binary report)

**Services (no routes):**
- `services/virustotalService.js` – Shared VT helpers, AI review, backfill support

---

## 8. Known Limitations

1. **Redis dependency:** Telemetry features require Redis. Core API continues without it.
2. **Swagger-jsdoc vulnerabilities:** 5 moderate vulnerabilities in validator.js (docs-only, accepted risk)
3. **serverConfig.js duplicate:** `/sync-server-config` exists in both `sustats.js` and `serverConfig.js`. Only the last-loaded version takes effect.
4. **No input validation:** Some routes lack Joi validation (admin routes rely on manual normalization)
5. **SQL injection in githubUpdates.js admin route:** Status filter is string-interpolated into query (line 65)
6. **No pagination:** Large datasets (among_games, compatibility_matrix) returned without pagination
