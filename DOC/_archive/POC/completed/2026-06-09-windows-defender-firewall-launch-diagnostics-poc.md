# POC: Diagnostyka uruchamiania modów i reakcja na blokady Defender/Firewall

**Data:** 2026-06-09  
**Status:** POC / plan do decyzji  
**Priorytet:** P1 dla jakości wsparcia użytkowników  
**Zakres:** SUSModder 2.x/3.0 desktop Windows, Steam + Epic, instalacje BepInEx  
**Powiązane:** `MainWindowViewModel.GameLaunch.cs`, `EpicVersionManager.cs`, `Diagnostics.cs`, `DOC/_archive/Updater-Refactoring/*`, `DOC/2026-05-25 - frontend-ideas/17-sha256-verification.md`

---

## 1. Problem

Część użytkowników zgłasza, że mody nie uruchamiają się, prawdopodobnie przez blokady Windows Defender, SmartScreen, Controlled Folder Access, firewall albo inne AV. Lokalnie problem nie jest łatwy do odtworzenia na Windows 10/11, więc potrzebujemy:

1. lepszej obserwowalności startu gry,
2. zebrania logów BepInEx i systemowych wskazówek,
3. klasyfikacji błędów bez fałszywych alarmów,
4. prowadzonego UX naprawy,
5. opcjonalnych wyjątków Defender/Firewall dopiero za świadomą zgodą użytkownika i z UAC/admin.

Przykładowe logi BepInEx dla pełnych modów:

```text
%APPDATA%\Among Us - Mody\Town of Us Mira\BepInEx\LogOutput.log
%APPDATA%\Among Us - Mody\Town of Us Mira\BepInEx\ErrorLog.log
```

Nie każdy wpis `Error` oznacza awarię. POC zakłada klasyfikację sygnałów, nie prosty grep po słowie `error`.

---

## 2. Stan obecny

### 2.1 Steam

`SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs` uruchamia Steam flow bez nadzoru procesu:

- waliduje `SelectedMod` i `InstallPath`,
- zapisuje `steam_appid.txt = 945360`,
- odpala `steam://`, czeka 1 sekundę,
- odpala `Among Us.exe` przez `Process.Start(exePath)`,
- po udanym `Process.Start` pokazuje „Gra uruchomiona”.

Braki:

- brak uchwytu `Process` i obserwacji `HasExited` / `ExitCode`,
- brak timeoutu „gra wystartowała, ale BepInEx nie załadował się”,
- brak odczytu `BepInEx\LogOutput.log` / `ErrorLog.log`,
- komunikaty są częściowo hardcoded PL mimo istniejącego i18n.

### 2.2 Epic

`EpicVersionManager` ma lepszą diagnostykę dla `legendary.exe`:

- zapisuje `epic.log.txt` i `legendary.log.txt`,
- zbiera stdout/stderr Legendary,
- wykrywa część launch errorów, np. `FileNotFoundError`, `[WinError 2]`,
- robi automatyczną reinstalację do 2 prób,
- emituje `EpicLaunchError(modName, logContent)`.

Braki:

- diagnostyka dotyczy głównie Legendary, nie BepInEx/mod DLL,
- brak wspólnego modelu diagnozy Steam/Epic,
- brak korelacji z Defender/Firewall i zdarzeniami systemowymi.

### 2.3 Istniejące diagnostyki

`SUSModder.Core/Diagnostics/Diagnostics.cs` potrafi wypisać zainstalowane mody i niestandardowe pluginy w `BepInEx\plugins`, z pominięciem znanych plików (`Mini.RegionInstall.dll`, `Reactor.dll`, itd.). To można rozbudować o „pakiet wsparcia” i snapshot uruchomienia.

---

## 3. Goal i non-goals

### Goal

- Użytkownik po nieudanym uruchomieniu widzi jasny, lokalizowany komunikat: co prawdopodobnie poszło źle, co może zrobić sam, i kiedy potrzebny jest administrator.
- SUSModder zbiera lokalnie minimalny „launch diagnostic report” z:
  - ścieżkami moda i exe,
  - statusem procesu,
  - timestampami logów BepInEx,
  - wycinkiem istotnych linii z `LogOutput.log` / `ErrorLog.log`,
  - korelacją ze zdarzeniami Defender/Controlled Folder Access/PUA, jeśli dostępne,
  - statusem reguł firewall/wyjątków tylko jako wynik lokalnej kontroli.
