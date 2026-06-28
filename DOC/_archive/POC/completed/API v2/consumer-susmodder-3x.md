# SUSModder API v2 – Consumer Documentation for SUSModder 3.x (C#)

> **Base URL (produkcja):** `https://api.susmodder-cdn.ovh/v2`  
> **Base URL (alt):** `https://api.susmodder.app/api/v2`  
> **Auth:** Bearer token dla `/lobby`; reszta publiczna (catalog/download)  
> **Version:** 2.0.0  
> **Status wdrożenia klienta:** [`DOC/PLAN/2026-06-07-api-v2-rollout-status.md`](../../PLAN/2026-06-07-api-v2-rollout-status.md)

---

## Quick Reference

### Endpoints used by SUSModder 3.x

| Method | Path | Purpose | Cached? |
|--------|------|---------|---------|
| `GET` | `/catalog` | Light mod list (no variants/deps) | 60s, ETag/304 |
| `GET` | `/catalog/:id` | Full mod details + variants + deps | 60s, ETag/304 |
| `GET` | `/catalog/:id/versions` | Mod version history | 120s, ETag/304 |
| `GET` | `/catalog-meta` | Revisions for status bar ping | 30s, ETag/304 |
| `GET` | `/versions` | Among Us version catalog | 300s, ETag/304 |
| `GET` | `/versions/:dbValue` | Single AU version with manifests | 300s, ETag/304 |
| `GET` | `/compatibility` | Query mod compatibility | 120s, ETag/304 |
| `GET` | `/compatibility/snapshot` | Full matrix for local SQLite cache | 120s, ETag/304 |
| `GET` | `/downloads/mod/:id/:version` | Download mod variant (302 redirect) | No cache |
| `GET` | `/roles` | Role/modifier catalog | 300s, ETag/304 |
| `GET` | `/icons/:filename` | Mod icon (302 → susmodder.app/icons/) | 60s |
| `GET` | `/releases` | Velopack manifests (release/beta) | 300s |
| `GET` | `/online` | Online users count | No cache |
| `POST` | `/telemetry/heartbeat` | Session telemetry | Rate-limited |
| `GET` | `/lobby` | Lobby board list | Auth required |
| `POST` | `/modpacks` | Create mod pack | Rate-limited |
| `POST` | `/modpacks/:code/dlls` | Upload custom DLL | Rate-limited |
| `GET` | `/modpacks/:code/dlls/:sha256/status` | Scan status of uploaded DLL | No cache |

---

## Response Format

All endpoints return:
```json
{
  "data": { ... },
  "meta": { "total": 42, "offset": 0, "limit": 50 }
}
```

Errors return:
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid request parameters"
  }
}
```

---

## ETag / 304 Not Modified

All cached GET endpoints support conditional requests:

```
// First request
GET /api/v2/catalog HTTP/1.1
→ 200 OK, ETag: "catalog-2026-06-05T12:00:00.000Z"

