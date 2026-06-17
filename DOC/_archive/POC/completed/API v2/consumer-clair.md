# SUSModder API v2 – Consumer Documentation for Clair / ClairBot

> **Base URL:** `https://api.susmodder.app/api/v2`  
> **Auth:** `Authorization: Bearer <CLAIRBOT_SYNC_SECRET>`  
> **Version:** 2.0.0

---

## Endpoints

### 1. Sync Server Config

```
POST /api/v2/sustats/sync-config
Authorization: Bearer <CLAIRBOT_SYNC_SECRET>
Content-Type: application/json
```

**Purpose:** Sync game server configuration from ClairBot to SUSModder.

**Auth:** ClairBot secret (`CLAIRBOT_SYNC_SECRET` env var). Use timing-safe comparison.

**Request body:** (TBD – same payload as v1 `/sync-server-config`)

**Response 200:**
```json
{
  "data": {
    "synced": true,
    "configsUpdated": 3
  }
}
```

**Response 401:**
```json
{
  "error": {
    "code": "UNAUTHORIZED",
    "message": "Invalid authorization token"
  }
}
```

**Idempotency:** Repeated sync with same data produces the same result. No duplicate configs.

---

### 2. Submit Game Result

```
POST /api/v2/sustats/games
Content-Type: application/json

{
  "token": "<server_token>",
  "secret": "<server_secret>",
  ...
}
```

**Purpose:** Record an Among Us game result.

**Auth:** Token + secret in request body (not Bearer header).

**Response 201:**
```json
{
  "data": {
    "gameId": 12345,
    "recorded": true
  }
}
```

---

### 3. Submit Role Draft

```
POST /api/v2/sustats/drafts
Content-Type: application/json
```

**Purpose:** Record role draft data.

**Auth:** Public (no auth).

**Response 201:** Draft recorded.

---

### 4. Role Draft Statistics

```
GET /api/v2/sustats/drafts/stats
```

**Purpose:** Aggregated role occurrence statistics.

**Auth:** Public.

**Response 200:**
```json
{
  "data": {
    "totalDrafts": 500,
    "roleStats": [
      { "roleId": 1, "roleName": "Sheriff", "occurrences": 120, "percentage": 24.0 }
    ]
  }
}
```

---

### 5. List Configs (Admin)

```
GET /api/v2/sustats/configs
Authorization: Bearer <HTTP_TOKEN>
X-Admin-API-Secret: <ADMIN_API_SECRET>
```

**Auth:** Admin (Bearer + admin secret).

**Response 200:** List of Among Us API configurations.

---

### 6. List Tokens (Admin)

```
GET /api/v2/sustats/tokens
Authorization: Bearer <HTTP_TOKEN>
X-Admin-API-Secret: <ADMIN_API_SECRET>
```

**Auth:** Admin.

**Response 200:** List of Among Us server tokens.

---

## Response Format

All v2 endpoints use the same format:

**Success:**
```json
{ "data": { ... }, "meta": { ... } }
```

**Error:**
```json
{ "error": { "code": "UNAUTHORIZED", "message": "Invalid token" } }
```

---

## Rate Limits

| Endpoint | Limit | Window |
|----------|-------|--------|
| `/sustats/games` POST | 60/min | 60s |
| `/sustats/sync-config` POST | 30/min | 60s |
| `/sustats/drafts` POST | 30/min | 60s |

---

## Error Handling

- `401 UNAUTHORIZED` – Missing or invalid ClairBot secret / server token
- `400 VALIDATION_ERROR` – Invalid request body (check details array)
- `429 RATE_LIMITED` – Too many requests (check `Retry-After` header)
- `500 INTERNAL_ERROR` – Server error (retry with exponential backoff)

**Retry strategy:** For 429, wait `Retry-After` seconds. For 500, exponential backoff (1s, 2s, 4s, 8s, max 5 retries).