- SUSModder proponuje naprawy kaskadowo: najpierw bez admina, potem opcjonalnie admin/UAC.
- Wyjątki Defender/Firewall są jawne, odwracalne i scoped do konkretnej instalacji/mod root, nie globalne.

### Non-goals

- Nie wyłączamy Defendera, PUA, SmartScreen ani firewall globalnie.
- Nie obchodzimy decyzji AV ani nie ukrywamy plików przed skanerami.
- Nie wysyłamy logów automatycznie na backend bez osobnej zgody.
- Nie wymagamy uruchamiania całego SUSModdera jako administrator.
- Nie próbujemy gwarantować, że każdy mod DLL jest bezpieczny; możemy tylko weryfikować integralność i źródło paczki.

---

## 4. Hipotezy przyczyn

| Hipoteza | Objawy | Jak sprawdzać | Naprawa |
|----------|--------|---------------|---------|
| Defender/AV skasował lub poddał kwarantannie DLL | brak pliku w `BepInEx\plugins`, gra startuje bez moda albo szybko się zamyka | porównanie z manifestem instalacji / oczekiwanymi plikami, zdarzenia Defender 1116/PUA | reinstalacja moda, wyjątek dla folderu/moda, instrukcja Protection history |
| Controlled Folder Access blokuje zapis | logi mówią o braku dostępu, brak configów/logów, eventy 1123/1127 | Windows Defender Operational log | allow app albo przeniesienie modów poza chroniony folder |
| Firewall blokuje ruch gry/moda | gra startuje, ale lobby/online nie działa | sprawdzenie reguł dla `Among Us.exe`, komunikat Windows firewall | reguła allow dla konkretnego `Among Us.exe` w folderze moda |
| SmartScreen / reputation blokuje EXE/narzędzie | popup przy starcie narzędzia, proces nie startuje | exit/error z `Process.Start`, brak procesu | instrukcja użytkownika, podpis/reputacja, Velopack fixed path |
| BepInEx/mod crash niezwiązany z AV | `ErrorLog.log`, stacktrace, `Chainloader`/plugin exception | parser logów BepInEx z whitelistą benign errorów | komunikat „problem moda”, link do Discord/GitHub moda |
| Zła wersja Among Us/moda | logi BepInEx mówią o niezgodności assembly/API | porównanie `AmongVersion`, mod version, wpisy logów | reinstall właściwej wersji, sprawdzenie katalogu |

---

## 5. Proponowany kierunek: Launch Supervisor + Guided Repair

### 5.1 Launch Supervisor

Nowy komponent Core, roboczo `LaunchSupervisor`, używany przez Steam i docelowo Epic:

```text
Prepare snapshot -> Start process -> Observe first 30-60s -> Collect logs -> Classify -> Return LaunchResult
```

Minimalny model:

```text
LaunchAttempt
- attemptId
- modId, modName, modType, platformMode
- installPath, actualModPath, exePath
- startedAtUtc, processId?, exitCode?, exitedWithinSeconds?
- bepinexLogStatus: missing/stale/updated
- diagnosisCodes[]
- supportBundlePath?
```

Stabilne `diagnosisCodes`, przykłady:

- `launch.process.start_failed`
- `launch.process.exited_early`
- `launch.bepinex.log_missing`
- `launch.bepinex.plugin_load_failed`
- `launch.defender.threat_detected`
- `launch.defender.cfa_blocked`
- `launch.firewall.rule_missing_or_blocked`
- `launch.mod.version_mismatch`
- `launch.unknown`

UI tłumaczy kody na PL/EN, a Core dostarcza techniczny fallback.

### 5.2 BepInEx Log Analyzer

Nowy parser powinien czytać tylko ogon plików po `startedAtUtc` albo od zapamiętanej długości pliku:

- `BepInEx\LogOutput.log`
- `BepInEx\ErrorLog.log`
- opcjonalnie `BepInEx\config\BepInEx.cfg` tylko jako metadata, bez sekretów.

Klasyfikacja:

- **Critical:** crash, unhandled exception, plugin load failed, missing dependency, access denied, bad image format, file not found dla DLL.
- **Warning:** pojedyncze `Error` bez crasha, deprecation, missing optional asset.
- **Info:** BepInEx chainloader started, plugin loaded, Unity logs.

Wynik musi zawierać listę istotnych linii z kontekstem, ale ograniczoną rozmiarem (np. 200 KB / 500 linii), aby nie zamrażać UI i nie wysyłać przypadkowych danych.

### 5.3 Windows Security Correlator

