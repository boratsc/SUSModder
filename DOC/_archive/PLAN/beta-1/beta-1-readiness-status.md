# Beta 1 Readiness Review — Status Report

**Plan:** [`2026-06-16-beta-1-readiness-review-plan.md`](2026-06-16-beta-1-readiness-review-plan.md)  
**Branch:** `susmodder-3.0`  
**Wersja:** `3.0.0-beta1`  
**Data rozpoczęcia:** 2026-06-16  
**Ostatnia aktualizacja:** 2026-06-17 (warn cleanup pass 3)  
**Status:** ✅ **READY FOR BETA 1**

---

## Decyzja

| Kategoria | Liczba |
|-----------|--------|
| **BLOCKER** | **0** |
| **MUST FIX** | **0** |
| **WARN** | ~9 (akceptowane / POST-BETA) |
| **POST-BETA** | ~8 |

Review wykonany w **3 rundach** (evidence → fix pass → głęboki code review gate A–I).  
**Źródło prawdy:** sekcje Gate A–I poniżej (Runda 3).

---

## Macierz gate (Runda 3 — głęboki review)

| Gate | Werdykt | BLOCKER | MUST FIX | Testy regresji |
|------|---------|---------|----------|----------------|
| A — Scope | ✅ PASS | 0 | 0 | build ✅, test 179/179, subset **90/90** |
| B — Security | ✅ PASS | 0 | 0 | subset security **50/50** |
| C — Architecture | ✅ PASS | 0 | 0 | build ✅ |
| D — i18n | ✅ PASS | 0 | 0 | klucze **1189/1189** |
| E — Data/migration | ✅ PASS | 0 | 0 | code review + Core tests |
| F — E2E | ✅ PASS (manual) | 0 | 0 | manual QA użytkownika ✅ |
| G — Packaging | ✅ PASS | 0 | 0 | skrypt review ✅ |
| H — Build/test | ✅ PASS | 0 | 0 | build ✅, test **188/188**, API smoke **24/24** |
| I — Dead code | ✅ PASS | 0 | 0 | grep + review ✅ |

---

## Testy regresji (ostatni run: 2026-06-17)

```
dotnet build SUSModder.sln -c Release     → 0 err, 0 warn
dotnet test SUSModder.Core.Tests -c Release → 188/188
subset Gate A (ModPack/SHA256/…)          → 90/90
subset Gate B (security)                  → 50/50
test-api-v2-client.ps1                    → 24 OK, 0 FAIL, 1 EXPECTED
test-modpack-custom-content-v2.ps1 -ValidateOnly → OK
Manual E2E (użytkownik)                   → PASS
```

**H-R3-1:** ✅ naprawione po stronie backendu (2026-06-17) — `test-api-v2-client.ps1` 24/24 OK (wcześniej FAIL: mod 13 steam x86 bez variants).

---

## Top known issues (release notes)

1. Unsigned builds — AV/SmartScreen
2. `Secrets.cs` — Base64 w binarce (świadome ryzyko; POC hardening post-beta)
3. Architektura: rozproszone `HttpClient`, ViewModels bez DI, legacy `ConfigRepository` (refactor post-beta)
4. 5 planów `DOC/PLAN` nieaktualnych (DOC STALE)

---

## Rejestr ustaleń (skonsolidowany, Runda 3)

### Naprawione w fix pass (Runda 1 → 2)

| ID | Opis | Status |
|----|------|--------|
| B1 | OAuth `state` anti-CSRF | ✅ naprawione |
| M1 | `version.json` → `3.0.0-beta1` | ✅ |
| M2–M5 | Docs .NET 8 → 10 | ✅ |
| M10 | LobbyBoard time ago → i18n | ✅ |
| M6–M9 | Hardcoded dialogi Epic/Platform/Steam QR | ↘ reklasyfikacja → **WARN** |

### Naprawione w warn cleanup pass (2026-06-17)

| ID | Opis | Status |
|----|------|--------|
| B-R3-1 | Debug log pełnego OAuth AuthUrl | ✅ |
| B-R3-2 | Logout nie czyści `sustats_credentials` | ✅ |
| B-R3-3 | `_lastOAuthState` nie czyszczone po logout | ✅ |
| B-R3-4 | Walidacja state pomijana gdy `_lastOAuthState` puste | ✅ |
| B-R3-5 | Brak unit testów OAuth PKCE/state | ✅ (+9 testów) |
| B-R3-9 | Telemetry/AI `language` bez `NormalizeLanguage` | ✅ |
| B-R3-10 | Privacy notice hardcoded PL | ✅ |
| A-R3-3 | Hardcoded PL w panelach launch/AI | ✅ |

