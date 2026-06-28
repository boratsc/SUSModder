# Plan review gotowości Beta 1 — SUSModder 3.0

**Data:** 2026-06-16  
**Status:** Do wykonania  
**Branch:** `susmodder-3.0`  
**Cel:** systematyczny review, security audit, audyt architektury i weryfikacja zakresu przed wydaniem Beta 1.

---

## 0. Stan wejściowy

- Branch `susmodder-3.0` jest lokalnie czysty.
- Zakres względem `origin/main`: **76 commitów, 553 pliki, ok. 68k insertions**.
- Aktualna wersja w `SUSModder/version.json`: `3.0.0-alpha2`.
- Projekty targetują obecnie `net10.0` / `net10.0-windows`, choć część starszych instrukcji nadal opisuje .NET 8.
- Główne obszary zmian:
  - API v2 i centralny klient HTTP,
  - SQLite/data layer,
  - modpacki, custom DLL/GitHub content, VirusTotal,
  - launch diagnostics i AI support,
  - Discord OAuth2 PKCE / SUStats,
  - UI refresh, lokalne instancje, system tray,
  - Steam DepotDownloader,
  - Velopack packaging/release channels,
  - i18n PL/EN.

---

## 1. Cel

Potwierdzić, czy branch `susmodder-3.0` nadaje się do wydania jako **Beta 1**.

Review ma odpowiedzieć na pytania:

1. Czy planowane funkcje są wdrożone albo jawnie oznaczone jako poza zakresem bety?
2. Czy nie ma blockerów bezpieczeństwa?
3. Czy podstawowe flow użytkownika działa end-to-end?
4. Czy dane użytkownika i instalacje modów są bezpieczne?
5. Czy PL/EN i fallback są spójne?
6. Czy packaging/updater nie popsuje instalacji ani kanałów release/beta?
7. Czy znane ryzyka są sklasyfikowane jako `BLOCKER`, `MUST FIX`, `WARN` albo `POST-BETA`?

---

## 2. Non-goals

- Nie naprawiamy kodu „przy okazji” w trakcie review.
- Nie robimy dużych refactorów.
- Nie dodajemy nowych funkcji.
- Nie próbujemy domknąć wszystkich planów z `DOC/PLAN`, tylko ustalamy, co jest wymagane dla Beta 1.
- Nie uznajemy dokumentacji za prawdę bez weryfikacji w kodzie i testach.

---

## 3. Review master matrix

Każdy punkt review ma dostać status:

| Status | Znaczenie |
|---|---|
| `PASS` | Zweryfikowane dowodem. |
| `WARN` | Ryzyko akceptowalne dla bety, opisane w release notes / known issues. |
| `MUST FIX` | Trzeba naprawić przed betą, ale nie jest to krytyczna luka bezpieczeństwa albo data loss. |
| `BLOCKER` | Nie wydajemy bety. |
| `POST-BETA` | Świadomie poza zakresem Beta 1. |

Dla każdego wpisu zapisujemy:

```text
Area:
Claim / planned item:
Evidence:
Files checked:
Tests run:
Result:
Owner:
Follow-up task:
```

---

## 4. Gate A — Scope / plan reconciliation

### Cel

Sprawdzić, czy to, co planowaliśmy, faktycznie jest wdrożone albo jawnie wyłączone z Beta 1.

### Źródła

- `DOC/PLAN/2026-06-13-modpack-custom-content-plan.md`
- `DOC/PLAN/2026-06-13-dll-auto-update-modpack-update-notifications-plan.md`
- `DOC/PLAN/2026-06-11-ai-support-launch-diagnostics-plan.md`
- `DOC/PLAN/2026-06-11-mod-changelog-client-integration-plan.md`
- `DOC/PLAN/2026-06-07-api-v2-rollout-status.md`
- `DOC/PLAN/2026-06-04-susmodder-client-api-sync-plan.md`
- `DOC/PLAN/2026-05-27-implement-discord-oauth2-pkce.md`
- `DOC/PLAN/2026-05-29-mod-pack-sharing-plan.md`
- `DOC/PLAN/MODPACK_API.md`

