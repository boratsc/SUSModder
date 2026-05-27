# Plan: Integracja SUSModder ↔ Discord OAuth2 — SUStats Auth

**Data:** 2026-05-27
**Status:** 📋 Plan (do akceptacji)
**Zależność:** `DOC/POC/SQLite-migration/README.md` (SQLite musi być już wdrożone)
**Stack:** .NET 10, Avalonia, SQLite (`Microsoft.Data.Sqlite`)

---

## 1. Cel i non-goals

**Cel:** Zastąpić obecny 3-hop flow (Clair → susmodder-api → SUSModder) bezpośrednią autoryzacją Discord OAuth2 w aplikacji SUSModder. Użytkownik loguje się Discordem, aplikacja pobiera listę serwerów gdzie ma uprawnienia, wybiera serwer — bez przepisywania tokenu/secretu.

**Non-goals:**
- Nie ruszamy mechanizmu wysyłania statystyk gry (token+secret nadal używane do clair-api jak obecnie).
- Nie migrujemy innych endpointów susmodder-api.
- Nie budujemy Discord Rich Presence ani komend Discorda w tej fazie.

---

## 2. Obecny flow

```
Admin Discord → Clair Hub (generuje token+secret)
                  → SUStatsSyncClient → susmodder-api (MySQL: among_tokens)
                                           → SUSModder GET /api/among-tokens
                                              → user ręcznie kopiuje secret z Clair Hub → wkleja w UI
```

**Problemy:**
1. Secret leci przez susmodder-api (dodatkowa kopia w MySQL).
2. User ręcznie kopiuje secret.
3. SUSModder pobiera wszystkie tokeny, nie tylko dla danego użytkownika.
4. `SecretProvider.GetDownloadToken()` (Base64 obfuscation) jako auth do `/api/among-tokens`.

---

## 3. Nowy flow

```
┌──────────┐       OAuth2 PKCE        ┌──────────┐
│ SUSModder│─────────────────────────▶│ Discord  │
│  (app)   │◀───────access token─────│          │
└────┬─────┘                          └──────────┘
     │
     │  POST /api/susmodder/guilds {discord_access_token}
     │─────────────────────────────────────────────▶┌───────┐
     │◀── [{guild_id, guild_name, has_sustats}]────│ Clair │
     │                                              │  API  │
     │  POST /api/susmodder/credentials             │       │
     │  {discord_access_token, guild_id}            │       │
     │─────────────────────────────────────────────▶│       │
     │◀── {token, secret, endpoint, server_name}────└───────┘
     │
     ▼ Zapis do SQLite (encrypted)
     
Przy uruchamianiu gry: używa token+secret z SQLite → Clair API (jak obecnie).
```

**OAuth flow:** PKCE (bez Client Secret w binarkach), systemowa przeglądarka, loopback `localhost:{random_port}`.

---

## 4. Discord — konfiguracja OAuth2 po stronie Clair

Używamy **istniejącej aplikacji Discord `Clair`** (nie tworzymy nowej).

**Nowy Redirect URI do dodania:**
- `http://localhost:{port}/susmodder/callback` — loopback, port z zakresu 49152-65535

**Scopes:** `identify` + `guilds`

**Client ID** — już istnieje w `.env` Clair jako `DISCORD_CLIENT_ID`. SUSModder będzie go pobierał z **nowego endpointu Clair API** (patrz sekcja 5 — `/api/susmodder/config`), żeby nie hardcodować.

---

## 5. Clair API — nowe endpointy

### 5.1 `GET /api/susmodder/config`
Zwraca publiczną konfigurację potrzebną SUSModder do OAuth:

```json
{
  "ok": true,
  "discord_client_id": "1234567890",
  "auth_endpoint": "https://clairbot.app/api/susmodder",
  "guilds_endpoint": "/guilds",
  "credentials_endpoint": "/credentials"
}
```

### 5.2 `POST /api/susmodder/guilds`

**Request:**
```json
{ "discord_access_token": "..." }
```

