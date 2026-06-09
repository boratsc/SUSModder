# Phase 2 Endpoint Contracts – Compatibility + Roles

---

## GET /api/v2/compatibility

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Query mod compatibility by fullModId or dllModId with optional version/status filters
**Side effects:** none
**Rate limit:** 120 req/min per IP
**Cache/headers:** `ETag`, `Cache-Control: public, max-age=120, stale-while-revalidate=600`, `304`

**Query params:**
| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `fullModId` | integer | (with dllModId) | Query by FULL mod |
| `fullModVersion` | string | no | Specific version (defaults to current) |
| `dllModId` | integer | (with fullModId) | Query by DLL mod |
| `dllModVersion` | string | no | Specific version |
| `status` | string | no | Comma-separated filter: F,W,NT,NW |
| `includeUntested` | boolean | no | Include NT status (default: true) |

**Response 200:**
```json
{
  "data": {
    "query": { "type": "full", "modId": 1, "modName": "Town of Us", "modVersion": "5.3.1" },
    "compatibilities": [
      { "id": 515, "dllMod": { "id": 5, "name": "AleLuduMod", "version": "1.1.2", "currentVersion": "1.1.2" }, "status": "W", "isCurrentVersion": true }
    ]
  },
  "meta": { "total": 22 }
}
```

**Errors:** `400 VALIDATION_ERROR` – Missing required params, `404 NOT_FOUND` – Mod not found

**Example:** `GET /api/v2/compatibility?fullModId=1&status=F,W`

---

## GET /api/v2/compatibility/matrix

**Consumer:** susadmin
**Auth:** Bearer HTTP_TOKEN
**Purpose:** Full matrix for admin UI
**Cache:** 120s

**Query params:** `onlyCurrentVersions` (boolean, default: true)

**Response 200:**
```json
{
  "data": {
    "fullMods": [{ "id": 1, "name": "Town of Us", "version": "5.3.1" }],
    "dllMods": [{ "id": 5, "name": "AleLuduMod", "version": "1.1.2" }],
    "entries": [{ "fullModId": 1, "dllModId": 5, "status": "W", "isExactVersion": true }]
  },
  "meta": { "total": 61 }
}
```

**Errors:** `401 UNAUTHORIZED`

---

## GET /api/v2/compatibility/snapshot

**Consumer:** SUSModder 3.x (local SQLite cache)
**Auth:** none
**Purpose:** Full compatibility snapshot in one request. Client caches locally.
**Rate limit:** 30 req/min per IP (separate limiter)
**Cache/headers:** `ETag`, `Cache-Control: public, max-age=120, stale-while-revalidate=600`, `304`

**Query params:**
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `onlyCurrentVersions` | boolean | true | Only entries where both mods at current version |
| `includeUntested` | boolean | true | Include NT status entries |
| `status` | string | none | Comma-separated filter |

**Response 200:**
```json
{
  "data": {
    "revision": "2026-05-31T16:50:03.943Z",
    "generatedAtUtc": "2026-06-05T11:31:50.722Z",
    "entries": [
      {
        "id": 515, "fullModId": 1, "fullModName": "Town of Us", "fullModVersion": "5.3.1",
        "dllModId": 5, "dllModName": "AleLuduMod", "dllModVersion": "1.1.2",
        "status": "W", "isExactVersion": true, "warning": null
      }
    ]
  },
  "meta": { "total": 61, "onlyCurrentVersions": true, "includeUntested": true }
}
```

**`isExactVersion` semantics:**
- `true` – Entry matches current versions of both mods (show in UI)
- `false` – Historical entry (hide from current compatibility view)

**Implementation notes:** `compatRevision = MAX(revision_compat_matrix, revision_config)` because changing a mod version in config changes isExactVersion semantics even without compatibility_matrix changes.

---

## GET /api/v2/roles

**Consumer:** SUSModder 3.x, susmodder-web
**Auth:** none
**Purpose:** Full catalog of roles and modifiers with associated mods and abilities
**Cache:** 300s (5 min), ETag/304

**Response 200:**
```json
{
  "data": [
    {
      "id": 497,
      "name": "ButtonBarry",
      "type": "Modifier",
      "category": "All",
      "description": "Może zwołać spotkanie z dowolnego miejsca na mapie.",
      "meta": {},
      "mods": [{ "id": 7, "name": "Syzyfowy ToU" }],
      "abilities": [{ "id": 68, "name": "Button (Naciśnij przycisk)", "icon": "https://townofus.pl/images/abilities/button.png" }]
    }
  ],
  "meta": { "total": 208 }
}
```

**Implementation notes:** `entity_meta` is empty (0 rows) but the schema supports key-value pairs. Revision query uses epoch fallback since entity table has no `updatedat` column.