### Checklist

- [ ] Dla każdego aktywnego planu wypisać elementy `planned`, `implemented`, `not implemented`, `post-beta`.
- [ ] Zweryfikować dokumenty z kodem, nie tylko z deklaracją statusu.
- [ ] Oznaczyć sprzeczności dokumentacji jako `DOC STALE` albo `REAL GAP`.
- [ ] Ustalić, czy custom FULL overlay validation jest `POST-BETA`, czy `BLOCKER`.
- [ ] Ustalić, czy DLL auto-update i modpack update notifications są wymagane dla Beta 1.
- [ ] Ustalić, czy AI support ma być włączony w becie, czy tylko launch diagnostics.
- [ ] Ustalić docelowe oznaczenie wersji Beta 1 w `version.json`.

### Wstępne czerwone flagi

- Custom FULL overlay validation jest opisany jako celowy stub.
- Część dokumentów API v2 może być nieaktualna względem kodu (`CatalogSyncService`, `sync_state`, `compatibility_cache`).
- Część dokumentów updatera/signingu jest archiwalna albo sprzeczna z aktualną decyzją o unsigned builds.
- `version.json` nadal wskazuje `3.0.0-alpha2`.

---

## 5. Gate B — Security audit

### Cel

Upewnić się, że Beta 1 nie wprowadza ryzyk: token leakage, path traversal, nieautoryzowana instalacja DLL, data exfiltration, data loss.

### Zakres

#### Discord OAuth2 PKCE / SUStats

- [ ] PKCE używa poprawnego `code_verifier` i `code_challenge`.
- [ ] Flow ma `state` parameter i walidację state.
- [ ] Redirect URI używa `127.0.0.1`, nie `localhost`.
- [ ] Lokalny callback ma timeout i limit jednego callbacku.
- [ ] Tokeny nie są logowane.
- [ ] Logout usuwa lokalne PII i próbuje revoke token.
- [ ] Access token nie jest wysyłany niepotrzebnie w body, jeśli kontrakt pozwala na `Authorization: Bearer`.
- [ ] DPAPI error path jest obsłużony user-visible błędem.

#### Secrets / API auth

- [ ] Brak nowych sekretów w repo.
- [ ] `SecretProvider` nie rozszerzył powierzchni ekspozycji tokenów.
- [ ] Logi diagnostyczne nie zawierają tokenów ani pełnych credentials.
- [ ] Nie ma nieuzasadnionych fallbacków HTTPS → HTTP.

#### Modpack/custom content

- [ ] Custom DLL ma SHA256 verification przed zapisem.
- [ ] GitHub DLL ma status backend/VirusTotal przed instalacją.
- [ ] Pending/suspicious/malicious artifacts blokują install.
- [ ] Path traversal jest zablokowany: `..`, absolutne ścieżki, ADS `:`, pliki bez `.dll`.
- [ ] `DllInstallPath` jest whitelistowane pod `BepInEx/plugins`.
- [ ] Safe write używa temp file + replace i nie usuwa starej DLL przy failure.
- [ ] Custom FULL overlay nie pozwala obejść walidacji backendu.

#### Launch diagnostics / AI support

- [ ] Support bundle nie wysyła pełnych prywatnych logów bez zgody.
- [ ] Payload ma limity długości i sanity checks.
- [ ] Ścieżki użytkownika są anonimizowane albo ograniczane.
- [ ] AI support nie wykonuje admin actions automatycznie.
- [ ] Defender/Firewall diagnostics są best-effort i nie crashują aplikacji.

#### Telemetry/privacy

