# SUSModder API v2 – Developer Documentation

> **Base URL (production):** `https://api.susmodder.app/api/v2`  
> **Base URL (dev):** `http://localhost:3001/api/v2`  
> **Version:** 2.0.0  
> **Last updated:** 2026-06-05

---

## Overview

The SUSModder API v2 provides a modern, consistent REST interface for the SUSModder modding platform.
It runs alongside v1 (no breaking changes) under the `/api/v2/` prefix.

### Design Principles

| Principle | Meaning |
|-----------|---------|
| **Consistent response format** | `{ data, meta }` for success, `{ error: { code, message } }` for errors |
| **ETag/304 support** | All cached GET endpoints return `ETag` and handle `If-None-Match → 304` |
| **SHA256 integrity** | Every mod download variant requires SHA256; client verifies before installing |
| **Input validation** | Every endpoint validates inputs with Joi before querying the database |
| **No SELECT *** | All queries use explicit column lists |
| **V2 independence** | V2 endpoints never delegate to v1; they return v2 format independently |

---

## Authentication

| Level | Mechanism | Used by |
|-------|-----------|---------|
| **Public** | None | `/catalog`, `/versions`, `/compatibility`, `/releases`, `/roles`, `/online` |
| **Soft user identity** | `X-User-Hash: <sha256>` | `/lobby/*` – rate limiting, ownership; NOT strong auth |
| **Admin token** | `Authorization: Bearer <HTTP_TOKEN>` + `X-Admin-API-Secret` | `/admin/*` |
| **ClairBot secret** | `Authorization: Bearer <CLAIRBOT_SYNC_SECRET>` | `/sustats/sync-config` |
| **Sustats token** | Body: `{ token, secret }` | `/sustats/games` POST |

---

## Response Format

### Success – single object

```json
{
  "data": {
    "id": 1,
    "name": "Town of Us"
  }
}
```
HTTP 200 (GET), 201 (POST created)

### Success – list

```json
{
  "data": [
    { "id": 1, "name": "Town of Us" }
  ],
  "meta": {
    "total": 42,
    "offset": 0,
    "limit": 50
  }
}
```
HTTP 200

### Error

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid request parameters",
    "details": [
      { "field": "modId", "message": "must be a positive integer" }
    ]
  }
}
```

### Error codes

| Code | HTTP | Meaning |
|------|------|---------|
| `VALIDATION_ERROR` | 400 | Invalid request parameters |
| `UNAUTHORIZED` | 401 | Missing or invalid token |
| `FORBIDDEN` | 403 | Missing admin API secret |
| `NOT_FOUND` | 404 | Resource not found |
| `VARIANT_NOT_FOUND` | 404 | No verified variant available for download |
| `RATE_LIMITED` | 429 | Too many requests |
| `INTERNAL_ERROR` | 500 | Unexpected server error |

---

## Caching & ETag/304

All cached GET endpoints support conditional requests:

```http
GET /api/v2/catalog HTTP/1.1
If-None-Match: "catalog-2026-06-05T12:00:00.000Z"

HTTP/1.1 304 Not Modified
ETag: "catalog-2026-06-05T12:00:00.000Z"
Cache-Control: public, max-age=60, stale-while-revalidate=300
```

### Cache TTLs

| Endpoint | TTL | ETag/304 |
|----------|-----|----------|
| `GET /catalog*` | 60s | ✅ |
| `GET /catalog-meta` | 30s | ✅ |
| `GET /versions*` | 300s | ✅ |
| `GET /compatibility*` | 120s | ✅ |
| `GET /compatibility/snapshot` | 120s | ✅ |
| `GET /releases` | 300s | ✅ |
| `GET /roles` | 300s | ✅ |
| `GET /discord/*` | 60-300s | ✅ |
| Admin endpoints | No cache | ❌ |

---

## Rate Limits

| Group | Limit | Window |
|-------|-------|--------|
| Public read | 120 req/min per IP | 60s |
| Compatibility snapshot | 30 req/min per IP | 60s |
| Telemetry write | 10 req/min per userHash | 600s |
| Downloads | 30 req/min per IP | 60s |
| Lobby | 20 req/min per userHash | 60s |
| Admin | 300 req/min per IP | 60s |

---

## Endpoint Catalog

### Phase 1 (Core MVP)
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `GET` | `/catalog` | 🔓 | Light catalog (no variants/deps) |
| `GET` | `/catalog/:id` | 🔓 | Full mod details with variants, deps, manifests |
| `GET` | `/catalog/:id/versions` | 🔓 | Mod version history |
| `GET` | `/catalog-meta` | 🔓 | Revisions for status bar |
| `GET` | `/versions` | 🔓 | Among Us version catalog |
| `GET` | `/versions/:dbValue` | 🔓 | Single AU version |
| `GET` | `/downloads/mod/:id/:version` | 🔓 | Download mod variant |

### Phase 2 (Compatibility + Roles)
| `GET` | `/compatibility` | 🔓 | Query compatibility |
| `GET` | `/compatibility/snapshot` | 🔓 | Full matrix snapshot |
| `GET` | `/compatibility/matrix` | 🔒 | Admin matrix |
| `GET` | `/roles` | 🔓 | Role/modifier catalog |

### Future phases
See `DOC/poc/susmodder-api/2026-06-05-v2-api-plan.md` for the complete catalog.

---

## Download Integrity

V2 requires SHA256 for all downloads. The flow:

1. Client reads `GET /catalog/:id` → gets `variants[].sha256` and `variants[].downloadUrl`
2. Client downloads from the URL, computes SHA256
3. If SHA256 matches → proceed with install
4. If SHA256 mismatch → **discard file, do not install**

V2 **never** redirects to a download URL without a verified SHA256 in the database.
If no variant with SHA256 exists, the endpoint returns `404 VARIANT_NOT_FOUND`.

---

## Versioning & Migration

- **V1 endpoints** (`/susmodder-config`, `/compatibility`, etc.) remain unchanged for SUSModder 2.x clients
- **V2 endpoints** (`/api/v2/*`) are for SUSModder 3.x+ clients
- No breaking changes to v1 – v2 is purely additive

---

## Contacts & Links

- **Swagger UI:** `https://susmodder.app/api-docs`
- **OpenAPI spec:** `DOC/api/v2/openapi.json` (generated after full implementation)
- **Implementation plan:** `DOC/poc/susmodder-api/2026-06-05-v2-api-plan.md`
