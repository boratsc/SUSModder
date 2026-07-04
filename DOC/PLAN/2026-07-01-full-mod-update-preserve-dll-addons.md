# Plan: odtwarzanie DLL po aktualizacji moda FULL

Status: aktywny plan do implementacji  
Data: 2026-07-01  
Zakres: SUSModder.Core + Avalonia UI, aktualizacje modów FULL, instancji modpacków i DLL addonów

## Kontekst i problem

Zgłoszony problem: gdy mod typu `full` ma doinstalowane mody `dll`, aktualizacja moda FULL potrafi odtworzyć samą bazę moda, ale nie gwarantuje ponownego doinstalowania wcześniej obecnych DLL. Dotyczy to szczególnie ścieżek auto-update, ale do przejrzenia są wszystkie warianty:

- klasyczny mod FULL z włączonym auto-update,
- klasyczny mod FULL aktualizowany ręcznie / bez auto-update,
- lokalna instancja modpacka i update paczki,
- pełny mod w instancji bez paczki,
- DLL z własnym `AutoUpdateEnabled` w `InstallationMap.InstalledDlls`.

Obecnie logika zachowania DLL jest rozproszona:

- `MainWindowViewModel.Updates.cs` robi snapshot `InstallationMap.InstalledDlls` przed aktualizacją klasycznego moda i próbuje wykonać `RestoreDllModsAfterUpdateAsync()` po instalacji nowej wersji.
- `ModInstanceInstaller.UpdateInstanceAsync()` zachowuje DLL z repozytorium instancji (`IModInstanceRepository.GetDlls`) i instaluje je ponownie po reinstalacji full moda.
- `ModPackInstaller.UpdateExistingInstanceAsync()` po aktualizacji full moda osobno wyrównuje DLL katalogowe i external DLL do manifestu paczki.
- Legacy `ModUpdates.UpdateModAsync()` usuwa i instaluje moda ponownie bez widocznego mechanizmu zachowania DLL.
- `DllModificationService.InstallDllToModAsync()` po reinstallu tworzy nowe wpisy DLL w mapie, ale nie przenosi flagi `DllModInstallation.AutoUpdateEnabled` ze starego wpisu.

Wniosek: zachowanie DLL po aktualizacji nie jest jednym kontraktem domenowym w Core, tylko efektem kilku ścieżek UI/Core. Trzeba ujednolicić mechanizm snapshot → reinstall FULL → replay DLL → zapis mapy/repozytorium → raport błędów.

## Cel

Po każdej udanej aktualizacji/reinstalacji moda FULL aplikacja ma zachować wybór addonów DLL użytkownika:

1. Jeżeli DLL był zainstalowany w tym full modzie przed aktualizacją, ma zostać doinstalowany z powrotem po aktualizacji.
2. Ma zostać zachowana flaga `AutoUpdateEnabled` dla każdego DLL.
3. Ma zostać zachowana flaga `FullModInstallation.AutoUpdateEnabled` dla full moda.
4. Zachowanie ma być identyczne dla aktualizacji cichej i ręcznej.
5. Dla modpacków wynik ma pozostać zgodny z manifestem paczki: najpierw zachowujemy lokalne DLL, ale finalny stan instancji packa wyrównujemy do aktualnego manifestu packa.
6. Błędy przywracania pojedynczego DLL nie mogą ukryć udanej aktualizacji full moda, ale muszą być widoczne w diagnostyce i UI/toastach.

## Non-goals

- Nie zmieniamy formatu backendowego katalogu modów ani API `susmodder.app` w pierwszym etapie.
- Nie wprowadzamy historycznych wersji DLL, jeżeli API/katalog nie daje URL do starych wersji. MVP używa aktualnego katalogowego artefaktu DLL, zachowując sam fakt instalacji i flagi użytkownika.
- Nie zmieniamy mechaniki instalacji vanilla Steam/Epic poza punktem integracji po reinstalacji.
- Nie migrujemy `.susmodder-install.json` do SQLite; plik nadal jest redundantnym źródłem prawdy dla zawartości katalogu moda.
- Nie dodajemy runtime downloadów tłumaczeń.

## Decyzje produktowe

### Co reinstalować

- Po update FULL reinstallujemy wszystkie DLL obecne w `InstallationMap.InstalledDlls` sprzed update.
- Jeżeli DLL nadal istnieje w katalogu, instalujemy najnowszy dostępny wariant dla bieżącej platformy (`steam`/`epic`).
- Jeżeli wpis DLL zniknął z katalogu, oznaczamy go jako `skipped/missingCatalog` i pokazujemy użytkownikowi informację, ale nie cofamy update full moda.
- Jeżeli DLL nie da się pobrać/zapisać, oznaczamy `failed` i zostawiamy nowy full mod z raportem częściowego sukcesu.

