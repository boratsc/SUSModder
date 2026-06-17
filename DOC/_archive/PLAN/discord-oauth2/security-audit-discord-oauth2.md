# Security Audit: Discord OAuth2 PKCE — SUSModder Desktop App

**Date:** 2026-05-27
**Auditor:** `sus-security-auditor` (apollo/DeepSeek V4 Pro)
**Scope:** Pre-implementation review of `DOC/POC/Discord Oauth - Clair/2026-05-27-discord-oauth2-sustats-auth.md` and `sustats-discord-oauth2-endpoints.md`
**Codebase:** SUSModder (C#/.NET 10, Avalonia, SQLite)
**Overall Risk:** 🔶 **MEDIUM** (with 2 high-severity findings requiring pre-implementation resolution)

---

## Executive Summary

The plan introduces Discord OAuth2 PKCE authentication to replace the existing 3-hop token flow. The architecture is sound in principle — PKCE avoids secrets in binaries, DPAPI provides hardware-backed encryption on Windows, and Clair API provides server-side rate limiting with audit logging.

However, two **high-severity issues** must be resolved before implementation begins:
1. The existing codebase has an **HTTPS→HTTP fallback pattern** (in `ModConfigHandler.cs`) that would catastrophically expose Discord access tokens if replicated.
2. The **AES-GCM Linux fallback** using `machine-id` for key derivation has **no recovery path** when machine-id changes (common on Debian, Docker, VM clones).

---

## Finding Summary

| # | Severity | Area | Title |
|---|----------|------|-------|
| H1 | 🔴 HIGH | API Communication | Existing HTTPS→HTTP fallback pattern must NOT propagate to new code |
| H2 | 🔴 HIGH | Token Storage | Linux AES-GCM key derivation from machine-id has no recovery path |
| M1 | 🟠 MEDIUM | OAuth Flow | Missing `state` parameter for CSRF protection |
| M2 | 🟠 MEDIUM | API Communication | Discord access token sent in POST body, not Authorization header |
| M3 | 🟠 MEDIUM | Token Storage | No DPAPI failure fallback strategy defined |
| M4 | 🟠 MEDIUM | Audit Logging | Existing logging patterns lack token redaction enforcement |
| M5 | 🟠 MEDIUM | Token Storage | Plaintext tokens in memory — no zeroing after use |
| L1 | 🟡 LOW | OAuth Flow | Redirect URI uses `localhost` instead of `127.0.0.1` |
| L2 | 🟡 LOW | Input Validation | Guild ID validation not specified for SQL storage |
| L3 | 🟡 LOW | Session Mgmt | No explicit timeout for loopback listener |
| L4 | 🟡 LOW | Compliance | GDPR implications of storing Discord user ID |
| L5 | 🟡 LOW | Token Refresh | No automated revocation on logout by default |

---

## 🔴 H1: HTTPS→HTTP Fallback Pattern — DO NOT REPLICATE

**File:** `SUSModder.Core/Configuration/ModConfigHandler.cs` (lines 289–312, 481–504)
**Status:** Exists TODAY in the codebase; at risk of being copied into new code.

### Finding

`ModConfigHandler.cs` implements an **automatic HTTPS→HTTP fallback** on SSL errors:

```csharp
catch (HttpRequestException ex) when (ex.Message.Contains("SSL connection could not be established"))
{
    if (isHttps)
    {
        string httpUrl = serverUrl.Replace("https://", "http://");
        // then sends the SAME request over plain HTTP...
        // THIS INCLUDES THE AUTHORIZATION HEADER WITH SecretProvider.GetDownloadToken()
    }
}
```

This means:
- The authorization token (`SecretProvider.GetDownloadToken()`) is sent **in cleartext** over HTTP whenever the HTTPS connection fails.
- A MITM attacker can deliberately cause SSL failures (e.g., blocking port 443) to force the fallback and capture the token.
- The token is currently used for `susmodder-api` access but can also be used to impersonate the app for config uploads/downloads.

### Risk to New Code

The plan introduces `ClairDiscordService` (section 6.6) which sends **Discord access tokens** to Clair API. If this service replicates the existing HTTP fallback pattern, Discord access tokens would be transmitted over plain HTTP, allowing:

- Full account takeover through the Discord access token (`identify` + `guilds` scopes).
- Access to any SUStats server the user has access to.
- Exposure of `sustats_tokens` credentials from Clair API.

The Discord token is a **bearer token** — whoever holds it can impersonate the user on Discord APIs.

### Remediation

**BLOCKER — Must fix before implementing `ClairDiscordService`:**

1. **New code rule:** All `ClairDiscordService` HTTP clients **MUST** use an `HttpClient` configured with certificate validation enabled and **ZERO tolerance** for SSL errors. No automatic HTTP fallback.

2. **Code contract for `ClairDiscordService`:**
   ```csharp
   // NEVER do this in ClairDiscordService:
   // client.GetAsync(httpUrl);  // ← forbidden
   
   // ALWAYS enforce HTTPS:
   private static readonly HttpClient _clairClient = new HttpClient(new HttpClientHandler
   {
       // Default: ServerCertificateCustomValidationCallback = null (strict validation)
       // Default: SslProtocols = SslProtocols.None (system defaults)
       // EXPLICITLY DO NOT override these
   })
   {
       BaseAddress = new Uri("https://clairbot.app"),  // HTTPS only
       Timeout = TimeSpan.FromSeconds(30)
   };
   ```

3. **Fix existing vulnerability in `ModConfigHandler.cs`:**
   - Remove the HTTPS→HTTP fallback (lines 289–312, 481–504).
   - Report SSL errors to the user with a clear message (e.g., "Cannot connect securely to server. Please check your internet connection and firewall settings.").

4. **Enforce at architecture level:** Add a lint rule or code review checklist item: "No new code may downgrade HTTPS to HTTP. Audit all `catch (HttpRequestException ex) when (ex.Message.Contains("SSL"))` patterns."

---

## 🔴 H2: Linux AES-GCM — No Recovery on machine-id Change

**Section:** Implementation plan, section 6.2
**Status:** Plan design issue.

### Finding

Plan specifies (section 6.2, line 210):
> **AES-GCM na Linux** — klucz pochodny z `machine-id` + aplikacyjny salt. Akceptowalne dla desktop app.

The problem: `/etc/machine-id` can change:

| Scenario | Effect |
|----------|--------|
| Debian/Ubuntu: machine-id missing at boot | New ID is **generated on boot**. Old tokens irrecoverably lost. |
| Docker container restart | New machine-id assigned. |
| VM cloning / snapshot restore | machine-id may be regenerated. |
| Distribution upgrade (e.g., Fedora → Fedora next) | machine-id may change. |
| Dual-boot with shared `/home` | Different machine-ids per OS. |

When this happens:
- All encrypted Discord tokens and SUStats credentials become **permanently unrecoverable**.
- User must re-authenticate via Discord OAuth and re-select their SUStats server.
- There is **no recovery path** and **no user-facing warning** in the plan.

### Remediation

**BLOCKER — Must design a recovery path before implementation:**

**Option A (Recommended): Key stored in user-writable path**

Derive the AES-GCM key from a combination of:
1. `machine-id` (as a factor, not the sole input)
2. A **persistent seed** stored in `%APPDATA%/SUSModder/.crypto-seed` (generated once, random 32 bytes, file permissions `600`/user-only)

The per-user seed file survives machine-id changes while still binding the encryption to the user's home directory. This is analogous to how SSH keys work.

```csharp
// Proposed CredentialProtector key derivation (Linux path)
private static byte[] DeriveKey()
{
    var appData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SUSModder");
    var seedPath = Path.Combine(appData, ".crypto-seed");
    
    byte[] seed;
    if (!File.Exists(seedPath))
    {
        seed = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(seedPath, seed);
        // Set file permissions to user-only on Linux
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(seedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    else
    {
        seed = File.ReadAllBytes(seedPath);
    }
    
    // Combine with machine-id as salt
    byte[] machineSalt = GetMachineIdBytes();
    using var derive = new Rfc2898DeriveBytes(seed, machineSalt, 100_000, HashAlgorithmName.SHA256);
    return derive.GetBytes(32);
}
```

**Option B (Fallback if Option A rejected):**

At minimum, detect when `machine-id` changes and display a user-facing message:
> "Your encrypted credentials could not be decrypted. This can happen if your system configuration changed. Please sign in with Discord again."

The plan says "Akceptowalne dla desktop app" — this is acceptable **only if the failure mode is explicitly communicated to the user**, not a silent data loss.

**Recommendation:** Implement **Option A** (persistent seed file). The UX cost of silent credential loss on machine-id change is higher than the implementation cost.

---

## 🟠 M1: Missing `state` Parameter for CSRF Protection

**Section:** Implementation plan, section 6.5
**Status:** Plan omission.

### Finding

The PKCE flow in section 6.5 and the loopback listener in section 7.3 do not mention the `state` parameter. PKCE alone (without `state`) is vulnerable to:

1. **CSRF attack on the callback URL:** An attacker can trick the user's browser into navigating to `http://localhost:{port}/susmodder/callback?code=attacker_controlled_code`, causing SUSModder to attempt a token exchange with a code the attacker generated.
2. **Cross-protocol confusion:** Without state verification, a response from one OAuth flow could be processed by a different one.

While the attack surface is limited (the attacker needs to target the specific random port), Discord's [OAuth2 documentation](https://discord.com/developers/docs/topics/oauth2) recommends `state` as a CSRF protection measure.

### Remediation

Add a `state` parameter to the OAuth flow:

```csharp
// In DiscordOAuthService.StartLoginAsync():
var state = RandomNumberGenerator.GetHexString(32); // 64 hex chars
StoreTemporaryState(state); // in-memory dictionary with 10-minute TTL

var authUrl = $"{discordAuthEndpoint}?" +
    $"client_id={clientId}&" +
    $"redirect_uri={redirectUri}&" +
    $"response_type=code&" +
    $"scope=identify+guilds&" +
    $"code_challenge={codeChallenge}&" +
    $"code_challenge_method=S256&" +
    $"state={state}";

// In callback handler:
var returnedState = queryParams["state"];
if (!ValidateAndRemoveTemporaryState(returnedState))
{
    // Reject — possible CSRF attack
    await WriteErrorResponse("Invalid state parameter. Please try logging in again.");
    return;
}
```

This is **strongly recommended** per OAuth 2.0 best practices (RFC 6749, section 10.12).

---

## 🟠 M2: Discord Access Token in POST Body — Not in Authorization Header

**Section:** Implementation plan, sections 5.2, 5.3
**Status:** Plan design issue.

### Finding

The Clair API endpoints accept the Discord access token in the POST body:

```json
// POST /api/susmodder/guilds
{ "discord_access_token": "ya29..." }

// POST /api/susmodder/credentials
{ "discord_access_token": "ya29...", "guild_id": "1372226857294106644" }
```

Issues with this approach:

1. **Request logging:** Many web servers log request bodies (e.g., nginx `$request_body`). If Clair logs request bodies, Discord access tokens end up in server logs. By contrast, `Authorization` headers are widely recognized as sensitive and often excluded from request logging.

2. **Convention violation:** OAuth 2.0 Bearer tokens are conventionally sent in the `Authorization: Bearer <token>` header (RFC 6750). Developers, security scanners, and monitoring tools expect this. Deviation from convention increases the risk of accidental exposure.

3. **GET-vs-POST confusion:** The `/api/susmodder/config` endpoint is a GET (idempotent, cachable). The other endpoints are POST. However, sending Bearer tokens in GET request bodies is impossible per HTTP spec — using the `Authorization` header works consistently across all HTTP methods.

### Remediation

Change Clair API endpoints to accept the Discord token via `Authorization` header:

```http
POST /api/susmodder/guilds
Authorization: Bearer ya29...
Content-Type: application/json

{ "guild_id": "1372226857294106644" }  // guild_id only when needed
```

**Clair API changes required (in `clair-hub/routes/susmodder.js`):**

```javascript
// OLD: const discordToken = req.body.discord_access_token;
// NEW:
const authHeader = req.headers.authorization || '';
const discordToken = authHeader.startsWith('Bearer ') 
    ? authHeader.slice(7) 
    : req.body.discord_access_token; // fallback for backward compat
```

**SUSModder changes:**
```csharp
// ClairDiscordService
_httpClient.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", accessToken);
```

This also implicitly protects against the M1 finding (no HTTP downgrade) since `Authorization` headers should never be sent over plain HTTP.

---

## 🟠 M3: No DPAPI Failure Strategy

**Section:** Implementation plan, section 6.2
**Status:** Plan omission.

### Finding

The plan states DPAPI is used on Windows with `DataProtectionScope.CurrentUser`. DPAPI can fail in several scenarios:

| Scenario | Effect |
|----------|--------|
| Corrupted user profile | `CryptographicException` — cannot protect/unprotect |
| System account context (services) | DPAPI with `CurrentUser` may fail |
| Roaming profile / temporary profile | Profile not fully loaded |
| User password reset (local account) | May invalidate DPAPI key material |

The plan does not specify what happens when `ProtectedData.Protect()` or `ProtectedData.Unprotect()` throws. The user would experience:
- **On login:** Token cannot be saved → user gets no error but token is silently not persisted.
- **On app restart:** Token cannot be decrypted → user is asked to log in again (not catastrophic, but confusing without explanation).

### Remediation

Add explicit error handling to `CredentialProtector`:

```csharp
public static class CredentialProtector
{
    public static string Protect(string plaintext)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var plainBytes = Encoding.UTF8.GetBytes(plaintext);
                var encrypted = ProtectedData.Protect(plainBytes, 
                    s_entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            else
            {
                return AesGcmProtect(plaintext); // Linux path
            }
        }
        catch (CryptographicException ex)
        {
            // Log sanitized error (no plaintext!)
            _log.Write($"[CredentialProtector] DPAPI failure: {ex.GetType().Name}. "
                + "User will need to re-authenticate.");
            throw new CredentialProtectionException(
                "Failed to protect credentials. Please try again.", ex);
        }
    }
}
```

Additionally, implement **optional entropy** (`s_additionalEntropy` in DPAPI) as an extra layer — this binds the encryption to the application, not just the user account.

---

## 🟠 M4: Logging Patterns Risk Token Exposure

**Section:** Implementation plan, section 11
**Status:** Plan partially addresses; existing codebase pattern is a risk.

### Finding

The plan states (section 11):
> "W logach: token maskowany (pierwsze 8 znaków + `...`)."

However, the existing codebase has pervasive patterns where **exception messages are logged verbatim**:

```csharp
// Pattern found in SUStatsService.cs, EpicVersionManager.cs, 
// MainWindowViewModel.GameLaunch.cs, and many others:
System.Diagnostics.Debug.WriteLine($"[SUStats] Błąd: {ex.Message}");
```

And HttpClient exceptions can include the full URL and headers in their messages. If a request with `Authorization: Bearer ya29...` fails, the exception message might contain the token.

Additionally, `ConsoleLogger.cs` redirects `Console.Out/Error` — meaning any accidental `Console.WriteLine($"token: {accessToken}")` would be captured in the console log window visible to the user.

### Remediation

1. **Create a `TokenSanitizer` utility:**
   ```csharp
   public static class TokenSanitizer
   {
       public static string Mask(string token)
       {
           if (string.IsNullOrEmpty(token) || token.Length <= 8)
               return "***";
           return token[..8] + "..." + token[^4..];
       }
       
       public static string SanitizeExceptionMessage(string message)
       {
           // Redact patterns like "Bearer ya29...", "secret=...", "token=..."
           return Regex.Replace(message, 
               @"(Bearer\s+|secret[=:]\s*|token[=:]\s*)([A-Za-z0-9_\-\.]{20,})",
               m => $"{m.Groups[1].Value}{Mask(m.Groups[2].Value)}",
               RegexOptions.IgnoreCase);
       }
   }
   ```

2. **Add to code review checklist:** No `Debug.WriteLine` or `_diagnosticsOutput.Write()` call may contain raw token/secret values. Use `TokenSanitizer.Mask()`.

3. **Consider structured logging:** Instead of raw string interpolation, use a logging abstraction that automatically redacts known secret patterns.

---

## 🟠 M5: Plaintext Tokens in Memory — No Zeroing After Use

**Section:** Implementation plan, section 6.2
**Status:** Plan omission.

### Finding

The plan specifies `CredentialProtector.Protect(string plaintext)` — taking a `string` as input. In .NET, strings are **immutable** and **not securely erasable** (they live in the managed heap until GC collects them, and they can be moved/compacted but never zeroed). This means:

- After `Protect()` returns, the plaintext token still exists in managed memory.
- A memory dump (crash dump, process dump, or even swap file) could contain Discord access tokens and SUStats secrets.
- The `SecureString` type is [deprecated in .NET](https://github.com/dotnet/platform-compat/blob/master/docs/DE0001.md) and not recommended for new development.

### Remediation

For the Discord OAuth2 flow, the access token is short-lived (expires in ~7 days with refresh). For SUStats credentials (which are long-lived and grant API access), implement these mitigations:

1. **Minimize plaintext lifetime:** Decrypt only when needed (just before API call), use immediately, then let GC collect. Do not store plaintext in fields/properties.

2. **Use `byte[]` instead of `string` for `CredentialProtector`:**
   ```csharp
   public static byte[] Protect(byte[] plaintext);
   public static byte[] Unprotect(byte[] encrypted);
   
   // After use, zero the buffer:
   CryptographicOperations.ZeroMemory(decryptedBytes);
   ```

   `CryptographicOperations.ZeroMemory()` is available in .NET Core 3.0+ and ensures the buffer is zeroed before being eligible for GC.

3. **For the critical `token+secret` used in game launch:**
   - Decrypt immediately before constructing `ApiSet.ini` content.
   - Write to the file.
   - Zero the buffer.
   - Total plaintext lifetime: milliseconds.

4. **Document the residual risk** in the code (summary XML comment on `CredentialProtector`):
   ```csharp
   /// <remarks>
   /// SECURITY NOTE: Decrypted tokens may persist in managed memory until GC.
   /// The caller SHOULD minimize plaintext token lifetime and use 
   /// CryptographicOperations.ZeroMemory on byte[] buffers after use.
   /// Crash dumps and swap files may contain residual plaintext.
   /// </remarks>
   ```

---

## 🟡 L1: Redirect URI Uses `localhost` Instead of `127.0.0.1`

**Section:** Implementation plan, section 4, line 68

### Finding

The plan specifies:
> `http://localhost:{port}/susmodder/callback`

Discord's OAuth2 documentation [recommends using `127.0.0.1` instead of `localhost`](https://discord.com/developers/docs/topics/oauth2#authorization-code-grant) for desktop loopback redirects. The reason:

- `localhost` resolves via DNS (hosts file, then DNS server) and is **theoretically** vulnerable to DNS rebinding attacks.
- `127.0.0.1` is a hardcoded loopback address that cannot be redirected by a DNS server.

In practice, the risk is low (the attacker needs to compromise the user's DNS or hosts file), but the Discord recommendation should be followed.

### Remediation

Change the redirect URI to use `127.0.0.1`:

```
http://127.0.0.1:{port}/susmodder/callback
```

This also needs to be updated in the Discord Developer Portal for the Clair application.

---

## 🟡 L2: Guild ID Validation Before SQL Storage

**Section:** Implementation plan, sections 5.3, 6.4

### Finding

The `/api/susmodder/credentials` endpoint returns a `guild_id` that is stored in the `sustats_credentials` table (PRIMARY KEY). The plan does not specify input validation on the guild_id before storage.

- Discord guild IDs are [Snowflake IDs](https://discord.com/developers/docs/reference#snowflakes) — 17-20 digit integers as strings.
- A non-numeric or oddly formatted value could indicate a compromised Clair API response or injection attempt.
- SQLite is parameterized (per SUSModder data layer rules), so SQL injection is not a concern, but data integrity is.

### Remediation

Add validation:

```csharp
private static readonly Regex DiscordSnowflake = new(@"^\d{17,20}$");

public async Task SaveAsync(SustatsCredentials creds)
{
    if (!DiscordSnowflake.IsMatch(creds.GuildId))
        throw new ArgumentException($"Invalid guild_id format: {creds.GuildId}");
    
    // ... proceed with save
}
```

---

## 🟡 L3: Loopback Listener — No Timeout or Maximum Requests

**Section:** Implementation plan, section 7.3

### Finding

The `OAuthLoopbackListener` plan says:
> "Nasłuchuje GET http://localhost:{port}/susmodder/callback?code=..."

No timeout or maximum request count is specified. If the user abandons the OAuth flow (closes the browser without completing), the listener will run indefinitely, consuming a port and a thread.

Potential attacks:
- A local process continuously sending requests to the port could trigger multiple `CodeReceived` events (no deduplication in the plan).
- Port exhaustion if multiple OAuth flows are started without proper cleanup.

### Remediation

Add constraints:

```csharp
public class OAuthLoopbackListener : IDisposable
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromMinutes(5));
    private int _callbackCount;
    
    public async Task StartAsync(int port)
    {
        // Only accept ONE callback
        while (!_cts.Token.IsCancellationRequested)
        {
            var ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            if (Interlocked.Increment(ref _callbackCount) > 1)
            {
                // Already processed a callback — ignore additional requests
                await WriteErrorResponse(ctx.Response, 400);
                continue;
            }
            // Process callback...
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
```

Also ensure the `Dispose()` is called on timeout, user cancellation, or application exit (e.g., in a `using` block or via `IDisposable` pattern).

---

## 🟡 L4: GDPR — Discord User ID as Personal Data

**Section:** Implementation plan, section 6.1 (SQLite schema)

### Finding

The `discord_auth` table stores:
- `discord_user_id` — directly identifies a natural person (Article 4(1) GDPR)
- `discord_username` — directly identifies a natural person

Under GDPR, storing personal data requires:
1. **Lawful basis** (Article 6) — likely "legitimate interest" for authentication, but must be documented.
2. **Transparency** (Article 12-14) — users must be informed what data is collected and why.
3. **Data minimization** (Article 5(1)(c)) — is `discord_username` necessary to store persistently? It's used for UI display ("Zalogowano jako Boracik#1234") but could be fetched fresh from Discord API each time instead of persisted.
4. **Right to erasure** (Article 17) — logout should erase `discord_user_id` and `discord_username` from the database.

### Remediation

1. **Update privacy policy / ToS** to include Discord OAuth data collection.
2. **Consider not persisting `discord_username`** — fetch it from Discord API on app start and cache in-memory only.
3. **Ensure `LogoutAsync()` clears all personally identifiable fields:**
   ```sql
   DELETE FROM discord_auth;           -- removes discord_user_id, discord_username
   DELETE FROM sustats_credentials;     -- removes SUStats credentials
   UPDATE user_settings SET active_sustats_guild_id = NULL;
   ```
4. **Add a "Delete my data" button** in Settings (for GDPR Article 17 compliance).

The risk is **low** because:
- Data is stored locally (not on a server), so it's the user's own data on their own machine.
- The Desktop App exemption in GDPR may apply partially.
- However, **transparency** is still required — the user should know their Discord ID is stored.

---

## 🟡 L5: Token Revocation on Logout — Not Explicitly Required

**Section:** Implementation plan, section 6.5

### Finding

`DiscordOAuthService.LogoutAsync()` is described as:
> "Wylogowanie (revoke + czyści DB)"

But the plan does not specify whether `revoke` means:
- **Option A:** Revoke via Discord API (`POST /oauth2/token/revoke`) — invalidates the token server-side.
- **Option B:** Just clear local storage — token remains valid until expiry.
- **Option C:** Both.

If only Option B is implemented, a compromised access token (e.g., from a memory dump or stolen DB file) remains valid even after the user "logs out."

### Remediation

Implement both:

```csharp
public async Task LogoutAsync()
{
    var tokenInfo = await _authRepo.GetTokenInfoAsync();
    if (tokenInfo?.AccessToken != null)
    {
        try
        {
            // Revoke the access token on Discord's side
            await RevokeDiscordTokenAsync(tokenInfo.AccessToken);
        }
        catch (Exception ex)
        {
            _log.Write($"[DiscordOAuth] Token revocation failed (token may still be valid): {ex.GetType().Name}");
            // Continue with local cleanup — don't block logout on revocation failure
        }
    }
    
    // Clear local storage
    await _authRepo.ClearTokenAsync();
    await _credsRepo.DeleteAllAsync(); // Delete all SUStats credentials
    _userSettingsService.SetActiveGuildId(null);
}
```

---

## Positive Findings (Working As Designed)

The following aspects of the plan are **well-designed** from a security perspective:

| Aspect | Why It's Good |
|--------|---------------|
| ✅ PKCE with S256 | No client secret in binary. Code challenge prevents authorization code interception. |
| ✅ DPAPI with `CurrentUser` scope | Other users on the same machine cannot decrypt tokens. |
| ✅ Clair API rate limiting (30/min guilds, 5/h credentials) | Prevents brute-force attacks on the credentials endpoint. |
| ✅ Clair API audit logging (`sustats_audit_log`) | Every credential request is logged server-side for forensic analysis. |
| ✅ Redis fail-closed on rate limiting | If Redis is down, rate-limited endpoints return 503 (deny by default). |
| ✅ Velopack updater from stable paths | New tables in `%APPDATA%`, not in app directory. Survives updates. |
| ✅ No migration of old SUStats data | Starts clean with Discord OAuth. No legacy secrets exposed. |
| ✅ i18n plan includes session expired message | User gets clear "session expired" message in their language. |
| ✅ Telemetry sends only `discord_auth_enabled: true/false` | No user/guild identifiers in telemetry. |
| ✅ Loopback listener on random port | Reduces predictable port scanning. |
| ✅ Code verifier stored in-memory only | Per PKCE spec, the verifier never touches disk. |

---

## Pre-existing Codebase Issues (Out of Scope but Related)

These issues exist in the current codebase and affect the overall security posture. They are **not introduced by the new plan** but should be tracked for future remediation:

| Issue | File | Risk |
|-------|------|------|
| `SecretProvider` uses Base64 "encryption" (trivial to reverse) | `SUSModder.Core/Secrets.cs` | All API tokens and 7z passwords are trivially extractable from the binary. |
| `ModConfigHandler` HTTPS→HTTP fallback with auth token | `ModConfigHandler.cs:289-312` | Authorization token sent over plain HTTP. |
| Static `HttpClient` with `DefaultRequestHeaders` mutated | Multiple files | Race condition risk. Prefer `HttpClientFactory` or per-request headers. |
| `appsettings.json` contains API secrets in plaintext | `SUSModder/appsettings.json` | The `appsettings.json` is copied to output. Contains API endpoint URLs (not secrets directly, but the BaseUrl is critical). |
| No certificate pinning for susmodder.app or clairbot.app | Multiple | MITM risk if a CA is compromised. Low priority for a mod manager but should be documented. |

---

## Compliance Note

### GDPR

The plan introduces storage of **Discord User ID** (personal data under Article 4(1) GDPR). While data is stored locally (not transmitted to SUSModder servers), GDPR transparency requirements still apply.

**Required actions:**
1. Update the app's privacy notice to disclose Discord User ID collection.
2. Ensure logout deletes all personally identifiable data.
3. Document the lawful basis for processing (legitimate interest — user authentication).

### Discord API Terms

The plan uses Discord's OAuth2 API properly:
- ✅ PKCE for public clients (required by Discord for apps that cannot keep a client secret).
- ✅ Loopback redirect URI (allowed by Discord for desktop apps).
- ⚠️ `localhost` vs `127.0.0.1` — change to `127.0.0.1` per Discord recommendation (see L1).
- ✅ No request to store Discord tokens on a server (tokens stay on the user's machine).

---

## Implementation Checklist (Security Gate)

Before starting implementation, resolve these items:

### Gate 1 — Must Resolve (Blockers)

- [ ] **H1:** Create code contract for `ClairDiscordService` — no HTTP fallback. Fix existing `ModConfigHandler.cs`.
- [ ] **H2:** Implement persistent seed file for Linux AES-GCM key derivation (Option A) or, at minimum, user-visible error on failure (Option B).

### Gate 2 — Should Resolve (Before Merge)

- [ ] **M1:** Add `state` parameter to PKCE flow.
- [ ] **M2:** Switch Discord token to `Authorization: Bearer` header (coordinated with Clair API).
- [ ] **M3:** Add `try/catch` with user-visible error in `CredentialProtector`.
- [ ] **M4:** Implement `TokenSanitizer` utility. Add code review rule against logging raw tokens.
- [ ] **M5:** Use `byte[]` + `CryptographicOperations.ZeroMemory()` in `CredentialProtector`.

### Gate 3 — Should Document or Track

- [ ] **L1:** Change redirect URI from `localhost` to `127.0.0.1` (with Discord Developer Portal update).
- [ ] **L2:** Add Snowflake validation for `guild_id`.
- [ ] **L3:** Add 5-minute timeout and single-callback limit to `OAuthLoopbackListener`.
- [ ] **L4:** Update privacy policy. Ensure `LogoutAsync()` clears PII from SQLite.
- [ ] **L5:** Implement Discord token revocation in `LogoutAsync()`.

---

## Overall Risk Assessment

| Factor | Assessment |
|--------|------------|
| **Architecture quality** | ✅ Good — PKCE, DPAPI, server-side rate limiting, audit logging |
| **Pre-existing codebase risk** | ⚠️ Medium — HTTPS→HTTP fallback pattern threatens new code |
| **Linux token recovery** | 🔴 Must fix before Linux support ships |
| **Token exposure surface** | ⚠️ Medium — logging patterns, memory, POST body |
| **Compliance** | ⚠️ Low risk — local-only data, but GDPR transparency needed |
| **Overall** | 🟠 **MEDIUM** |

**Decision:** The plan is **APPROVED with conditions**. Implementation may begin after **H1** and **H2** are resolved. **M1–M5** should be resolved before code merge. **L1–L5** should be tracked as issues.

---

## Sources Used

- **Local files:** `DOC/POC/Discord Oauth - Clair/2026-05-27-discord-oauth2-sustats-auth.md`, `DOC/POC/Discord Oauth - Clair/sustats-discord-oauth2-endpoints.md`, `SUSModder.Core/Secrets.cs`, `SUSModder.Core/Configuration/ModConfigHandler.cs`, `SUSModder.Core/Configuration/SUStatsService.cs`, `SUSModder.Core/Data/DatabaseService.cs`, `SUSModder.Core/Services/TelemetryService.cs`, `SUSModder/Services/ConsoleLogger.cs`, `SUSModder/appsettings.json`, `*.csproj`
- **Microsoft Learn:** `ProtectedData` class documentation, DPAPI usage patterns, `CryptographicOperations.ZeroMemory`, `PlatformNotSupportedException` for non-Windows DPAPI
- **NuGet:** Package version audit (Velopack 0.0.1298 → 1.0.1 upgrade available, `Newtonsoft.Json` 13.0.4 — no critical CVEs in this version)
- **MCP-RAG:** SUSModder codebase search for existing auth patterns, logging, SQLite migration, API communication patterns
- **MCP-Obsidian:** No relevant notes found for this audit scope