- [ ] Telemetry opt-out działa.
- [ ] Wysyłany locale to canonical `pl`/`en`, nie raw system locale.
- [ ] User hash/HWID nie jest odwracalny.
- [ ] Privacy copy opisuje Discord User ID / userHash / telemetry.
- [ ] Logout usuwa dane osobowe powiązane z Discordem.

### Kandydaci na `BLOCKER`

- Brak `state` w OAuth.
- Tokeny lub credentials w logach.
- Możliwość instalacji DLL poza dozwolonym folderem.
- Custom artifact install bez checksum.
- VirusTotal suspicious/malicious nie blokuje install.
- Support/AI wysyła prywatne dane bez zgody.
- Ryzyko data loss przy update/install.

---

## 6. Gate C — Architecture audit

### Cel

Upewnić się, że zmiany są zgodne z granicami produktu: Avalonia UI + `SUSModder.Core` jako business logic, SQLite jako runtime config, `appsettings.json` read-only, kompatybilność backendu `susmodder.app` / API v2.

### Core business logic responsibilities

- [ ] `SUSModder.Core` nie zależy od UI/Avalonia.
- [ ] API v2 idzie przez `ISUSModderApiClient`, a nie przez rozproszone klienty HTTP tam, gdzie powinien być centralny gateway.
- [ ] `ConfigManager` deleguje do SQLite repozytoriów.
- [ ] `UserSettingsService` używa domyślnego repozytorium SQLite po starcie aplikacji.
- [ ] `appsettings.json` pozostaje read-only runtime.
- [ ] Mod install/update/delete nie obchodzi `InstallationMapManager`.
- [ ] Local instances nie psują zwykłych instalacji Steam/Epic.
- [ ] Modpack install/update ma jasną granicę odpowiedzialności między `ModPackService`, `ModPackInstaller`, `ModInstanceInstaller`.
- [ ] `CatalogSyncService`, `sync_state`, `compatibility_cache` mają jasny fallback offline.

### UI/Avalonia responsibilities

- [ ] ViewModels nie wykonują surowej logiki instalacji, tylko wywołują Core/services.
- [ ] Dialogi mają cancel/error/loading states.
- [ ] System tray nie ukrywa krytycznych błędów.
- [ ] Modpack creator/preview jasno pokazuje non-clean/pending artifacts.
- [ ] Launch diagnostics UI nie pokazuje technicznych stack trace jako user copy.
- [ ] `MainWindowViewModel` partiale nie mają duplikacji sprzecznej logiki.

### Config/migration implications

- [ ] Nowe tabele mają migracje w `DatabaseService` z `PRAGMA user_version`.
- [ ] Query SQL są parametryzowane.
- [ ] Column names, jeśli dynamiczne, są whitelistowane.
- [ ] Fallback JSON działa tylko jako legacy/recovery, nie jako nowe źródło prawdy.
- [ ] `.susmodder-install.json` pozostaje niezależną mapą instalacji.

---

## 7. Gate D — i18n / PL-EN audit

### Cel

Potwierdzić, że Beta 1 spełnia wymagania multilingual: PL i EN od startu, fallback PL, brak hardcoded user-facing copy poza świadomie zaakceptowanymi wyjątkami.

### Checklist

- [ ] Wszystkie nowe user-facing stringi mają klucze i18n.
- [ ] `SUSModder/Localization/pl.json` i `SUSModder/Localization/en.json` mają komplet kluczy.
- [ ] Placeholdery są zgodne między PL/EN.
- [ ] Count/plural strings używają formatu nadającego się do pluralizacji.
- [ ] Core errors mają stabilny error code + fallback techniczny, a UI mapuje je na lokalizację.
- [ ] Language selection i settings language switch działają.
- [ ] Telemetry wysyła canonical locale `pl`/`en`.
- [ ] Future locale można dodać przez locale JSON/metadata, nie zmianę logiki.

### Wstępnie znalezione hardcoded copy do klasyfikacji

