# POC: Migracja konfiguracji z plików JSON na SQLite

**Data:** 2026-05-27
**Status:** ✅ Zatwierdzony (2026-05-27)
**Autor:** SUSModder planning agent (DeepSeek V4 Pro)
**Target:** .NET 10.0, `Microsoft.Data.Sqlite` 10.0.8
**Zależność:** `SUSModder.Core`

---

## 1. Problem

SUSModder używa obecnie **4 osobnych plików JSON** do przechowywania danych runtime:

| Plik | Lokalizacja | Dane |
|------|-------------|------|
| `config.json` | `{exeDir}/` | Katalog modów (`List<ModConfiguration>`) |
| `user-settings.json` | `%APPDATA%/SUSModder/` | Preferencje użytkownika (~15 pól) |
| `touConfigsBase.json` | `%APPDATA%/SUSModder/` | Historia zapisanych konfiguracji ToU |
| `.susmodder-install.json` | Per-mod w katalogu moda | Mapa instalacji (FULL + DLL) |

**Zidentyfikowane problemy:**
1. **Duplikacja danych** – `config.json` i `.susmodder-install.json` przechowują te same informacje (InstallPath, ModVersion) bez gwarancji spójności
2. **Brak transakcyjności** – operacje wieloplikowe nie są atomowe; crash w połowie zapisu = rozjazd danych
3. **Słaba wydajność przy N modach** – każdy odczyt/zapis wymaga deserializacji/serializacji CAŁEGO pliku
4. **Brak współbieżności** – wiele operacji musi być szeregowanych ręcznie
5. **Brak wersjonowania schematu** – każda zmiana struktury wymaga ręcznej migracji w kodzie
6. **appsettings.json jako runtime-writable** – antywzorzec (plik w katalogu apki, nadpisywany przez updater)

---

## 2. Rozwiązanie – SQLite

**Wybrana technologia:** `Microsoft.Data.Sqlite` 10.0.8 (embedded, zero zależności systemowych)

**Plik bazy:** `%APPDATA%/SUSModder/susmodder.db`

### 2.1 Co migrujemy do SQLite

| Dane | Obecny plik | Tabela SQLite | Uzasadnienie |
|------|-------------|---------------|-------------|
| Katalog modów | `config.json` | `mods` | Tablica obiektów, częste zapytania warunkowe, korzyść z indeksów i selektywnego UPDATE |
| Preferencje użytkownika | `user-settings.json` | `user_settings` | Płaski key-value, ale zysk z transakcyjności i braku konfliktu z appsettings |
| Historia konfiguracji ToU | `touConfigsBase.json` | `tou_configs` | Mała tablica, ale zysk z atomowości |

### 2.2 Co ZOSTAJE jako plik JSON

| Dane | Plik | Dlaczego NIE migrujemy |
|------|------|------------------------|
| Mapa instalacji | `.susmodder-install.json` | **Celowa redundancja** – plik w katalogu moda przetrwa przeinstalowanie aplikacji, utratę bazy, itp. To feature, nie bug. |
| Lokalizacje | `pl.json`, `en.json` | Dane **statyczne, bundlowane**. JSON jest idealny dla tłumaczy (zero toolingu). |
| Wersja aplikacji | `version.json` | Aktualizowany przez Velopack/CI. Trywialny rozmiar. |
| Konfiguracja API | `appsettings.json` | Konfiguracja aplikacji – powinna być edytowalna notatnikiem. **Ma być read-only** (koniec z runtime writes). |

---

## 3. Schemat bazy (propozycja)

```sql
-- Tabela: mods (zastępuje config.json)
CREATE TABLE mods (
    Id              INTEGER PRIMARY KEY,
    ModName         TEXT    NOT NULL,
    PngFileName     TEXT    NOT NULL,
    InstallPath     TEXT,
    GitHubRepoOrLink TEXT   NOT NULL DEFAULT '',
    EpicGitHubRepoOrLink TEXT,
    ModType         TEXT    NOT NULL CHECK (ModType IN ('full', 'dll', 'Vanilla')),
    DllInstallPath  TEXT,
    ModVersion      TEXT    NOT NULL DEFAULT '',
    AmongVersion    TEXT    NOT NULL DEFAULT '',
    LastUpdated     TEXT,          -- ISO 8601
    Description     TEXT    NOT NULL DEFAULT '',
    HasRoles        INTEGER,       -- 0/1/NULL (bool?)
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_mods_type ON mods(ModType);
CREATE INDEX idx_mods_name ON mods(ModName);

-- Tabela: user_settings (zastępuje user-settings.json)
CREATE TABLE user_settings (
    key             TEXT PRIMARY KEY,
    value           TEXT NOT NULL
);

-- Tabela: tou_configs (zastępuje touConfigsBase.json)
CREATE TABLE tou_configs (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    hash            TEXT    NOT NULL,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_tou_configs_hash ON tou_configs(hash);
```