### Naprawione w warn cleanup pass 2 (2026-06-17)

| ID | Opis | Status |
|----|------|--------|
| A-R3-1 | Brak UI listy diff `PackUpdateInfo.Changes` | ✅ |
| A-R3-2 | Update paczki przez katalog FULL, nie remote pack | ✅ |
| D-* | Hardcoded PL: Epic/Platform/Steam QR dialogi | ✅ |

### Naprawione w warn cleanup pass 3 (2026-06-17)

| ID | Opis | Status |
|----|------|--------|
| A-R3-3b | Opisy diff w `ModPackUpdateChecker` hardcoded PL | ✅ (`ModPackChangeFormatter` + i18n) |
| B-R3-7 | `ModConfigHandler` HTTPS→HTTP fallback | ✅ (tylko `DeveloperMode`) |
| B-R3-11 | DPAPI error — brak dedykowanego komunikatu UI | ✅ |
| C-R3-3 | `DeveloperModeSettings` zapisuje `appsettings.json` | ✅ (SQLite `developer_mode` v12) |
| D-* | Hardcoded PL: `InfoPanel`, `PromptDialog` | ✅ |

### Naprawione po stronie backendu (2026-06-17)

| ID | Opis | Status |
|----|------|--------|
| H-R3-1 | API catalog mod 13 — brak variants (steam x86) | ✅ backend fix, smoke 24/24 OK |

### WARN (akceptowane — nie blokuje bety)

| ID | Opis | Gate | Uwagi |
|----|------|------|-------|
| B-R3-6 | `Secrets.cs` Base64 | B | Świadome; POC hardening post-beta |
| B-R3-8 | Clair API: token w JSON body | B | Kontrakt backendu |
| C-R3-1 | Rozproszone `HttpClient` | C | Refactor post-beta |
| C-R3-2 | ViewModels `new` bez DI | C | Refactor post-beta |
| C-R3-4 | `ConfigRepository` legacy | C, I | Migracja post-beta |
| C-R3-5 | 25 partiali `MainWindowViewModel` | C | Organizacja kodu, nie bug |
| G-* | Unsigned, Velopack, live beta endpoint | G | Środowisko / release process |
| I-* | Obsolete, `ConvertBack`, legacy scripts | I | Cleanup post-beta |

### POST-BETA

| ID | Opis |
|----|------|
| P1 | Custom FULL overlay validation (backend stub) |
| P2 | AI Support LLM (feature flag off) |
| P3 | Admin auto-actions (Defender/Firewall repair) |
| P4 | Linux `CredentialProtector` |
| P5 | `FirewallRuleInspector` — kod diagnozy bez emitera |
| P6 | SQLite `launch_attempts` / `support_sessions` |
| P7 | Epic partial launch supervisor |
| P8 | Modpack: ręczny „sprawdź aktualizacje” w UI |

### DOC STALE (plany do aktualizacji post-beta)

- `2026-06-07-api-v2-rollout-status.md`
- `2026-06-13-dll-auto-update-modpack-update-notifications-plan.md`
- `2026-06-11-mod-changelog-client-integration-plan.md`
- `2026-06-11-ai-support-launch-diagnostics-plan.md`
- `2026-05-27-implement-discord-oauth2-pkce.md`

---

## Historia review

| Runda | Data | Zakres | Wynik |
|-------|------|--------|-------|
| 1 | 2026-06-16 | Evidence collection, gate A–I (pierwszy przegląd) | BLOCKER: OAuth `state` |
| 2 | 2026-06-16 | Fix pass + re-weryfikacja | BLOCKER=0, MUST FIX=0 |
| 3 | 2026-06-17 | Głęboki code review gate A–I + testy regresji | **READY FOR BETA 1** |
| 4 | 2026-06-17 | Warn cleanup pass (OAuth, logout, i18n, testy) | WARN −8 |
| 5 | 2026-06-17 | Warn cleanup pass 2 (modpack UX, dialogi Epic) | WARN −4 |
| 6 | 2026-06-17 | Warn cleanup pass 3 (i18n checker, DPAPI, dev mode, InfoPanel) | WARN −8 |
| 7 | 2026-06-17 | Backend fix H-R3-1 (mod 13 variants) | API smoke 24/24 |

### Pliki zmienione w fix pass (Runda 2)

