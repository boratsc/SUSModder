# SUSModder API v2 – Endpoint Checklist

> **Branch:** `api-v2` | **Commit:** `791877a` | **Updated:** 2026-06-05  
> **Definition of Done per endpoint:** Route ✅ | Joi ✅ | Swagger ✅ | Contract ✅ | Examples ✅ | Tests ✅ | Consumer Doc ✅
> **Live tested:** ✅ = HTTP test passed on production (`api.susmodder-cdn.ovh/v2/`)

---

## Phase 0 – Prerequisites ✅ COMPLETE

| Area | Status | Notes |
|------|--------|-------|
| `cache.deletePattern()` | ✅ | Selective prefix-based invalidation |
| Migration `010_api_v2_baseline.sql` | ✅ | 6 tables, githubprojecturl, 3 views, 15+ indexes – running on prod ✅ |
| Migration `010_api_v2_baseline_rollback.sql` | ✅ | Safe rollback |
| `routes/v2/_helpers.js` | ✅ | 4 revisions, handleETag, 10 response helpers, 4 mappers, withTransaction |
| `routes/v2/_schemas.js` | ✅ | URL allowlist, variant cross-field validation, 12+ schemas |
| `routes/v2/_router.js` | ✅ | Main router + catch-all JSON 404 |
| `routes/v2/admin/_router.js` | ✅ | Dual-auth chain, cache/clear endpoint |
| `middleware/userHash.js` | ✅ | Soft identity (64-char hex) |
| `middleware/adminSecret.js` | ✅ | Production fail-fast + timingSafeEqual |
| `server.js` v2 mount | ✅ | Path-aware CORS, admin 300/min + snapshot 30/min rate limiters |
| `swagger.js` v2 tags | ✅ | 14 tags (6 public + 8 admin) |
| Docker hardening | ✅ | dev: `127.0.0.1:3001`, prod: `expose: [3001]` |
| `docker-compose.prod.yml` | ✅ | Port 3001 not publicly exposed |
| Susadmin proxy RBAC | ✅ | `/v2/admin/*` in ADMIN_WRITE/READ paths |
| ADMIN_API_SECRET + HTTP_TOKEN alignment | ✅ | Set on production ✅ |
| `DOC/api/v2/` structure | ✅ | README, 2 consumer docs, 3 contracts, checklist, examples |
| Tests (node:test) | ✅ | 209+ passing (all phases) |
| Reviews (senior/security/quality) | ✅ | All completed, findings resolved |

---

## Phase 1 – Core MVP ✅ VERIFIED ON PRODUCTION

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `GET /catalog` | ✅ | ✅ | ✅ | ✅ 200 | Light, ETag/304 ✅ |
| `GET /catalog/:id` | ✅ | ✅ | ✅ | ✅ 200 | Full with variants/deps |
| `GET /catalog/:id/versions` | ✅ | ✅ | ✅ | ✅ 200 | Version history |
| `GET /catalog-meta` | ✅ | ✅ | ✅ | ✅ 200 | 4 revisions, ETag/304 ✅ |
| `GET /versions` | ✅ | ✅ | ✅ | ✅ 200 | AU catalog, ETag/304 ✅ |
| `GET /versions/:dbValue` | ✅ | ✅ | ✅ | ✅ 200 | Single version detail |
| `GET /downloads/mod/:id/:version` | ✅ | ✅ | ✅ | ✅ 404 (correct – no SHA256 variants) |

