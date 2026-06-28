# Plan: AI Support Assistant + diagnostyka uruchamiania/Defender/Firewall

**Data:** 2026-06-11  
**Status:** Plan do wdrożenia etapowego  
**Priorytet:** P1; lokalna diagnostyka launch jako pierwszy milestone  
**Zakres:** SUSModder desktop Windows + `SUSModder.Core` + backend `susmodder.app`/API v2 support  
**Docelowy kontrakt:** `DOC/POC/API v2/contracts/AI_SUPPORT_API.md`

Powiązane POC:
- `DOC/POC/2026-06-09-windows-defender-firewall-launch-diagnostics-poc.md`
- `DOC/POC/2026-06-09-ai-support-assistant-backend-poc.md`
- `DOC/PLAN/2026-06-07-api-v2-rollout-status.md`
- `DOC/PLAN/2026-06-04-susmodder-client-api-sync-plan.md`

---

## 0. Decyzje bazowe

1. **Najpierw diagnostyka lokalna, potem asystent.** AI Support bez stabilnych `diagnosisCodes`, parsera BepInEx i lokalnego support bundle będzie odpowiadał zbyt ogólnie.
2. **Kontrakt API jest źródłem prawdy dla klienta.** Implementujemy `AI_SUPPORT_API.md`: `X-User-Hash`, `actionCode` allowlist, brak uploadu ZIP, limity `bepInExSummary`.
3. **KB-only jest trybem startowym.** LLM pozostaje wyłączony feature flagą backendu. Klient obsługuje `source=llm`, ale MVP nie wymaga LLM.
4. **Naprawy admin są osobnym etapem.** MVP nie dodaje wyjątków Defender/Firewall automatycznie. Pierwsza wersja pokazuje diagnozę, instrukcje, foldery/logi i raport.
5. **Nie uruchamiamy całej aplikacji jako administrator.** Tylko konkretna naprawa może uruchomić elevated helper/PowerShell po jasnej zgodzie użytkownika.
6. **Nie piszemy runtime do `appsettings.json`.** Ustawienia użytkownika idą do SQLite; endpointy są read-only build/deploy config.
7. **PL i EN są wymagane w MVP.** PL fallback; przyszły język przez locale/KB metadata, nie przez zmianę logiki.

### Rozbieżności do rozstrzygnięcia w WP0

| Temat | Obserwacja | Decyzja planu |
|---|---|---|
| Base URL | Kontrakt: `https://susmodder.app/api/v2/support`; obecny klient v2: `https://api.susmodder-cdn.ovh/v2`. | WP0 robi probe produkcji/staging. Klient używa jednego finalnego support base URL albo read-only `SupportApiBaseUrl`. |
| `userHash` | POC ma `userHash` w body, kontrakt wymaga `X-User-Hash`. | Implementować kontrakt: header, nie body. |
| Provider LLM | POC mówi Gemini, kontrakt/privacy notice wspomina OpenRouter. | UI provider-neutral: „model AI może zostać użyty przez backend, jeśli funkcja jest włączona”. |
| `requiresAdmin` | Kontrakt ma `steps[].requiresAdmin`; schema LLM POC nie zawsze. | Backend normalizuje `requiresAdmin: boolean`; klient nigdy nie auto-wykonuje admin action. |
| Status kontraktu | Kontrakt mówi Production KB-only, klient nie ma integracji. | Traktować jako target; WP0 potwierdza realny endpoint przed kodem klienta. |

---

## 1. Goal i non-goals

### Goal

- Zmniejszyć liczbę zgłoszeń „mod nie działa” przez lokalną, zrozumiałą diagnostykę startu gry.
- Ustandaryzować pierwszą linię wsparcia dla: launch, BepInEx, Defender/AV, Firewall/network, instalacja, wersje modów, Proximity Chat, SUStats.
- Wysyłać do `/api/v2/support/query` tylko minimalny, zanonimizowany kontekst diagnostyczny i opis użytkownika.
- Dostarczyć lokalny `SUSModder Support Bundle` ZIP do ręcznego dołączenia na Discordzie, bez automatycznego uploadu.
- Przygotować bezpieczną ścieżkę przyszłych napraw admin: plan zmian → zgoda → UAC → możliwość cofnięcia.
- Zachować kompatybilność z API v2, SQLite runtime settings, Steam/Epic flows i Velopack updaterem.

### Non-goals