### 3.1 Rozważenie alternatywnego schematu dla user_settings

**Opcja A** – key-value (jak wyżej): proste, elastyczne, łatwe do rozszerzania. Ale tracimy type safety.

**Opcja B** – kolumny per pole: type-safe, ale wymaga migracji przy każdym nowym polu.

**Decyzja:** 🔒 **ZATWIERDZONA – Opcja B (kolumny).** Type safety, SQL constraints, lepsza wydajność przy UPDATE pojedynczych pól. Przy dodawaniu nowego pola: ALTER TABLE + migracja w `DatabaseService`.

```sql
-- 🔒 ZATWIERDZONE: Opcja B – kolumny per pole
-- Pełny schemat (16 pól), odzwierciedla UserSettings.cs (2026-05-27)
CREATE TABLE user_settings (
    id                  INTEGER PRIMARY KEY CHECK (id = 1),  -- singleton
    mode                TEXT    NOT NULL DEFAULT '',
    last_launch_id      INTEGER NOT NULL DEFAULT 0,
    theme               TEXT    NOT NULL DEFAULT 'dark',
    language            TEXT    NOT NULL DEFAULT '',
    telemetry_enabled   INTEGER NOT NULL DEFAULT 1,
    mods_install_path   TEXT    NOT NULL DEFAULT '',
    license_accepted    INTEGER NOT NULL DEFAULT 0,
    first_run_date      TEXT    NOT NULL DEFAULT '',
    update_channel      TEXT    NOT NULL DEFAULT 'release',
    vanilla_install_path TEXT   NOT NULL DEFAULT '',
    av_warning_sig      TEXT    NOT NULL DEFAULT '',
    last_seen_version   TEXT    NOT NULL DEFAULT '',
    minimize_to_tray    INTEGER NOT NULL DEFAULT 1,
    show_quick_launch_tray INTEGER NOT NULL DEFAULT 1,
    tray_first_minimize_shown INTEGER NOT NULL DEFAULT 0,
    settings_version    INTEGER NOT NULL DEFAULT 0
);
```

---

## 4. Architektura warstwy danych

### 4.1 Nowe klasy w `SUSModder.Core`

```
SUSModder.Core/
├── Data/
│   ├── DatabaseService.cs          // zarządza połączeniem, migracjami, inicjalizacją
│   ├── IModRepository.cs           // interfejs
│   ├── ModRepository.cs            // CRUD dla mods
│   ├── IUserSettingsRepository.cs  // interfejs
│   ├── UserSettingsRepository.cs   // CRUD dla user_settings
│   ├── ITouConfigRepository.cs     // interfejs
│   └── TouConfigRepository.cs      // CRUD dla tou_configs
```

### 4.2 DatabaseService – odpowiedzialności

1. **Inicjalizacja** – tworzy/otwiera plik `susmodder.db` w `%APPDATA%/SUSModder/`
2. **Migracje** – sprawdza wersję schematu (`PRAGMA user_version`), aplikuje migracje SQL
3. **Migracja danych z JSON** – przy pierwszym uruchomieniu (brak tabel) importuje dane ze starych plików JSON
4. **Wal Validation** – przy starcie sprawdza integralność (`PRAGMA integrity_check`)
5. **Backup** – przed migracjami tworzy kopię zapasową

### 4.3 Wzorzec dostępu

- **Repozytoria** (Repository Pattern) – każda tabela ma własne repozytorium z interfejsem
- **Connection pooling** – `Microsoft.Data.Sqlite` nie wspiera connection poolingu (SQLite jest single-writer). Zamiast tego: jedna współdzielona koneksja z lockiem (`SemaphoreSlim`) lub otwieranie/zamykanie per operacja (WAL mode pozwala na concurrent reads)
- **WAL mode** – `PRAGMA journal_mode=WAL;` – pozwala na współbieżne odczyty podczas zapisu
- **Cache w pamięci** – dla `user_settings` (czytane bardzo często) – `ConcurrentDictionary` + leniwy flush do DB
- **Asynchroniczność** – `Microsoft.Data.Sqlite` nie ma natywnego async API (Sqlite jest synchronous z natury). Metody repozytoriów będą `async` (owinięte w `Task.Run` dla ciężkich operacji) dla zgodności z resztą kodu (Avalonia UI)

