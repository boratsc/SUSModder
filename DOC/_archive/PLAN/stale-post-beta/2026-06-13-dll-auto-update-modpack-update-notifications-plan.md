# Plan: automatyczne aktualizacje DLL i powiadomienia aktualizacji modpacków

Data: 2026-06-13  
Status: plan do implementacji  
Zakres: SUSModder 3.x, API v2, SQLite, InstallationMap, panel DLL, panel modpacków / Moje zestawy

## Kontekst

Po naprawie wykrywania aktualizacji modów FULL i zabezpieczeniu procesu update przed utratą starej instalacji zostały dwa powiązane tematy:

1. Mody DLL mają ręczne sprawdzanie i aktualizowanie, ale nie mają per-DLL ustawienia automatycznej aktualizacji.
2. Modpacki mają bardziej złożony model wersji i źródeł, więc nie powinny dostać automatycznej aktualizacji w MVP. Potrzebne jest bezpieczne powiadomienie i ręczna decyzja użytkownika.

Istniejące mechanizmy:

- FULL mod auto-update jest trzymany w `FullModInstallation.AutoUpdateEnabled` w `.susmodder-install.json`.
- DLL instalacje są trzymane w `InstallationMap.InstalledDlls` jako `DllModInstallation`.
- `DllUpdateManager.CheckDllUpdatesAsync()` wykrywa różnice wersji DLL względem API.
- `DllModificationService.InstallDllToModAsync()` instaluje / aktualizuje DLL w wybranym modzie FULL.
- Modpacki mają `ModPack`, `ModPackFullMod`, `ModPackDllMod`, `ModPackExternalDll`, `ModInstance`, `ModInstanceDll`, `SourcePackCode`.
- `ModInstanceUpdateChecker` obecnie porównuje głównie wersję full moda instancji z katalogiem.

## Goal

### DLL

Dodać możliwość włączenia automatycznej aktualizacji per mod DLL w panelu DLL, z bezpiecznym update flow: jeśli download albo zapis się nie powiedzie, stara DLL zostaje nienaruszona, a aktualizacja będzie ponowiona przy następnym sprawdzaniu.

### Modpacki

Dodać ręczne powiadomienie o możliwej aktualizacji modpacka / instancji utworzonej z udostępnionego modpacka, bez automatycznej instalacji zmian.

## Non-goals

- Brak automatycznej aktualizacji modpacków w MVP.
- Brak wymuszania aktualizacji modpacków autora na lokalnych instancjach użytkownika.
- Brak runtime pobierania tłumaczeń.
- Brak migracji DLL na CDN w tym etapie.
- Brak rozbudowanego backendowego systemu rewizji paczek w pierwszym klienckim etapie, chyba że backend już udostępnia porównywalne dane.

## Language / i18n impact

Nowe teksty muszą być kluczami PL/EN, bez hardcodowanych stringów w UI:

- `DLL.AutoUpdate.Enable`
- `DLL.AutoUpdate.Enabled`
- `DLL.AutoUpdate.Disabled`
- `DLL.AutoUpdate.Mixed`
- `DLL.AutoUpdate.FailedOldPreserved`
- `DLL.AutoUpdate.NextRetry`
- `ModPack.UpdateAvailable`
- `ModPack.CheckUpdates`
- `ModPack.UpdateManualOnly`
- `ModPack.NoUpdateAvailable`
- `ModPack.UpdateCheckFailed`

Zasady:

- fallback: `pl`,
- placeholdery muszą być zgodne między PL/EN, np. `{dllName}`, `{modName}`, `{packName}`,
- jeśli pokazujemy liczby lokalizacji DLL, użyć ICU MessageFormat / istniejącego wzorca pluralizacji,
- przyszły locale ma być dodawalny przez zasoby, bez zmiany logiki.

## User workflow

### DLL auto-update

