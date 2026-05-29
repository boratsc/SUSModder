# Plan Wdrożenia: Voice Chat / Discord / Clair

**Data:** 2026-05-29  
**Status:** 🔧 Częściowo zrealizowane (OAuth ✅), reszta ⏳  
**Priorytet:** P2  
**Koncepcja:** [`DOC/2026-05-25 - frontend-ideas/12-voice-chat-integration.md`](../2026-05-25%20-%20frontend-ideas/12-voice-chat-integration.md)

---

## Przegląd

Trzy niezależne ścieżki — można implementować równolegle po ukończeniu OAuth:

| Ścieżka | Opis | Status | Effort |
|---------|------|--------|--------|
| **A** | Serwer Discord + rozbudowa Clair | ⏳ | ~1 dzień |
| **B** | BetterCrewLink (proximity voice) | ⏳ | ~1 dzień (V1) |
| **C** | Discord Rich Presence | ⏳ | ~1 dzień |
| **D** | Clair REST (SUStats OAuth) | ✅ | — |
| **D+** | Statystyki Mira API w UI | ⏳ | ~4h |

> **Lobby codes NIE są częścią tego planu** — działają na `susmodder.app/api/lobby-board`.

---

## ✅ Faza 0 — Discord OAuth2 + Clair REST (ZAKOŃCZONA)

**Plan źródłowy:** [`2026-05-27-implement-discord-oauth2-pkce.md`](2026-05-27-implement-discord-oauth2-pkce.md)

### Zaimplementowane komponenty

| Komponent | Plik |
|-----------|------|
| Discord OAuth2 PKCE | `DiscordOAuthService.cs` |
| Clair REST client | `ClairDiscordService.cs` |
| Credential encryption | `CredentialProtector.cs` |
| SQLite repos | `DiscordAuthRepository.cs`, `SustatsCredentialsRepository.cs` |
| OAuth loopback | `OAuthLoopbackListener.cs` |
| UI flow | `SUStatsConfigViewModel.cs`, `SUStatsConfigView.axaml` |
| DI + startup auto-login | `App.axaml.cs` |
| i18n | Sekcja `DiscordAuth` w pl.json/en.json |

### Weryfikacja (checklist)

- [x] `dotnet build SUSModder.sln` — kompilacja OK
- [ ] Manual: login Discord → wybór guildy → credentials zapisane w SQLite
- [ ] Manual: restart app → auto-login z SQLite
- [ ] Manual: logout → token usunięty

---

## Faza 1 — Serwer Discord SUSModder + Clair bot (~1 dzień)

**Repo:** clair-bot (discord.js v14, Docker) — poza tym repozytorium.

### 1.1 Infrastruktura Discord (manual, ~2h)

- [ ] Utworzenie serwera Discord SUSModder
- [ ] Struktura kanałów (patrz koncepcja 12):
  - `#ogłoszenia`, `#changelog`, `#general`
  - `#lobby-kody/town-of-us`, `#the-other-roles`, `#pozostałe`
  - Voice: General, Town of Us, The Other Roles
- [ ] Role: @Member, @Moderator, opcjonalnie per-mod
- [ ] Invite link permanentny → zapis w backendzie / appsettings

### 1.2 Clair — slash commands (~5h)

| Komenda | Implementacja | Uwagi |
|---------|---------------|-------|
| `/kod <code> [mod]` | Embed + TTL metadata | Kod trafia na kanał moda |
| `/kody` | Lista embedów <15 min | Cron cleanup co 5 min |
| Auto-cleanup | node-cron | Usuwa wiadomości >15 min |

**Decyzja:** Kody Discord są **kanałem alternatywnym** — źródło prawdy dla SUSModder UI = susmodder.app.

### 1.3 SUSModder UI — panel serwera (~2h)

**Pliki do modyfikacji:**
- `MainWindowViewModel` lub `AdditionalActionsPanel` — sekcja „Społeczność”
- Nowy przycisk: „Dołącz do serwera Discord SUSModder” → `Process.Start(inviteUrl)`
- Opcjonalnie: „Otwórz #town-of-us” — deep link `discord://discord.com/channels/{guildId}/{channelId}`

**i18n:** ~5 kluczy `DiscordCommunity.*`

### 1.4 (Opcjonalnie) Webhook cross-post (~2h, P3)

Gdy user publikuje kod w Lobby Board → opcjonalny POST na webhook Clair.

- Ustawienie użytkownika: `lobbyCrossPostDiscord` (bool, default false)
- Backend NIE wymagany — webhook URL w appsettings (read-only) lub per-mod config z API