Na Windows można spróbować odczytać lokalne zdarzenia z `Microsoft-Windows-Windows Defender/Operational` przez `System.Diagnostics.Eventing.Reader` (`System.Diagnostics.EventLog`). Interesujące rodziny zdarzeń wg dokumentacji Microsoft:

- PUA / malware detection, np. 1116 / 1160,
- Attack Surface Reduction block, np. 1121,
- Controlled Folder Access block/audit, np. 1123, 1124, 1127, 1128,
- settings changed, np. 5007.

Korelacja tylko w oknie czasu, np. `startedAtUtc - 2 min` do `now + 1 min`, i tylko jeśli payload/ścieżka zawiera:

- `actualModPath`,
- `BepInEx`,
- `Among Us.exe`,
- pliki w `BepInEx\plugins`.

Jeśli odczyt event logów wymaga uprawnień lub jest zablokowany, raport powinien mieć kod `launch.defender.events_unavailable` i UI pokaże instrukcję ręcznego sprawdzenia „Historia ochrony”.

### 5.4 Firewall Checker

Firewall nie blokuje ładowania DLL; blokuje ruch sieciowy procesu. Dlatego nie należy mieszać komunikatów „DLL zablokowana” z „firewall”. Firewall checker ma sens gdy:

- gra się uruchamia, ale nie łączy z usługami/lobby,
- użytkownik widzi popup Windows Firewall,
- mod wymaga dodatkowego lokalnego portu/usługi.

Zakres MVP:

- sprawdzić, czy istnieje reguła Allow dla konkretnego `actualModPath\Among Us.exe`,
- nie dodawać reguł automatycznie,
- proponować „Dodaj regułę firewall” tylko gdy diagnoza wskazuje problem sieciowy lub użytkownik wybierze narzędzie ręcznie.

---

## 6. Opcje naprawy

### Opcja A — diagnostyka bez admina (rekomendowany MVP)

- Nadzór procesu + BepInEx log analyzer.
- Snapshot plików `BepInEx\plugins`.
- Korelacja z Defender event log best-effort.
- Dialog „Nie udało się uruchomić moda” z przyciskami:
  - „Pokaż szczegóły”,
  - „Otwórz folder moda”,
  - „Otwórz logi BepInEx”,
  - „Utwórz pakiet wsparcia”,
  - „Przeinstaluj moda”.

Plusy: bezpieczne, szybkie, bez UAC.  
Minusy: nie naprawi realnej kwarantanny/allowlisty bez akcji użytkownika.

### Opcja B — naprawa z adminem na żądanie

Po jasnym wyjaśnieniu i zgodzie użytkownika uruchomić wyniesiony proces/PowerShell z UAC:

- Defender folder exclusion dla konkretnego folderu moda lub root `%APPDATA%\Among Us - Mody`:
  - `Add-MpPreference -ExclusionPath <path>`
- Defender process exclusion dla konkretnego `Among Us.exe`:
  - `Add-MpPreference -ExclusionProcess <exePath>`
- Controlled Folder Access allow app, tylko jeśli diagnoza CFA:
  - `Add-MpPreference -ControlledFolderAccessAllowedApplications <exePath>`
- Firewall allow dla konkretnego `Among Us.exe`, tylko dla profili Private/Public wg wyboru:
  - `New-NetFirewallRule -DisplayName "SUSModder - <modName>" -Direction Inbound/Outbound -Program <exePath> -Action Allow`

Zasady:

- domyślnie proponować folder moda, nie cały dysk ani `%APPDATA%`,
- zapisać, co dodaliśmy, aby można było cofnąć,
- nie powtarzać UAC bez potrzeby,
- obsłużyć odmowę admina jako normalny rezultat, nie błąd krytyczny.

### Opcja C — preflight po instalacji

Po instalacji moda:

- sprawdzić obecność oczekiwanych DLL,
- sprawdzić, czy BepInEx folder/logi istnieją po pierwszym starcie,
- opcjonalnie przy pierwszym uruchomieniu pokazać „Jeśli Windows zapyta o firewall, kliknij Zezwól”.

Nie rekomenduję automatycznych wyjątków zaraz po instalacji; to może pogorszyć zaufanie i reputację aplikacji.

### Opcja D — pakiet wsparcia

`SUSModder Support Bundle` ZIP lokalnie, tworzony po kliknięciu:

