# Plan implementacji: Discord OAuth2 PKCE + SUStats Auth

**Data:** 2026-05-27
**Status:** 🏗️ W trakcie implementacji (Fazy 2-8 równolegle)
**Zależność:** SQLite migration (v2) — rozszerzenie istniejącego schema
**Stack:** .NET 8, Avalonia, SQLite (Microsoft.Data.Sqlite), DPAPI/AES-GCM

---

## Przegląd

Zastąpienie obecnego 3-hop flow (Clair → susmodder-api → SUSModder) bezpośrednią autoryzacją Discord OAuth2 PKCE. User loguje się przez Discord, aplikacja pobiera listę serwerów gdzie ma SUStats, wybiera serwer, pobiera credentials — bez ręcznego kopiowania secretów.

---

## Architektura

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SUSModder (UI Layer)                          │
│                                                                      │
│  SUStatsConfigView.axaml ←→ SUStatsConfigViewModel                   │
│       │                       │                                      │
│       │                  OAuthLoopbackListener                        │
│       │                  (HttpListener :53124/susmodder/callback)     │
│       │                       │                                      │
│       │            ┌──────────┴──────────────┐                       │
│       │            │   IDiscordOAuthService   │                      │
│       │            │   IClairDiscordService   │                      │
│       │            └──────────┬──────────────┘                       │
├───────┼────────────────────────┼─────────────────────────────────────┤
│       │          SUSModder.Core (Business Logic)                     │
│       │                        │                                     │
│       │            ┌───────────┴────────────┐                        │
│       │            │   CredentialProtector   │                       │
│       │            │   (DPAPI / AES-GCM)     │                       │
│       │            └───────────┬────────────┘                        │
│       │                        │                                     │
│       │            ┌───────────┴────────────┐                        │
│       │            │   DiscordAuthRepository │                       │
│       │            │   SustatsCredentialsRepo│                       │
│       │            │   UserSettingsRepo (+1 col)                     │
│       │            └───────────┬────────────┘                        │
│       │                        │                                     │
│       │                  ┌─────┴──────┐                              │
│       │                  │   SQLite   │                              │
│       │                  │ susmodder.db│                             │
│       │                  └────────────┘                              │
└───────┼──────────────────────────────────────────────────────────────┘
        │
        │  HTTPS POST /api/susmodder/guilds
        │  HTTPS POST /api/susmodder/credentials
        ▼
