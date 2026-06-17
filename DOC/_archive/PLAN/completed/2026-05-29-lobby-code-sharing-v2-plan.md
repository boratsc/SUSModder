# Plan Wdrożenia: Lobby Code Sharing — V2 (po MVP)

**Data:** 2026-05-29 (aktualizacja: bridge DLL istnieje)  
**Status:** ✅ Bridge **gotowy** (gra → auto-fill). Backlog: auto-publish, dystrybucja, live lookup — **P3, odłożone**  
**Priorytet:** P2  
**MVP (Fazy 0–3):** ✅ Zakończone — [`2026-05-28-lobby-code-sharing-phase0-plan.md`](2026-05-28-lobby-code-sharing-phase0-plan.md)  
**Repo bridge DLL:** `D:\Development\susmodder-integration` (osobne repozytorium Git)

---

## Stan mostu (Bridge) — 2026-05-29

### ✅ Zaimplementowane — `susmodder-integration`

| Komponent | Plik | Opis |
|-----------|------|------|
| BepInEx plugin | `Plugin.cs` | Entry point, detekcja moda po chainloader Finished |
| Bridge writer | `Bridge/LobbyBridgeWriter.cs` | Atomic JSON → `%APPDATA%/SUSModder/lobby-bridge.json` |
| Mod detector | `ModSupport/ModDetector.cs` | TOU-Mira (13), Syzyf (7) |
| Integracje | `TouMiraIntegration.cs`, `SyzyfIntegration.cs` | Refleksja: kod, region, maxPlayers, isPublic |
| Harmony | `GameStartManagerPatch`, `AmongUsClientPatch`, `LobbyBehaviourPatch` | MakePublic, join/leave, Start |
| UI w grze | `UI/LobbyUIInjector.cs` | 🔧 Przycisk „Share Code” — w trakcie rozbudowy |
| Protokół | `docs/lobby-bridge-protocol.md` | v1: `code`, `modId`, `region`, `isPublic`, TTL 90s |

### ✅ Zaimplementowane — SUSModder (klient)

| Komponent | Plik | Opis |
|-----------|------|------|
| File reader | `SUSModder.Core/Lobby/LobbyBridgeFileReader.cs` | FileSystemWatcher, TTL 90s, filtr `isPublic` |
| Model | `LobbyBridgeModels.cs` | Zgodny z protokołem v1 |
| UI hook | `LobbyBoardPanelViewModel.OnLobbyCodeDetected` | Auto-fill `CodeInput`, region, maxPlayers |
| DI | `App.axaml.cs` | Singleton + `Start()` przy starcie app |

### ✅ Uznane za gotowe (scope bridge)

- [x] Plugin BepInEx → zapis `lobby-bridge.json` (TOU-Mira, Syzyf)
- [x] SUSModder `LobbyBridgeFileReader` → auto-fill formularza Lobby Board
- [x] Flow: Make Public → plik bridge → wykrycie w SUSModder

**Obecny UX (wystarczający):** auto-fill + user klika „Udostępnij kod” ręcznie.

### ⏸️ Backlog (P3 — odłożone, brak presji czasowej)

| # | Zadanie | Effort | Priorytet |
|---|---------|--------|-----------|
| 1 | Auto-publish na `/api/lobby-board` po wykryciu bridge | ~2h | P3 ⏸️ |
| 2 | Toast „Wykryto kod z gry” | ~1h | P3 ⏸️ |
| 3 | Ustawienie `lobbyAutoShareFromBridge` | ~1h | P3 ⏸️ |
| 4 | Dystrybucja DLL przez katalog API | ~3-4h | P3 ⏸️ |
| 5 | `LobbyUIInjector` — przycisk Share Code w grze | ~2-3h | P3 🔧 |
| 6 | Live player count + AmongUsAuth | ~1-1.5 dnia | P3 ⏸️ |

> **Decyzja właściciela (2026-05-29):** Auto-publish i dalsze E2E po stronie SUSModder **zostają w spokoju** — mniejszy priorytet, więcej czasu na testy później. Bridge jako feature jest **generalnie zrobiony**.

---

## Architektura (aktualna)

```
┌─ Among Us + BepInEx ─────────────────┐     ┌─ SUSModder.exe ──────────────────┐
│ D:\Development\susmodder-integration │     │ SUSModder (repo główne)          │
│  SUSModder.Integration.dll           │     │                                  │
│   ModDetector → TouMira / Syzyf      │     │  LobbyBridgeFileReader           │
│   Harmony → MakePublic, Join/Leave   │ JSON│   └─ OnLobbyCodeDetected         │
│   LobbyBridgeWriter ─────────────────┼────▶│       └─ auto-fill form (✅)     │
│                                      │ file│       └─ auto-publish (❌)       │
└──────────────────────────────────────┘     │  LobbyBoardService → API (✅)    │
                                             └──────────────────────────────────┘
                    %APPDATA%/SUSModder/lobby-bridge.json
```

**Protokół bridge v1** (obie strony zgodne):

```json
{
  "code": "ABCDEF",
  "modId": 13,
  "region": "Modded EU",
  "maxPlayers": 15,
  "isPublic": true,
  "timestamp": "2026-05-29T12:00:00.000Z",
  "bridgeVersion": 1
}
```

---

