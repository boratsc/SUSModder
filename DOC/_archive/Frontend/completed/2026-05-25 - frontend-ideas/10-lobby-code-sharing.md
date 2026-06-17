# 10 – Udostępnianie kodów do lobby (Lobby Board)

**Priorytet:** 🟢 P2  
**Effort:** MVP ✅ ~4 dni, Bridge ✅ ~2 dni, polish/backlog ⏸️  
**Status:** ✅ **MVP** + ✅ **Bridge (gra → auto-fill w SUSModder)** — feature uznany za gotowy; auto-publish na API odłożony  
**Repo bridge:** `D:\Development\susmodder-integration` (osobne repozytorium)  
**Plan wdrożenia:** [`DOC/PLAN/2026-05-28-lobby-code-sharing-phase0-plan.md`](../PLAN/2026-05-28-lobby-code-sharing-phase0-plan.md) (Fazy 0–3 ✅)  
**Plan V2:** [`DOC/PLAN/2026-05-29-lobby-code-sharing-v2-plan.md`](../PLAN/2026-05-29-lobby-code-sharing-v2-plan.md)

---

## Cel

Umożliwić użytkownikom SUSModder dzielenie się kodami do lobby oraz krótkimi ogłoszeniami per mod — bez konieczności przeszukiwania Discorda.

## Dlaczego to potrzebne

- **Serwery Among Us mają bugi** – wyszukiwanie publicznych lobby często nie działa
- **Społeczność jest rozproszona** – gracze są na wielu serwerach Discord
- **Obecny flow jest uciążliwy** – Discord → serwer → kanał → kopiuj → wklej w grze
- **SUSModder może to skrócić do 2 kliknięć** w panelu moda

---

## Stan implementacji (2026-05-29)

### ✅ Zrobione (MVP — Etap 1)

| Warstwa | Co | Pliki / endpoint |
|---------|-----|------------------|
| **Backend** | `GET/POST/DELETE/PATCH /api/lobby-board`, `POST .../report` | susmodder.app |
| **Backend** | Moderacja W0–W5, Redis rate limit, heat system | PostgreSQL + Redis |
| **Backend** | TTL: kod 20 min, wiadomość 4 h | zgodnie z D4/D3 |
| **Core** | `ILobbyBoardService` + `LobbyBoardService` | `SUSModder.Core/Services/` |
| **Core** | `LobbyEntryValidator`, `IHardwareIdProvider` | `Validators/`, `Utilities/` |
| **Core** | `SupportsLobbySharing`, `LobbyRegionBaseUrl` w `ModConfiguration` | `ModConfig.cs`, SQLite v3 |
| **Core** | Konwerter kod ↔ gameId + `LookupLobbyStateAsync` (API regionu) | `LobbyBoardService.cs` |
| **UI** | `LobbyBoardPanel` — zakładki Kody / Ogłoszenia | `Views/LobbyBoardPanel.axaml` |
| **UI** | Publikacja, refresh 30 s, kopiowanie, zgłaszanie, usuwanie własnych | `LobbyBoardPanelViewModel.cs` |
| **UI** | Integracja w prawym panelu moda (`ShowLobbyBoard`) | `MainWindowViewModel.cs` |
| **i18n** | Sekcja `Lobby.*` PL/EN | `pl.json`, `en.json` |

### ✅ Bridge — gotowe (2026-05-29)

| Co | Stan | Uwagi |
|----|------|-------|
| **`SUSModder.Integration.dll`** | ✅ | Repo: `D:\Development\susmodder-integration` — TOU-Mira, Syzyf, Harmony |
| **`LobbyBridgeFileReader`** | ✅ | Gra → `lobby-bridge.json` → auto-fill formularza Lobby Board (E2E OK) |
| Protokół bridge v1 | ✅ | Zgodny obustronnie (`isPublic`, TTL 90s) |

### ⏸️ Odłożone (niski priorytet — backlog)

| Co | Stan | Uwagi |
|----|------|-------|
| Auto-publikacja na API | ⏸️ | Nie testowane; user klika „Udostępnij” ręcznie po auto-fill — wystarczy na teraz |
| Toast po wykryciu kodu | ⏸️ | Polish UX |
| Dystrybucja DLL przez katalog | ⏸️ | Ręczna instalacja do `BepInEx/plugins/` wystarczy na dev |
| Przycisk Share Code w grze | 🔧 | `LobbyUIInjector` — rozbudowa w susmodder-integration |
| Live `currentPlayers` | ⏸️ | `LookupLobbyStateAsync` w Core, bez UI |
| AmongUsAuth settings | ⏸️ | Do live lookupu |

### ⏳ Poza scope / backlog (V2+)

| Feature | Priorytet | Uwagi |
|---------|-----------|-------|
| Auto-publish bridge → API | P3 ⏸️ | Odłożone — auto-fill + ręczny „Udostępnij” wystarczy |
| Live player count w UI | P3 ⏸️ | `LookupLobbyStateAsync` gotowy w Core |
| Dystrybucja DLL katalogowa | P3 ⏸️ | Dev: ręczna instalacja |
| Admin panel moderacji | P3 | Backend V2 |
| Nostr (decentralizacja) | P4 | Nie planowane |

---