1. Użytkownik otwiera panel DLL.
2. Przy każdym DLL widzi toggle `Automatycznie aktualizuj`.
3. Jeśli DLL nie jest nigdzie zainstalowany, toggle jest nieaktywny albo opisany jako `Zainstaluj DLL, aby włączyć auto-update`.
4. Jeśli DLL jest zainstalowany w wielu full modach:
   - MVP: toggle globalnie ustawia flagę dla wszystkich lokalizacji tego DLL,
   - UI może pokazać stan mieszany, jeśli część lokalizacji ma inną flagę.
5. Przy starcie / post-init aplikacja sprawdza aktualizacje DLL.
6. Dla DLL z auto-update ON aktualizacja idzie bez dialogu.
7. Dla DLL z auto-update OFF zostaje obecny ręczny dialog.
8. Jeśli update DLL się nie uda, stara DLL zostaje, log / toast informuje o błędzie, a kolejna próba nastąpi przy następnym sprawdzeniu.

### Modpack update notification

1. Użytkownik widzi swoje instancje modpacków w panelu `Moje zestawy` / modpacków.
2. Instancje pochodzące z udostępnionej paczki (`Origin = shared_pack`, `SourcePackCode != null`) mogą dostać badge `Dostępna aktualizacja paczki`.
3. Kliknięcie pokazuje różnice:
   - full mod: stara / nowa wersja,
   - DLL katalogowe: stare / nowe wersje,
   - external DLL: zmiana SHA256 / pliku,
   - konfiguracje: informacja o możliwej zmianie configu, jeśli da się porównać hash.
4. Użytkownik ręcznie decyduje, czy zaktualizować / odtworzyć instancję z nowej paczki.
5. Brak cichej automatycznej aktualizacji modpacka.

## Core business logic responsibilities

### DLL

- Rozszerzyć `DllModInstallation`:

```csharp
[JsonPropertyName("autoUpdateEnabled")]
public bool AutoUpdateEnabled { get; set; }
```

- Dodać helpery do odczytu/zapisu flagi w InstallationMap:
  - ustaw auto-update dla konkretnego DLL we wszystkich lokalizacjach,
  - odczytaj stan: off/on/mixed,
  - zachowaj flagę przy aktualizacji DLL.

- Rozszerzyć `DllUpdateManager` albo dodać filtr w UI/service:
  - pełna lista update nadal dla ręcznego dialogu,
  - osobna lista auto-update: tylko DLL z `AutoUpdateEnabled = true`.

- Zabezpieczyć `DllModificationService.InstallDllToModAsync()`:
  - pobierz DLL do temp,
  - zweryfikuj sukces downloadu,
  - dopiero potem zastąp docelowy plik,
  - jeśli zapis/pobranie nie powiedzie się, stara DLL zostaje.

Proponowany flow DLL:

```text
resolve URL -> download temp -> if fail: return failed, old DLL preserved
if target exists: move/copy old to backup
write new DLL -> update InstallationMap
if write fails: restore old DLL
on success: delete backup/temp
```

### Modpacki

- Dodać `ModPackUpdateChecker` po stronie Core.
- Dla lokalnej instancji z `SourcePackCode` pobierać aktualny pack przez `ModPackService.GetPackAsync(code)`.
- Porównywać snapshot lokalny z paczką:
  - `ModInstance.BaseModId` vs `ModPack.FullMod.Id`,
  - `ModInstance.FullModVersion` vs `ModPack.FullMod.Version`,
  - `ModInstanceDll.DllModId` / `DllVersion` vs `ModPack.DllMods`,
  - external DLL `Sha256`, jeśli lokalnie zapisany,
  - config hash, jeśli dostępny.
- Zwracać strukturę `ModPackUpdateInfo` z listą różnic i statusem.

## UI / Avalonia responsibilities

### DLL panel

Plik bazowy: `SUSModder/Views/DllManagementPanel.axaml`.

Dodać przy każdym DLL:

- toggle auto-update,
- etykietę stanu: ON/OFF/MIXED,
- tooltip wyjaśniający, że dotyczy wszystkich lokalizacji instalacji DLL,
- opcjonalnie mały badge `update available` jeśli DLL ma dostępny update.

ViewModel powinien wystawić:

- `DllAutoUpdateState`,
- command `ToggleDllAutoUpdateCommand`,
- informację o liczbie lokalizacji, gdzie DLL jest zainstalowany.

### Modpack panel

Pliki bazowe:

- `MainWindowViewModel.ModPacks.cs`,
- `MainWindowViewModel.ModInstances.cs`,
- `ModInstanceItem.cs`,
- widoki modpacków / instancji.

Dodać:

- badge `Dostępna aktualizacja`,
- command `CheckModPackUpdateCommand`,
- modal/drawer z różnicami,
- manualny przycisk `Zaktualizuj ręcznie` / `Utwórz nową instancję z aktualnej paczki`.

## Config and migration implications

### DLL InstallationMap

- Dodanie `autoUpdateEnabled` do `DllModInstallation` jest kompatybilne wstecz: brak pola = `false`.
- Nie wymaga migracji SQLite, bo `.susmodder-install.json` pozostaje plikiem per instalacja.
- Trzeba uważać przy odtwarzaniu DLL po aktualizacji full moda: flaga musi zostać zachowana.

### SQLite / mod_instances

Aktualnie `mod_instances.auto_update_enabled` istnieje, ale dla modpacków nie powinno oznaczać automatycznej aktualizacji paczki. Należy doprecyzować semantykę:

- dla manual/clone/full instance: może oznaczać przyszłe auto-update full moda,
- dla `shared_pack`: na razie ignorować dla auto-update paczki albo traktować tylko jako ustawienie bazowego full moda, nie paczki.

Jeśli w przyszłości będzie backendowa rewizja paczki, można dodać:

- `source_pack_revision`,
- `last_checked_pack_revision`,
- `pack_update_available`,
- albo trzymać to jako cache w osobnej tabeli.

## Platform, packaging, updater, telemetry, privacy, AV constraints

- Steam/Epic: DLL install path musi dalej przechodzić przez `PathSettings.GetActualModPath`, bo Epic ma podkatalog `AmongUs`.
- Packaging: brak nowych zewnętrznych binarek.
- Velopack: bez wpływu.
- AV/reputation: download do temp i atomic replace jest lepszy niż częściowe nadpisywanie pliku.
- Telemetry: można wysyłać tylko agregaty, np. liczba DLL auto-update enabled / liczba nieudanych update, bez ścieżek plików i bez nazw lokalnych folderów.
- Privacy: nie wysyłać lokalnych ścieżek instalacji, lokalnych nazw instancji ani custom notatek.

## Backend compatibility / susmodder.app

### DLL

MVP może działać bez zmian backendu, bo wersje DLL są już w katalogu API, a URL może być GitHub/CDN zależnie od konfiguracji moda.

### Modpacki

Powiadomienie może działać częściowo przez istniejące `GET /modpacks/:code`, jeśli kod wskazuje aktualizowalny rekord. Jeśli kod jest niezmiennym snapshotem, potrzebny będzie backendowy mechanizm rewizji:

Opcja docelowa:

```http
GET /modpacks/:code/update-status
```

Przykładowa odpowiedź:

```json
{
  "data": {
    "packCode": "ABC123",
    "currentRevision": 1,
    "latestRevision": 2,
    "hasUpdate": true,
    "changes": [
      { "type": "fullMod", "name": "Town of Us", "from": "5.3.1", "to": "5.5.0" },
      { "type": "dll", "name": "AUnlocker", "from": "1.3.0", "to": "1.4.0" }
    ]
  }
}
```

Ale klient MVP powinien najpierw wspierać ręczne sprawdzenie i lokalne porównanie tego, co już jest dostępne.

## Verification plan