- `launch-report.json`,
- wycinek `LogOutput.log` i `ErrorLog.log`,
- `epic.log.txt` / `legendary.log.txt` jeśli Epic,
- lista plików `BepInEx\plugins` z nazwą, rozmiarem, SHA256,
- wersja SUSModder, platforma Windows, tryb Steam/Epic, locale,
- bez tokenów, bez pełnych ścieżek użytkownika jeśli użytkownik wybierze tryb anonimizacji.

Domyślnie bundle zostaje lokalnie; wysyłka na Discord/backend tylko ręczna.

### Opcja E — reputacja i packaging

Długoterminowo:

- utrzymać Velopack fixed path, bo istniejące analizy wskazują mniej problemów AV/firewall przy stabilnej ścieżce EXE,
- rozważyć powrót do podpisu kodu, jeśli budżet pozwoli,
- hash/manifest paczek modów i DLL jako podstawa komunikatu „plik zgodny z katalogiem SUSModder”.

---

## 7. Language / i18n impact

MVP locales: `pl` i `en`, fallback `pl`.

Wszystkie nowe teksty UI muszą być kluczami w `SUSModder/Localization/pl.json` i `en.json`, np.:

- `LaunchDiagnostics.Title`
- `LaunchDiagnostics.Summary.DefenderBlocked`
- `LaunchDiagnostics.Summary.FirewallPossible`
- `LaunchDiagnostics.Actions.OpenBepInExLogs`
- `LaunchDiagnostics.Actions.CreateSupportBundle`
- `LaunchDiagnostics.Actions.AddDefenderExceptionAdmin`
- `LaunchDiagnostics.Privacy.SupportBundleNotice`
- `LaunchDiagnostics.AdminConsent.Explanation`

Core zwraca stabilne kody diagnozy + techniczny fallback. UI mapuje kody na tekst. Placeholdery muszą mieć parytet PL/EN, np. `{modName}`, `{path}`, `{count}`. Jeśli pokazujemy liczbę wykrytych problemów, użyć ICU MessageFormat po wdrożeniu mechanizmu pluralizacji albo unikać odmiany w MVP.

Future locale: nowy język powinien wymagać dodania pliku locale/metadata, bez zmian w `LaunchSupervisor` i parserach.

---

## 8. User workflow

### 8.1 Normalny start

1. Użytkownik klika „Uruchom”.
2. UI pokazuje status „Uruchamianie…”.
3. `LaunchSupervisor` robi snapshot logów i startuje grę.
4. Jeśli proces działa i BepInEx log się aktualizuje, UI kończy stan busy.

### 8.2 Szybki crash / brak BepInEx

1. Gra zamyka się w ciągu 30 sekund albo log BepInEx nie powstaje.
2. SUSModder zbiera logi i klasyfikuje problem.
3. Dialog pokazuje diagnozę:
   - „Wygląda na blokadę Defendera” jeśli jest event/plik zniknął,
   - „BepInEx zgłosił błąd moda” jeśli jest stacktrace,
   - „Nie mamy pewności” jeśli brak twardych sygnałów.
4. Użytkownik wybiera: szczegóły, reinstall, folder/logi, pakiet wsparcia, opcjonalne wyjątki admin.

### 8.3 Naprawa admin

1. Dialog wyjaśnia dokładnie, co zostanie dodane i dla jakiej ścieżki.
2. Użytkownik klika „Dodaj wyjątek (wymaga administratora)”.
3. Windows pokazuje UAC.
4. SUSModder zapisuje wynik i proponuje ponowny start.

---

## 9. Core business logic responsibilities

- `LaunchSupervisor` / `ILaunchSupervisor`:
  - start procesu i obserwacja,
  - wspólny model `LaunchResult`,
  - kody diagnoz i severity,
  - cancellation/timeout.
- `BepInExLogAnalyzer`:
  - tail logów,
  - reguły klasyfikacji,
  - ignorowanie benign errors.
- `WindowsSecurityDiagnostics`:
  - event log query best-effort,
  - wykrywanie braków uprawnień,
  - brak twardej zależności na Windows poza projektem UI `net*-windows` albo adapterem platformowym.
- `FirewallRuleInspector`:
  - odczyt istniejących reguł,
  - generowanie planu zmian, bez wykonywania bez zgody.
- `SecurityExceptionManager`:
  - wykonanie zmian przez elevated helper/PowerShell,
  - zapis metadanych zmian w SQLite/user settings lub osobnej tabeli.
- `SupportBundleService`:
  - anonimizacja,
  - limity rozmiaru,
  - brak automatycznego uploadu.

---

## 10. UI / Avalonia responsibilities

- Dialog diagnostyczny po launch failure:
  - summary + confidence,
  - zakładki: „Diagnoza”, „Logi”, „Naprawa”, „Prywatność”.