┌──────────────┐
│  Clair API   │
│  clairbot.app│
└──────────────┘
```

---

## Fazy implementacji

### Faza 1 ✅ — Analiza istniejącego kodu
- [x] Przegląd SUStatsService, SUStatsConfigViewModel, SUStatsConfigView
- [x] Przegląd DatabaseService (schema v1, ApplyMigrations)
- [x] Przegląd UserSettings, IUserSettingsRepository
- [x] Przegląd systemu i18n (LocalizationService, pl.json, en.json)
- [x] Przegląd appsettings.json
- [x] Przegląd wzorców repository (ModRepository)

### Faza 2 🏗️ — Schema SQLite + Repositories (sus-core-backend)
- [ ] Migracja v2: `discord_auth`, `sustats_credentials`, `ALTER TABLE user_settings`
- [ ] Modele: `DiscordTokenInfo`, `SustatsCredentials`, `DiscordGuildInfo`, `ClairOAuthConfig`
- [ ] `IDiscordAuthRepository` + `DiscordAuthRepository`
- [ ] `ISustatsCredentialsRepository` + `SustatsCredentialsRepository`
- [ ] Rozszerzenie `IUserSettingsRepository` o `UpdateSingleField`
- [ ] Rozszerzenie `UserSettings.ActiveSustatsGuildId`

### Faza 3 🏗️ — CredentialProtector (sus-core-backend)
- [ ] `CredentialProtector.Protect()` / `Unprotect()`
- [ ] Windows: DPAPI (ProtectedData, CurrentUser)
- [ ] Linux: AES-256-GCM z machine-id
- [ ] Cross-platform detection

### Faza 4 🏗️ — Serwisy OAuth + Clair (sus-core-backend)
- [ ] `IDiscordOAuthService` + `DiscordOAuthService`
- [ ] PKCE flow: code_verifier, code_challenge (S256)
- [ ] Token exchange, refresh, revoke
- [ ] `IClairDiscordService` + `ClairDiscordService`
- [ ] HTTP klienci do Clair API endpoints

### Faza 5 🏗️ — SUStatsService refactor (sus-core-backend)
- [ ] Usunięcie zależności od `/api/among-tokens`
- [ ] `GetActiveCredentialsAsync()` przez ISustatsCredentialsRepository
- [ ] `SendGameStatsAsync(token, secret, endpoint, statsData)`
- [ ] Oznaczenie starych metod jako `[Obsolete]`

### Faza 6 🏗️ — OAuthLoopbackListener (sus-desktop-integration)
- [ ] `OAuthLoopbackListener.StartAsync(port)` — HttpListener na 127.0.0.1:53124
- [ ] Callback: wyciąga `?code=` z query string
- [ ] Zwraca HTML "Możesz zamknąć okno" (PL/EN)
- [ ] Proper Dispose/Cleanup

### Faza 7 🏗️ — UI: ViewModels + Views (sus-ui)
- [ ] Przebudowa `SUStatsConfigViewModel` — nowe stany i komendy
- [ ] `LoginCommand`, `LogoutCommand`, `RefreshGuildsCommand`
- [ ] Guild selector (ComboBox)
- [ ] Przebudowa `SUStatsConfigView.axaml` — nowy layout

### Faza 8 🏗️ — i18n (sus-i18n-copy-checker)
- [ ] Nowa sekcja `DiscordAuth` w pl.json i en.json
- [ ] 13 nowych kluczy (LoginButton, LoggedInAs, itd.)
- [ ] Placeholder parity ({0})

### Faza 9 ⏳ — Review i poprawki
- [ ] sus-quality-reviewer: przegląd kodu
- [ ] sus-senior-quality-reviewer: high-stakes review
- [ ] sus-security-auditor: audyt bezpieczeństwa
- [ ] Wdrożenie poprawek z review

### Faza 10 ⏳ — Build + Test
- [ ] `dotnet build SUSModder.sln` — czysty build
- [ ] `dotnet test` — wszystkie testy przechodzą
- [ ] Ręczne sprawdzenie: old SUStats flow nadal działa

---

## Nowe pliki do utworzenia

### SUSModder.Core (Business Logic)

| Plik | Odpowiedzialność |
|------|-----------------|
| `Models/DiscordTokenInfo.cs` | Model tokenu Discord |
| `Models/SustatsCredentials.cs` | Model credentials SUStats |
| `Models/DiscordGuildInfo.cs` | Model serwera Discord |
| `Models/ClairOAuthConfig.cs` | Konfiguracja OAuth z Clair API |
| `Data/IDiscordAuthRepository.cs` | Interfejs repo auth tokenów |
| `Data/DiscordAuthRepository.cs` | Implementacja (singleton, cache) |
| `Data/ISustatsCredentialsRepository.cs` | Interfejs repo credentials |
| `Data/SustatsCredentialsRepository.cs` | Implementacja (dict cache) |
| `Services/Discord/CredentialProtector.cs` | Szyfrowanie (DPAPI/AES-GCM) |
| `Services/Discord/IDiscordOAuthService.cs` | Interfejs serwisu OAuth |
| `Services/Discord/DiscordOAuthService.cs` | PKCE flow implementacja |
| `Services/Discord/IClairDiscordService.cs` | Interfejs klienta Clair API |
| `Services/Discord/ClairDiscordService.cs` | HTTP klient Clair API |

### SUSModder (UI Layer)

| Plik | Odpowiedzialność |
|------|-----------------|
| `Services/OAuthLoopbackListener.cs` | HTTP listener na callback |

## Modyfikowane pliki

| Plik | Zmiana |
|------|--------|
| `SUSModder.Core/Data/DatabaseService.cs` | CreateAllTables + ApplyMigrations v2 |
| `SUSModder.Core/Configuration/UserSettings.cs` | +`ActiveSustatsGuildId` |
| `SUSModder.Core/Data/IUserSettingsRepository.cs` | +`UpdateSingleField` |
| `SUSModder.Core/Data/UserSettingsRepository.cs` | +`UpdateSingleField` z whitelistem kolumn |
| `SUSModder.Core/Configuration/SUStatsService.cs` | Refactor: usunięcie among-tokens, dodanie wysyłki |
| `SUSModder/ViewModels/SUStatsConfigViewModel.cs` | Przebudowa na Discord OAuth flow |
| `SUSModder/Views/SUStatsConfigView.axaml` | Nowy layout |
| `SUSModder/Views/SUStatsConfigView.axaml.cs` | Nowe event handlery |
| `SUSModder/Localization/pl.json` | +13 kluczy DiscordAuth |
| `SUSModder/Localization/en.json` | +13 kluczy DiscordAuth |
| `SUSModder/appsettings.json` | +`ClairApiBaseUrl`, +`ClairApiSusmodderEndpoint` |
| `SUSModder/App.axaml.cs` | +DI rejestracje dla nowych serwisów |

---

## Backward Compatibility

- Stare `SUStatsService.GetSUStatsServersAsync()` → `[Obsolete]`, nadal działa dla starych klientów
- susmodder-api `/api/among-tokens` zostaje dla backward compat
- Stare klucze i18n `SUStatsConfig.*` zostają (nie usuwamy)
- User wybiera: stary flow (ręczne hasło) lub nowy (Discord OAuth)

## Bezpieczeństwo

- Tokeny Discord szyfrowane DPAPI (Windows) / AES-GCM (Linux)
- Code verifier tylko w pamięci podczas flow OAuth
- Rate limiting na Clair API: 30 req/min dla guilds, 5 req/h dla credentials
- Audit log na Clair API przy każdym pobraniu credentials
- HTTPS dla wszystkich wywołań API
- Port 53124 stały, zarejestrowany w Discord Developer Portal

## i18n

Nowa sekcja `DiscordAuth` w pl.json/en.json z 13 kluczami. Placeholder `{0}` dla dynamicznych danych (username, error message, server name).