- Brak ticket systemu, Discord bota i obietnicy kontaktu supportu.
- Brak wysyłania pełnych logów, ZIP-ów, crash dumpów, tokenów, pełnych ścieżek i konfiguracji systemu do backendu/LLM w MVP.
- Brak dowolnych komend PowerShell generowanych przez AI.
- Brak globalnego wyłączania Defendera, SmartScreen, PUA, Controlled Folder Access lub Firewall.
- Brak wymogu admina do zwykłego działania SUSModdera.
- Brak blokowania MVP na embeddingach, pgvector, panelu admina KB lub LLM.

---

## 2. User workflow

### 2.1 Normalny launch moda

1. Użytkownik klika `Uruchom`.
2. UI pokazuje `Uruchamianie…`, a nie natychmiastowy sukces po `Process.Start`.
3. Core tworzy `LaunchAttempt`: `attemptId`, `modId`, `modName`, `modType`, `platformMode`, `installPath`, `exePath`, `startedAtUtc`.
4. `LaunchSupervisor` startuje Steam/Epic flow i obserwuje pierwsze 30-60 sekund.
5. Jeśli proces działa i BepInEx log został odświeżony, UI kończy progress bez dialogu błędu.

### 2.2 Crash / brak BepInEx / podejrzenie AV

1. Proces nie startuje, zamyka się wcześnie albo log BepInEx jest missing/stale.
2. Core analizuje: `LogOutput.log`, `ErrorLog.log`, snapshot `BepInEx\plugins`, status plików, best-effort Defender events.
3. Core zwraca stabilne `diagnosisCodes` i severity/confidence.
4. UI pokazuje lokalizowany dialog: podsumowanie, sygnały, przyciski `Otwórz folder moda`, `Otwórz logi`, `Kopiuj diagnozę`, `Przeinstaluj`, `Utwórz pakiet wsparcia`, `Pomoc SUSModder/AI`.
5. Jeśli diagnoza jest niepewna, UI mówi „Nie mamy pewności” i proponuje raport.

### 2.3 AI Support / Pomoc SUSModder

1. Użytkownik otwiera `Pomoc SUSModder` ręcznie lub z dialogu diagnostycznego.
2. Przy pierwszym użyciu UI pokazuje privacy notice:
   - wysyłamy: opis problemu, kody diagnozy, wersja aplikacji, Steam/Epic, update channel, krótkie redacted summary logów,
   - nie wysyłamy: pełne logi, ZIP, tokeny, ścieżki użytkownika, crash dumps,
   - backend może użyć modelu AI tylko gdy funkcja jest włączona.
3. Użytkownik wpisuje problem i może zostawić `Dołącz podstawową diagnostykę`.
4. Klient wysyła `POST /api/v2/support/query` z timeoutem 15 s, bez auto-retry.
5. UI pokazuje `summary`, `steps`, `warnings`, `safetyNotice`, `matchedArticles`, confidence/source i przyciski z `actionCode` allowlist.
6. Użytkownik wybiera `Pomogło` lub `Problem nadal występuje`; klient wysyła best-effort feedback.

### 2.4 Problem nadal występuje

1. UI wysyła feedback `not_helped`.
2. UI proponuje `Wygeneruj raport diagnostyczny`.
3. Core tworzy lokalny ZIP i pokazuje listę zawartości przed otwarciem folderu.
4. UI wysyła `POST /report-metadata` bez ZIP-a/logów.
5. UI pokazuje link Discord i tekst do skopiowania z `supportSessionId`, kodami diagnozy i artykułami.

### 2.5 Guided Repair z adminem (po MVP)

1. UI pokazuje dokładny plan: typ wyjątku, ścieżka, nazwa reguły, jak cofnąć.
2. Użytkownik klika `Dodaj wyjątek (wymaga administratora)`.
3. Windows pokazuje UAC dla osobnego procesu/elevated helpera.
4. Helper wykonuje tylko przygotowany allowlistowany plan.
5. Core zapisuje wynik w SQLite i oferuje `Cofnij wyjątek`.

---

## 3. Language / i18n impact

- Locale MVP: `pl`, `en`; fallback: `pl`.
- Wszystkie nowe user-facing strings w UI jako i18n keys w `SUSModder/Localization/pl.json` i `en.json`.
- Core zwraca stabilne kody (`diagnosisCodes`, `errorCode`) + techniczny fallback; UI mapuje kody na copy.
- Placeholdery muszą mieć parytet PL/EN: `{modName}`, `{path}`, `{count}`, `{supportSessionId}`, `{version}`.
- Jeśli pojawia się liczba problemów, unikać odmiany w MVP albo użyć ICU/pluralizacji po wdrożeniu mechanizmu.
- `language` wysyłany do API ma być kanoniczny `pl`/`en`, nie raw system locale.
- Future locale: dodanie pliku locale + KB articles/metadata, bez zmian w `LaunchSupervisor`, parserach i action mapping.

