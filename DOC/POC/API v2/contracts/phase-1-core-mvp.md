# Phase 1 Endpoint Contracts

---

## GET /api/v2/catalog

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Lightweight catalog listing (no variants or dependencies per mod)
**Side effects:** none
**Rate limit:** 120 req/min per IP
**Cache/headers:** `ETag`, `Cache-Control: public, max-age=60, stale-while-revalidate=300`, `304 Not Modified`

**Query params:**
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `modType` | `full`\|`dll` | none | Filter by mod type |
| `amongVersion` | string | none | Filter by Among Us version dbValue |
| `offset` | integer | 0 | Pagination offset |
| `limit` | integer | 50 (max 200) | Page size |

**Response 200:**
```json
{
  "data": [
    {
      "id": 1,
      "name": "Town of Us",
      "type": "full",
      "currentVersion": "5.3.1",
      "description": "...",
      "iconUrl": "/icons/tou.png",
      "installPath": "BepInEx/plugins/",
      "gitHubProjectUrl": "https://github.com/townofus/TownOfUs",
      "lastUpdated": "2026-05-31T16:50:03.943Z",
      "amongVersion": { "dbValue": "2025-3-31", "label": "2025-3-31", "releaseDate": "2025-03-31" }
    }
  ],
  "meta": { "total": 19, "offset": 0, "limit": 50 }
}
```

**Errors:**
- `400 VALIDATION_ERROR` – Invalid query parameters
- `304 Not Modified` – ETag match (no body)

**Example request:** `GET /api/v2/catalog?modType=full&limit=10`

---

## GET /api/v2/catalog/:id

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Full mod details with variants, dependencies, and Among Us manifests
**Side effects:** none
**Rate limit:** 120 req/min per IP
**Cache/headers:** `ETag`, `Cache-Control: public, max-age=60`, `304`

**Path params:** `id` (integer) – Mod ID

**Response 200:**
```json
{
  "data": {
    "id": 1,
    "name": "Town of Us",
    "type": "full",
    "currentVersion": "5.3.1",
    "description": "...",
    "gitHubProjectUrl": null,
    "amongVersion": { "dbValue": "2025-3-31", "steam": null, "epic": null },
    "variants": [],
    "dependencies": []
  }
}
```

**Errors:**
- `400 VALIDATION_ERROR` – Invalid mod ID
- `404 NOT_FOUND` – Mod not found

**Implementation notes:** Variants only returned when `sha256 IS NOT NULL AND file_size_bytes IS NOT NULL`. Dependencies joined on `target_slug` matching config.modname.

---

## GET /api/v2/catalog/:id/versions

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Mod version history from config_versions table
**Cache:** 120s, ETag/304

**Response 200:**
```json
{
  "data": {
    "modId": 1,
    "modName": "Town of Us",
    "currentVersion": "5.3.1",
    "versions": [
      { "versionId": 1, "version": "5.3.1", "amongVersion": "2025-3-31", "createdAt": "2025-10-22T13:21:30.000Z", "notes": null }
    ]
  },
  "meta": { "total": 1 }
}
```

**Errors:** `404 NOT_FOUND` – Mod not found

---

## GET /api/v2/catalog-meta

**Consumer:** SUSModder 3.x (status bar ping)
**Auth:** none
**Purpose:** Lightweight endpoint (~200 bytes) returning all revision timestamps
**Cache:** 30s, ETag/304
**Rate limit:** 120 req/min per IP

**Response 200:**
```json
{
  "data": {
    "catalogRevision": "2026-05-31T16:50:03.943Z",
    "compatibilityRevision": "2026-05-31T16:50:03.943Z",
    "versionsRevision": "2026-04-19T21:55:57.410Z",
    "serverTimeUtc": "2026-06-05T11:19:34.706Z"
  }
}
```

**Implementation notes:** Each revision is individually cached for 30s. `compatibilityRevision` may be null if no compatibility data exists.

---

## GET /api/v2/versions

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Among Us version catalog with Steam and Epic manifest data
**Cache:** 300s, ETag/304

**Response 200:**
```json
{
  "data": [
    {
      "id": 1,
      "dbValue": "2026-3-31",
      "label": "2026-3-31",
      "releaseDate": "2026-03-31",
      "hasSteamPkg": true,
      "steam": { "appId": 945360, "depotId": 945361, "manifestId": "1536693410725915190" },
      "epic": { "appId": "963137e4c29d4c79a81323b8fab03a40", "manifestPath": "AmongUs_Windows_2026.3.31.manifest", "downloadSize": 55657 }
    }
  ],
  "meta": { "total": 22 }
}
```

---

## GET /api/v2/versions/:dbValue

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Single Among Us version with full manifests
**Cache:** 300s, ETag/304

**Path params:** `dbValue` (string) – e.g. "2026-3-31"

**Errors:** `404 NOT_FOUND` – Version not found

---

## GET /api/v2/downloads/mod/:id/:version

**Consumer:** SUSModder 3.x
**Auth:** none
**Purpose:** Download a verified mod variant (302 redirect with integrity headers)
**Side effects:** none
**Rate limit:** 30 req/min per IP
**Cache:** none (no-store)

**Path params:** `id` (integer), `version` (string)
**Query params:** `platform` (steam|epic|msstore|itchio, default: steam), `arch` (x64|x86, default: x64)

**Response 302:** Redirect with headers:
- `Location: <download_url>`
- `X-SUSModder-SHA256: <64-char hex>`
- `X-SUSModder-File-Size: <bytes>`

**Response 404 VARIANT_NOT_FOUND:**
```json
{
  "error": {
    "code": "VARIANT_NOT_FOUND",
    "message": "No verified variant (with SHA256) found for mod X version Y platform Z architecture W",
    "legacyFallbackAvailable": true
  }
}
```

**Implementation notes:** Only redirects when `mod_variants.sha256 IS NOT NULL AND file_size_bytes IS NOT NULL`. Client MUST verify SHA256 after download. No legacy fallback – use v1 endpoint for legacy downloads.
