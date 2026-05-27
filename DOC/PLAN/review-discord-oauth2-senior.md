# Senior Quality Review: Discord OAuth2 PKCE Implementation Plan

**Reviewer:** sus-senior-quality-reviewer (GLM-5.1)  
**Date:** 2026-05-27  
**Plan reviewed:** `DOC/POC/Discord Oauth - Clair/2026-05-27-discord-oauth2-sustats-auth.md`  
**Endpoints doc:** `DOC/POC/Discord Oauth - Clair/sustats-discord-oauth2-endpoints.md`  
**Status:** 📋 Plan stage (pre-implementation)

---

## Executive Summary

The plan replaces the current manual secret-copy flow (Clair Hub → susmodder-api → SUSModder) with a Discord OAuth2 PKCE flow where SUSModder authenticates directly with Discord, then exchanges the Discord access token for SUStats credentials via Clair API. This is a significant security and UX improvement. However, there are several blocking issues and important recommendations that must be addressed before implementation begins.

**Go/No-Go: CONDITIONAL GO** — proceed after fixing the 3 blocking issues below.

---

## 🔴 BLOCKING ISSUES (must fix before implementation)

### B-1. Discord Redirect URI Port Conflict — Fixed vs. Random Port

**Severity:** HIGH  
**Files:** Plan §4, endpoints doc §"Konfiguracja Discord Developer Portal"

The plan states (§4): *"Redirect URI: `http://localhost:{port}/susmodder/callback` — loopback, port z zakresu 49152-65535"* (random port).

But the endpoints doc states: *"Discord NIE wspiera zmiennych portów w redirect URI — port musi być stały i zarejestrowany."* and specifies a **fixed port**: `http://127.0.0.1:53124/susmodder/callback`.

**This is a direct contradiction.** Discord OAuth2 requires the exact redirect URI (including port) to be pre-registered in the Developer Portal. You cannot use a random port with Discord PKCE.

**Required fix:**
- Use a **fixed port** (e.g., 53124) for the loopback listener.
- Register `http://127.0.0.1:53124/susmodder/callback` in Discord Developer Portal.
- Handle the case where port 53124 is already in use (show clear error, offer retry).
- Update the plan to remove the "random port" language and specify the fixed port.

**Risk if unfixed:** OAuth flow will fail at the redirect step — Discord will reject the callback URL because it doesn't match the registered redirect URI.

---

### B-2. DPAPI Is Windows-Only — Plan Claims Cross-Platform Support

**Severity:** HIGH  
**Files:** Plan §6.2 `CredentialProtector`, §13

The plan states: *"DPAPI na Windows — klucz związany z kontem użytkownika. Inny user = nie odszyfruje."* and *"Linux: AES-256-GCM z kluczem pochodzącym z machine-id + salt."*

**Microsoft documentation confirms:** `System.Security.Cryptography.ProtectedData` (DPAPI) throws `PlatformNotSupportedException` on non-Windows platforms. The plan acknowledges this with the AES-GCM fallback, but:

1. **The plan targets .NET 10** (per §1 header). SUSModder currently targets .NET 8.0 (per CLAUDE.md). This needs clarification.
2. **AES-GCM with machine-id is NOT equivalent security to DPAPI.** Machine-id on Linux (`/etc/machine-id`) is world-readable. Any process on the machine can derive the same key. This means:
   - On Windows: tokens are bound to the user account (DPAPI `CurrentUser` scope).
   - On Linux: tokens are effectively bound to the machine, not the user. Any user on the same machine can decrypt.
3. **The plan says "w przyszłości, gdy będzie wsparcie Linux"** (§13) for the AES-GCM path, but the `CredentialProtector` interface is defined as if both paths are implemented now.