Minimalne grupy kluczy klienta:

```text
LaunchDiagnostics.Title
LaunchDiagnostics.Progress.Starting
LaunchDiagnostics.Progress.Observing
LaunchDiagnostics.Summary.ProcessStartFailed
LaunchDiagnostics.Summary.ExitedEarly
LaunchDiagnostics.Summary.BepInExMissing
LaunchDiagnostics.Summary.DefenderPossible
LaunchDiagnostics.Summary.FirewallPossible
LaunchDiagnostics.Summary.Unknown
LaunchDiagnostics.Actions.OpenModFolder
LaunchDiagnostics.Actions.OpenLogs
LaunchDiagnostics.Actions.CopyDiagnosis
LaunchDiagnostics.Actions.CreateSupportBundle
LaunchDiagnostics.Actions.ReinstallMod
LaunchDiagnostics.Actions.OpenAiSupport
LaunchDiagnostics.Privacy.SupportBundleNotice
LaunchDiagnostics.AdminConsent.Explanation

AiSupport.Title
AiSupport.ProblemPlaceholder
AiSupport.IncludeDiagnostics
AiSupport.PrivacyNotice
AiSupport.AnalyzeButton
AiSupport.Result.Summary
AiSupport.Result.Steps
AiSupport.Result.Warnings
AiSupport.Result.SafetyNotice
AiSupport.Actions.Helped
AiSupport.Actions.NotHelped
AiSupport.Actions.GenerateReport
AiSupport.Actions.JoinDiscord
AiSupport.Errors.Validation
AiSupport.Errors.RateLimited
AiSupport.Errors.ServiceUnavailable
AiSupport.Errors.Timeout
AiSupport.Errors.UnknownActionIgnored
```

Backend KB:
- KB ma teksty PL/EN albo canonical article z `translations`.
- Jeśli brak artykułu EN, backend może zwrócić fallback PL tylko z `fallbackLocale: "pl"`; UI informuje neutralnie.
- LLM, jeśli włączony, odpowiada w `request.language`, ale tylko na bazie locale-matched KB.
- Feedback zapisuje `language` jako `pl`/`en`.

---

## 4. Core business logic responsibilities

### 4.1 Diagnostyka launch

Nowe komponenty w `SUSModder.Core`:

| Komponent | Odpowiedzialność |
|---|---|
| `ILaunchSupervisor` / `LaunchSupervisor` | wspólny model startu i obserwacji Steam/Epic; cancellation; timeout 30-60 s; wynik `LaunchResult` |
| `LaunchAttempt` / `LaunchResult` / `DiagnosisCode` | stabilne DTO, severity, timestamps, process info, BepInEx status, support bundle path |
| `BepInExLogAnalyzer` | ogon `LogOutput.log`/`ErrorLog.log`, klasyfikacja critical/warning/info, ignorowanie benign errors, limity rozmiaru |
| `LaunchResultClassifier` | łączenie sygnałów procesu, logów, plików, eventów i firewall w `diagnosisCodes` |
| `WindowsSecurityDiagnostics` | best-effort Event Log query dla Defender/Controlled Folder Access; brak twardej awarii gdy brak dostępu |
| `FirewallRuleInspector` | read-only sprawdzanie reguł dla konkretnego `Among Us.exe`; bez modyfikacji w MVP |
| `SecurityRepairPlanner` | tworzy allowlistowany plan zmian admin; nie wykonuje go sam |
| `SecurityExceptionManager` | etap po MVP: wykonanie elevated zmian + zapis/cofanie zmian |
| `SupportBundleService` | lokalny ZIP, redakcja ścieżek/tokenów, limity plików, manifest zawartości |

Stabilne kody startowe:

```text
launch.process.start_failed
launch.process.exited_early
launch.bepinex.log_missing
launch.bepinex.log_stale
launch.bepinex.plugin_load_failed
launch.bepinex.access_denied
launch.defender.threat_detected
launch.defender.cfa_blocked
launch.defender.events_unavailable
launch.firewall.rule_missing_or_blocked
launch.mod.version_mismatch
launch.unknown
```

### 4.2 AI Support client

Nowe komponenty w `SUSModder.Core`:

| Komponent | Odpowiedzialność |
|---|---|
| `SupportAssistantClient` | `GET /support/knowledge/meta`, `POST /support/query`, `/feedback`, `/report-metadata`; timeout 15 s; brak auto-retry dla query |
| `SupportDiagnosticContextBuilder` | agreguje dane z launch diagnostics, settings, mod repo; redaguje PII przed wysłaniem |
| `SupportActionCode` | enum/allowlist: `none`, `open_logs`, `open_mod_folder`, `open_firewall_repair`, `open_defender_instructions`, `generate_report`, `open_discord` |
| `SupportFeedbackService` | best-effort feedback/report metadata; nie blokuje UI |
| `SupportSessionStore` | opcjonalna lokalna historia sesji bez raw problem/logs |

Klient używa wzorca `ISUSModderApiClient` / `SUSModderApiRequest`:
- `X-User-Hash` przez `SusModderApiRequest.UserHash`, jeśli privacy pozwala na soft identity.
- Envelope `{ data, meta }` i `{ error: { code, message } }` zgodnie z API v2.
- Unknown `actionCode` ignorować i logować diagnostycznie, nie wykonywać.

Core nie wysyła: pełnych `LogOutput.log`, `ErrorLog.log`, `Player.log`, ZIP-a diagnostycznego, pełnych ścieżek `C:\Users\Name\...`, tokenów, haseł, crash dumps, memory dumps ani surowych outputów `legendary.exe` bez redakcji.

---

## 5. UI / Avalonia responsibilities

### 5.1 Launch diagnostics UX

- Zastąpić obecne hardcoded PL error strings w launch flow kluczami i18n.
- Nie pokazywać sukcesu po samym `Process.Start`; sukces dopiero po `LaunchResult` albo po zakończonej obserwacji bez sygnałów błędu.
- Dialog po failure:
  - summary + confidence/severity,
  - sekcje: `Diagnoza`, `Logi`, `Naprawa`, `Prywatność`,
  - przyciski: folder moda, logi, kopiuj diagnozę, reinstall, support bundle, AI Support,
  - jeśli admin action: osobny consent dialog.
- Narzędzie w Settings/Tools: `Diagnostyka uruchamiania` dostępne ręcznie.
- Opcjonalnie lista ostatnich prób launch z lokalnego store, bez raw logów.

### 5.2 AI Support UX

- Native Avalonia controls, bez WebView/chatu zewnętrznego w MVP.
- Widok/zakładka `Pomoc SUSModder`:
  - textarea problemu,
  - checkbox `Dołącz podstawową diagnostykę`,
  - privacy notice first-use,
  - wynik: summary, steps, warnings, safetyNotice, matched articles,
  - feedback buttons.
- Action mapping wyłącznie z allowlisty:
  - `open_logs` → folder BepInEx/logs,
  - `open_mod_folder` → folder moda,
  - `open_firewall_repair` → tylko otwórz narzędzie/plan, bez automatycznej reguły,
  - `open_defender_instructions` → lokalizowana instrukcja albo Windows Security,
  - `generate_report` → lokalny ZIP,
  - `open_discord` → istniejący Discord favorites/invite flow.
- `requiresAdmin=true` pokazuje badge/ostrzeżenie i wymaga osobnego kliknięcia.
- Timeout/service unavailable daje offline fallback: `Wygeneruj raport` + `Discord`, bez crasha UI.

---

## 6. Backend responsibilities

Docelowo w `susmodder-api`:

| Obszar | Zakres MVP |
|---|---|
| Router | `routes/v2/support.js` albo `routes/support.js` mountowane pod `/api/v2/support` |
| Validation | Joi schema dla `/query`, `/feedback`, `/report-metadata`; strict enums; limity długości |
| Rate limiting | `/query`: 10/min/IP; `/feedback`: 30/min/IP; dodatkowo per `X-User-Hash` jeśli obecny |
| KB loader | JSON KB w repo backendu, walidacja przy starcie, `kbVersion`, `articleCount`, locales/categories |
| Search | keyword/diagnosisCode/category/platform scoring; top 3-5 articles; no embeddings w MVP |
| Redaction | PII scrubber przed logowaniem, storage i ewentualnym LLM |
| Session store | metadata bez raw `problem`; TTL/retencja krótka dla sesji |
| Feedback store | agregaty `helped/not_helped/report_generated/discord_clicked`; komentarz tylko po redakcji |
| Health/meta | `/knowledge/meta` cache 5 min, ETag; `/health` bez sekretów |
| LLM | feature flag disabled w MVP; jeśli enabled, structured output + allowlist validation + fallback KB |

