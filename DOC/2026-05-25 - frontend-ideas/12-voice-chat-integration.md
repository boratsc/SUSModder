# 12 – Integracja voice chat / Discord / Clair

**Priorytet:** 🟢 P2  
**Effort:** V1 ~1 dzień, V2 ~1-2 dni, V3 ~5-7 dni, V4 ✅ ~2-3 dni (częściowo)  
**Status:** 🔧 **Częściowo zaimplementowane** — Discord OAuth2 + Clair REST (SUStats) ✅; reszta ⏳  
**Plan wdrożenia:** [`DOC/PLAN/2026-05-29-voice-chat-integration-plan.md`](../PLAN/2026-05-29-voice-chat-integration-plan.md)  
**Powiązany plan:** [`DOC/PLAN/2026-05-27-implement-discord-oauth2-pkce.md`](../PLAN/2026-05-27-implement-discord-oauth2-pkce.md) (OAuth ✅)

---

## Cel

1. Ułatwić graczom znalezienie społeczności (serwer Discord SUSModder)
2. Zintegrować **Clair** (bot Discord, Mira API, SUStats) z SUSModder
3. Opcjonalnie: **BetterCrewLink** jako proximity voice w grze
4. Pobieranie tokenów SUStats przez Clair REST API (bez ręcznego kopiowania)

> **Uwaga:** Lobby codes są **osobnym, działającym systemem** na susmodder.app (`/api/lobby-board`). Ten dokument **nie obejmuje** udostępniania kodów lobby — patrz [`10-lobby-code-sharing.md`](10-lobby-code-sharing.md).

---

## Stan implementacji (2026-05-29)

### ✅ Zrobione

| Feature | Opis | Pliki |
|---------|------|-------|
| **Discord OAuth2 PKCE** | Logowanie Discord → wybór serwera → credentials SUStats | `DiscordOAuthService.cs`, `SUStatsConfigViewModel.cs` |
| **Clair REST API** | OAuth config, guilds, credentials | `ClairDiscordService.cs`, `IClairDiscordService.cs` |
| **Credential storage** | DPAPI/AES-GCM + SQLite | `CredentialProtector.cs`, `DiscordAuthRepository.cs`, `SustatsCredentialsRepository.cs` |
| **OAuth loopback** | Callback `http://127.0.0.1:53124/susmodder/callback` | `OAuthLoopbackListener.cs` |
| **Auto-login startup** | Przywracanie aktywnej guildy z SQLite | `SUStatsConfigViewModel.TryAutoLoginOnStartupAsync` |
| **i18n DiscordAuth** | Sekcja PL/EN | `pl.json`, `en.json` |
| **Ulubione serwery Discord** | Istniejący panel + API `/api/susmodder-discordfavs` | `MainWindowViewModel` (promo) |

### ⏳ Do zrobienia

| Opcja | Feature | Effort | Priorytet |
|-------|---------|--------|-----------|
| **A** | Własny serwer Discord SUSModder (struktura kanałów) | ~2h | P2 |
| **A** | Clair: `/kod`, `/kody`, auto-cleanup | ~5h | P2 |
| **A** | Webhook SUSModder → Discord (opcjonalny cross-post kodu) | ~2h | P3 |
| **A** | Panel „Dołącz do serwera SUSModder” w UI | ~2h | P2 |
| **B** | BetterCrewLink jako DLL (instalacja jednym klikiem) | ~1 dzień | P2 |
| **B** | Fork BCL + localhost API (volume, lista graczy) | ~5-7 dni | P3 |
| **C** | Discord Rich Presence | ~1 dzień | P3 |
| **D** | Statystyki gier Mira API w UI SUSModder | ~4h | P3 |
| **D** | SignalR real-time (kody Discord ↔ SUSModder) | ~1-2 dni | ❌ **Nie rekomendowane** — lobby board już na susmodder.app |

---

## Opcja A: Serwer Discord SUSModder + Clair

### Bot Clair — co już ma

- Wyniki gier Among Us przez Mira API (SUSTATS → Clair → Discord embed)
- SignalR WebSocket (real-time w bocie)
- System ekonomii, role, slash commands
- Kategoryzacja ról z `susmodder.app/api/roles-modifiers`

### Co dodać do Clair (V1)

| Feature | Opis | Effort | Status |
|---------|------|--------|--------|
| `/kod ABCDEF` | Publikuje kod na kanale moda | ~1h | ⏳ |
| `/kody` | Lista aktywnych kodów | ~1h | ⏳ |
| Auto-cleanup | Usuwanie kodów > 15 min (cron) | ~1h | ⏳ |
| `#lobby-kody/{mod}` | Per-mod kanały | ~30 min | ⏳ |
| Webhook z SUSModder | Kod z apki → Discord (opcjonalnie) | ~1h | ⏳ |

> **Decyzja:** Kody w SUSModder UI idą przez **susmodder.app**, nie Clair API. Komendy Discord to **dodatkowy kanał dystrybucji**, nie źródło prawdy.

### Struktura serwera Discord (propozycja)