## Phase 2 – Compatibility + Roles ✅ VERIFIED ON PRODUCTION

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `GET /compatibility` | ✅ | ✅ | ✅ | ✅ 200 | Query per mod, ETag/304 |
| `GET /compatibility/matrix` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /compatibility/snapshot` | ✅ | ✅ | ✅ | ✅ 200 | isExactVersion, 30/min rate |
| `GET /roles` | ✅ | ✅ | ✅ | ✅ 200 | 208 entities with mods+abilities |

## Phase 3 – Remaining Public ✅ VERIFIED ON PRODUCTION

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `GET /releases` | ✅ | ✅ | ✅ | ✅ 200 | Velopack manifests |
| `POST /telemetry/heartbeat` | ✅ | ✅ | ✅ | – | Write-only (needs body) |
| `GET /telemetry/stats` | ✅ | ✅ | ✅ | ✅ 200 | Aggregated stats |
| `GET /telemetry/health` | ✅ | ✅ | ✅ | ✅ 200 | Redis status |
| `GET /discord/favs` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /discord/favs/public` | ✅ | ✅ | ✅ | ✅ 200 | 8 servers returned |
| `GET /discord/server-counts` | ✅ | ✅ | ✅ | ✅ 200 | Live member counts |
| `POST /discord/favs` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /online` | ✅ | ✅ | ✅ | ✅ 200 | User count |
| `GET /virustotal/report` | ✅ | ✅ | ✅ | ✅ 200 | VT scan results |

## Phase 4 – Sustats + Lobby + ModPacks ✅ ROUTES IMPLEMENTED

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `POST /sustats/games` | ✅ | ✅ | ✅ | – | Requires token+secret in body |
| `GET /sustats/games` | ✅ | ✅ | ✅ | ✅ 400 | Requires valid query params |
| `POST /sustats/drafts` | ✅ | ✅ | ✅ | – | Write-only |
| `GET /sustats/drafts/stats` | ✅ | ✅ | ✅ | ❌ 500 | Missing `role_drafts` table in prod DB |
| `POST /sustats/sync-config` | ✅ | ✅ | ✅ | – | ClairBot auth required |
| `GET /sustats/configs` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /sustats/tokens` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /lobby` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /lobby` | ✅ | ✅ | ✅ | – | userHash required |
| `DELETE /lobby/:id` | ✅ | ✅ | ✅ | – | userHash required |
| `PATCH /lobby/:id` | ✅ | ✅ | ✅ | – | userHash required |
| `POST /lobby/:id/report` | ✅ | ✅ | ✅ | – | userHash required |
| `POST /modpacks` | ✅ | ✅ | ✅ | – | userHash required |
| `GET /modpacks` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /modpacks/:code` | ✅ | ✅ | ✅ | – | Needs valid code |
| `GET /modpacks/:code/web` | ✅ | ✅ | ✅ | – | Needs valid code |
| `DELETE /modpacks/:code` | ✅ | ✅ | ✅ | – | userHash required |
| `POST /modpacks/:code/dlls` | ✅ | ✅ | ✅ | – | userHash + multipart required |
| `GET /modpacks/:code/dlls/:sha256` | ✅ | ✅ | ✅ | – | Needs valid SHA |
| `GET /modpacks/:code/dlls/:sha256/status` | ✅ | ✅ | ✅ | – | userHash required |

## Phase 5 – Admin ✅ COMPLETE

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `GET /admin/mods` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/mods/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/mods` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/mods/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/mods/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/mods/:id/download-to-cdn` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/mods/:id/variants` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/mods/:id/variants` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/mods/:id/variants/:vid` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/mods/:id/variants/:vid` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/mods/:id/dependencies` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/mods/:id/dependencies` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/mods/:id/dependencies/:did` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/mods/:id/dependencies/:did` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/cache/clear` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/entities` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/entities/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/entities` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/entities/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/entities/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/compatibility` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/versions` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/versions` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/versions/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/versions/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/versions/:id/steam` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/versions/:id/epic` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/github-updates` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/github-updates/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/github-updates/:id/dismiss` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/github-updates/:id/apply` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/github-updates/check-now` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/webhooks` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/webhooks` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/webhooks/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/webhooks/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/webhooks/:id/test` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/lobby` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/lobby/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/lobby/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/lobby/blocklist` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/lobby/blocklist` | ✅ | ✅ | ✅ | – | Admin auth required |
| `PUT /admin/lobby/blocklist/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/lobby/blocklist/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/lobby/blocklist/refresh` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/lobby/reports` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/lobby/reports/:id` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/lobby/ban/:userHash` | ✅ | ✅ | ✅ | – | Admin auth required |
| `DELETE /admin/lobby/ban/:userHash` | ✅ | ✅ | ✅ | – | Admin auth required |
| `POST /admin/lobby/shadowban/:userHash` | ✅ | ✅ | ✅ | – | Admin auth required |
| `GET /admin/lobby/stats` | ✅ | ✅ | ✅ | – | Admin auth required |

---

## Production Verification Summary

| Phase | Public GET tested | Status |
|-------|------------------|--------|
| Phase 1 – Core MVP | 9/9 ✅ | All endpoints return 200, ETag/304 verified |
| Phase 2 – Compatibility + Roles | 4/4 ✅ | All return 200, snapshot with filters |
| Phase 3 – Public | 6/6 ✅ | releases, telemetry, online, virustotal |
| Phase 4 – Sustats/Lobby/ModPacks | 1/7 (write/auth/data-dependent) | Routes exist ✅, need auth/data for full test |
| Phase 5 – Admin | 0/51 (admin auth required) | Routes exist ✅, test suite covers schemas |

**CORS** ✅ | **Rate limiting** ✅ | **JSON 404** ✅ | **ETag/304** ✅ | **Cloudflare CDN** ✅

### Fixed this session
- `GET /discord/favs/public` ✅ – 8 servers, v2 discord.js created
- `GET /discord/server-counts` ✅ – live counts via Discord API
- `GET /versions/:dbValue/steam` ✅ – Steam manifest by version
- `GET /versions/:dbValue/epic` ✅ – Epic manifest by version

### Known (deferred)
- Sustats/drafts - deprecated, Clair took over
- `role_drafts` table missing in prod (not needed)
- ModEditorPage variants/deps tabs (Phase 6 backlog)

---

## Phase 6 – Mod Changelog ✅ DEPLOYED (2026-06-11)

| Endpoint | Route | Schema | Swagger | Live | Notes |
|----------|-------|--------|---------|------|-------|
| `GET /catalog/:id/changelog` | ✅ | ✅ | ✅ | ✅ 200 | PL/EN changelog, ETag/304, limit 1..20 |

**Smoke tested:** `curl` on production returns 200 with PL/EN body, ETag, Cache-Control. `404` for non-existent mods. `400` for invalid lang/limit. `If-None-Match` with quoted ETag returns `304 Not Modified`.

**Client implemented:** SUSModder 3.x (2026-06-11). See `DOC/PLAN/2026-06-11-mod-changelog-client-integration-plan.md`.