**Required fix:**
- Clarify: is Linux support in MVP scope or not?
- If NOT in MVP (Windows-only for now): remove the AES-GCM path from the implementation, add a clear `PlatformNotSupportedException` for non-Windows, and document this as a known limitation.
- If IN MVP: the AES-GCM key derivation must NOT use `/etc/machine-id` alone. Use a user-specific secret store (e.g., keyring/keyctl, or a per-user encrypted file with a passphrase-derived key). At minimum, acknowledge the reduced security posture in the plan.
- Add `[SupportedOSPlatform("windows")]` attribute to the DPAPI code path (matching the existing pattern in `TelemetryService.cs`).

---

### B-3. SQLite Migration v2 — Missing Transaction and Rollback Strategy

**Severity:** HIGH  
**Files:** Plan §12.1, existing `DatabaseService.cs`

The plan proposes migration v2 that:
1. Creates `discord_auth` table
2. Creates `sustats_credentials` table
3. `ALTER TABLE user_settings ADD COLUMN active_sustats_guild_id`

**Problems:**

1. **No transaction wrapping.** The existing `ApplyMigrations()` method (line 210-232 of `DatabaseService.cs`) runs migrations without an explicit transaction. SQLite DDL statements (`CREATE TABLE`, `ALTER TABLE`) are implicitly transactional for single statements, but if the migration fails between the two `CREATE TABLE` statements, you end up with a partial schema and `user_version` not yet set to 2. On next startup, `ApplyMigrations` will try to run the same migration again, and `CREATE TABLE IF NOT EXISTS` will succeed for the first table (already exists), but the `ALTER TABLE` may fail if the column already exists from a partial run.

2. **No rollback strategy.** The existing `BackupDatabase()` method creates a `.bak` file, but there is no automatic restore on migration failure. If migration v2 fails halfway, the user's database is in an inconsistent state with no recovery path.

3. **`ALTER TABLE` is not idempotent.** Unlike `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ADD COLUMN` will fail if the column already exists. If the migration is re-run after a partial failure, this will crash.