Minimalny KB startowy:
- `install_wrong_path`
- `install_no_write_permissions`
- `launch_bepinex_log_missing`
- `launch_plugin_load_failed`
- `antivirus_defender_quarantine_detected`
- `antivirus_cfa_blocked`
- `firewall_rule_missing_or_blocked`
- `network_disconnected_from_server`
- `mods_au_version_mismatch`
- `mods_dll_conflict`
- `proximity_no_microphone`
- `sustats_not_saving`

Każdy artykuł: PL/EN, category, severity, symptoms, diagnosisCodes, appliesTo, solutionSteps z `actionCode`, warnings, updatedAt.

---

## 7. Config and migration implications

### 7.1 Klient SQLite

Nie pisać runtime do `appsettings.json`. Proponowane nowe pola w `user_settings` albo osobna tabela `support_settings`:

```text
launch_diagnostics_enabled default true
support_bundle_anonymize_paths default true
ai_support_enabled default true
ai_support_include_diagnostics default true
ai_support_privacy_notice_accepted default false
ai_support_last_feedback_at nullable
security_repair_prompted_at nullable
```

Proponowane nowe tabele:

```text
launch_attempts
- id / attemptId
- createdAtUtc
- modId, modName, modType, platformMode
- resultStatus, severity, diagnosisCodesJson
- processId nullable, exitCode nullable, elapsedMs
- supportBundlePath nullable local only
- redactedSummaryJson
- retention: max 20-50 rows

security_exceptions
- id
- type: defender_path | defender_process | firewall_rule | cfa_allowed_app
- scopePathHash / redactedScopePath
- displayName / ruleName
- createdAtUtc
- createdBySusModder bool
- removedAtUtc nullable
- lastResultCode

support_sessions
- supportSessionId
- createdAtUtc
- language
- source, confidence
- matchedArticleIdsJson
- diagnosisCodesJson
- result nullable
- reportPath nullable local only
- no raw problem text
```

Każda nowa tabela wymaga migracji w `DatabaseService.ApplyMigrations()` i podbicia `PRAGMA user_version`. Wszystkie SQL parametrized, kolumny whitelistowane.

### 7.2 Backend Redis/PostgreSQL

MVP może działać na Redis + pliki KB:

```text
support:session:{supportSessionId} TTL 7-30 dni, metadata bez problemu raw
support:ratelimit:ip:{ip}
support:ratelimit:user:{hash}
support:feedback:{date}
support:cache:query:{normalizedKey} opcjonalnie bez sessionId
```

Późniejszy PostgreSQL dla agregatów:

```sql
support_sessions(id, created_at, language, source, confidence, diagnosis_codes, article_ids, app_version, platform_mode)
support_feedback(id, session_id, result, article_ids, diagnosis_codes, language, optional_comment_redacted, created_at)
support_report_metadata(id, session_id, diagnosis_codes, language, article_count, created_at)
```

---

## 8. Platform, packaging, updater, telemetry, privacy, AV constraints

### Platform

- Feature Windows-first. UI jest `net10.0-windows`; Core jest `net10.0`, więc Windows-only event log code powinien być w adapterze albo zabezpieczony `[SupportedOSPlatform("windows")]`.
- `System.Diagnostics.Eventing.Reader.EventLogReader` czyta kanały Event Log; dla nowoczesnego .NET plan zakłada dodanie zależności `System.Diagnostics.EventLog`, jeśli nie ma jej transitively.
- Odczyt Event Log może nie mieć uprawnień albo kanał może być wyłączony; traktować jako `launch.defender.events_unavailable`, nie hard error.

### Packaging / updater

- Velopack fixed path pomaga reputacji głównego EXE, ale każdy mod ma osobny `Among Us.exe`; reguły firewall/Defender muszą być per-mod/per-path.
- Buildy są unsigned; UI nie może obiecywać, że Windows/SmartScreen zaufa aplikacji.
- Helper admin/PowerShell musi być jawny, bez obfuskacji i bez ukrytych działań.
- Jeśli wróci code signing, zaktualizować copy i verification, ale nie blokować MVP.

### Telemetry

- AI Support feedback/report metadata to nie to samo co telemetry heartbeat; działa best-effort i respektuje privacy notice.
- Do telemetry opt-in można później dodać wyłącznie agregaty: `diagnosisCode`, `platformMode`, `appVersion`, `modType`, `language`, `source`, `confidence`.
- Bez ścieżek, stacktrace, nazw użytkownika i raw logów.
- Istniejący `TelemetryService` fallback do system locale jest długiem: przy najbliższym tasku i18n/telemetry skorygować do canonical `pl`/`en`.

### Privacy