```
📢 ogłoszenia
📋 changelog
💬 general
🎮 lobby-kody
   ├── #town-of-us
   ├── #the-other-roles
   └── #pozostałe
🔊 voice
   ├── General
   ├── Town of Us
   └── The Other Roles
```

### Integracja z SUSModder (UI)

- Panel „Serwer Discord SUSModder” obok polecanych serwerów
- Invite link → dołączenie do serwera
- Przycisk „Znajdź lobby na Discordzie” → deep link do kanału moda
- Opcjonalny cross-post kodu lobby na Discord (webhook Clair)

---

## Opcja B: BetterCrewLink (proximity voice)

**Repo:** https://github.com/OhMyGuus/BetterCrewLink

| Wersja | Scope | Effort | Status |
|--------|-------|--------|--------|
| **V1** | Instalacja BCL jako DLL/proces obok moda | ~1 dzień | ⏳ |
| **V2** | Fork + localhost API — UI w SUSModder (volume, gracze) | ~5-7 dni | ⏳ |

**Zasada:** Nie przepisywać WebRTC na C# — BCL zostaje osobnym procesem Node.js.

**Powiązanie z mod packs:** Opcja „Dołącz susmodder-integration.dll” w [`16-mod-pack-sharing.md`](16-mod-pack-sharing.md) obejmuje też integrację BCL (osobny plan DLL).

---

## Opcja C: Discord Rich Presence

Pokazuje w profilu Discord: „Gra w Town of Us przez SUSModder”.

| Effort | ~1 dzień | Status ⏳ |
|--------|----------|------------|
| Wymaga | Discord Game SDK lub RPC library dla .NET | |

---

## Opcja D: Clair REST API ✅ (częściowo)

Integracja SUSModder ↔ Clair przez REST na clair-hub. **Lobby codes NIE są częścią tej opcji.**

### Endpointy Clair (istniejące)

| Endpoint | Opis | Status |
|----------|------|--------|
| `GET /api/susmodder/config` | OAuth client_id, redirect | ✅ Używany |
| `POST /api/susmodder/guilds` | Lista serwerów użytkownika | ✅ Używany |
| `POST /api/susmodder/credentials` | Token + secret SUStats | ✅ Używany |
| `GET /api/roles-modifiers` | Role i modyfikatory | ✅ Istnieje |
| `GET /api/susmodder-discordfavs` | Ulubione serwery | ✅ Istnieje |
| `GET /api/among-tokens` | Legacy token picker | ⚠️ `[Obsolete]` — zastąpiony OAuth |

### Co jeszcze z Opcji D

| Krok | Opis | Effort | Status |
|------|------|--------|--------|
| Statystyki gier | Ostatnie wyniki Mira API w UI | ~4h | ⏳ |
| Discord server browser | Zintegrowana lista z Clair + favs | ~3h | 🔧 Częściowo (favs istnieją) |

### Architektura (zrealizowana)

```
SUSModder (C#/.NET)
  ├── Discord OAuth2 PKCE → Discord API
  ├── HttpClient → clair-hub /api/susmodder/*
  │     ├── config, guilds, credentials
  │     └── (przyszłość) statystyki Mira
  └── SQLite: discord_auth, sustats_credentials (DPAPI)
```

---

## Rekomendowana strategia (zaktualizowana 2026-05-29)

```
✅ V4 (OAuth):     Discord OAuth2 + Clair REST (SUStats) — ZROBIONE
⏳ V1 (~1 dzień):  Serwer Discord + Clair (/kod, /kody) + panel invite w UI
⏳ V1b (~1 dzień): BetterCrewLink jako opcjonalny DLL
⏳ V2 (~4h):       Statystyki Mira API w UI
⏳ V3 (~5-7 dni):  Fork BCL + voice UI (opcjonalnie)
⏳ V4b (~1 dzień): Discord Rich Presence (opcjonalnie)
❌ SignalR lobby:  NIE — lobby board na susmodder.app wystarczy
```

---

## Decyzje (rozstrzygnięte / otwarte)

| # | Pytanie | Odpowiedź |
|---|---------|-----------|
| D1 | Clair REST vs SignalR dla tokenów? | **REST** ✅ |
| D2 | SignalR dla kodów lobby? | **Nie** — susmodder.app `/api/lobby-board` |
| D3 | Stawiać własny serwer Discord? | ⏳ **Do potwierdzenia** (rekomendacja: tak) |
| D4 | Clair `/kod` + `/kody`? | ⏳ **Tak**, jako dodatkowy kanał (nie źródło prawdy) |
| D5 | BetterCrewLink V1 (tylko DLL)? | ⏳ **Rekomendacja: tak** przed forkem |
| D6 | Rich Presence? | ⏳ Przy okazji, niski priorytet |
| D7 | Cross-post kodu SUSModder → Discord? | ⏳ Opcjonalny webhook, P3 |

Szczegółowy plan implementacji: [`2026-05-29-voice-chat-integration-plan.md`](../PLAN/2026-05-29-voice-chat-integration-plan.md).