- `SUSModder/Views/EpicAuthDialog.axaml`
- `SUSModder/Views/EpicLoginRequiredDialog.axaml`
- `SUSModder/Views/LanguageSelectionDialog.axaml`
- `SUSModder/Views/PlatformSelectionDialog.axaml`
- `SUSModder/Views/MainWindow.axaml` — placeholder AI support
- `SUSModder/Views/SteamQrAuthDialog.axaml`
- `SUSModder/ViewModels/LobbyBoardItemViewModel.cs` — `Przed chwilą`, `min temu`, `h temu`

Te punkty nie są automatycznie blockerami, ale muszą dostać status `MUST FIX`, `WARN` albo `POST-BETA`.

---

## 8. Gate E — Data/config/migration audit

### Cel

Potwierdzić, że Beta 1 nie niszczy danych użytkownika i poprawnie migruje runtime config.

### Scenariusze

- [ ] Fresh install bez istniejącego `%APPDATA%/SUSModder`.
- [ ] Upgrade z JSON configów do SQLite.
- [ ] Upgrade z poprzedniej wersji SQLite.
- [ ] Recovery po pustych tabelach po migracji.
- [ ] Corrupted DB — aplikacja pokazuje bezpieczny błąd albo recovery path.
- [ ] `.susmodder-install.json` zostaje zachowany.
- [ ] `user_settings` zachowuje:
  - `language`,
  - `telemetry_enabled`,
  - `update_channel`,
  - `mods_install_path`,
  - `license_accepted`,
  - `mode`,
  - tray/settings flags.
- [ ] `mod_instances` i `mod_pack_history` migrują bez błędów.
- [ ] `sync_state` i `compatibility_cache` działają offline.
- [ ] Factory reset nie usuwa niezamierzonych danych.
- [ ] Aplikacja nie zapisuje runtime do `appsettings.json`.

---

## 9. Gate F — Functional E2E workflows

### Steam

- [ ] Auto-detect Among Us Steam.
- [ ] DepotDownloader flow.
- [ ] Vanilla cache.
- [ ] Full mod install.
- [ ] DLL install/update/uninstall.
- [ ] Launch mod.
- [ ] Launch diagnostics po błędzie.
- [ ] Update moda bez utraty starej instalacji.
- [ ] Offline/error state jest czytelny.

### Epic

- [ ] Auto-detect `.egstore`.
- [ ] Epic auth flow.
- [ ] Legendary download/use.
- [ ] Install/reinstall.
- [ ] Launch.
- [ ] Error recovery.
- [ ] Zmiana Epic ↔ Steam nie psuje konfiguracji.

### Modpacki

- [ ] Create local.
- [ ] Create and share.
- [ ] Share online.
- [ ] Import przez code.
- [ ] Import przez `susmodder://pack`.
- [ ] Custom DLL local upload.
- [ ] GitHub DLL declaration.
- [ ] Custom FULL overlay sklasyfikowany: `POST-BETA` albo `BLOCKER`.
- [ ] Partial failure pokazuje czytelny wynik.
- [ ] Non-clean artifact blokuje install.
- [ ] Modpack update notification działa albo jest jawnie poza Beta 1.

### Changelogi modów

- [ ] Panel szczegółów moda.
- [ ] Post-install success.
- [ ] Update success.
- [ ] Cache TTL / ETag / 304.
- [ ] PL/EN lang normalization.
- [ ] 404/empty changelog nie crashuje UI.

### Launch diagnostics / AI support

- [ ] BepInEx log analyzer.
- [ ] Windows Defender/Firewall diagnostics.
- [ ] Support bundle generation.
- [ ] AI support disabled/fallback path, jeśli backend feature flag jest off.
- [ ] User-facing guidance bez admin auto-actions.

### UI refresh / tray / local instances