**Logika Clair:**
1. Waliduje token → `GET https://discord.com/api/users/@me`
2. Pobiera guildy użytkownika → `GET https://discord.com/api/users/@me/guilds`
3. Dla każdej guildy sprawdza w `sustats_tokens`: czy istnieje aktywny token (`is_active = true`)
4. Sprawdza, czy użytkownik ma dostęp:
   - **Zawsze:** właściciel serwera (`owner: true`) i admin (`permissions & 0x8` lub `ManageGuild`)
   - **Przyszłość (poza MVP):** konfigurowalne rangi (tabela `sustats_access_roles`)
5. Zwraca tylko guildy spełniające warunki.

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

### 5.3 `POST /api/susmodder/credentials`

**Request:**
```json
{ "discord_access_token": "...", "guild_id": "1372226857294106644" }
```

**Logika Clair:**
1. Waliduje token (jw.)
2. Sprawdza uprawnienia użytkownika do danej guildy
3. Pobiera aktywny token+secret z `sustats_tokens`
4. Loguje audyt (kto, kiedy, dla jakiej guildy pobrał credentials)
5. Zwraca credentials

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

**Security:** Rate limiting (max 5 żądań/godzinę na usera). Audyt log do `sustats_audit_log`.

---

## 6. SUSModder.Core — nowe komponenty

### 6.1 Schemat SQLite (rozszerzenie istniejącego)

**Nowa tabela `discord_auth`:**

```sql
CREATE TABLE discord_auth (
    id                  INTEGER PRIMARY KEY CHECK (id = 1),
    access_token_enc    TEXT    NOT NULL,   -- DPAPI encrypted
    refresh_token_enc   TEXT    NOT NULL,   -- DPAPI encrypted
    token_type          TEXT    NOT NULL DEFAULT 'Bearer',
    expires_at          TEXT    NOT NULL,   -- ISO 8601
    discord_user_id     TEXT,              -- do wyświetlenia w UI
    discord_username    TEXT,              -- do wyświetlenia w UI
    created_at          TEXT    NOT NULL DEFAULT (datetime(''now'')),
    updated_at          TEXT    NOT NULL DEFAULT (datetime(''now''))
);
```

**Nowa kolumna w `user_settings`:**

```sql
ALTER TABLE user_settings ADD COLUMN active_sustats_guild_id TEXT DEFAULT NULL;
```

**Nowa tabela `sustats_credentials`:**

```sql
CREATE TABLE sustats_credentials (
    guild_id            TEXT PRIMARY KEY,
    server_name         TEXT    NOT NULL,
    token_enc           TEXT    NOT NULL,   -- DPAPI encrypted
    secret_enc          TEXT    NOT NULL,   -- DPAPI encrypted
    endpoint            TEXT    NOT NULL,
    created_at          TEXT    NOT NULL DEFAULT (datetime(''now'')),
    updated_at          TEXT    NOT NULL DEFAULT (datetime(''now''))
);
```

### 6.2 Szyfrowanie — `CredentialProtector`

```csharp
// SUSModder.Core/Services/Discord/CredentialProtector.cs
public static class CredentialProtector
{
    public static string Protect(string plaintext);
    public static string Unprotect(string ciphertextBase64);
    // Windows: DPAPI (ProtectedData.Protect, DataProtectionScope.CurrentUser)
    // Linux:   AES-256-GCM z kluczem pochodzącym z machine-id + salt
}
```

- **DPAPI na Windows** — klucz związany z kontem użytkownika. Inny user = nie odszyfruje.
- **AES-GCM na Linux** — klucz pochodny z `machine-id` + aplikacyjny salt. Akceptowalne dla desktop app.

### 6.3 Nowe repozytorium: `IDiscordAuthRepository`

```csharp
public interface IDiscordAuthRepository
{
    Task<DiscordTokenInfo?> GetTokenInfoAsync();
    Task SaveTokenInfoAsync(DiscordTokenInfo info);
    Task ClearTokenAsync();
}
```