### Auto-update DLL

- Flaga `DllModInstallation.AutoUpdateEnabled` oznacza automatyczne aktualizowanie samego DLL w już istniejących lokalizacjach.
- Update FULL ma odtworzyć DLL niezależnie od flagi DLL auto-update, bo to jest zachowanie instalacji użytkownika, a nie decyzja o aktualizowaniu DLL.
- Po odtworzeniu wpisu DLL trzeba skopiować flagę `AutoUpdateEnabled` ze snapshotu.

### Modpacki

- Dla instancji `shared_pack` źródłem docelowego stanu pozostaje manifest paczki.
- Po aktualizacji full moda instancji należy przywrócić wcześniej zainstalowane DLL tylko jako etap bezpieczeństwa, a następnie wykonać istniejące wyrównanie do manifestu paczki (`ModPackInstaller.UpdateExistingInstanceAsync`).
- External DLL z manifestu paczki pozostają obsługiwane przez `ModPackInstaller`; klasyczny mechanizm preservation nie powinien traktować losowych DLL z dysku jako zaufanych addonów bez wpisu w repo/mapie.

## User workflow

### Auto-update full moda

1. Aplikacja wykrywa update full moda przez `ModUpdateManager`.
2. Jeżeli `FullModInstallation.AutoUpdateEnabled == true`, update startuje bez dialogu.
3. Przed ruszeniem katalogu aplikacja robi snapshot addonów DLL z mapy instalacji.
4. Full mod jest aktualizowany/reinstalowany.
5. Aplikacja odtwarza DLL ze snapshotu i zachowuje ich flagi auto-update.
6. Użytkownik dostaje toast: sukces pełny albo sukces częściowy z informacją o DLL.

### Manual update full moda

1. Użytkownik widzi dialog potwierdzenia aktualizacji full moda.
2. Po potwierdzeniu flow jest taki sam jak auto-update, ale z widocznym dialogiem postępu.
3. Podsumowanie po aktualizacji pokazuje również DLL przywrócone/pominięte/nieudane.

### Update modpacka / instancji

1. Użytkownik uruchamia update instancji albo paczki.
2. Jeżeli zmienia się full mod, instancja robi snapshot DLL przed usunięciem katalogu.
3. Po instalacji nowego full moda DLL wracają.
4. Dla `shared_pack` końcowy etap wyrównuje stan do manifestu paczki, żeby paczka pozostała deterministyczna.

## Proponowana architektura

### Nowy kontrakt w Core

Dodać usługę w `SUSModder.Core`, roboczo:

- `FullModAddonPreservationService`
- modele:
  - `FullModAddonSnapshot`
  - `PreservedDllAddon`
  - `FullModAddonRestoreResult`
  - `DllRestoreStatus` (`Restored`, `SkippedMissingCatalog`, `Failed`, `SkippedUnsafePath`)

Odpowiedzialności:

1. `CaptureFromInstallationMapAsync(ModConfiguration fullMod)`
   - czyta `.susmodder-install.json`,
   - weryfikuje, że `FullMod.ModId/ModName` pasuje do aktualizowanego moda,
   - zapisuje full-mod flags: `AutoUpdateEnabled`, `DisableAutoUpdatePrompt`, `PinnedInstallVersion`,
   - zapisuje listę DLL: `ModId`, `ModName`, `ModVersion`, `InstallPath`, `InstalledFrom`, `AutoUpdateEnabled`.
2. `RestoreToFullModAsync(ModConfiguration updatedFullMod, FullModAddonSnapshot snapshot, string platform, ...)`
   - znajduje konfiguracje DLL w katalogu,
   - instaluje DLL przez istniejący bezpieczny mechanizm `DllModificationService.InstallDllToModAsync`,
   - po instalacji ponownie ładuje mapę i przywraca `AutoUpdateEnabled` oraz metadane, których `DllModificationService` dziś nie zachowuje,
   - zwraca wynik per DLL.