- [ ] Main layout.
- [ ] Detail drawer.
- [ ] Bulk mode.
- [ ] System tray minimize/restore/quick launch.
- [ ] Local instances list/detail/clone/delete.
- [ ] Theme switching.
- [ ] Responsywność i brak UI freeze w długich operacjach.

---

## 10. Gate G — Packaging / updater / release

### Cel

Potwierdzić, że Beta 1 da się zbudować, spakować i zaktualizować przez właściwy kanał bez regresji.

### Checklist

- [ ] `SKRYPTY/Build/build-dual-channel.ps1` używa `PublishSingleFile=false` dla Velopack.
- [ ] Skrypt generuje `RELEASES` bez suffixu.
- [ ] Skrypt generuje poprawny beta channel.
- [ ] `version.json` w publish output ma właściwą wersję Beta 1.
- [ ] `releases.beta.json` trafia do katalogu czytanego przez API.
- [ ] `https://susmodder.app/api/releases?channel=beta` zwraca właściwy manifest.
- [ ] `.nupkg`, `RELEASES`, `Setup.exe`, `Portable.zip`, `Update.exe` istnieją tam, gdzie oczekuje workflow.
- [ ] Update beta → beta działa.
- [ ] Channel switch release ↔ beta działa albo znane ograniczenie jest opisane.
- [ ] Unsigned build risk jest opisany w known issues / release notes.
- [ ] AV/SmartScreen ryzyka są świadomie zaakceptowane.

### Wstępne ryzyka

- Projekt używa Velopack `0.0.1298`, a najnowszy NuGet to `1.2.0`.
- Dokumentacja częściowo opisuje podpisywanie jako wymagane, mimo aktualnej decyzji o unsigned builds.
- `version.json` nadal jest alpha.

---

## 11. Gate H — Build/test quality

### Minimum przed Beta 1

```powershell
dotnet restore SUSModder.sln
dotnet build SUSModder.sln -c Debug
dotnet build SUSModder.sln -c Release
dotnet test SUSModder.Core.Tests\SUSModder.Core.Tests.csproj -c Release
.\SKRYPTY\Test\test-api-v2-client.ps1
.\SKRYPTY\Test\test-modpack-custom-content-v2.ps1 -ValidateOnly
.\SKRYPTY\Build\build-dual-channel.ps1 -Version <beta-version> -SkipRelease
```

### Dodatkowe smoke tests

- [ ] Uruchomienie aplikacji z czystym profilem.
- [ ] Uruchomienie aplikacji z istniejącym profilem użytkownika.
- [ ] Tryb offline.
- [ ] Wolne API / timeout.
- [ ] Brak Among Us.
- [ ] Brak uprawnień do katalogu modów.
- [ ] Defender Controlled Folder Access, jeśli możliwe.
- [ ] Update z poprzedniej wersji dev/alpha.

---

## 12. Gate I — Martwy kod / final polishing

### Cel

Zidentyfikować kod, zasoby i dokumentację, które nie są już używane po dużych zmianach brancha, oraz wskazać bezpieczne porządki przed Beta 1. Martwy kod może mieć dwa źródła: **legacy zastąpione przez nowe mechanizmy 3.0** oraz **pre-existing dead code / stare niedopatrzenia z 2.x i wcześniejszych etapów**, które dopiero teraz warto ujawnić przed betą. Ten gate nie ma wymuszać dużych refactorów — chodzi o końcowy polishing, usunięcie oczywistych śmieci i ograniczenie ryzyka utrzymywania martwych ścieżek.

### Zasady

- Nie usuwamy niczego tylko na podstawie jednego grepa.
- Rozdzielamy znaleziska na `3.0-replaced legacy` oraz `pre-existing dead code`, bo mają inne ryzyko i inną historię decyzji.
- Każdy kandydat do usunięcia musi mieć evidence: brak referencji, brak dynamicznego ładowania, brak użycia przez XAML/resources/reflection/build scripts.
- Elementy ryzykowne klasyfikujemy jako `POST-BETA`, nie ruszamy ich przed betą.
- Polishing nie może opóźnić security/data-loss blockerów.
- Drobne cleanupy można zrobić przed betą tylko jeśli są małe, mechaniczne i łatwe do zweryfikowania.