### 6.4 Nowe repozytorium: `ISustatsCredentialsRepository`

```csharp
public interface ISustatsCredentialsRepository
{
    Task<SustatsCredentials?> GetForGuildAsync(string guildId);
    Task SaveAsync(SustatsCredentials creds);
    Task DeleteAsync(string guildId);
    Task<SustatsCredentials?> GetActiveAsync();  // używa user_settings.active_sustats_guild_id
}
```

### 6.5 Nowy serwis: `DiscordOAuthService`

```csharp
public interface IDiscordOAuthService
{
    // Zwraca URL do otwarcia w przeglądarce
    Task<OAuthStartResult> StartLoginAsync();

    // Wymienia code na token, zapisuje w DB
    Task<OAuthCompleteResult> CompleteLoginAsync(string code, string redirectUri);

    // Sprawdza czy mamy ważny token
    Task<bool> IsLoggedInAsync();

    // Refresh tokenu
    Task<bool> RefreshTokenAsync();

    // Wylogowanie (revoke + czyści DB)
    Task LogoutAsync();

    // Pobiera nazwę usera do UI
    Task<string?> GetUsernameAsync();
}

public record OAuthStartResult(string AuthUrl, int Port);
public record OAuthCompleteResult(bool Success, string? ErrorMessage);
```

- Implementuje PKCE: generuje `code_verifier` (SHA256), buduje URL z `code_challenge`.
- Woła `GET /api/susmodder/config` z Clair po `discord_client_id`.
- Po uzyskaniu access tokenu: encrypt + save do `discord_auth`.
- Refresh token: wykrywa expiry, automatycznie refreshuje przed wywołaniami API.

### 6.6 Nowy serwis: `ClairDiscordService`

```csharp
public interface IClairDiscordService
{
    // Pobiera konfigurację OAuth z Clair
    Task<ClairOAuthConfig> GetOAuthConfigAsync();

    // Pobiera listę dostępnych guild
    Task<List<DiscordGuildInfo>> GetAccessibleGuildsAsync(string accessToken);

    // Pobiera credentials dla wybranej guildy
    Task<SustatsCredentials> GetCredentialsAsync(string accessToken, string guildId);
}
```

### 6.7 Zmiany w `SUStatsService`

- Usuwamy zależność od `susmodder-api` `/api/among-tokens` dla pobierania listy serwerów.
- Zamiast `GetSUStatsServersAsync()` → korzystamy z `ISustatsCredentialsRepository.GetActiveAsync()`.
- `ValidateServerBySecretAsync()` → do usunięcia (już nie potrzebne, credentials przychodzą z Clair API, nie od usera).

---

## 7. SUSModder UI (Avalonia) — zmiany

### 7.1 Przebudowa `SUStatsConfigView`

**Nowy flow UI:**

```
┌─────────────────────────────────────────────┐
│  SUStats Configuration                      │
│                                             │
│  [ Stan: Niezalogowany ]                    │
│  ┌──────────────────────────────────────┐   │
│  │  Aby korzystać ze statystyk,         │   │
│  │  zaloguj się przez Discord.          │   │
│  │                                      │   │
│  │  [ Zaloguj przez Discord ]           │   │
│  └──────────────────────────────────────┘   │
│                                             │
│  ═══════════ po zalogowaniu ═══════════════  │
│                                             │
│  [ Stan: Zalogowano jako Boracik#1234 ]     │
│  [ Wyloguj ]                                │
│                                             │
│  Wybierz serwer Discord:                    │
│  ┌──────────────────────────────────────┐   │
│  │ ▼ Psychopaci (SUStats aktywny)      │   │
│  │   Among Us Polska (brak SUStats)    │   │
│  └──────────────────────────────────────┘   │
│                                             │
│  Statystyki: [ ■ Włączone ]                 │
│  Zapisane dla serwera: Psychopaci SUStats   │
└─────────────────────────────────────────────┘
```

### 7.2 Nowy ViewModel: `DiscordAuthViewModel` (lub rozszerzenie `SUStatsConfigViewModel`)