3. `ApplyFullModFlagsAsync(updatedInstallPath, snapshot)`
   - przywraca `FullMod.AutoUpdateEnabled`,
   - nie wymusza `DisableAutoUpdatePrompt/PinnedInstallVersion`, jeżeli aktualizacja była świadomie wykonana na nowszą wersję; zachowanie pinning trzeba potwierdzić w implementacji. Bezpieczny domyślny wariant: zachować tylko `AutoUpdateEnabled`, a pinning traktować jako decyzję dotyczącą starej wersji.

### Unifikacja ścieżek aktualizacji

#### Klasyczne FULL: auto-update i manual update

Refaktoryzować `MainWindowViewModel.Updates.cs` tak, aby:

- przed backupem katalogu wołał Core service `CaptureFromInstallationMapAsync`,
- po udanym `ModManager.ModifyAsync` / `EpicVersionManager.ModifyEpicAsync` wołał `RestoreToFullModAsync`,
- usunął lokalną, UI-specyficzną logikę `RestoreDllModsAfterUpdateAsync` albo zostawił ją jako cienki wrapper do Core,
- w UI wyświetlał podsumowanie: full update OK, DLL restored/skipped/failed.

To obejmuje zarówno ciche auto-update (`ProcessAutoUpdatesSilentlyAsync`) jak i ręczne potwierdzenie (`ProcessUpdatesWithIndividualDialogsAsync`), bo oba idą przez `UpdateSingleModWithDialogAsync`.

#### Legacy `ModUpdates.UpdateModAsync`

`SUSModder.Core.GameIntegration.ModUpdates.UpdateModAsync()` nie może dalej robić delete → reinstall bez preservation. Opcje:

1. oznaczyć jako legacy i przekierować do nowego Core orchestratora, albo
2. usunąć użycie z UI i testów, ale zostawić test regresyjny, że bezpośrednie wywołanie też zachowuje DLL.

Preferowane: mały wrapper do tej samej usługi, żeby żadna stara ścieżka nie gubiła DLL.

#### Instancje i modpacki

`ModInstanceInstaller.UpdateInstanceAsync()` już robi snapshot z `_instances.GetDlls(instanceId)` i replay po full update. Do doprecyzowania:

- zachować/potwierdzić `AutoUpdateEnabled` instancji full moda,
- jeżeli `ModInstanceDll` nie ma pola auto-update, źródłem flagi DLL jest nadal `.susmodder-install.json`; trzeba albo rozszerzyć repozytorium instancji, albo przy restore odczytać mapę przed usunięciem katalogu,
- nie nadpisywać pack-pinned wersji DLL najnowszą katalogową, jeżeli update jest wykonywany z `ModPackInstaller.UpdateExistingInstanceAsync`.

`ModPackInstaller.UpdateExistingInstanceAsync()` powinien jasno dokumentować kolejność:

1. update full moda instancji z preservation lokalnych DLL,
2. wyrównanie katalogowych DLL do manifestu packa,
3. instalacja/aktualizacja external DLL z manifestu,
4. ToU config snapshot.

Wariant bulk `MainWindowViewModel.BulkOperations.cs` używa `ConfigService.CheckInstanceUpdateAsync()` i `ModInstanceInstaller.UpdateInstanceAsync()` dla instancji. Po zmianie testy muszą pokryć, że DLL wracają również w tej ścieżce.

## Core business logic responsibilities

- Jeden mechanizm snapshot/replay DLL po aktualizacji full moda.
- Walidacja spójności mapy instalacji z aktualizowanym modem.
- Bezpieczne ścieżki: używać istniejących `TryResolveSafeDllDirectory` / `TryResolveSafeDllPath` przez `DllModificationService`.
- Raportowanie per-DLL, bez rzucania wyjątku na cały update przy częściowym niepowodzeniu restore.
- Aktualizacja `.susmodder-install.json` po replayu wraz z zachowaniem flag auto-update.
- Unikanie zapisów runtime do `appsettings.json`; platformę brać z `UserSettingsService` / parametru.

## UI / Avalonia responsibilities

- Dla auto-update: toast startowy i końcowy powinny rozróżniać pełny sukces od częściowego sukcesu DLL.
- Dla manual update: dialog podsumowania powinien wymienić DLL pominięte/nieudane, bez technicznego stack trace.
- Nie kodować nowych komunikatów na sztywno; nowe teksty przez i18n keys.
- Pasek postępu: dodać etap „Przywracanie addonów DLL” po instalacji full moda.
- Nie przenosić logiki wyboru DLL do ViewModelu; UI tylko pokazuje wynik z Core.

## Language / i18n impact

Nowe user-facing copy wymaga kluczy PL i EN:

- `Updates.FullMod.RestoringDllAddons`
- `Updates.FullMod.DllRestorePartialSuccess`
- `Updates.FullMod.DllRestoreFailed`
- `Updates.FullMod.DllRestoreSkippedMissingCatalog`
- `Updates.FullMod.DllRestoreSummary`

Wymagania:

- PL i EN muszą mieć te same placeholdery, np. `{modName}`, `{restoredCount}`, `{failedCount}`, `{skippedCount}`.
- Liczniki powinny używać istniejącego mechanizmu formatowania; jeżeli pojawi się pluralizacja z liczbą DLL, docelowo użyć ICU MessageFormat albo osobnych wariantów zgodnie z aktualnym wzorcem projektu.
- Core zwraca stabilne statusy/kody (`missingCatalog`, `restoreFailed`), a UI mapuje je na lokalizowane komunikaty.
- Future locale: dodanie nowego języka ma wymagać tylko dopisania kluczy w zasobach lokalizacji.

## Config, data and migration implications

- `.susmodder-install.json` zostaje źródłem metadanych per katalog moda.
- SQLite `mods` i `user_settings` bez migracji w MVP.
- Jeżeli zdecydujemy, że `ModInstanceDll` ma przechowywać `AutoUpdateEnabled`, potrzebna będzie migracja `DatabaseService.ApplyMigrations()` z nowym `PRAGMA user_version`.
- Nie pisać do `appsettings.json`.
- Nie usuwać `.sqlite-migrated` ani nie mieszać z migracją JSON→SQLite.

## Platform, packaging, updater, telemetry, privacy, AV constraints

- Steam i Epic muszą używać tej samej usługi preservation, ale platformowy installer full moda pozostaje osobny.
- Epic ma strukturę `ModName/AmongUs`; ścieżki DLL muszą dalej przechodzić przez `PathSettings.GetActualModPath`.
- Zachować safe replace DLL (`.tmp` + backup) i nie wprowadzać skanowania/instalowania arbitralnych DLL z katalogu użytkownika.
- Nie dodawać nowych zewnętrznych binarek ani runtime translatorów, żeby nie pogarszać reputacji AV.
- Telemetria, jeśli zostanie dodana do tego flow, może raportować tylko agregaty: liczba DLL restored/failed/skipped, platforma, typ flow (`auto`, `manual`, `pack`). Bez ścieżek lokalnych, nazw użytkownika i pełnych URL.
- Velopack/app updater bez zmian.

## Verification plan

### Unit tests Core

1. `FullModAddonPreservationService`:
   - capture zwraca pusty snapshot dla braku mapy,
   - capture odrzuca mapę innego moda,
   - restore instaluje wszystkie DLL ze snapshotu,
   - restore zachowuje `DllModInstallation.AutoUpdateEnabled`,
   - missing catalog daje `SkippedMissingCatalog`, nie wyjątek globalny.
2. `DllModificationService`:
   - aktualizacja istniejącego wpisu DLL nie zeruje `AutoUpdateEnabled`,
   - dodanie nowego wpisu może przyjąć flagę ze snapshotu przez nową metodę/pomocnik.
3. `ModInstanceInstaller.UpdateInstanceAsync`:
   - po usunięciu i reinstalacji full moda DLL z repo/mapy wracają,
   - flaga DLL auto-update wraca, jeżeli była w mapie.
4. `ModPackInstaller.UpdateExistingInstanceAsync`:
   - update full moda nie usuwa DLL z manifestu packa,
   - pack-pinned wersje DLL są finalnie zgodne z manifestem.

### Integration / smoke tests

1. Klasyczny FULL + 2 DLL, auto-update ON:
   - po cichym update katalog zawiera DLL w `BepInEx/plugins`,
   - nowa `.susmodder-install.json` ma oba wpisy i zachowane flagi.
2. Klasyczny FULL + 2 DLL, auto-update OFF i update ręczny:
   - ten sam wynik, dodatkowo dialog podsumowania.
3. FULL z DLL, jeden DLL usunięty z katalogu/API:
   - full update kończy się sukcesem częściowym, missing DLL raportowany.
4. Modpack shared_pack:
   - update packa z nową wersją full moda i tym samym DLL zachowuje DLL,
   - update packa ze zmienioną wersją DLL kończy z wersją z manifestu packa.
5. Epic:
   - ścieżka `AmongUs/BepInEx/plugins` poprawna po restore.
6. Regression legacy:
   - bezpośrednie wywołanie `ModUpdates.UpdateModAsync()` nie gubi DLL albo test potwierdza, że metoda jest nieużywana i zdeprecjonowana.