---

## Faza 2 — BetterCrewLink V1 (~1 dzień)

**Repo:** https://github.com/OhMyGuus/BetterCrewLink

### 2.1 Instalacja jako opcjonalny komponent

| Krok | Opis |
|------|------|
| 2.1.1 | Pobieranie release BCL (portable) do `{modFolder}/BetterCrewLink/` |
| 2.1.2 | Przycisk w UI moda: „Zainstaluj BetterCrewLink” |
| 2.1.3 | Launch: start BCL przed/grą obok Among Us |
| 2.1.4 | Dokumentacja w UI: wymaga mikrofonu, firewall exception |

**Pliki:**
- `SUSModder.Core/Services/BetterCrewLinkService.cs` (nowy)
- Integracja w `GameService` / launch flow

### 2.2 Non-goals V1

- ❌ Fork BCL
- ❌ UI volume / lista graczy (Faza 3)
- ❌ WebRTC w C#

---

## Faza 3 — BetterCrewLink fork + voice UI (~5-7 dni, opcjonalnie)

Tylko jeśli Faza 2 ma adoption >10% użytkowników modów voice.

- Fork BCL z localhost REST API
- Panel w SUSModder: lista graczy, volume slider, mute
- Integracja z `susmodder-integration.dll` (plan lobby V2)

**Decyzja:** Odroczyć do Q3 2026 — najpierw V1.

---

## Faza 4 — Statystyki Mira API w UI (~4h)

### 4.1 Endpoint

Clair Hub lub susmodder.app — ostatnie wyniki gier dla aktywnej guildy SUStats.

**UI:** Sekcja w panelu SUStats lub osobna zakładka „Ostatnie gry” — embed-style lista (data, mapa, zwycięzca).

### 4.2 Pliki

- `SUSModder.Core/Services/MiraStatsService.cs` (nowy)
- `SUStatsConfigViewModel` — rozszerzenie o `RecentGames`
- i18n: ~8 kluczy `MiraStats.*`

---

## Faza 5 — Discord Rich Presence (~1 dzień, opcjonalnie)

- Biblioteka: DiscordRPC (NuGet) lub Discord Game SDK
- Wyświetlanie: mod name, wersja SUSModder, opcjonalnie kod lobby (tylko własny)
- Rejestracja aplikacji w Discord Developer Portal
- **Plik:** `SUSModder.Core/Services/DiscordRichPresenceService.cs`

---

## Kolejność rekomendowana

```
✅ Faza 0   Discord OAuth + Clair REST
⏳ Faza 1   Serwer Discord + Clair /kod + panel invite     (~1 dzień)
⏳ Faza 2   BetterCrewLink V1 (instalacja)                  (~1 dzień)
⏳ Faza 4   Statystyki Mira w UI                             (~4h)
⏳ Faza 5   Rich Presence (opcjonalnie)                      (~1 dzień)
⏳ Faza 3   BCL fork (tylko po metrykach adoption)          (~5-7 dni)
```

**Szacowany czas do wartości użytkowej (Fazy 1+2):** ~2 dni

---

## Decyzje do potwierdzenia (właściciel)

| # | Pytanie | Rekomendacja |
|---|---------|--------------|
| V1 | Stawiać serwer Discord SUSModder? | Tak |
| V2 | Clair `/kod` jako alternatywa, nie sync z API? | Tak |
| V3 | BCL V1 przed forkem? | Tak |
| V4 | Cross-post kodu na Discord webhook? | P3, opcjonalnie |
| V5 | SignalR lobby codes? | **Nie** — odrzucone |

---

## Test plan (Fazy 1–2)

| Test | Oczekiwany wynik |
|------|------------------|
| OAuth login → guild → credentials | SUStats działa w grze |
| Invite link z SUSModder | Otwiera Discord, dołącza do serwera |
| `/kod ABCDEF` na Discordzie | Embed na kanale moda, znika po 15 min |
| Instalacja BCL | Pliki w folderze moda, launch startuje BCL |
| Brak internetu | Graceful error, istniejący offline state (plan 09) |

---

## Powiązane plany

- OAuth (✅): [`2026-05-27-implement-discord-oauth2-pkce.md`](2026-05-27-implement-discord-oauth2-pkce.md)
- Lobby codes (✅ MVP): [`2026-05-28-lobby-code-sharing-phase0-plan.md`](2026-05-28-lobby-code-sharing-phase0-plan.md)
- Integration DLL: [`2026-05-29-lobby-code-sharing-v2-plan.md`](2026-05-29-lobby-code-sharing-v2-plan.md)