### 4.4 Uwaga: SQLite i async

`Microsoft.Data.Sqlite` **nie wspiera prawdziwego async I/O**. Wszystkie operacje są synchroniczne. W kontekście aplikacji Avalonia (UI) oznacza to:
- Lekkie odczyty (np. `user_settings`) mogą być robione synchronicznie z UI thread (są mikrosekundowe)
- Cięższe operacje (zapis listy modów) powinny być robione w `Task.Run`, żeby nie blokować UI
- Alternatywnie: rozważyć `Microsoft.Data.Sqlite` + własne wątki background

### 4.5 Strategia INotifyPropertyChanged dla danych z bazy

**Problem:** `ModConfiguration` implementuje `INotifyPropertyChanged` (wymagane przez Avalonia data binding). Jak zachować INPC przy przejściu z JSON na SQLite?

**Decyzja:** 🔒 **`ModConfiguration` zostaje bez zmian.** Wzorzec dostępu:

```
┌──────────────┐     ┌──────────────────┐     ┌─────────────┐
│  Avalonia UI │ ←→  │ ModConfiguration │ ←→  │ ModRepository│
│  (binding)   │     │ (INPC + dane)    │     │ (SQLite)    │
└──────────────┘     └──────────────────┘     └─────────────┘
```

**Przepływ odczytu (startup):**
1. `ModRepository.GetAllMods()` → `SELECT * FROM mods` → deserializuje każdy wiersz w `ModConfiguration`
2. Zwraca `List<ModConfiguration>` – identycznie jak obecny `ConfigManager.LoadConfig()`
3. UI/Avalonia binduje się do tych samych obiektów co wcześniej
4. INPC działa dokładnie tak samo – zmiana właściwości → UI się odświeża

**Przepływ zapisu (install/uninstall/update moda):**
1. ViewModel modyfikuje obiekt `ModConfiguration` (np. `mod.InstallPath = newPath`)
2. INPC automatycznie odświeża UI
3. ViewModel woła `ModRepository.UpdateMod(mod)` → `UPDATE mods SET InstallPath = @path WHERE Id = @id`
4. Zapis jest granularny – aktualizujemy tylko zmienione pole, nie całą listę

**Dlaczego to działa bez wrapperów/proxy:**
- `ModConfiguration` jest POCO z INPC – repozytorium nie musi wiedzieć o INPC
- Ten sam obiekt służy jako model domenowy i ViewModel (wzorzec "model-as-VM" już istnieje w kodzie)
- Zero dodatkowych warstw – minimalna zmiana istniejącego kodu

**Cache w pamięci:**
- `ModRepository` ładuje wszystkie mody do `List<ModConfiguration>` przy starcie (cache)
- Wszystkie odczyty idą z cache (O(1) lookup po ID)
- Zapis do DB jest natychmiastowy (write-through cache)
- Przy dużej liczbie modów (>100): rozważyć `ConcurrentDictionary<int, ModConfiguration>` zamiast `List`

---

## 5. Migracja danych – strategia

### 5.1 First-run migration flow

```
Start aplikacji
  ↓
DatabaseService.InitializeAsync()
  ├── Czy plik susmodder.db istnieje?
  │   ├── TAK → Otwórz, sprawdź wersję schematu, aplikuj pending migrations
  │   └── NIE → CREATE TABLES + IMPORT Z JSON:
  │        ├── Import config.json → tabela mods
  │        ├── Import user-settings.json → tabela user_settings
  │        ├── Import touConfigsBase.json → tabela tou_configs
  │        └── Ustaw PRAGMA user_version = 1
  ↓
  ✔ Gotowe – aplikacja działa na SQLite
```

### 5.2 Backward compatibility

- **Stare pliki JSON NIE są usuwane** po migracji – zostają jako backup
- Po udanej migracji zapisujemy flagę `.sqlite-migrated` w `%APPDATA%/SUSModder/`
- Przy kolejnych startach: jeśli flaga istnieje → używamy SQLite; jeśli nie → próbujemy migracji
- Jeśli migracja się nie powiedzie → aplikacja kontynuuje ze starymi plikami JSON (fallback)

### 5.3 Rollback strategy

- Przed migracją: kopia `susmodder.db` → `susmodder.db.bak`
- Przed każdą migracją schematu: backup bazy
- Jeśli coś pójdzie nie tak → przywróć backup, kontynuuj na JSON