```csharp
// Stany
public bool IsLoggedIn { get; set; }
public string? DiscordUsername { get; set; }
public bool IsLoadingGuilds { get; set; }
public ObservableCollection<DiscordGuildInfo> AvailableGuilds { get; }
public DiscordGuildInfo? SelectedGuild { get; set; }

// Komendy
public ReactiveCommand<Unit, Unit> LoginCommand { get; }
public ReactiveCommand<DiscordGuildInfo, Unit> SelectGuildCommand { get; }
public ReactiveCommand<Unit, Unit> LogoutCommand { get; }
```

### 7.3 Loopback OAuth handler

```csharp
// W tle podczas OAuth flow
public class OAuthLoopbackListener : IDisposable
{
    public event Action<string>? CodeReceived;

    public Task StartAsync(int port);
    // Nasłuchuje GET http://localhost:{port}/susmodder/callback?code=...
    // Emituje CodeReceived, zwraca HTML: "Możesz zamknąć to okno"
    public void Dispose(); // Stop listener
}
```

### 7.4 Integracja z istniejącym flow uruchamiania gry

`MainWindowViewModel.GameLaunch.cs` — zamiast odczytywać `SUStatsConfigViewModel.GetSelectedServerData()`:
- Odczytuje z `ISustatsCredentialsRepository.GetActiveAsync()`
- API bez zmian: token+secret leci do Clair API jak dotychczas

---

## 8. Konfiguracja — appsettings.json

Nowe klucze (w sekcji `Configuration`):
```json
{
  "Configuration": {
    "ClairApiBaseUrl": "https://clairbot.app",
    "ClairApiSusmodderEndpoint": "/api/susmodder",
    "SusmodderApiBaseUrl": "https://susmodder.app"
  }
}
```

**Uwaga:** `SusmodderApiBaseUrl` zostaje dla innych endpointów (config, download, roles), ale NIE dla among-tokens.

---

## 9. Co się dzieje z susmodder-api?

| Endpoint | Los |
|---|---|
| `/api/among-tokens` | **Zostaje** dla backward compat (starzy klienci). Nowy klient go nie używa. |
| Sync z Clair (`SUStatsSyncClient`) | **Zostaje** jako fallback. |
| Pozostałe endpointy | **Bez zmian.** |

Long-term: gdy wszyscy klienci przejdą na nowy flow → `/api/among-tokens` do deprecation.

---

## 10. i18n — nowe klucze

| Klucz | PL | EN |
|---|---|---|
| `DiscordAuth.LoginButton` | Zaloguj przez Discord | Sign in with Discord |
| `DiscordAuth.LoggedInAs` | Zalogowano jako: {0} | Signed in as: {0} |
| `DiscordAuth.LogoutButton` | Wyloguj | Sign out |
| `DiscordAuth.SelectGuild` | Wybierz serwer Discord | Select Discord server |
| `DiscordAuth.NoGuilds` | Brak serwerów z aktywnym SUStats | No servers with active SUStats |
| `DiscordAuth.NoPermission` | Nie masz uprawnień na tym serwerze | You don''t have permission on this server |
| `DiscordAuth.LoadingGuilds` | Pobieranie serwerów... | Loading servers... |
| `DiscordAuth.LoginError` | Błąd logowania: {0} | Login error: {0} |
| `DiscordAuth.BrowserOpened` | Otworzono przeglądarkę — zaloguj się przez Discord | Browser opened — sign in with Discord |
| `DiscordAuth.SessionExpired` | Sesja Discord wygasła. Zaloguj się ponownie. | Discord session expired. Please sign in again. |

---

## 11. Telemetry & privacy

- Discord Access Token **nigdy** nie opuszcza maszyny (tylko wymiana z Discord API i Clair API).
- W logach: token maskowany (pierwsze 8 znaków + `...`).
- Telemetry event: `discord_auth_enabled: true/false` (bez user/guild ID).
- DPAPI: dane szyfrowane powiązane z kontem Windows — inny user na tej samej maszynie nie odszyfruje.