// Subsequent requests – only send data if changed
GET /api/v2/catalog HTTP/1.1
If-None-Match: "catalog-2026-06-05T12:00:00.000Z"
→ 304 Not Modified (no body, save ~15-70KB transfer)
```

**Client implementation:** Store the ETag from the first response. On subsequent polls, send it as `If-None-Match`. If you get `304`, reuse the previously cached data.

---

## Download Integrity (SHA256)

**Critical:** Every download MUST be verified against SHA256.

1. Get variant info from `GET /catalog/:id`:
```json
{
  "variants": [{
    "platform": "steam",
    "architecture": "x86",
    "downloadUrl": "https://susmodder-cdn.ovh/mods/1/5.4.0/TownOfUs-Steam-x86.zip",
    "sha256": "a1b2c3d4e5f6789012345678901234567890123456789012345678901234567890",
    "fileSizeBytes": 45678901
  }]
}
```

2. Request `GET /downloads/mod/1/5.4.0?platform=steam&arch=x86`
   → `302` redirect to download URL
   → Headers: `X-SUSModder-SHA256`, `X-SUSModder-File-Size`

3. Download the file, compute SHA256 locally.

4. **If SHA256 matches** → extract and install.
   **If SHA256 does NOT match** → discard file, show error, do NOT install.

5. If no verified variant exists → `404 VARIANT_NOT_FOUND` with `legacyFallbackAvailable: true`. Client 3.x should NOT install via legacy fallback.

---

## Rate Limits

| Endpoint Group | Limit | Window |
|---------------|-------|--------|
| Public reads (`/catalog`, `/versions`, etc.) | 120/min per IP | 60s |
| Compatibility snapshot | 30/min per IP | 60s |
| Modpack create/upload | 10/min per userHash | 60s |
| Downloads | 30/min per IP | 60s |

---

## Status Bar (catalog-meta)

For the status bar in the SUSModder UI, use `GET /catalog-meta` instead of a full catalog poll:

```json
{
  "data": {
    "catalogRevision": "2026-06-05T12:00:00.000Z",
    "compatibilityRevision": "2026-06-05T11:45:00.000Z",
    "versionsRevision": "2026-05-20T08:00:00.000Z",
    "serverTimeUtc": "2026-06-05T12:30:00.000Z"
  }
}
```

This is a lightweight ping (~200 bytes) that tells you whether to refresh your cached data. Poll every 5-10 minutes with ETag support.

---

## Compatibility Snapshot

Instead of making N queries for each DLL mod, use the snapshot endpoint:

```
GET /api/v2/compatibility/snapshot?onlyCurrentVersions=true
```

This returns ALL compatibility entries in one request. Cache locally in SQLite.
Each entry has `isExactVersion: true/false` – only show entries where `isExactVersion: true` for current compatibility.

---

## Modpack DLL Upload

Uploading a custom DLL:

1. `POST /api/v2/modpacks/:code/dlls` (multipart/form-data)
2. Response: `{ "data": { "sha256": "...", "status": "pending", "downloadAvailable": false } }`
3. Poll `GET /api/v2/modpacks/:code/dlls/:sha256/status` until status is `clean`
4. Only then can the DLL be downloaded

Status flow: `pending` → `scanning` → `clean` (or `suspicious`/`rejected`)

---

## Mod Changelog (v2.2.0+)

Fetch per-mod changelogs from GitHub releases with optional PL translation.

```
GET /v2/catalog/:id/changelog?lang=pl|en&limit=1..20
```

**Parameters:**
| Param | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `lang` | `string` | yes | `pl` | `pl` or `en`; other values → `400` |
| `limit` | `int` | no | `5` | Range `1..20`; outside range → `400` |

**Response (200):**
```json
{
  "data": [{
    "id": "123",
    "modId": 1,
    "version": "5.4.0",
    "releaseName": "v5.4.0",
    "body": "- Fixed lobby crash\n- Added new roles",
    "language": "pl",
    "requestedLanguage": "pl",
    "fallbackLanguage": null,
    "translationStatus": "auto",
    "translationProvider": "deepl",
    "translationModel": null,
    "releaseUrl": "https://github.com/user/repo/releases/tag/v5.4.0",
    "source": "github"
  }],
  "meta": { "total": 45, "offset": 0, "limit": 5 }
}
```

**Caching:** ETag/304 supported. Use `If-None-Match` with **quoted** ETag values. Object-level cache TTL: 120s.

**Error cases:**
- `404 NOT_FOUND` – No changelog available for this mod.
- `400 VALIDATION_ERROR` – Invalid `lang` or `limit`.
- `500 INTERNAL_ERROR` – Backend failure.

**Client behavior (SUSModder 3.x):**
- Always send `lang=pl` or `lang=en` based on app locale.
- Tolerate `id` as string or number (use `JsonNumberHandling.AllowReadingFromString`).
- On `304`, reuse cached entries from memory cache (TTL 2 min).
- On `404`, show localized empty state, not a crash.
- On error, show localized error message and allow user to close the dialog.
- Do NOT block install/update flows when changelog API is unavailable.
