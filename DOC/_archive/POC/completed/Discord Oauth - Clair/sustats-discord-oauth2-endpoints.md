---
title: sustats-discord-oauth2-endpoints
created: 2026-05-27
tags:
  - sustats
  - discord-oauth2
  - endpoints
  - documentation
  - internal
---

# SUSTATS Discord OAuth2 — dokumentacja endpointów

**Data:** 2026-05-27
**Status:** wdrożone
**Serwis:** `clair-hub`

---

## Endpointy publiczne (bez sesji) — `clair-hub/routes/susmodder.js`

Wszystkie endpointy pod `/api/susmodder/*`. Wywoływane przez aplikację desktopową SUSModder (.NET/Avalonia) w flow Discord OAuth2 PKCE.

CSRF wyłączony dla tych ścieżek w `clair-hub/middleware/csrf.js`.

---

### `GET /api/susmodder/config`

Publiczna konfiguracja OAuth2. Zwraca `DISCORD_CLIENT_ID` potrzebny do rozpoczęcia flow Discord OAuth.

**Response 200:**
```json
{
  "ok": true,
  "discord_client_id": "1467560422352748605",
  "auth_endpoint": "https://clairbot.app/api/susmodder",
  "guilds_endpoint": "/guilds",
  "credentials_endpoint": "/credentials"
}
```

**Rate limiting:** brak (endpoint lekki, bezstanowy)
**Cache:** Redis, TTL 1h (client_id nie zmienia się)

---

### `POST /api/susmodder/guilds`

Zwraca listę serwerów Discord gdzie użytkownik ma dostęp do SUSTATS.

**Request:**
```json
{
  "discord_access_token": "ya29..."
}
```

**Logika:**
1. Waliduje Discord access token → `GET /users/@me`
2. Pobiera guildy użytkownika → `GET /users/@me/guilds`
3. Dla każdej guildy (równolegle, po 5 naraz):
   - Sprawdza czy istnieje aktywny token w `sustats_tokens`
   - Sprawdza uprawnienia: owner → admin → sustats_access_roles
4. Zwraca tylko guildy spełniające warunki

**Response 200:**
```json
{
  "ok": true,
  "guilds": [
    {
      "guild_id": "1372226857294106644",
      "guild_name": "Psychopaci",
      "has_sustats": true,
      "sustats_server_name": "Psychopaci SUStats",
      "user_access_level": "admin"
    }
  ]
}
```

**`user_access_level`:**
- `"owner"` — właściciel serwera
- `"admin"` — ADMINISTRATOR lub MANAGE_GUILD
- `"role"` — dostęp przez `sustats_access_roles`
- (brak wpisu = brak dostępu)

**Błędy:**
- `400` — brak `discord_access_token`
- `401` — nieprawidłowy/wygasły token Discord
- `429` — rate limit (max 30/min per token)
- `502` — błąd Discord API

**Rate limiting:** Redis, 30 req/min, klucz: hash tokena

---

### `POST /api/susmodder/credentials`

Zwraca token + secret SUSTATS dla wskazanej guildy. **To jest endpoint krytyczny bezpieczeństwa.**

**Request:**
```json
{
  "discord_access_token": "ya29...",
  "guild_id": "1372226857294106644"
}
```

**Logika:**
1. Waliduje Discord access token → `GET /users/@me`
2. **Rate limiting** na `user.id` (Redis, 5 req/h) — **fail closed** (503 gdy Redis down)
3. Pobiera guildy usera, znajduje wskazaną
4. Sprawdza uprawnienia (owner → admin → sustats_access_roles)
5. Pobiera token+secret z `sustats_tokens` (najnowszy aktywny)
6. **Zapisuje audyt** do `sustats_audit_log`

**Response 200:**
```json
{
  "ok": true,
  "credentials": {
    "token": "abc123...",
    "secret": "xyz789...",
    "endpoint": "https://clairbot.app/api/among-data",
    "server_name": "Psychopaci SUStats",
    "guild_id": "1372226857294106644"
  }
}
```

**Błędy:**
- `400` — brak wymaganych pól lub nieprawidłowy `guild_id`
- `401` — nieprawidłowy/wygasły token Discord
- `403` — brak uprawnień do guildy
- `404` — guild nie ma aktywnego SUSTATS tokena
- `429` — rate limit (max 5/h per user)
- `502` — błąd Discord API
- `503` — Redis niedostępny (rate limiting fail-closed)

**Rate limiting:** Redis, 5 req/h, klucz: `susmodder:rate:creds:{discord_user_id}`
**Audyt:** każda udana prośba → `sustats_audit_log`

---

## Endpointy Hub (wymagają sesji) — `clair-hub/routes/among-us.js`

Dostępne w panelu admina pod `/among-us?tab=sustats` → sub-tab "Uprawnienia".

---