---

## 12. Migracja danych

### 12.1 SQLite migration (nowa wersja schematu)

`DatabaseService` → nowa migracja:
```sql
-- v2: Discord OAuth2 + SUStats credentials
PRAGMA user_version = 2;

CREATE TABLE IF NOT EXISTS discord_auth (
    id                  INTEGER PRIMARY KEY CHECK (id = 1),
    access_token_enc    TEXT NOT NULL,
    refresh_token_enc   TEXT NOT NULL,
    token_type          TEXT NOT NULL DEFAULT ''Bearer'',
    expires_at          TEXT NOT NULL,
    discord_user_id     TEXT,
    discord_username    TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime(''now'')),
    updated_at          TEXT NOT NULL DEFAULT (datetime(''now''))
);

CREATE TABLE IF NOT EXISTS sustats_credentials (
    guild_id            TEXT PRIMARY KEY,
    server_name         TEXT NOT NULL,
    token_enc           TEXT NOT NULL,
    secret_enc          TEXT NOT NULL,
    endpoint            TEXT NOT NULL,
    created_at          TEXT NOT NULL DEFAULT (datetime(''now'')),
    updated_at          TEXT NOT NULL DEFAULT (datetime(''now''))
);

ALTER TABLE user_settings ADD COLUMN active_sustats_guild_id TEXT DEFAULT NULL;
```

### 12.2 Backward compat dla istniejących użytkowników

- Jeśli `user-settings.json` ma stare dane SUStats → **nie migrujemy**. User przechodzi przez nowy Discord flow.
- Stare dane z `config.json` (sekcja SUStats) → **nie migrowane**. Discord OAuth to nowa ścieżka.
- Jeśli user ma stary config z ręcznie wpisanym secretem → po wdrożeniu musi przejść przez Discord OAuth.

---

## 13. Platform, packaging, AV

- **Loopback HTTP** (`localhost:random_port`) — działa bez uprawnień admina. Firewall nie blokuje loopback.
- **DPAPI** — Windows built-in. Na Linux fallback AES-GCM (w przyszłości, gdy będzie wsparcie Linux).
- **PKCE** — brak Client Secret w binarkach = bezpieczne dla open-source.
- **Systemowa przeglądarka** zamiast WebView — zero dodatkowych zależności.
- **Velopack updater** — bez zmian. Nowa tabela SQLite jest w `%APPDATA%`, nie w katalogu aplikacji.

---

## 14. Verification plan

### 14.1 Build
- [ ] `dotnet build SUSModder.sln` bez błędów
- [ ] Testy jednostkowe: `DiscordOAuthService`, `CredentialProtector`, `ClairDiscordService`
- [ ] Testy SQLite migracji v2

### 14.2 OAuth flow
- [ ] Loopback listener poprawnie przechwytuje `?code=`
- [ ] Wymiana code → access_token + refresh_token działa
- [ ] Refresh token działa (przed expiry)
- [ ] Expired token → czytelny komunikat w UI, przycisk "Zaloguj ponownie"
- [ ] Wylogowanie: revoke + czyści tabele

### 14.3 Clair API
- [ ] `GET /api/susmodder/config` zwraca client_id
- [ ] `POST /api/susmodder/guilds` zwraca tylko guildy z SUStats + uprawnieniami
- [ ] `POST /api/susmodder/credentials` zwraca prawidłowy token+secret
- [ ] Brak uprawnień → 403
- [ ] Nieprawidłowy token Discord → 401
- [ ] Rate limiting działa (max 5/h)

### 14.4 UX
- [ ] PL/EN — wszystkie stringi
- [ ] Przejście przez pełen flow: login → wybór guildy → zapis → uruchomienie gry z SUStats
- [ ] Ponowne uruchomienie aplikacji: auto-refresh tokenu, nie wymaga ponownego logowania
- [ ] Zmiana guildy: nowe credentials pobrane i zapisane