### Manual QA checklist

- Sprawdzić logi `[Update]`, `[InstallationMap]`, `[DllRestore]`.
- Sprawdzić, że backup full moda jest usuwany tylko po pełnej instalacji full moda; restore DLL może być częściowy bez rollbacku full moda.
- Sprawdzić, że po failed full update backup starej wersji nadal zawiera stare DLL i mapa jest spójna.
- Sprawdzić status bar updates count po auto-update.

## Suggested implementation order

### Etap 1 — Core preservation service

- Dodać modele snapshot/result.
- Dodać usługę capture/restore w `SUSModder.Core.Services`.
- Dodać unit testy dla capture/restore.

Można robić równolegle z etapem 2.

### Etap 2 — DllModificationService preservation flags

- Zmienić update/dodanie wpisu w `InstallationMap.InstalledDlls`, aby nie zerować `AutoUpdateEnabled`.
- Dodać pomocnik do nadpisania flag po instalacji, jeżeli nie chcemy zmieniać publicznej sygnatury `InstallDllToModAsync`.
- Test regresyjny flagi.

### Etap 3 — Klasyczny full update UI → Core

- `MainWindowViewModel.Updates.cs`: zastąpić lokalny snapshot/listę `DllModInstallation` wywołaniem Core service.
- `RestoreDllModsAfterUpdateAsync` usunąć albo zmienić w wrapper.
- Dodać lokalizowane komunikaty PL/EN.

### Etap 4 — Legacy GameIntegration path

- `ModUpdates.UpdateModAsync()` przekierować do wspólnej ścieżki albo zdeprecjonować z testem braku użycia.
- Usunąć delete→install bez preservation.

### Etap 5 — Instancje i modpacki

- Uzupełnić `ModInstanceInstaller.UpdateInstanceAsync()` o capture flag z mapy, jeśli repozytorium instancji ich nie trzyma.
- Potwierdzić kolejność w `ModPackInstaller.UpdateExistingInstanceAsync` i dodać testy pack-pinned DLL.

### Etap 6 — QA i dokumentacja

- Testy automatyczne.
- Manual smoke Steam/Epic.
- Krótka notatka w dokumentacji Core o kontrakcie: „update FULL preserves DLL addons”.

## Parallelizable tasks

- Core service + tests można robić równolegle z i18n copy keys.
- Testy `ModInstanceInstaller` można przygotować równolegle z refaktorem UI, jeżeli kontrakt modeli snapshot/result jest ustalony.
- Manual QA Epic dopiero po integracji UI/Core.

## Open questions before implementation

1. Czy przy DLL bez auto-update mamy zachować dokładną starą wersję, jeżeli API zacznie wspierać historyczne artifacty? MVP: najnowszy dostępny DLL.
2. Czy `ModInstanceDll` powinien dostać `AutoUpdateEnabled` w SQLite, czy wystarczy `InstallationMap` jako źródło flagi dla instancji?
3. Czy częściowy failure restore DLL ma pokazywać dialog po cichym auto-update, czy tylko toast + diagnostyka?
4. Czy pinning `FullModInstallation.PinnedInstallVersion` powinien być czyszczony po ręcznej aktualizacji do nowej wersji?

## Sources used

- `mcp-rag`: użyty jako pierwsza warstwa discovery, ale zapytania dla tego tematu nie zwróciły trafień; analiza została zweryfikowana lokalnymi plikami.
- Delegowany scan dokumentacji: `sus-free-doc-scout` dla `DOC/PLAN`, `DOC/POC`, `DOC/Core`, `DOC/Updater`.
- `SUSModder/ViewModels/MainWindowViewModel.Updates.cs`
- `SUSModder.Core/Services/ModUpdateManager.cs`
- `SUSModder.Core/Services/DllModificationService.cs`
- `SUSModder.Core/Services/InstallationMapManager.cs`
- `SUSModder.Core/Services/ModInstanceInstaller.cs`
- `SUSModder.Core/Services/ModPackInstaller.cs`
- `SUSModder.Core/Services/ModPackUpdateChecker.cs`
- `SUSModder.Core/GameIntegration/ModManager.cs`
- `SUSModder.Core/GameIntegration/ModUpdate.cs`
- `SUSModder.Core/GameIntegration/EpicVersionManager.cs`
- `SUSModder.Core/Models/InstallationMap.cs`, `FullModInstallation.cs`, `DllModInstallation.cs`