**Required fix:**
- Wrap the entire v2 migration in an explicit SQLite transaction (`BEGIN TRANSACTION` / `COMMIT`).
- Add `IF NOT EXISTS` checks or use `try/catch` for the `ALTER TABLE` statement (SQLite doesn't support `IF NOT EXISTS` for `ALTER TABLE ADD COLUMN` — use a pragma check: `SELECT COUNT(*) FROM pragma_table_info('user_settings') WHERE name='active_sustats_guild_id'`).
- Add a rollback mechanism: if migration fails, restore from `.bak` and log the error.
- Add integration test for: (a) fresh install, (b) upgrade from v1, (c) re-run after partial failure.

---

## 🟡 NON-BLOCKING RECOMMENDATIONS

### N-1. PKCE code_verifier Storage and Lifecycle

**Severity:** MEDIUM  
**Files:** Plan §6.5 `DiscordOAuthService`

The plan says PKCE is used but doesn't specify:
- How long the `code_verifier` is held in memory
- Whether it's zeroed after use
- What happens if the app crashes between generating the verifier and completing the exchange

**Recommendation:**
- Store `code_verifier` in a private field of `DiscordOAuthService`, zero it with `CryptographicOperations.Zero()` after the token exchange.
- If the app crashes, the verifier is lost (acceptable — user just re-authenticates).
- Add a timeout (e.g., 10 minutes) after which the pending OAuth state is discarded.

---

### N-2. HttpListener Security on localhost

**Severity:** MEDIUM  
**Files:** Plan §7.3 `OAuthLoopbackListener`

The plan uses `http://localhost:{port}/susmodder/callback` (or `http://127.0.0.1:53124/susmodder/callback` per the endpoints doc).

**Security considerations:**
- **Localhost MITM is not a practical concern** for this use case. RFC 8252 §8.3 explicitly allows loopback redirect URIs for native apps. The PKCE `code_challenge` prevents authorization code interception even if a local process captures the redirect.
- **Port hijacking is a concern.** A malicious local process could bind to port 53124 before SUSModder starts, intercepting the OAuth callback. However, PKCE mitigates this — the attacker cannot exchange the code without the `code_verifier`.
- **Recommendation:** Bind to `127.0.0.1` (not `0.0.0.0` or `localhost`). The endpoints doc correctly uses `127.0.0.1`. Ensure the implementation matches.
- **Recommendation:** Validate the `state` parameter in the callback to prevent CSRF. The plan doesn't mention `state` parameter generation/validation, which is critical for preventing CSRF attacks in the OAuth flow.

---

### N-3. Missing CSRF Protection (state Parameter)

**Severity:** MEDIUM  
**Files:** Plan §6.5 `DiscordOAuthService`

The plan describes PKCE but does not mention the `state` parameter. Per OAuth2 best practices (RFC 6749 §10.12), the `state` parameter is **required** to prevent CSRF attacks, even with PKCE.

**Without `state`:** An attacker could craft a malicious link that pre-authenticates the victim's SUSModder with the attacker's Discord account, causing the victim's game stats to be sent to the attacker's server.

**Recommendation:**
- Generate a cryptographically random `state` value (at least 128 bits).
- Store it alongside the `code_verifier` during the OAuth flow.
- Validate `state` in the callback before exchanging the code.
- Discard both `state` and `code_verifier` after use.

---

### N-4. Discord Access Token Sent to Clair API — Security Model

**Severity:** MEDIUM  
**Files:** Plan §3, §5.2, §5.3

The plan sends the Discord `access_token` directly to Clair API endpoints (`POST /api/susmodder/guilds` and `POST /api/susmodder/credentials`). This means:
- The Clair backend sees the user's Discord access token.
- The token is transmitted over HTTPS (good), but it's still a credential exposure surface.

**This is acceptable for the current architecture** (Clair is the trusted backend), but:
- The Discord access token should be used only for the immediate API call and not stored by Clair.
- Clair should not log the access token.
- The plan should explicitly state that Clair must not persist the Discord access token.

**Recommendation:** Add a security note to the Clair endpoint documentation: "Discord access tokens are validated and immediately discarded. They are never logged or stored."

---

### N-5. Token Refresh and Expiry Handling

**Severity:** MEDIUM  
**Files:** Plan §6.5 `DiscordOAuthService`

The plan mentions `RefreshTokenAsync()` and automatic refresh before API calls, but doesn't specify:
- Discord access tokens expire after **7 days** (for bot applications) or **1 week** by default. What happens if the token expires mid-session?
- What if the refresh token is also expired or revoked? (Discord invalidates refresh tokens after the user changes password or revokes access.)
- The `discord_auth` table stores `expires_at` as ISO 8601 text — is this compared with wall clock time or is there a safety margin?

**Recommendation:**
- Add a 5-minute safety margin before token expiry (refresh when `expires_at - 5min < now`).
- On refresh failure (401 from Discord), clear the stored token and show the "Session expired" UI (key `DiscordAuth.SessionExpired` already exists in the i18n table — good).
- On game launch, if the token cannot be refreshed, fall back to showing the "not logged in" state rather than crashing or silently failing.

---

### N-6. Rate Limiting — Discord API 429 Handling

**Severity:** LOW  
**Files:** Plan §6.5, §6.6

The Clair endpoints doc specifies rate limits (30/min for guilds, 5/hour for credentials), but the plan doesn't mention handling Discord's own rate limits (429 responses from Discord API when validating tokens).

**Recommendation:**
- Add `Retry-After` header parsing for Discord 429 responses.
- Implement exponential backoff for Discord API calls.
- Show user-facing message: "Too many requests, please try again in a moment" (add i18n key).

---

### N-7. Error Handling During OAuth Flow — Network Failures

**Severity:** MEDIUM  
**Files:** Plan §6.5, §7.3

The plan doesn't specify error handling for:
1. **Browser fails to open** (no default browser, sandboxed environment).
2. **User closes browser without completing auth** (no callback received).
3. **Network failure during token exchange** (Discord API unreachable).
4. **Clair API unreachable** during guild/credentials fetch.

**Recommendation:**
- Add a timeout for the loopback listener (e.g., 5 minutes). If no callback received, show "Login timed out" message.
- For browser open failure, fall back to showing the URL for manual copy.
- For network failures during token exchange, show a clear error with retry option.
- Add i18n keys for all error states.

---

### N-8. Backward Compatibility — Existing SUStats Users

**Severity:** MEDIUM  
**Files:** Plan §9, §12.2

The plan states that existing users with old SUStats config (manually entered secrets) will NOT be migrated and must go through the new Discord OAuth flow. This is a **breaking change** for existing users.

**Current flow:** `SUStatsConfigViewModel` allows manual secret entry → `ValidateServerBySecretAsync()` → `AmongToken` model.  
**New flow:** Discord OAuth → `ISustatsCredentialsRepository.GetActiveAsync()`.

**Concerns:**
- Users who currently have working SUStats configs will lose their setup after update.
- The plan says `/api/among-tokens` endpoint "stays for backward compat" (§9), but the client code will no longer call it.

**Recommendation:**
- Provide a **grace period migration path**: if `user_settings` has an existing SUStats config (from the old flow), keep it working for one version cycle while showing a banner: "Switch to Discord login for a better experience."
- Alternatively, add a one-time import: read the old secret from `SUStatsConfigViewModel`, call `/api/among-tokens` one last time, and store the result in `sustats_credentials` via the new repository.
- At minimum, document the breaking change clearly in release notes.

---

### N-9. i18n Coverage

**Severity:** LOW  
**Files:** Plan §10

The plan provides 9 i18n keys with PL and EN translations. This is a good start, but:

**Missing keys:**
- Error states for OAuth flow (browser failed to open, network error, timeout)
- Rate limit messages
- Token refresh failure
- Guild fetch loading/error states
- "Connecting to Discord..." / "Authenticating..."
- Success confirmation after guild selection

**Placeholder consistency:** The keys use `{0}` format (C# string.Format style). Ensure this is consistent with the existing localization system (check if it uses `{0}` or `{name}` style).

**Recommendation:** Add at least 10-15 more i18n keys for error and loading states before implementation begins.

---

### N-10. Telemetry and Privacy

**Severity:** LOW  
**Files:** Plan §11

The plan states:
- Discord Access Token never leaves the machine (only exchanged with Discord API and Clair API).
- Tokens are masked in logs (first 8 chars + `...`).
- Telemetry event: `discord_auth_enabled: true/false` (no user/guild ID).

**This is good.** However:

1. **`discord_user_id` and `discord_username` are stored in `discord_auth` table.** These are personal data under GDPR. The plan should specify:
   - How users can delete their Discord data (right to erasure).
   - Whether `discord_user_id` is sent in any telemetry events (plan says no — verify during implementation).
   
2. **`sustats_credentials` stores `token_enc` and `secret_enc`.** These are SUStats credentials. Under GDPR, these are also personal data (they identify the user's game server).

3. **The existing telemetry system** (`TelemetryService.cs`) sends `userHash` (SHA256 of Hardware ID). The plan should confirm that no new telemetry events include Discord user IDs or guild IDs.

**Recommendation:** Add a GDPR deletion endpoint or mechanism: when a user logs out, clear all rows from `discord_auth` and `sustats_credentials`. Document this in the privacy policy.

---

### N-11. appsettings.json — Read-Only Constraint

**Severity:** LOW  
**Files:** Plan §8, existing `appsettings.json`

The plan adds new keys to `appsettings.json` (`ClairApiBaseUrl`, `ClairApiSusmodderEndpoint`, `SusmodderApiBaseUrl`). Per the data layer instructions: *"appsettings.json is strictly read-only (API endpoints, default paths)."*

**This is fine** — these are API endpoint URLs, which is exactly what `appsettings.json` is for. No issue here, just confirming alignment with existing patterns.

---

### N-12. `SecretProvider.GetDownloadToken()` — Still Used for Old Flow

**Severity:** LOW  
**Files:** Existing `SUStatsService.cs`, `Secrets.cs`

The current `SUStatsService` uses `SecretProvider.GetDownloadToken()` (Base64 obfuscation) to authenticate with `/api/among-tokens`. The plan doesn't remove this — it's kept for backward compat.

**However**, the plan's new flow sends the Discord access token directly to Clair API, which uses a completely different auth mechanism. This means:
- `SecretProvider.GetDownloadToken()` is still needed for the old `/api/among-tokens` endpoint.
- The new flow doesn't need it at all.

**No action needed** — just confirming that `Secrets.cs` should not be removed during this implementation.

---

## 🔵 SECURITY FINDINGS SUMMARY

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| B-1 | Discord redirect URI: random port vs. fixed port contradiction | HIGH | 🔴 Blocking |
| B-2 | DPAPI Windows-only; AES-GCM Linux key derivation weak | HIGH | 🔴 Blocking |
| B-3 | SQLite migration v2: no transaction, no rollback, non-idempotent ALTER | HIGH | 🔴 Blocking |
| N-2 | HttpListener should bind to 127.0.0.1, not 0.0.0.0 | MEDIUM | 🟡 Recommendation |
| N-3 | Missing CSRF `state` parameter in OAuth flow | MEDIUM | 🟡 Recommendation |
| N-4 | Discord access token sent to Clair API — needs explicit no-persist policy | MEDIUM | 🟡 Recommendation |
| N-5 | Token refresh: no safety margin, no refresh failure UX | MEDIUM | 🟡 Recommendation |
| N-6 | Discord 429 rate limit handling not specified | LOW | 🟡 Recommendation |
| N-7 | OAuth flow error states not fully specified | MEDIUM | 🟡 Recommendation |
| N-8 | Breaking change for existing SUStats users — no migration path | MEDIUM | 🟡 Recommendation |
| N-1 | PKCE code_verifier lifecycle not specified | MEDIUM | 🟡 Recommendation |
| N-9 | i18n keys incomplete (need ~15 more) | LOW | 🟡 Recommendation |
| N-10 | GDPR deletion mechanism for Discord data not specified | LOW | 🟡 Recommendation |
| N-11 | appsettings.json additions align with existing patterns | — | ✅ OK |
| N-12 | SecretProvider still needed for old flow | — | ✅ OK |

---

## 🟢 POSITIVE OBSERVATIONS

1. **PKCE is the correct choice** for a desktop app — no client secret in binaries, which is a major improvement over the current `SecretProvider` Base64 obfuscation.
2. **DPAPI `CurrentUser` scope** is the right choice for Windows — binds encryption to the Windows user account.
3. **The plan correctly identifies** that the old `/api/among-tokens` endpoint should be preserved for backward compat.
4. **i18n keys are provided** for both PL and EN from the start.
5. **The `discord_auth` table uses a singleton pattern** (`CHECK id = 1`), matching the existing `user_settings` pattern.
6. **Audit logging on the Clair side** (`sustats_audit_log`) is a good security practice.
7. **Rate limiting on the Clair side** (5 req/hour for credentials) is appropriate.
8. **The plan correctly uses `127.0.0.1`** in the endpoints doc (not `localhost`), which avoids DNS rebinding issues.

---

## DATA INTEGRITY CONCERNS

### DI-1. SQLite Migration Atomicity

The existing `DatabaseService.ApplyMigrations()` method (line 210-232) runs migrations without explicit transactions. The v2 migration adds 2 new tables and 1 ALTER TABLE. If the ALTER TABLE fails after the tables are created, the database is in an inconsistent state with `user_version = 1` (not yet updated to 2), so the migration will be re-attempted on next startup. But `CREATE TABLE IF NOT EXISTS` will silently succeed for the already-created tables, and `ALTER TABLE ADD COLUMN` will fail with "duplicate column" error.

**Fix:** Wrap the entire v2 migration in a transaction and check for column existence before ALTER.

### DI-2. `discord_auth` Singleton Constraint

The `discord_auth` table uses `CHECK (id = 1)` to enforce a single row. This is correct and matches the `user_settings` pattern. However, the plan should specify what happens when a user logs out and logs in with a different Discord account — the existing row should be overwritten (UPSERT pattern), not inserted.

### DI-3. `sustats_credentials` — Multiple Guilds

The `sustats_credentials` table allows multiple rows (one per guild). The `user_settings.active_sustats_guild_id` column points to the active one. This is a good design. However, the plan should specify:
- What happens to credentials when a user switches guilds? (Old credentials are kept, new ones fetched — this is fine.)
- What happens when a user logs out of Discord? (All credentials should be cleared.)

---

## RESIDUAL RISKS

1. **Discord API changes:** Discord could change their OAuth2 API or rate limits. Mitigate by adding versioned API calls and monitoring for deprecation notices.
2. **Clair API availability:** If `clairbot.app` is down, the entire OAuth flow fails. The plan should add a health check or cached config for `discord_client_id`.
3. **Token storage on shared machines:** On shared Windows machines, DPAPI `CurrentUser` scope protects tokens from other users. On Linux (if implemented), the AES-GCM key derived from `machine-id` would NOT protect against other users on the same machine.
4. **First-run experience:** Users who have never used Discord OAuth will need to authenticate before using SUStats. The plan should ensure the UI clearly explains why Discord login is needed and what data is shared.

---

## GO/NO-GO RECOMMENDATION

**CONDITIONAL GO** — Proceed with implementation after addressing:

1. **B-1:** Resolve the redirect URI port contradiction. Use a fixed port (53124) as specified in the endpoints doc. Update the plan accordingly.
2. **B-2:** Decide on Linux support scope. If Windows-only MVP, remove AES-GCM path and add `PlatformNotSupportedException`. If cross-platform, redesign the Linux key derivation to be user-specific.
3. **B-3:** Add transaction wrapping, idempotent column checks, and rollback logic to the SQLite v2 migration.

**Recommended implementation order adjustment:**
- Phase 4 (CredentialProtector) should include a clear platform support decision.
- Phase 5 (SQLite migration) should include transaction wrapping and idempotent checks before any other work.
- Phase 10 (OAuthLoopbackListener) should use fixed port 53124 and add CSRF `state` parameter.

---

## Sources Used

- `DOC/POC/Discord Oauth - Clair/2026-05-27-discord-oauth2-sustats-auth.md` — full plan
- `DOC/POC/Discord Oauth - Clair/sustats-discord-oauth2-endpoints.md` — Clair API endpoints
- `SUSModder.Core/Data/DatabaseService.cs` — existing migration code
- `SUSModder.Core/Data/UserSettingsRepository.cs` — existing repository pattern
- `SUSModder.Core/Secrets.cs` — current SecretProvider (Base64 obfuscation)
- `SUSModder.Core/Configuration/SUStatsService.cs` — current SUStats auth flow
- `SUSModder.Core/Models/AmongTokensResponse.cs` — current token model
- `SUSModder.Core/Services/TelemetryService.cs` — telemetry patterns
- `SUSModder/ViewModels/SUStatsConfigViewModel.cs` — current SUStats UI
- `SUSModder/appsettings.json` — current configuration
- `SUSModder/App.axaml.cs` — DI setup
- Microsoft Learn: `System.Security.Cryptography.ProtectedData` — DPAPI is Windows-only
- Microsoft Learn: OAuth2 loopback redirect URI guidance (RFC 8252)
- CLAUDE.md — project architecture and data layer instructions