## Faza V2.0 — ✅ ZAKOŃCZONA (scope bridge)

Flow **gra → bridge file → SUSModder auto-fill** działa. User publikuje kod ręcznym kliknięciem „Udostępnij” — to akceptowany UX na obecny etap.

Auto-publish, toast, dystrybucja DLL — przeniesione do backlogu (sekcja powyżej).

### V2.0.2 UX po wykryciu kodu — ⏸️ BACKLOG

**Plik:** `LobbyBoardPanelViewModel.cs`

- [ ] Toast: „Wykryto kod lobby z gry — kliknij Udostępnij” (i18n `Lobby.Bridge.CodeDetected`)
- [ ] Opcjonalnie: podświetlenie przycisku „Udostępnij kod”
- [ ] Ustawienie `lobbyAutoShareFromBridge` (default `false`) w `user_settings`
- [ ] Gdy true → `PublishCodeAsync` z debounce 30 s (unikaj duplikatów)

### V2.0.3 Dystrybucja DLL

| Opcja | Opis |
|-------|------|
| A | Wpis w katalogu API modów jako opcjonalny DLL (`type: integration`) |
| B | Auto-kopiowanie przy instalacji moda full (TOU-Mira, Syzyf) |
| C | Bundling w mod pack (`includeIntegrationDll`) — plan 16 |

**Rekomendacja:** A + B — katalog + auto-install przy launch moda wspierającego bridge.

**Pliki SUSModder:**
- `DllModificationService` lub `ModManager` — kopiuj DLL do `BepInEx/plugins/` folderu moda
- Wersjonowanie: pole `IntegrationDllVersion` w API moda (opcjonalnie)

### V2.0.4 Przycisk Share Code w grze (susmodder-integration)

Plan: `D:\Development\susmodder-integration\docs\plan-share-button-phase1.md`

- [ ] Dokończyć `LobbyUIInjector.Inject()` — przycisk obok Make Public
- [ ] Handler → `BridgePublisher.PublishLobbyCode()` (identyczny flow jak auto)
- [ ] Feedback: „Code Shared ✓” po kliknięciu

---

## Faza V2.1 — Live player count w UI (~1 dzień)

*(Bez zmian względem poprzedniej wersji planu)*

### V2.1.1 AmongUsAuth — ustawienia użytkownika

SQLite migracja: `AmongUsIdToken`, `AmongUsPuid`, `AmongUsUsername`, `AmongUsClientVersion` (encrypted).

### V2.1.2 Podpięcie `LookupLobbyStateAsync`

W `RefreshAsync`: dla każdego kodu z `LobbyRegionBaseUrl` → live lookup (rate limit 30 s/kod).

### V2.1.3 Testy

- [ ] Lookup z auth → player count
- [ ] Brak auth → `?/15`
- [ ] PATCH cache opcjonalny

---

## Faza V2.2 — Polish + telemetry (~0.5 dnia)

| Zadanie | Opis |
|---------|------|
| Telemetria | `lobby_bridge_detected`, `lobby_bridge_published`, `lobby_lookup_success` |
| i18n | `Lobby.Bridge.*`, AmongUsAuth settings |
| Error UX | Region server errors → czytelne komunikaty |

---

## Faza V2.5 — Admin moderacji (opcjonalnie, ~1-1.5 dnia)

Backend admin endpoints — po stabilizacji bridge w produkcji.

---

## Kolejność implementacji

```
✅ Bridge core     Gra → auto-fill — GOTOWE
⏸️ Backlog P3     auto-publish, toast, dystrybucja DLL, Share Code UI, live lookup
⏸️ V2.5           Admin panel moderacji — opcjonalnie, po zebraniu danych z prod
```

---

## Decyzje V2 (zaktualizowane)

| # | Pytanie | Odpowiedź |
|---|---------|-----------|
| V2-D1 | Repo bridge? | ✅ `D:\Development\susmodder-integration` (osobne repo) |
| V2-D2 | Protokół bridge? | ✅ v1 — `isPublic`, TTL 90s, zgodny obustronnie |
| V2-D3 | Auto-publish? | Domyślnie **off** — ustawienie usera |
| V2-D4 | Pierwsze mody? | TOU-Mira (13), Syzyf (7) — **zaimplementowane** |
| V2-D5 | Share Code button? | 🔧 W trakcie — `LobbyUIInjector` |

---

## Ryzyka

| Ryzyko | Mitygacja |
|--------|-----------|
| Regresja bridge po update ToU | Test E2E po każdej większej wersji moda |
| ToU update łamie Harmony | Wersjonowanie patchy per wersja moda |
| MSBuild / GameLibs refleksja | Obecnie refleksja — działa bez unhollow |
| AV false positive | Ten sam model co BepInEx |

---

## Powiązane

- MVP: [`2026-05-28-lobby-code-sharing-phase0-plan.md`](2026-05-28-lobby-code-sharing-phase0-plan.md)
- Bridge repo README: `D:\Development\susmodder-integration\README.md`
- Protokół: `D:\Development\susmodder-integration\docs\lobby-bridge-protocol.md`
- Share button plan: `D:\Development\susmodder-integration\docs\plan-share-button-phase1.md`
- Mod packs (bundling DLL): [`2026-05-29-mod-pack-sharing-plan.md`](2026-05-29-mod-pack-sharing-plan.md)