- Support bundle domyślnie anonimizuje ścieżki i tokeny.
- Przed otwarciem/wysłaniem użytkownik widzi manifest zawartości ZIP.
- Backend nie zapisuje raw `problem`; logować tylko hash/sessionId/kody/metadane.
- `optionalComment` feedback zapisuje się tylko po dobrowolnym wpisaniu i redakcji.

### AV constraints

- Nie dodawać masowych wykluczeń; minimalny scope: konkretny folder moda albo konkretny `Among Us.exe`.
- Nie mieszać firewall z blokadą DLL: firewall dotyczy ruchu sieciowego procesu, Defender/AV dotyczy plików/procesów.
- Nie instruować użytkownika do wyłączania ochrony globalnie.
- Każda admin action musi mieć undo, jeśli została dodana przez SUSModder.

---

## 9. Verification plan

### Core unit tests

- `BepInExLogAnalyzerTests`: benign `Error` nie daje critical; `FileNotFoundException` DLL → `launch.bepinex.plugin_load_failed`; `Access denied` → `launch.bepinex.access_denied`; stale/missing logs; limit linii/rozmiaru.
- `LaunchResultClassifierTests`: process start failed; process exits early + no BepInEx log; updated log + benign errors; Defender event near timestamp + matching redacted path.
- `SupportDiagnosticContextBuilderTests`: usuwa `C:\Users\Name`, emails, token-like strings; limituje `bepInExSummary`; language canonical `pl`/`en`.
- `SupportAssistantClientTests`: `X-User-Hash`; timeout/fallback; 400/429/500 mapping; unknown action ignored.
- `SupportBundleServiceTests`: manifest ZIP, anonimizacja, brak pełnych configów/tokenów, limity rozmiaru.

### UI tests / manual QA

- PL/EN switch w dialogu diagnostycznym i AI Support.
- Action buttons działają lub są disabled z jasnym powodem.
- Timeout support API pokazuje fallback i nie zawiesza UI.
- `requiresAdmin=true` nigdy nie wykonuje się bez osobnej zgody.
- First-use privacy notice zapisuje stan w SQLite i można go obejrzeć/zmienić w Settings/Privacy.

### Backend tests

- Joi validation: bad language, too short problem, invalid actionCode, oversized `bepInExSummary`.
- Redaction: Windows paths, emails, bearer/API tokens, Discord token-like strings.
- KB loader: missing translation, placeholder mismatch, duplicate IDs, invalid category.
- Search: diagnosisCode exact match > keyword; categoryHint boosts; no random answer.
- Rate limiting: per-IP and per-user hash.
- LLM disabled: `meta.llmUsed=false`.
- LLM enabled future: schema parse fail/safety timeout returns KB fallback, not 500.

### Integration/manual scenarios

- Steam mod with deliberately missing DLL.
- BepInEx log with benign `Error` and successful start.
- Process exits immediately.
- No `LogOutput.log` after start.
- Epic launch through Legendary with `epic.log.txt`/`legendary.log.txt`.
- Controlled Folder Access Audit/Block on test folder.
- Firewall rule missing/present for test `Among Us.exe`.
- UAC denied for admin repair (post-MVP).
- Undo firewall/Defender exception added by SUSModder (post-MVP).
- Support API 429, 500, offline.
- Support bundle generated and manually inspected.

### Required reviews before release

- `sus-security-auditor`: support bundle/redaction, admin helper, Defender/Firewall exceptions, telemetry/support metadata.
- `sus-i18n-copy-checker`: PL/EN copy, placeholders, fallback locale.
- `sus-quality-reviewer`: API client, launch flow regressions, SQLite migrations.
- `sus-senior-quality-reviewer`: before admin repair or LLM fallback in production.

---

## 10. Suggested implementation order

### WP0 — Contract and environment reconciliation (gating)

**Owner:** backend + client  
**Parallel:** no  
**Deliverables:** final support base URL; smoke for `/knowledge/meta`, `/health`, `/query`, `/feedback`, `/report-metadata`; decision whether support host uses `ApiV2BaseUrl` or read-only `SupportApiBaseUrl`; contract cleanup for provider-neutral copy; confirmation `X-User-Hash` header.

**Acceptance criteria:** contract examples match backend; smoke script documents 200/400/429; client can code against one stable base URL.

### WP1 — Backend KB-only support API

**Owner:** backend  
**Parallel:** with WP2 after WP0  
**Deliverables:** router + Joi + rate limit; KB JSON PL/EN; redaction service; session metadata; feedback/report metadata; `/knowledge/meta` ETag/cache; `/health`; `AI_SUPPORT_LLM_ENABLED=false` default.