---

## 6. Wpływ na istniejący kod

### 6.1 Co się zmienia

| Komponent | Zmiana |
|-----------|--------|
| `ConfigManager` | Zastąpiony przez `ModRepository` + `DatabaseService` |
| `ConfigRepository` | Usunięty (zastąpiony przez `ModRepository`) |
| `ConfigService` | Przepisany – deleguje do `ModRepository` zamiast `ConfigManager` |
| `UserSettingsService` | Przepisany – deleguje do `UserSettingsRepository` |
| `ModConfigHandler.AddConfigToJSON` | Przepisany – deleguje do `TouConfigRepository` |
| `LoadServerConfigDialog.LoadSavedConfigs` | Przepisany – deleguje do `TouConfigRepository` |
| `InstallationMapManager` | **Bez zmian** – nadal używa `.susmodder-install.json` |
| `LocalizationService` | **Bez zmian** – nadal używa `pl.json`/`en.json` |
| `ModManager`, `EpicVersionManager`, `DllModificationService` | Aktualizacja – zapisują do DB zamiast `ConfigManager` |
| `App.axaml.cs` | Dodana inicjalizacja `DatabaseService` |
| `Program.cs` | Dodana inicjalizacja bazy przed startem Avalonia |
| `AppSettingsViewModel` | Aktualizacja – zapisuje do `UserSettingsRepository` |

### 6.2 Co NIE jest ruszane

- ❌ `.susmodder-install.json` – zostaje jako plik
- ❌ `pl.json` / `en.json` – lokalizacje
- ❌ `appsettings.json` – staje się read-only (API endpoints)
- ❌ `version.json` – bez zmian
- ❌ `Secrets.cs` – nie dotyczy
- ❌ Wszystkie serwisy HTTP (API) – nie dotyczy
- ❌ Velopack updater – nie dotyczy

---

## 7. Harmonogram – fazy implementacji

### Faza 1: Infrastruktura + `user_settings` + cleanup appsettings.json
- 🔒 Cleanup `appsettings.json` (razem z migracją – nie osobno):
  - Usunąć runtime writes: `SaveConfigurationSetting`, `SaveThemeSetting`, `SaveLanguageSetting` z `ConfigManager`
  - `appsettings.json` staje się strictly read-only (API endpoints, default paths)
- Dodać `Microsoft.Data.Sqlite` 10.0.8 do `SUSModder.Core.csproj`
- Stworzyć `DatabaseService` (init, migracje schematu, backup, `PRAGMA user_version`)
- Dodać PRAGMA konfigurację: `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`
- Stworzyć `UserSettingsRepository` (pełny schemat 16 kolumn)
- Przepisać `UserSettingsService` na korzystanie z repozytorium (zachować istniejące API: `LoadUserSettings`, `SaveUserSettings`, `UpdateUserSetting`, `RunMigrations`)
- Testy: migracja z JSON → DB, backward compatibility, rollback
- **Czas:** 2 dni

### Faza 2: `mods` + `tou_configs`
- Stworzyć `ModRepository` (CRUD, bulk load, upsert)
- Przepisać `ConfigManager` / `ConfigService` na `ModRepository`
  - `ConfigManager` – klasa statyczna, ~30 call sites – refaktoryzacja wymaga DI lub wrappera
  - Strategia: zachować `ConfigManager` jako fasadę delegującą do `ModRepository` (minimalne zmiany w call sites)
- Stworzyć `TouConfigRepository` + typed model (pozbyć się `dynamic` z Newtonsoft.Json)
- Przepisać `ModConfigHandler.AddConfigToJSON` i `LoadServerConfigDialog.LoadSavedConfigs`
- Testy: migracja katalogu modów, install/uninstall/update, integralność po crashu
- **Czas:** 2-3 dni

### Faza 3: Optymalizacja i testy E2E
- Dodać cache w pamięci dla często czytanych danych
- Dodać indeksy (już w schemacie: `idx_mods_type`, `idx_mods_name`)
- Performance testing (50+ modów, symulacja crashu podczas zapisu)
- Testy E2E: pełny cykl install → update → uninstall na SQLite
- **Czas:** 1-2 dni

### Łączny szacowany czas: 5-7 dni programistycznych

---

## 8. Ryzyka