### Obszary do sprawdzenia

#### Kod C#

- [ ] Klasy bez referencji w `SUSModder.Core`.
- [ ] Klasy bez referencji w `SUSModder` UI.
- [ ] Stare serwisy zastąpione przez API v2 / SQLite / nowe repozytoria.
- [ ] Stare ścieżki 2.x zastąpione przez funkcje 3.0, np. API v1, JSON config, legacy updater, stare install/update flow.
- [ ] Pre-existing dead code z 2.x lub wcześniejszych refactorów, niezależny od aktualnego brancha.
- [ ] Duplikaty starych i nowych flow instalacji/update.
- [ ] Niepodłączone ViewModels.
- [ ] Niepodłączone Views/dialogi.
- [ ] Martwe partiale `MainWindowViewModel.*`.
- [ ] Stare modele DTO zastąpione przez API v2 models.
- [ ] Stare helpery path/config/network.
- [ ] Puste lub placeholderowe klasy.
- [ ] `TODO`, `FIXME`, `HACK`, `NotImplementedException` — sklasyfikować jako realny problem albo akceptowalny pattern, np. `ConvertBack` w one-way converterach.

#### Avalonia / XAML / resources

- [ ] Niepodłączone `.axaml`.
- [ ] Niepodłączone style i resources.
- [ ] Niepodłączone konwertery.
- [ ] Niepodłączone assets/icons/images.
- [ ] Stare theme resources po UI refresh.
- [ ] Dead bindings po zmianach ViewModeli.
- [ ] Resource keys nieużywane albo zdublowane.

#### Lokalizacja

- [ ] Klucze i18n nieużywane po UI refresh.
- [ ] Klucze brakujące względem kodu.
- [ ] Zduplikowane sekcje PL/EN.
- [ ] Tymczasowe hardcoded copy, które miały zostać przeniesione do lokalizacji.

#### Dokumentacja i skrypty

- [ ] Stare docs przenieść/oznaczyć jako archive, jeśli przeczą aktualnemu stanowi.
- [ ] Stare instrukcje podpisywania vs aktualna decyzja unsigned builds.
- [ ] Skrypty build/deploy, które nie powinny być używane przy Beta 1, oznaczyć jako legacy albo odseparować.
- [ ] Nieaktualne test scripts / dev scripts.
- [ ] Nieaktualne README/statusy planów.

#### Dependencies / packages

- [ ] Pakiety NuGet używane faktycznie w kodzie.
- [ ] Pakiety dodane przez eksperymenty, ale nieużywane.
- [ ] `Avalonia.Diagnostics` nie trafia do Release assets poza zamierzonym warunkiem.
- [ ] WebView/SharpCompress/Velopack usage zgodny z realnym kodem.

#### Build output / repo hygiene

- [ ] Brak przypadkowych artefaktów build/test w repo.
- [ ] `.gitignore` obejmuje nowe katalogi generowane przez build/test.
- [ ] Brak lokalnych konfiguracji IDE/secrets, które nie powinny być śledzone.
- [ ] Line endings i `.gitattributes` nie generują masowego szumu.

### Sugerowane narzędzia weryfikacji

- `grep` dla `TODO|FIXME|HACK|NotImplementedException|Obsolete|Deprecated`.
- `grep`/AST-grep dla klas, ViewModels, Views i konwerterów.
- LSP references dla podejrzanych symboli.
- Build Release po każdym cleanupie.
- Manual XAML smoke dla usuwanych Views/resources.
- `dotnet test` po cleanupie Core.

### Klasyfikacja wyników