`DiscordOAuthService.cs`, `IDiscordOAuthService.cs`, `OAuthLoopbackListener.cs`, `SUStatsConfigViewModel.cs`, `LobbyBoardItemViewModel.cs`, `LobbyBoardPanelViewModel.cs`, `pl.json`, `en.json`, `version.json`, docs .NET 10.

### Pliki zmienione w warn cleanup pass (Runda 4)

`DiscordOAuthPkce.cs`, `DiscordOAuthService.cs`, `ISustatsCredentialsRepository.cs`, `SustatsCredentialsRepository.cs`, `SUStatsConfigViewModel.cs`, `TelemetryService.cs`, `MainWindowViewModel.LaunchDiagnostics.cs`, `MainWindowViewModel.cs`, `MainWindow.axaml`, `pl.json`, `en.json`, `DiscordOAuthPkceTests.cs`.

### Pliki zmienione w warn cleanup pass 2 (Runda 5)

`ModPackInstaller.cs`, `MainWindowViewModel.ModInstances.cs`, `ModInstanceItem.cs`, `PackInstanceDetailDrawer.axaml`, `EpicLoginRequiredDialog.axaml`, `EpicAuthDialog.axaml`, `EpicAuthDialogViewModel.cs`, `PlatformSelectionDialog.axaml`, `SteamQrAuthDialog.axaml`, `SteamQrAuthDialogViewModel.cs`, `pl.json`, `en.json`.

### Pliki zmienione w warn cleanup pass 3 (Runda 6)

`ModPackUpdateChecker.cs`, `ModPackChangeFormatter.cs`, `ModInstanceItem.cs`, `MainWindowViewModel.ModInstances.cs`, `CredentialProtector.cs`, `CredentialProtectionException.cs`, `SUStatsConfigViewModel.cs`, `DeveloperModeSettings.cs`, `UserSettings.cs`, `UserSettingsRepository.cs`, `DatabaseService.cs`, `ModConfigHandler.cs`, `InfoPanel.axaml`, `MainWindowViewModel.cs`, `PromptDialog.axaml`, `pl.json`, `en.json`.

---

## Gate A — Scope / plan reconciliation

**Werdykt:** ✅ PASS (0 BLOCKER, 3 WARN, 4 POST-BETA, 5 DOC STALE)  
**Testy:** build ✅ | test 179/179 | subset **90/90**

### A.1 Modpack custom content

| # | Element | Status | Evidence |
|---|---------|--------|----------|
| A1.1–A1.11 | SHA256, safe write, path traversal, VT block, kreator 3-opcyjny, i18n | ✅ | `ModPackInstaller`, `ModPackService`, `ModPackPreviewViewModel` |
| A1.12 | Custom FULL overlay backend | POST-BETA | klient blokuje `status != clean` |
| A1.13 | Custom FULL GitHub UI | ✅ (klient) | `ModPackCreatorView.axaml.cs` |

### A.2 DLL auto-update + modpack notifications

| # | Element | Status | Evidence |
|---|---------|--------|----------|
| A2.1–A2.7 | Auto-update DLL, badge/toast, `ModPackUpdateChecker` | ✅ | `DllUpdateManager`, `ModInstances.cs` |
| A2.8 | UI lista diff `Changes` | ⚠️ WARN | `PackInstanceDetailDrawer` — tylko badge |
| A2.9 | Update przez katalog FULL | ⚠️ WARN | `UpdateSelectedPackInstanceAsync` :315-365 |
| A2.10 | Ręczny check w UI | POST-BETA | auto przy starcie |
| A2.11 | Brak auto-update paczek | ✅ | read-only checker |

### A.3 AI support / launch diagnostics

| Element | Status |
|---------|--------|
| BepInEx analyzer, Steam supervisor, Defender diagnostics, support bundle, KB client | ✅ |
| Firewall inspector, LLM, admin repair, SQLite sessions | POST-BETA |
| Epic partial supervisor | POST-BETA |
| Hardcoded PL w AXAML launch/AI | ⚠️ WARN |

### A.4–A.7 Pozostałe

| Obszar | Status |
|--------|--------|
| API v2 (CatalogSync, sync_state, compatibility_cache, SHA256) | ✅ (plan API v2 = DOC STALE) |
| Discord OAuth PKCE + `state` | ✅ |
| Changelog modów | ✅ (plan = DOC STALE) |
| DepotDownloader, vanilla cache, deep links, tray, instances | ✅ |
| `version.json` = `3.0.0-beta1` | ✅ |