**Acceptance criteria:** API contract tests pass; `meta.llmUsed=false`; raw problem text is not stored.

### WP2 — Core launch diagnostics model + BepInEx analyzer

**Owner:** Core  
**Parallel:** WP1, UI copy prep  
**Deliverables:** `LaunchAttempt`, `LaunchResult`, `DiagnosisCode`; `BepInExLogAnalyzer` with fixtures; shared redaction primitives; unit tests.

**Acceptance criteria:** deterministic classification; no full logs in default DTOs; no UI dependency.

### WP3 — Steam LaunchSupervisor MVP + diagnostics dialog

**Owner:** Core + UI  
**Depends:** WP2  
**Deliverables:** Steam launch moved from fire-and-forget `Process.Start`; observe process and BepInEx log; basic Avalonia diagnostics dialog; launch success copy fixed.

**Acceptance criteria:** Steam launch still works; missing/stale BepInEx log shows localized diagnosis; no admin actions in MVP dialog.

### WP4 — Epic normalization into shared `LaunchResult`

**Owner:** Core + UI  
**Depends:** WP2; can overlap WP3  
**Deliverables:** map Legendary/Epic errors into shared result; redacted `epic.log.txt`/`legendary.log.txt` summaries; same dialog model for Steam/Epic.

**Acceptance criteria:** existing Epic reinstall behavior not broken; Legendary-specific failure still visible; shared codes used where possible.

### WP5 — Local support bundle

**Owner:** Core + UI  
**Depends:** WP2  
**Deliverables:** ZIP with `launch-report.json`, redacted log excerpts, plugin file list + SHA256, app/platform/locale metadata; manifest preview; size limits and retention.

**Acceptance criteria:** no automatic upload; redaction tests pass; user can open folder and copy Discord text.

### WP6 — Core `SupportAssistantClient` + DTOs

**Owner:** Core  
**Depends:** WP0  
**Deliverables:** DTOs for request/response/errors; client using `ISUSModderApiClient` or configured support base URL; `SupportDiagnosticContextBuilder`; best-effort feedback service.

**Acceptance criteria:** tests cover 200/fallback/400/429/500/timeout; sends `X-User-Hash` only when allowed; does not cache `/query`.

### WP7 — Avalonia AI Support MVP

**Owner:** UI  
**Depends:** WP6; best with WP5  
**Deliverables:** `Pomoc SUSModder` view; privacy notice first-use; problem input + diagnostics checkbox; result rendering; allowlisted action buttons; feedback; report/Discord fallback.

**Acceptance criteria:** PL/EN complete; unknown `actionCode` ignored safely; service unavailable path works offline.

### WP8 — Windows Security + Firewall read-only inspectors

**Owner:** Core/platform  
**Depends:** WP2  
**Deliverables:** Event Log best-effort correlator for Defender/CFA; firewall rule inspector for concrete `Among Us.exe`; `events_unavailable` fallback.

**Acceptance criteria:** no admin required; no system changes; tests/mocks for event correlation and firewall parser.

### WP9 — Guided admin repair (separate release gate)

**Owner:** Core + UI + security  
**Depends:** WP8 + security design review  
**Deliverables:** repair planner + UI consent; elevated helper/PowerShell runner with allowlisted operations; add/undo Defender path/process, CFA allowed app, firewall rule per `Among Us.exe`; SQLite `security_exceptions`.

**Acceptance criteria:** security auditor approves; UAC denial is non-fatal; undo works only for exceptions added by SUSModder.

### WP10 — Optional LLM fallback

**Owner:** backend + security  
**Depends:** KB-only metrics proving need  
**Deliverables:** provider behind env flag; structured output schema; prompt injection guard; cost/rate circuit breaker; backend validation.

**Acceptance criteria:** LLM cannot introduce non-allowlisted actions; timeout/schema fail returns KB fallback; privacy notice/health reflect LLM state.

### WP11 — Aggregated support analytics / telemetry

**Owner:** backend + client  
**Depends:** WP7 stable  
**Deliverables:** opt-in aggregated metrics only; helped/not_helped by article/diagnosis code; no raw text/log/path storage.

**Acceptance criteria:** GDPR/privacy review passes; user opt-out respected.

---

## 11. Parallelizable task map