| Ryzyko | Prawdopodobieństwo | Wpływ | Mitygacja |
|--------|:---:|:---:|------|
| Utrata danych podczas migracji z JSON → DB | Niskie | Wysoki | Backup plików JSON przed migracją, flaga `.sqlite-migrated`, rollback |
| Regresja – niedziałający install/uninstall modów | Średnie | Wysoki | Testy E2E przed releasem, faza beta z wybranymi użytkownikami |
| Problem z SQLite w Velopack (plik bazy w katalogu apki?) | Niskie | Średni | Baza w `%APPDATA%`, nie w katalogu apki – Velopack jej nie rusza |
| Blokowanie UI przez synchroniczne operacje SQLite | Średnie | Średni | `Task.Run` dla ciężkich operacji, WAL mode dla concurrent reads |
| Konflikt z antywirusem (nowy plik .db) | Niskie | Niski | SQLite jest powszechnie używany, ma dobrą reputację AV |
| Użytkownicy z uszkodzonymi plikami JSON | Niskie | Średni | Walidacja przed migracją, pomiń uszkodzone wpisy, zaloguj |

---

## 9. Decyzje podjęte (2026-05-27)

| # | Decyzja | Wybór | Uzasadnienie |
|---|---------|-------|-------------|
| 1 | **Kierunek** | ✅ SQLite | Zatwierdzony. `Microsoft.Data.Sqlite` 10.0.8, embedded, zero zależności systemowych. |
| 2 | **ORM** | 🔒 Raw `Microsoft.Data.Sqlite` | 3 proste tabele, nie potrzebujemy EF Core. Mniej zależności, prostsza konfiguracja. |
| 3 | **user_settings schema** | 🔒 Kolumny per pole (16 kolumn) | Type safety, SQL constraints, lepsza wydajność UPDATE. Nowe pola przez ALTER TABLE + migrację. |
| 4 | **Cleanup appsettings.json** | 🔒 Razem z Fazą 1 | Nie ma sensu robić osobno – i tak dotykamy ConfigManager w Fazie 1. |
| 5 | **Backward compatibility** | 🔒 Brak fallbacku do JSON | One-shot migracja. Stare pliki JSON zostają jako archiwum, ale aplikacja po migracji używa wyłącznie SQLite. |
| 6 | **INotifyPropertyChanged** | 🔒 `ModConfiguration` bez zmian | Ten sam obiekt służy jako model i VM. Repozytorium zwraca `List<ModConfiguration>`. Zero wrapperów. |
| 7 | **`.susmodder-install.json`** | 🔒 Zostaje jako plik | Celowa redundancja. DB jest source of truth, plik jako kopia + recovery. |
| 8 | **touConfigsBase.json** | 🔒 Migruje do `tou_configs` | Okazja na wprowadzenie typed modelu (pozbycie się `dynamic` + Newtonsoft.Json). |

### Rozstrzygnięte (2026-05-27, decyzje użytkownika)

| # | Pytanie | Decyzja | Uzasadnienie |
|---|---------|---------|-------------|
| A | Jak głęboko refaktoryzować `ConfigManager`? | 🔒 **Pełne usunięcie + DI** | `ConfigManager` jako statyczna klasa z side-effectami to antywzorzec. Zastąpiony przez `IModRepository` rejestrowany w DI. Wszystkie ~30 call sites przepisane na constructor injection. |
| B | Czy wspierać fallback do JSON przez 1 release? | 🔒 **NIE** | Mniej kodu do utrzymania. Migracja jest one-shot: JSON → SQLite, potem tylko SQLite. W razie problemów – backup manualny użytkownika. |
| C | Testy E2E – jakie minimalne scenariusze? | 🔒 **3 scenariusze** | (1) Install → update → uninstall moda, (2) Crash aplikacji podczas zapisu do DB (WAL recovery), (3) Migracja z częściowo uszkodzonym JSON (pomiń uszkodzone wpisy, zaloguj). |

---

## 10. Kontekst środowiskowy

| Element | Wartość |
|---------|---------|
| Target framework | `net10.0` (SUSModder.Core), `net10.0-windows` (SUSModder UI) |
| Pakiet SQLite | `Microsoft.Data.Sqlite` **10.0.8** (NuGet, 2026-05-12) |
| Platforma | Windows 10/11 (x64) |
| Updater | Velopack (pliki w `%APPDATA%` nie są ruszane) |
| Języki | PL + EN (i18n) |

---

*Analiza źródłowa: `mcp-rag` (repo-wide), bezpośredni odczyt `UserSettings.cs`, `UserSettingsService.cs`, `TelemetryService.cs`, `ConfigManager`, `ConfigRepository`, `InstallationMapManager.cs`, `ModConfigHandler.cs`.*