**Gate A werdykt:** ✅ PASS

---

## Gate B — Security audit

**Werdykt:** ✅ PASS (0 BLOCKER, 11 WARN, 2 POST-BETA)  
**Testy:** subset **50/50**

### Kluczowe PASS

- PKCE S256 + OAuth `state` (naprawione w fix pass)
- Loopback `127.0.0.1:53124`, timeout 5 min, jeden callback
- Tokeny DPAPI → SQLite; Bearer do Discord API
- SHA256 + path traversal + `ValidatePack` blokuje non-clean
- Support bundle/AI: redakcja, limity, `actionCode` allowlist
- Telemetry opt-out; DLL safe replace z backup restore

### Kluczowe WARN

- Logout nie czyści `sustats_credentials` (B-R3-2)
- Debug log AuthUrl ze `state` (B-R3-1)
- Brak testów OAuth (B-R3-5)
- `Secrets.cs` Base64, HTTPS→HTTP fallback ToU, telemetry locale

**Gate B werdykt:** ✅ PASS — 0 BLOCKER

---

## Gate C — Architecture audit

**Werdykt:** ✅ PASS (5 WARN)

- Core bez Avalonia ✅
- `ISUSModderApiClient` + `CatalogSyncService` w DI ✅
- SQLite repos, `ConfigManager` facade ✅
- Modpack: Service / Installer / InstanceInstaller ✅
- WARN: rozproszone HttpClient, VM bez DI, `DeveloperModeSettings` write appsettings, 25 partiali

**Gate C werdykt:** ✅ PASS

---

## Gate D — i18n / PL-EN audit

**Werdykt:** ✅ PASS (8 WARN, 1 POST-BETA)

- Klucze PL/EN: **1189 / 1189** ✅
- `LobbyBoard.Time.*` ✅ (fix pass)
- WARN: `MainWindow.axaml` launch/AI, 4 dialogi Epic/Platform/Steam QR, InfoPanel, PromptDialog

**Gate D werdykt:** ✅ PASS

---

## Gate E — Data/config/migration audit

**Werdykt:** ✅ PASS

- Schema **v11**, migracje v1–v11 z `BackupDatabase()` + rollback
- Tabele: mods, user_settings, discord_auth, sustats_credentials, sync_state, compatibility_cache, mod_instances, …
- JSON→SQLite one-shot, `EnsureDataMigratedIfEmpty`, factory reset bezpieczny
- `.susmodder-install.json` niezależna; brak runtime write user settings do appsettings

**Gate E werdykt:** ✅ PASS

---

## Gate F — Functional E2E workflows

**Werdykt:** ✅ PASS (manual)

Potwierdzenie użytkownika + weryfikacja ścieżek w kodzie: Steam, Epic, modpacki, DLL, changelog, tray, instances, theme, Discord OAuth, SQLite migration.

WARN: brak UI/E2E automation w CI.

**Gate F werdykt:** ✅ PASS

---

## Gate G — Packaging / updater / release

**Werdykt:** ✅ PASS (5 WARN)

- `build-dual-channel.ps1`: `PublishSingleFile=false`, `version.json` + channel, beta suffix, RELEASES, Update.exe ✅
- WARN: unsigned, Velopack 0.0.1298, live endpoint nie testowany, channel switch

**Gate G werdykt:** ✅ PASS

---

## Gate H — Build/test quality

**Werdykt:** ✅ PASS (0 WARN API)

- Release build ✅, Core tests **188/188** ✅
- API smoke: **24/24 OK** (+ 1 EXPECTED lobby 401)
- H-R3-1 (mod 13 variants): ✅ naprawione backend 2026-06-17

**Gate H werdykt:** ✅ PASS

---

## Gate I — Dead code / final polishing

**Werdykt:** ✅ PASS (WARN)

- Brak oczywistych martwych klas/Views
- WARN: `ConfigRepository` legacy, obsolete SUStats/DllUpdate, 11× converter `ConvertBack`, 3 TODO produkcyjne
- Legacy signing scripts — archiwalne

**Gate I werdykt:** ✅ PASS

---

## Rekomendacje post-beta (nie blokują wydania)

1. POC hardening `Secrets.cs` (Base64 → bezpieczniejsze przechowywanie)
2. Refactor: scentralizowany `HttpClient` / DI w ViewModels
3. Migracja z legacy `ConfigRepository`
4. Zaktualizować 5 planów DOC STALE
5. Cleanup obsolete code (`ConvertBack`, legacy signing scripts)