### `GET /api/among-us/sustats-access-roles`

Zwraca listę ról Discorda z dostępem do SUSTATS dla bieżącej guildy.

**Response 200:**
```json
{
  "ok": true,
  "roles": [
    {
      "id": 1,
      "guild_id": "1372226857294106644",
      "role_id": "123456789",
      "is_enabled": true,
      "created_at": "2026-05-27T19:00:00.000Z",
      "updated_at": "2026-05-27T19:00:00.000Z"
    }
  ]
}
```

**Auth:** `requireGuildId` (sesja Hub)

---

### `PUT /api/among-us/sustats-access-roles`

Zapisuje konfigurację ról z dostępem do SUSTATS. Operacja atomowa (DELETE wszystkie + INSERT nowe w transakcji).

**Request:**
```json
{
  "role_ids": ["123456789", "987654321"]
}
```

**Response 200:**
```json
{
  "ok": true,
  "message": "Zapisano 2 ról",
  "roleCount": 2
}
```

**Błędy:**
- `400` — `role_ids` nie jest tablicą
- `500` — błąd bazy danych (transakcja rollback)

**Auth:** `requireGuildId` (sesja Hub)
**Walidacja:** każdy `role_id` sprawdzany regex `/^\d{17,20}$/`

---

## Modele danych

### `sustats_access_roles` (migracja 288)

| Kolumna | Typ | Opis |
|---------|-----|------|
| `id` | SERIAL PK | |
| `guild_id` | VARCHAR(20) | ID serwera Discord |
| `role_id` | VARCHAR(20) | ID roli Discord |
| `is_enabled` | BOOLEAN | Czy rola jest aktywna |
| `created_at` | TIMESTAMP | |
| `updated_at` | TIMESTAMP | |
| `created_by` | VARCHAR(20) | Discord user ID twórcy |

**Indeksy:** `(guild_id)`, `(guild_id, is_enabled)`
**Constraint:** `UNIQUE (guild_id, role_id)`

### `sustats_audit_log` (migracja 288)

| Kolumna | Typ | Opis |
|---------|-----|------|
| `id` | SERIAL PK | |
| `guild_id` | VARCHAR(20) | ID serwera Discord |
| `discord_user_id` | VARCHAR(20) | Discord user ID |
| `discord_username` | VARCHAR(255) | Nazwa użytkownika Discord |
| `action` | VARCHAR(50) | Typ akcji (`credentials_request`) |
| `ip_address` | VARCHAR(45) | IP klienta (pierwszy z `x-forwarded-for`) |
| `user_agent` | TEXT | User-Agent (sanityzowany, max 500 znaków) |
| `metadata` | JSONB | Dodatkowe dane (domyślnie `{}`) |
| `created_at` | TIMESTAMP | |

**Indeksy:** `(guild_id, created_at DESC)`, `(discord_user_id, created_at DESC)`

---

## Schemat uprawnień

```
Użytkownik chce pobrać credentials SUSTATS dla guildy X
│
├── owner: true?                         → ✅ dostęp (level: owner)
├── permissions & ADMINISTRATOR (0x8)?   → ✅ dostęp (level: admin)
├── permissions & MANAGE_GUILD (0x20)?   → ✅ dostęp (level: admin)
├── sustats_access_roles skonfigurowane?  │
│   └── user ma którąś z ról?            → ✅ dostęp (level: role)
└── żadne z powyższych                   → ❌ 403 FORBIDDEN
```

**Uwaga:** Sprawdzanie ról przez `sustats_access_roles` wymaga, aby bot Clair był członkiem guildy. Jeśli bot nie jest na serwerze, rola NIE jest weryfikowalna i dostęp zostanie odmówiony (fail-closed).

---

## Konfiguracja Discord Developer Portal

**Redirect URI:** `http://127.0.0.1:53124/susmodder/callback`
**Scopes:** `identify` + `guilds`
**Auth method:** PKCE (bez Client Secret)

Discord NIE wspiera zmiennych portów w redirect URI — port musi być stały i zarejestrowany.

---

## Pliki źródłowe

| Plik | Opis |
|------|------|
| `clair-hub/routes/susmodder.js` | 3 publiczne endpointy dla SUSModder |
| `clair-hub/routes/among-us.js` (L.1461-1510) | Zarządzanie rolami w panelu admina |
| `clair-bot/migrations/288_sustats_access_roles_and_audit.sql` | Schemat bazy danych |
| `clair-hub/app.js` (L.422) | Rejestracja routera susmodder |
| `clair-hub/middleware/csrf.js` (L.45) | Wyłączenie CSRF dla `/api/susmodder/*` |
| `clair-hub-frontend/.../AuSustats.svelte` | UI zarządzania rolami |
| `clair-hub-frontend/.../hub-api.js` (L.1658-1669) | Funkcje API frontendu |