## Jak to działa (z perspektywy usera)

1. User wybiera mod z `SupportsLobbySharing = true` (np. Town of Us)
2. W prawym panelu otwiera sekcję **Lobby**
3. Zakładka **Kody** — lista aktywnych kodów (TTL 20 min), region Modded EU/NA/Asia
4. Zakładka **Ogłoszenia** — krótkie wiadomości (max 280 znaków, TTL 4 h, tylko discord.gg)
5. User wkleja swój kod ręcznie i klika **Udostępnij kod**
6. Inni gracze kopiują kod jednym kliknięciem

```
+--------------------------------------------------------------+
|  Lobby - Town of Us                              [Odśwież]   |
|  [ Kody (3) ]  [ Ogłoszenia (1) ]                            |
|                                                              |
|  Modded EU  |  ABCDEF  |  ?/15  |  2 min temu  [Kopiuj][Zgłoś]|
|  Modded NA  |  XYZ123  |  8/10  |  5 min temu  [Kopiuj][Zgłoś]|
|                                                              |
|  Kod: [______]  Region: [Modded EU ▼]  Gracze: [_]/15       |
|  [Udostępnij kod]                                            |
+--------------------------------------------------------------+
```

---

## Architektura (zrealizowana)

**Decyzja D1:** Backend = **susmodder.app** (nie Clair, nie Nostr).

```
SUSModder klient ──GET/POST──▶ susmodder.app/api/lobby-board
SUSModder klient ──GET /api/games/{id}──▶ serwer modowanego regionu AU  (live lookup, opcjonalnie)
```

| Aspekt | Wybór | Uzasadnienie |
|--------|-------|--------------|
| Protokół MVP | REST API susmodder.app | Pełna kontrola moderacji, TTL, heat system |
| Chat | Tablica ogłoszeń (nie IRC) | Prostsze, mniejsze ryzyko spamu |
| Tożsamość | `X-User-Hash` (SHA256 HWID) | Anonimowość, spójne z telemetrią |
| Regiony | Modded EU / NA / Asia | Vanilla regiony usunięte (D11) |
| Nostr | ❌ Odrzucone na MVP | API działa; Nostr = ewentualny Etap 3 |

---

## Decyzje (rozstrzygnięte)

| # | Pytanie | Odpowiedź |
|---|---------|-----------|
| D1 | Backend? | susmodder.app `/api/lobby-board` |
| D2 | Chat w scope? | Tak — tablica ogłoszeń per mod |
| D3 | TTL wiadomości? | **4 h** |
| D4 | TTL kodu? | **20 min** |
| D5 | MVP: API czy Nostr? | **API** (Nostr = przyszłość, nie planowane) |
| D6 | Treść chatu? | Tylko kody + metadane + ogłoszenia (discord.gg allowlist) |
| D7 | Gdzie w UI? | **Prawy panel moda** (expandable Lobby Board) |
| D8 | Zgłaszanie kodów? | **Tak** — `POST /api/lobby-board/{id}/report` |
| D9 | Auto-detect kodu? | ✅ **Bridge gotowy** — auto-fill; auto-publish ⏸️ P3 |
| D10 | Admin moderacji? | **V2** |
| D11 | Regiony? | Tylko modowane (Modded EU/NA/Asia) |
| D12 | Live currentPlayers? | Klient → REST regionu AU; PATCH = opcjonalny cache |

---

## Bridge — auto-wykrywanie kodu lobby ✅

Flow produkcyjny (wystarczający na obecny etap):

```
Gra (Make Public) → lobby-bridge.json → SUSModder auto-fill → user klika „Udostępnij kod”
```

**Repo pluginu:** `D:\Development\susmodder-integration`  
**E2E auto-fill:** ✅ zweryfikowany  
**Auto-publish na API:** ⏸️ odłożone (P3) — nie priorytet

| Mod | ModId | Status |
|-----|-------|--------|
| Town of Us: Mira | 13 | ✅ |
| Town of Us: Syzyf | 7 | ✅ |

Backlog: [`2026-05-29-lobby-code-sharing-v2-plan.md`](../PLAN/2026-05-29-lobby-code-sharing-v2-plan.md) (auto-publish, dystrybucja, live lookup).

---

## Wyzwania i mitygacje (zachowane)

| Wyzwanie | Mitygacja |
|----------|-----------|
| Spam | Rate limit 5 min, 20/dzień, heat system, word filter |
| Linki | Tylko `discord.gg`, max 1 link |
| Brak moderacji centralnej | Zgłoszenia + shadow/hard ban po heat |
| Nieaktualny player count | TTL 20 min; live lookup w V2 |
| Token w .exe | Akceptowane ryzyko (istniejąca architektura) + warstwy W1–W5 |

---

## Powiązane dokumenty

- POC: [`DOC/POC/2026-05-27-lobby-code-sharing.md`](../POC/2026-05-27-lobby-code-sharing.md)
- Etap 2 (Lobby Searcher): [`11-lobby-searcher.md`](11-lobby-searcher.md)
- Voice (BetterCrewLink): [`12-voice-chat-integration.md`](12-voice-chat-integration.md)
- Mod packs (integration.dll): [`16-mod-pack-sharing.md`](16-mod-pack-sharing.md)