### 14.5 Backward compat
- [ ] Stary klient (bez Discord OAuth) dalej działa z susmodder-api `/api/among-tokens`
- [ ] Clair Hub → susmodder-api sync nie jest ruszony

---

## 15. Implementation order

| # | Faza | Gdzie | Zależność | Dni |
|---|---|---|---|---|
| 1 | Clair: endpoint `/api/susmodder/config` | `clair-hub` | — | 0.5 |
| 2 | Clair: endpoint `POST /api/susmodder/guilds` | `clair-hub` | 1 | 1.5 |
| 3 | Clair: endpoint `POST /api/susmodder/credentials` + audyt | `clair-hub` | 2 | 1 |
| 4 | SUSModder.Core: `CredentialProtector` (DPAPI) | `SUSModder.Core/Services/Discord/` | SQLite migration done | 1 |
| 5 | SUSModder.Core: SQLite migration v2 (3 nowe obiekty) | `SUSModder.Core/Data/` | SQLite migration done | 1 |
| 6 | SUSModder.Core: `DiscordOAuthService` (PKCE, token mgmt) | `SUSModder.Core/Services/Discord/` | 4, 5 | 2 |
| 7 | SUSModder.Core: `ClairDiscordService` (HTTP client) | `SUSModder.Core/Services/` | 1, 2, 3 | 1.5 |
| 8 | SUSModder.Core: `SustatsCredentialsRepository` | `SUSModder.Core/Data/` | 5 | 1 |
| 9 | SUSModder.Core: update `SUStatsService` (usuwa zależność od susmodder-api) | `SUSModder.Core/Configuration/` | 5, 8 | 1 |
| 10 | SUSModder UI: `OAuthLoopbackListener` | `SUSModder/Services/` | — | 0.5 |
| 11 | SUSModder UI: `DiscordAuthViewModel` / przebudowa `SUStatsConfigView` | `SUSModder/ViewModels/`, `SUSModder/Views/` | 6, 7, 10 | 3 |
| 12 | SUSModder UI: integracja z `GameLaunch` flow | `SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs` | 9, 11 | 1 |
| 13 | i18n: PL + EN klucze | `SUSModder/Localization/` | — | 0.5 |
| 14 | Testy + review | całość | wszystkie | 2 |

**Równolegle:** 1-3 (Clair) ∥ 4-5 (Core foundation) ∥ 13 (i18n).

**Total:** ~10-13 dni roboczych (1 dev, z częściową równoległością).

---

## 16. Decyzje właściciela (✅ potwierdzone)

| # | Pytanie | Decyzja |
|---|---|---|
| 1 | Nowa Discord App czy reuse Clair? | ✅ **Reuse Clair** |
| 2 | MVP dostęp: tylko admin+właściciel, czy system rang? | ✅ **Admin + właściciel zawsze.** System rang później (po stronie Clair). |
| 3 | Fallback offline/ręczny? | ✅ **Brak fallbacku.** Mechanizm jest Discord-only, nie ma sensu. |
| 4 | Cachować listę guild? | ✅ **Tak**, w pamięci na czas działania aplikacji. |
| 5 | Zabezpieczyć token+secret? | ✅ **DPAPI na Windows**, AES-GCM na Linux. |

---

## 17. Dalsze fazy (poza scope)

- **Faza 2:** Discord Rich Presence
- **Faza 3:** Komendy Discorda do zarządzania modami
- **Faza 4:** System rang w Clair Hub (UI do konfiguracji `sustats_access_roles`)

---

## Źródła użyte przy planie

- `mcp-rag`: SUSModder, Clair-Bot, susmodder-backend (obecny flow, API)
- Pliki źródłowe: `SUSModder.Core/Secrets.cs`, `SUStatsService.cs`, `AmongToken.cs`, `appsettings.json`
- Clair: `clair-hub/routes/among-us.js`, `clair-hub/utils/sustats-sync.js`, `clair-api/src/middleware/sustats-auth.js`, `clair-holdem/OAUTH_SETUP.md`
- SQLite migration POC: `DOC/POC/SQLite-migration/README.md`