- Progress launch bez fałszywego „Gra uruchomiona” po samym `Process.Start`.
- Przyciski akcji:
  - otwórz folder moda,
  - otwórz `LogOutput.log` / `ErrorLog.log`,
  - skopiuj diagnozę,
  - utwórz bundle,
  - przeinstaluj,
  - dodaj wyjątek admin,
  - cofnij wyjątek.
- Settings / Tools:
  - „Diagnostyka uruchamiania” jako ręcznie dostępne narzędzie,
  - lista ostatnich prób uruchomienia (opcjonalnie).
- Wszystkie teksty przez i18n PL/EN.

---

## 11. Config and migration implications

Nie pisać runtime do `appsettings.json`.

Proponowane przechowywanie:

- `user_settings`:
  - `launch_diagnostics_enabled` default `true`,
  - `support_bundle_anonymize_paths` default `true`,
  - `security_repair_prompted_at` opcjonalnie.
- Nowa tabela SQLite `launch_attempts` albo plik rotacyjny w `%APPDATA%\SUSModder\logs`:
  - max np. 20 ostatnich prób,
  - bez pełnych logów, tylko metadata i ścieżka do bundle jeśli utworzony.
- Nowa tabela `security_exceptions`:
  - typ wyjątku (`defender_path`, `defender_process`, `firewall_rule`, `cfa_allowed_app`),
  - scope path,
  - display name/rule name,
  - createdAt,
  - czy dodane przez SUSModder.

Nowa tabela wymaga migracji w `DatabaseService.ApplyMigrations()` i zwiększenia `PRAGMA user_version`.

---

## 12. Platform, packaging, updater, telemetry, privacy, AV constraints

### Platform

- Funkcja jest Windows-first. Na przyszłym Linux POC odpowiednikiem będą logi procesu i BepInEx, bez Defender/Firewall.
- Nie uruchamiać całej aplikacji jako admin; tylko konkretna akcja naprawcza przez UAC.
- Część API event logów wymaga package `System.Diagnostics.EventLog` i jest Windows-specific.

### Packaging / updater

- Velopack fixed path pomaga reputacji głównego EXE i regułom firewall, ale mody mają osobne foldery i `Among Us.exe`, więc potrzebujemy per-mod diagnozy.
- Buildy są obecnie unsigned; komunikaty nie mogą obiecywać, że Windows zawsze zaufa aplikacji.
- Jeśli wróci signing, warto opisać w UI „SUSModder jest podpisany”, ale nie jako warunek MVP.

### Telemetry

- Domyślnie nie wysyłać logów BepInEx ani eventów Defender.
- Możliwa przyszła telemetryka opt-in tylko jako agregaty:
  - `diagnosisCode`,
  - `platformMode`,
  - `appVersion`,
  - `modType`,
  - `language`,
  - bez ścieżek, bez nazw użytkownika, bez pełnych stacktrace.

### Privacy

- Logi mogą zawierać nazwę użytkownika w ścieżce, nazwy serwerów, konfigurację lobby albo tokeny modów. Bundle musi mieć tryb anonimizacji.
- Przed otwarciem lub wysłaniem bundle pokazać listę zawartości.

### AV constraints

- Automatyczne masowe dodawanie wykluczeń może wyglądać podejrzanie i pogorszyć reputację. Dlatego tylko opt-in, po diagnozie, z minimalnym zakresem.
- Nie używać obfuskacji ani ukrytych helperów.
- PowerShell/elevated helper powinien logować wykonane komendy i wynik.

---

## 13. Verification plan

### Unit tests

- `BepInExLogAnalyzerTests`:
  - benign `Error` nie daje critical,
  - `FileNotFoundException` DLL daje `plugin_load_failed`,
  - `Access denied` daje `access_denied`,
  - log stale/missing daje właściwy kod.
- `LaunchResultClassifierTests`:
  - process exits early + missing log => `bepinex.log_missing` / `process.exited_early`,
  - Defender event near timestamp + matching path => `defender.threat_detected`.
- `SupportBundleServiceTests`:
  - anonimizuje `C:\Users\Name`,
  - respektuje limit rozmiaru,
  - nie pakuje sekretów/pełnych configów.

### Integration/manual