### DLL

1. DLL auto-update OFF: update wykryty, ale nie instaluje się automatycznie.
2. DLL auto-update ON: update instaluje się automatycznie.
3. DLL download 404: stara DLL zostaje, InstallationMap bez zmiany wersji.
4. DLL write failure / file locked: stara DLL zostaje lub backup przywrócony.
5. DLL zainstalowany w kilku full modach: toggle ustawia flagę we wszystkich lokalizacjach MVP.
6. Mixed state: UI pokazuje MIXED albo normalizuje do ON/OFF po kliknięciu.
7. Epic path: DLL trafia do właściwego `AmongUs/BepInEx/plugins`.

### Modpacki

1. Instancja bez `SourcePackCode`: brak sprawdzania update paczki.
2. Instancja `shared_pack` bez zmian: brak badge.
3. Full mod version różna: badge + diff.
4. DLL version różna: badge + diff.
5. External DLL hash różny: badge + ostrzeżenie.
6. Backend 404/Gone dla packCode: status `nie można sprawdzić`, bez błędu krytycznego.
7. Brak auto-update: żaden modpack nie aktualizuje się sam.

## Suggested implementation order

### Kolejność główna

1. DLL safe replace w `DllModificationService.InstallDllToModAsync`.
2. Model `DllModInstallation.AutoUpdateEnabled` + odczyt/zapis InstallationMap.
3. UI toggle w panelu DLL.
4. Auto-update runner dla DLL po post-init.
5. Modpack update checker jako read-only diff.
6. Badge/panel różnic modpacków.
7. Dopiero później backend revision endpoint, jeśli potrzebny.

### Równoległe zadania

Można robić równolegle:

- Core DLL safe replace + InstallationMap helpery.
- UI panel DLL i lokalizacja PL/EN.
- Modpack checker read-only i model wyniku.
- Backend analiza endpointu rewizji modpacka.

Nie robić równolegle bez synchronizacji:

- zmiany semantyki `mod_instances.auto_update_enabled`,
- automatyczne apply update modpacków,
- migracje SQLite pod rewizje paczek.

## Open questions

1. Czy toggle DLL ma działać globalnie per DLL, czy per DLL w konkretnej lokalizacji full moda? MVP: globalnie po lokalizacjach.
2. Czy DLL z GitHuba ma mieć SHA256 w katalogu, czy tylko best-effort download? Dla bezpieczeństwa docelowo SHA256.
3. Czy `packCode` jest snapshotem, czy może reprezentować aktualizowalną paczkę autora?
4. Czy autor paczki ma mieć możliwość publikowania nowej rewizji pod tym samym kodem?
5. Czy ręczna aktualizacja paczki ma nadpisywać istniejącą instancję, czy tworzyć nową instancję obok starej? Bezpieczniejszy MVP: nowa instancja albo backup/swap z potwierdzeniem.

## Sources used

- `mcp-rag` lookup attempted; local source fallback used where RAG output was inconclusive.
- `mcp-obsidian` lookup attempted; no relevant external note returned.
- `SUSModder.Core/Services/DllUpdateManager.cs`
- `SUSModder.Core/Services/DllModificationService.cs`
- `SUSModder.Core/Models/DllModInstallation.cs`
- `SUSModder.Core/Models/InstallationMap.cs`
- `SUSModder.Core/Models/FullModInstallation.cs`
- `SUSModder.Core/Models/ModPack.cs`
- `SUSModder.Core/Services/ModPackInstaller.cs`
- `SUSModder.Core/Services/ModPackService.cs`
- `SUSModder.Core/Models/ModInstance.cs`
- `SUSModder.Core/Models/ModInstanceDll.cs`
- `SUSModder.Core/Services/ModInstanceUpdateChecker.cs`
- `SUSModder/Views/DllManagementPanel.axaml`
- `SUSModder/ViewModels/MainWindowViewModel.Updates.cs`