| Parallel track | Can start after | Notes |
|---|---|---|
| Backend KB-only API | WP0 | Independent from client UI once contract is frozen. |
| Core log analyzer fixtures | now/WP0 | No backend dependency. |
| UI i18n key prep | WP2 model draft | Can prepare locale files and mock view models. |
| Support bundle redaction | WP2 | Useful for diagnostics and AI fallback. |
| AI Support client DTOs | WP0 | Can use fake server before backend production. |
| Event log/firewall read-only research | WP2 | Separate from admin repair. |
| KB article writing PL/EN | WP0 | Needs diagnosis code list and actionCode allowlist. |

---

## 12. Milestone release proposal

| Release | Zakres | User value |
|---|---|---|
| A — Local launch diagnostics | WP2 + WP3 partial | lepsze failure messages, brak fałszywego sukcesu, log buttons |
| B — Support bundle + Epic normalization | WP4 + WP5 | standardowy raport Discord; Steam/Epic share diagnosis model |
| C — AI Support KB-only | WP1 + WP6 + WP7 | guided KB answers i feedback bez LLM ryzyka |
| D — Read-only security/firewall correlation | WP8 | precyzyjniejsze Defender/CFA/firewall hints bez admin mutation |
| E — Guided repair admin | WP9 | reversible scoped repairs z UAC |
| F — Optional LLM + analytics | WP10/WP11 | tylko po metrykach KB-only |

---

## 13. Open questions

1. Czy support endpoint ma mieszkać pod `susmodder.app/api/v2/support`, czy pod `api.susmodder-cdn.ovh/v2/support`?
2. Czy `X-User-Hash` wolno wysłać przy wyłączonej telemetrii, jeśli użytkownik zaakceptował privacy notice AI Support?
3. Jaki jest docelowy provider LLM w ops/copy: Gemini, OpenRouter, czy provider-neutral?
4. Czy lokalne `support_sessions` przechowuje opis problemu? Rekomendacja: nie, tylko metadata; ewentualny draft użytkownika oddzielnie i jawnie.
5. Czy `System.Diagnostics.EventLog` 10.0.9 dodajemy do Core, czy adapter Event Log trafia do UI `net10.0-windows`?
6. Jak długo trzymać lokalne bundles i launch attempts? Rekomendacja: max 20-50 attempts, bundles ręcznie albo cleanup po 30 dniach z potwierdzeniem.
7. Czy backend KB ma mieć admin hot reload, czy restart deploy wystarczy w MVP? Rekomendacja: restart deploy.
8. Nazwa publiczna: „Pomoc AI” czy „Pomoc SUSModder”? Rekomendacja: „Pomoc SUSModder”, z informacją, że AI może pomagać, bo MVP jest KB-first.

---

## 14. Definition of done dla MVP KB-only

- Steam launch diagnostics działa i nie pokazuje fałszywego sukcesu po samym `Process.Start`.
- Support bundle generuje lokalny ZIP z redakcją i manifestem zawartości.
- Backend `/api/v2/support/*` przechodzi kontraktowe smoke tests.
- Klient potrafi wysłać `/query`, pokazać steps/warnings/safetyNotice i obsłużyć 429/offline.
- PL/EN kompletne; fallback PL działa.
- Nie ma automatycznego uploadu logów/ZIP.
- Nie ma automatycznych admin repairs.
- Security + i18n review bez blockerów.
- Release notes jasno opisują privacy i ograniczenia.

---

## Sources used

- `mcp-rag`: repo/backend pattern lookup (`ISUSModderApiClient`, telemetry/backend Express/Joi/Redis patterns, API v2 rollout docs).
- `sus-free-doc-scout`: broad documentation scan of AI Support + Defender/Firewall POCs and related API v2 plans.
- Local files:
  - `DOC/POC/API v2/contracts/AI_SUPPORT_API.md`
  - `DOC/POC/2026-06-09-windows-defender-firewall-launch-diagnostics-poc.md`
  - `DOC/POC/2026-06-09-ai-support-assistant-backend-poc.md`
  - `DOC/PLAN/2026-06-07-api-v2-rollout-status.md`
  - `SUSModder.Core/Api/SUSModderApiClient.cs`
  - `SUSModder.Core/Api/ISUSModderApiClient.cs`
  - `SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs`
  - `SUSModder.Core/Diagnostics/Diagnostics.cs`
  - `SUSModder/Services/Localization/LocalizationService.cs`
  - `SUSModder.Core/Data/DatabaseService.cs`
- `microsoft-learn`: `System.Diagnostics.Eventing.Reader.EventLogReader`, `ProcessStartInfo.Verb`, `ProcessStartInfo.UseShellExecute`.
- `nuget`: latest stable `System.Diagnostics.EventLog` package observed as 10.0.9 on 2026-06-09.