| Typ znaleziska | Decyzja |
|---|---|
| Oczywisty nieużywany plik bez dynamicznych referencji | `MUST FIX` albo `POST-BETA`, zależnie od ryzyka. |
| Nieaktualny dokument mylący release | `MUST FIX` dla release docs, `WARN`/`POST-BETA` dla historycznych docs. |
| Niepodłączony eksperymentalny UI | `POST-BETA`, jeśli usunięcie jest ryzykowne. |
| Tymczasowy placeholder widoczny dla użytkownika | `MUST FIX`. |
| Dead code w krytycznym install/update/security path | `MUST FIX`, jeśli może być przypadkowo użyty. |
| `ConvertBack` throwing w one-way converterze | Zwykle `WARN`/accepted, jeśli nie ma bindingu TwoWay. |

### Output tego gate’u

- Lista kandydatów do usunięcia albo oznaczenia jako legacy.
- Lista rzeczy do poprawienia przed betą.
- Lista rzeczy świadomie odłożonych po becie.
- Krótka decyzja: czy polishing jest bezpieczny przed Beta 1, czy cleanup robimy po wydaniu.

---
## 13. Platform, packaging, updater, telemetry, privacy, AV constraints

### Platform

- Beta 1 jest traktowana jako Windows desktop release.
- Linux TODO w `CredentialProtector` nie blokuje Windows Beta 1, ale musi być oznaczony jako `POST-BETA` / future platform.
- `net10.0` wymaga świadomej decyzji release: akceptujemy .NET 10, albo wracamy do .NET 8 zgodnie ze starszą dokumentacją.

### Packaging/updater

- Velopack wymaga unpacked publish.
- Buildy są unsigned, więc AV/SmartScreen risk musi być jawnie opisany.
- Release/beta channels muszą mieć osobną walidację.

### Telemetry/privacy

- Telemetry musi respektować opt-out.
- User hash/HWID nie może być odwracalny.
- Support bundle i AI support nie mogą wysyłać prywatnych danych bez zgody.
- Discord OAuth wymaga privacy notice update.

### AV

Szczególnie ryzykowne obszary:

- download DLL/ZIP,
- replace files,
- updater,
- DepotDownloader,
- legendary,
- VirusTotal pending/suspicious handling,
- unsigned binaries.

---

## 14. Workstreamy review

### Workstream 1 — Plan reconciliation

**Owner:** planner/doc reviewer  
**Output:** tabela `planned vs implemented vs beta scope`.

Zakres:

- `DOC/PLAN/*`
- `DOC/POC/*`
- release docs
- znane TODO/stuby

### Workstream 2 — Security audit

**Owner:** security reviewer  
**Output:** lista `BLOCKER/MUST FIX/WARN`.

Zakres:

- Discord OAuth2,
- custom DLL/GitHub content,
- path traversal,
- SHA256,
- VirusTotal,
- secrets/logging,
- telemetry/privacy,
- support bundle.

### Workstream 3 — Architecture audit

**Owner:** senior reviewer  
**Output:** architecture risk notes + decyzje.

Zakres:

- API v2 boundaries,
- SQLite repo pattern,
- Core/UI separation,
- appsettings read-only,
- install/update transaction safety,
- compatibility/cache sync.

### Workstream 4 — i18n/copy audit

**Owner:** i18n reviewer  
**Output:** missing keys/hardcoded copy list.

Zakres:

- `.axaml`,
- ViewModels,
- Core user-facing messages,
- `pl.json`,
- `en.json`,
- placeholders.

### Workstream 5 — Functional QA

**Owner:** QA/manual tester  
**Output:** smoke/E2E checklist z logami/screenshotami.

Zakres:

- Steam,
- Epic,
- full mod,
- DLL,
- modpack,
- changelog,
- diagnostics,
- tray/settings.


### Workstream 6 — Packaging/release

**Owner:** release engineer  
**Output:** lokalny beta package + endpoint verification.