- Mod z celowo brakującą DLL.
- Mod z wpisem `Error` benign i działającą grą.
- Gra zamykająca się natychmiast.
- Brak `LogOutput.log` po starcie.
- Controlled Folder Access w Audit/Block mode na katalog testowy.
- Firewall rule missing/present dla testowego `Among Us.exe`.
- Odmowa UAC przy dodawaniu wyjątku.
- Cofnięcie wyjątku dodanego przez SUSModder.
- PL/EN switch w trakcie dialogu diagnostycznego.

### Security review

Wymagany review przez `sus-security-auditor` dla:

- dodawania Defender/Firewall exceptions,
- elevated helper/PowerShell,
- support bundle i anonimizacji,
- telemetry fields.

Wymagany i18n review przez `sus-i18n-copy-checker` dla dialogów i komunikatów.

---

## 14. Suggested implementation order

### Faza 0 — instrumentacja bez UX ryzyk (równolegle)

1. **Core:** model `LaunchAttempt` / `LaunchResult` / `DiagnosisCode`.
2. **Core:** `BepInExLogAnalyzer` + testy fixture logów.
3. **UI:** zamiana hardcoded launch strings na i18n keys PL/EN.
4. **Docs/support:** instrukcja ręcznego zbierania `LogOutput.log` / `ErrorLog.log`.

### Faza 1 — Steam Launch Supervisor MVP

1. Przenieść Steam launch z „fire-and-forget `Process.Start`” do `LaunchSupervisor`.
2. Obserwować proces przez pierwsze 30-60s.
3. Czytać BepInEx logi po starcie.
4. Pokazywać prosty dialog diagnostyczny bez admin actions.

### Faza 2 — wspólny Epic + support bundle

1. Podłączyć Epic do wspólnego `LaunchResult` po Legendary launch.
2. Dodać support bundle lokalny.
3. Dodać przyciski „Otwórz logi” i „Kopiuj diagnozę”.

### Faza 3 — Windows Security Correlator

1. Dodać `System.Diagnostics.EventLog` / adapter Windows-only.
2. Query Defender Operational event log w oknie czasu.
3. Mapować eventy do diagnoz z confidence.
4. Fallback manual instructions, gdy brak uprawnień/dostępu.

### Faza 4 — Guided Repair z adminem

1. Projekt elevated helper albo PowerShell runner z UAC.
2. Dodawanie i cofanie Defender path/process exceptions.
3. Dodawanie i cofanie firewall rule per mod exe.
4. Rejestr zmian w SQLite.
5. Security review przed wydaniem.

### Faza 5 — preflight i reputacja

1. Manifest/hash expected files dla paczek modów/DLL.
2. Po instalacji sprawdzanie brakujących plików.
3. Telemetry opt-in agregatów diagnosis code.
4. Decyzja o code signing / submission flow, jeśli false positives zostaną częste.

### Parallelizable tasks

- Parser logów BepInEx i test fixtures można robić równolegle z UI dialogiem.
- Windows event correlator można robić niezależnie od support bundle.
- i18n keys PL/EN można przygotować równolegle z modelami Core.
- Elevated repair musi poczekać na security review i stabilne diagnosis codes.

---

## 15. Otwarte pytania

1. Czy użytkownicy zgłaszający problem mają konkretny alert Defender/Protection history, czy tylko „gra się nie odpala”? Potrzebne screenshoty/teksty alertów.
2. Czy problem dotyczy Steam, Epic, czy obu?
3. Czy blokowany jest `Among Us.exe`, konkretna DLL w `BepInEx\plugins`, `winhttp.dll`, `BepInEx\core`, czy narzędzie pomocnicze?
4. Czy mody są instalowane przez SUSModder, czy część użytkowników ręcznie kopiuje paczki pobrane z przeglądarki (Mark-of-the-Web)?
5. Czy backend katalogu modów ma wystarczająco danych, aby opisać oczekiwane DLL/hashes?
6. Czy chcemy mieć centralny endpoint do dobrowolnego uploadu support bundle, czy na razie zostajemy przy ręcznym udostępnianiu?

---

## 16. Decyzja rekomendowana na teraz

Wdrożyć najpierw **Opcję A**: Launch Supervisor + BepInEx Log Analyzer + lokalny support bundle. To da realne dane o awariach bez ryzyka UAC/AV reputation. Dopiero po zebraniu kilku przypadków dodać **Opcję B** jako guided repair dla potwierdzonych blokad Defender/Firewall.

Najważniejsza zmiana UX: nie mówić „Gra uruchomiona” po samym `Process.Start`. Komunikat powinien oznaczać „proces wystartował”, a status „mod załadowany” powinien wynikać z logu BepInEx albo braku szybkiego crasha.