Zakres:

- build scripts,
- Velopack output,
- version bump,
- beta channel,
- update simulation,
- server upload checklist.

### Workstream 7 — Dead code / final polishing

**Owner:** code quality reviewer  
**Output:** lista kandydatów do cleanupu z klasyfikacją `MUST FIX/WARN/POST-BETA`.

Zakres:

- nieużywane klasy/metody,
- stare flow zastąpione przez nowe,
- niepodłączone Views/ViewModels/converters,
- nieużywane assets/resources/styles,
- nieaktualne docs/skrypty,
- zbędne dependencies,
- TODO/FIXME/NotImplementedException classification.

---

## 15. Suggested implementation order for review

### Faza 1 — Evidence collection

1. Freeze branch for review.
2. Generate changed-files inventory.
3. Run build/test baseline.
4. Fill planned-vs-implemented matrix.
5. Run hardcoded i18n scan.
6. Run security grep/audit pass.
7. Run docs freshness pass.

### Faza 2 — Specialist reviews równolegle

Równolegle:

- security audit,
- architecture audit,
- i18n audit,
- functional QA,
- packaging audit.

### Faza 3 — Triage

Każde znalezienie klasyfikujemy:

- `BLOCKER`,
- `MUST FIX`,
- `WARN`,
- `POST-BETA`.

Beta może wyjść tylko jeśli:

- `BLOCKER = 0`,
- `MUST FIX = 0`,
- `WARN` mają release note / known issues,
- `POST-BETA` mają taski.

### Faza 4 — Fix pass

Dopiero tutaj robimy implementację naprawczą.

### Faza 5 — Final release candidate

1. Re-run full test matrix.
2. Build beta package.
3. Install on clean machine/profile.
4. Upgrade from previous alpha/dev profile.
5. Verify beta update channel.
6. Prepare release notes + known issues.

---

## 16. Minimalna lista rzeczy do odhaczenia przed Beta 1

- [ ] Build Debug/Release przechodzi.
- [ ] Wszystkie testy Core przechodzą.
- [ ] API v2 smoke script przechodzi.
- [ ] Modpack custom content validate script przechodzi.
- [ ] Steam full mod install/update/launch działa.
- [ ] Epic auth/install/launch działa albo jest jasno poza zakresem bety.
- [ ] SQLite migration/fresh install działa.
- [ ] Brak runtime writes do `appsettings.json`.
- [ ] Discord OAuth security findings rozstrzygnięte.
- [ ] Custom DLL path traversal/SHA256/VirusTotal zweryfikowane.
- [ ] Hardcoded PL/EN sklasyfikowane.
- [ ] `version.json` ustawione na wersję Beta 1.
- [ ] Velopack beta package zbudowany lokalnie.
- [ ] Beta update endpoint zwraca właściwy manifest.
- [ ] Known issues gotowe.
- [ ] Martwy kod / final polishing audit wykonany i sklasyfikowany.
- [ ] Oczywiste user-visible placeholdery i nieaktualne release docs usunięte albo poprawione.
- [ ] Privacy/telemetry copy zgodne z realnym zachowaniem.

---

## 17. Źródła użyte przy przygotowaniu planu

- `mcp-rag` repo discovery.
- Agent scans: `explore`, `sus-free-doc-scout`, `librarian`, `oracle`.
- Git diff/status względem `origin/main`.
- Lokalne pliki:
  - `DOC/PLAN/*`,
  - `SUSModder/version.json`,
  - `SUSModder/appsettings.json`,
  - `SUSModder.Core/Data/*`,
  - `SUSModder.Core/Services/*`,
  - `SUSModder/Localization/*.json`,
  - `SKRYPTY/Build/*`,
  - `SKRYPTY/Test/*`.
- `microsoft-learn` dla założeń `.NET publish`, single-file, ReadyToRun.
- `nuget` dla wersji pakietów.





