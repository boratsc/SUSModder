# 19 – Lokalne instancje modpacków (Moje zestawy)

**Priorytet:** 🔴 P0/P1 dla SUSModder 3.0  
**Effort:** ~10-16 dni (Core + SQLite + UI + migracje + testy)  
**Status:** 📄 **POC / koncepcja produktu** — rozszerzenie modpacków poza sharing przez kod  
**Powiązane:** [`16-mod-pack-sharing.md`](16-mod-pack-sharing.md), [`18-beanmodmanager-ideas.md`](18-beanmodmanager-ideas.md), [`../POC/2026-06-01-ui-refresh-v3-poc.md`](../POC/2026-06-01-ui-refresh-v3-poc.md), [`../PLAN/MODPACK_API.md`](../PLAN/MODPACK_API.md)

---

## Cel

Dodać lokalny system **wielu jednoczesnych instancji modpacków**: użytkownik może mieć kilka instalacji tego samego moda full, każdą z osobną nazwą, folderem, DLL-ami i konfiguracją.

Przykłady:

- `ToU - Psychopaci piątek`
- `ToU - lobby 25 osób`
- `ToU - test beta`
- `TOR - klasyczny zestaw`
- `Town of Us + AleLudu + custom config`

Obecny system modpacków opisany w #16 i `MODPACK_API.md` dotyczy głównie **udostępniania snapshotu** przez kod/link. Ten POC dodaje brakującą warstwę: **Moje zestawy** jako lokalne, trwałe instalacje, które można uruchamiać, klonować, edytować i dopiero opcjonalnie udostępniać.

---

## Non-goals

- ❌ Nie tworzymy marketplace'a ani publicznego katalogu modpacków.
- ❌ Nie zmieniamy semantyki API sharingu z #16: paczka z kodem nadal jest snapshotem z TTL.
- ❌ Nie hostujemy lokalnych konfiguracji użytkownika bez jego akcji `Udostępnij`.
- ❌ Nie dodajemy zewnętrznych repozytoriów modów.
- ❌ Nie przenosimy lokalizacji PL/EN do runtime downloadów.
- ❌ Nie usuwamy od razu legacy `mods.InstallPath`; w MVP traktujemy je jako fallback/migrację.

---

## Problem obecnego modelu

Aktualnie katalog modów zakłada w praktyce relację:

```text
Mod full z API -> maksymalnie jedna instalacja / InstallPath
```

To blokuje scenariusze:

1. dwie instalacje Town of Us z różnymi DLL;
2. ten sam mod z różnymi configami ToU;
3. wersja stabilna i testowa obok siebie;
4. modpack zaimportowany z kodu bez nadpisywania istniejącej instalacji;
5. klonowanie działającego zestawu pod inne lobby.

Dodatkowo obecny `ModPackInstaller` działa w duchu: jeśli full mod już jest zainstalowany, użyj istniejącej instalacji i doinstaluj dodatki. Dla lokalnych modpacków domyślnym zachowaniem powinno być odwrotne: **instaluj jako nowy zestaw**, żeby niczego nie nadpisać.

---

## Model pojęć

### 1. Katalog modów

To, co przychodzi z API i jest przechowywane w `mods`:

- mody full;
- mody DLL;
- Vanilla;
- metadane wersji, ikon, opisów i linków pobierania.

To jest katalog: **co można zainstalować**.

### 2. Lokalna instancja / lokalny modpack

Nowy byt:

> konkretna instalacja moda full z własną nazwą, ścieżką, DLL-ami i konfiguracją.

To jest biblioteka użytkownika: **co użytkownik ma i uruchamia**.

### 3. Udostępniany modpack

Istniejący koncept z #16 i `MODPACK_API.md`:

- kod `XXXX-XXXX-XXXX`;
- TTL 7/30/90 dni;
- deep link `susmodder://pack/...`;
- web fallback `https://susmodder.app/pack/...`.

Lokalny modpack może być źródłem requestu `POST /api/mod-packs`, ale nie jest tym samym bytem.

---

## User workflow

### Start aplikacji

Domyślnym ekranem powinno być **Moje zestawy**, nie katalog modów. Użytkownik najczęściej chce uruchomić istniejący zestaw, nie ponownie instalować moda.

```text
[Moje zestawy] [Katalog modów] [Dodatki DLL]
```

### Tworzenie nowego zestawu z katalogu

1. Użytkownik wchodzi w `Katalog modów`.
2. Wybiera np. `Town of Us`.
3. Klikka `Utwórz nowy zestaw`.
4. Podaje nazwę, np. `ToU - znajomi`.
5. Wybiera wersję full moda.
6. Wybiera DLL-e, config ToU i opcjonalne dodatki.
7. SUSModder tworzy osobny folder instalacji.
8. Nowy zestaw pojawia się w `Moje zestawy`.

### Import kodu modpacka

1. Użytkownik klika link lub wpisuje kod.
2. SUSModder pokazuje preview jak obecnie.
3. Domyślna akcja: `Zainstaluj jako nowy zestaw`.
4. Użytkownik może zmienić nazwę lokalną przed instalacją.
5. Instalacja tworzy nową lokalną instancję i nie nadpisuje istniejących zestawów.

### Klonowanie zestawu

1. Użytkownik wybiera istniejący zestaw.
2. Klikka `Klonuj zestaw`.
3. Podaje nową nazwę.
4. Wybiera, czy kopiować:
   - DLL-e;
   - config ToU;
   - integration.dll;
   - przypięcie wersji.
5. SUSModder tworzy osobny folder i nowy wpis w `mod_instances`.

### Udostępnianie lokalnego zestawu

1. Użytkownik wybiera lokalny zestaw.
2. Klikka `Udostępnij zestaw`.
3. SUSModder mapuje lokalną instancję do `ModPackCreateRequest`.
4. API zwraca kod/link zgodnie z #16.

---

## UI / Avalonia

### Widok główny: Mod Browser + Inspector

POC UI 3.0 powinien zostać rozszerzony z listy modów do biblioteki elementów różnego typu.

```text
┌──────────────────────────────────────────────────────────────────────┐
│ SUSModder        [Steam/Epic]   [Szukaj...]   [Nowy zestaw] [⚙]      │
├──────────────────────────────────────────────────────────────────────┤
│ [Moje zestawy] [Katalog modów] [Dodatki DLL]                         │
├───────────────────────────────────────┬──────────────────────────────┤
│ MOD BROWSER                           │ INSPECTOR                    │
│ [ToU - piątek] [ToU - test]           │ Szczegóły wybranego zestawu  │
│ [TOR classic]  [Vanilla]              │ Akcje / config / sharing     │
└───────────────────────────────────────┴──────────────────────────────┘
```

### Karta lokalnego zestawu

```text
┌────────────────────────────────────────────┐
│ ToU - Psychopaci piątek           [UPDATE] │
│ Town of Us v5.4.0 · Among Us 2024.6        │
│ 3 DLL · config ToU · Discord: Psychopaci   │
│                                            │
│ [Uruchom]                        [⋯]       │
└────────────────────────────────────────────┘
```

Na karcie:

- customowa nazwa;
- bazowy mod full;
- wersja full moda i Among Us;
- liczba DLL;
- informacja o configu;
- status aktualizacji;
- primary action `Uruchom`.

### Inspector dla zestawu

```text
ToU - Psychopaci piątek
Town of Us · v5.4.0 · Among Us 2024.6
Steam

[Uruchom]

Zawartość:
- AleLuduMod v2.0
- ExtraRoles v1.3
- config ToU
- susmodder-integration.dll

Konfiguracja:
[Edytuj config] [Zmień nazwę] [Klonuj]

Udostępnianie:
[Utwórz kod modpacka]

Niebezpieczne:
[Odinstaluj zestaw]
```

### Typy elementów w browserze

Docelowo nie wszystko powinno być `ModItem`. Warto wprowadzić prezentacyjny model:

```text
ModBrowserItem
- Kind: Instance | CatalogFull | CatalogDll | Vanilla
- Title
- Subtitle
- Status
- PrimaryAction
```

Dzięki temu `Moje zestawy`, katalog i DLL mogą korzystać z jednego browsera, ale z innymi kartami i akcjami.

---

## Core business logic

### Nowe modele

#### `ModInstance`

```text
InstanceId           GUID / string
DisplayName          lokalna nazwa użytkownika
BaseModId            ID full moda z katalogu
BaseModName          snapshot nazwy full moda
FullModVersion       zainstalowana wersja
AmongVersion         wersja Among Us
Platform             steam | epic
InstallPath          pełna ścieżka folderu zestawu
Origin               manual | shared_pack | clone | legacy
SourcePackCode       opcjonalny kod źródłowej paczki
PinnedVersion        opcjonalnie
AutoUpdateEnabled    bool
CreatedAt            timestamp
UpdatedAt            timestamp
LastLaunchedAt       timestamp nullable
Notes                opcjonalnie
```

#### `ModInstanceDll`

```text
InstanceDllId
InstanceId
DllModId
DllName
DllVersion
Source               catalog | external
Sha256               dla external DLL
VtStatus             unknown | clean | suspicious
InstalledPath
CreatedAt
```

#### `ModInstanceConfig`

```text
ConfigId
InstanceId
ConfigType           tou | custom
ConfigName
ConfigJson
CreatedAt
UpdatedAt
```

### Usługi

- `IModInstanceRepository` / `ModInstanceRepository` — CRUD instancji w SQLite.
- `ModInstanceService` — tworzenie, rename, clone, delete, launch metadata.
- `ModInstanceInstaller` — instalacja full moda do konkretnej nowej ścieżki.
- `ModPackToInstanceMapper` — mapowanie `ModPack` z API do lokalnej instancji.
- `InstanceToModPackMapper` — mapowanie lokalnego zestawu do `ModPackCreateRequest`.

### Zasady

1. `mods` pozostaje katalogiem modów, nie listą lokalnych instalacji.
2. Jedna instancja = jeden folder instalacji.
3. DLL-e są przypisane do instancji, nie globalnie do `ModId`.
4. Usunięcie instancji usuwa tylko jej folder i rekordy zależne.
5. Import kodu modpacka domyślnie tworzy nową instancję.

---

## Config i migracje

### SQLite

Dodać tabele:

```sql
CREATE TABLE mod_instances (
    instance_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    base_mod_id INTEGER NOT NULL,
    base_mod_name TEXT NOT NULL,
    full_mod_version TEXT NOT NULL DEFAULT '',
    among_version TEXT NOT NULL DEFAULT '',
    platform TEXT NOT NULL DEFAULT '',
    install_path TEXT NOT NULL,
    origin TEXT NOT NULL DEFAULT 'manual',
    source_pack_code TEXT,
    pinned_version TEXT,
    auto_update_enabled INTEGER NOT NULL DEFAULT 0,
    notes TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_launched_at TEXT
);

CREATE TABLE mod_instance_dlls (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    instance_id TEXT NOT NULL,
    dll_mod_id INTEGER,
    dll_name TEXT NOT NULL,
    dll_version TEXT NOT NULL DEFAULT '',
    source TEXT NOT NULL DEFAULT 'catalog',
    sha256 TEXT,
    vt_status TEXT NOT NULL DEFAULT 'unknown',
    installed_path TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY(instance_id) REFERENCES mod_instances(instance_id) ON DELETE CASCADE
);

CREATE TABLE mod_instance_configs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    instance_id TEXT NOT NULL,
    config_type TEXT NOT NULL,
    config_name TEXT NOT NULL DEFAULT '',
    config_json TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY(instance_id) REFERENCES mod_instances(instance_id) ON DELETE CASCADE
);
```

Dodać indeksy:

- `idx_mod_instances_base_mod_id`
- `idx_mod_instances_install_path`
- `idx_mod_instance_dlls_instance`
- `idx_mod_instance_configs_instance`

### Migracja legacy

Jeśli `mods.InstallPath` istnieje dla full moda:

1. utwórz `mod_instances` z `Origin = 'legacy'`;
2. `DisplayName = ModName`;
3. `InstallPath = mods.InstallPath`;
4. odczytaj `.susmodder-install.json`, jeśli istnieje, i zaimportuj DLL-e;
5. nie kasuj od razu `mods.InstallPath`.

### `.susmodder-install.json` v2

Rozszerzyć lokalną mapę instalacji:

```json
{
  "version": "2.0",
  "instanceId": "guid",
  "displayName": "ToU - Psychopaci piątek",
  "origin": "manual",
  "sourcePackCode": null,
  "platform": "steam",
  "fullMod": { },
  "installedDlls": [],
  "metadata": {
    "notes": "",
    "customTags": []
  }
}
```

---

## Wpływ na istniejące modpack sharing (#16)

### Obecnie

`ModPackInstaller.InstallPackAsync`:

- pobiera full moda z katalogu;
- jeśli full mod już ma `InstallPath`, pomija instalację full moda;
- instaluje DLL-e do istniejącego full moda.

### Docelowo

Dodać tryby:

```text
InstallAsNewInstance       domyślny, bezpieczny
ApplyToExistingInstance    opcjonalny tryb zaawansowany
```

MVP powinno implementować tylko `InstallAsNewInstance`, ponieważ minimalizuje ryzyko nadpisania działającej instalacji.

---

## Language / i18n impact

MVP locale: PL i EN. Fallback: PL.

Nowe klucze PL/EN, przykładowo:

```text
UI.Tabs.MyPacks
UI.Tabs.Catalog
UI.Tabs.DllAddons
UI.Packs.CreateLocal
UI.Packs.Clone
UI.Packs.Rename
UI.Packs.Share
UI.Packs.InstallAsNew
UI.Packs.ApplyToExisting
UI.Packs.Contents
UI.Packs.NoPacksTitle
UI.Packs.NoPacksDescription
UI.Packs.DeleteTitle
UI.Packs.DeleteConfirmation
UI.Packs.SelectedCount
UI.Packs.Origin.Manual
UI.Packs.Origin.SharedPack
UI.Packs.Origin.Clone
UI.Packs.Origin.Legacy
```

Wymagania:

- brak hardcoded user-facing copy w XAML/ViewModel/Core;
- błędy Core jako stabilne `errorCode` + techniczny fallback;
- placeholdery zgodne w PL/EN (`{packName}`, `{modName}`, `{count}`);
- liczniki przez ICU MessageFormat;
- przyszły język dodawany przez plik locale/metadata, bez zmian logiki komponentów.

---

## Platform, packaging, updater, telemetry, privacy, AV

### Platforma

- Steam i Epic muszą wspierać osobne foldery instancji.
- Nazwy folderów muszą być sanityzowane.
- Launch zawsze używa `InstallPath` instancji.
- Epic/legendary nie może globalnie nadpisać innej instancji.

### Packaging / updater

- Brak nowych ciężkich zależności.
- Velopack bez zmian.
- Nie dodawać WebView/runtime downloads.
- Migracja SQLite wykonywana przez `DatabaseService.ApplyMigrations()` z `PRAGMA user_version`.

### Privacy

- Lokalne nazwy zestawów pozostają lokalne.
- Nazwa zestawu trafia do API tylko po jawnej akcji `Udostępnij`.
- Telemetry, jeśli dodana, nie wysyła ścieżek ani nazw lokalnych folderów.

### AV / bezpieczeństwo

- External DLL nadal wymagają consentu i statusu VT.
- Usuwanie zestawu musi pokazywać dokładną ścieżkę i usuwać tylko jedną instancję.
- Import z kodu nie może nadpisywać istniejącej instalacji bez osobnego trybu zaawansowanego.

---

## Verification plan

### Testy Core

1. Migracja legacy `mods.InstallPath` -> `mod_instances`.
2. Dwie instancje tego samego `base_mod_id` mogą istnieć równolegle.
3. Usunięcie jednej instancji nie usuwa drugiej.
4. DLL przypisuje się do poprawnego `instance_id`.
5. `InstallAsNewInstance` z kodu modpacka tworzy nowy folder.
6. `InstanceToModPackMapper` generuje poprawny `ModPackCreateRequest`.
7. `.susmodder-install.json` v1 nadal jest odczytywany jako legacy.

### Testy UI/manualne

1. Start aplikacji pokazuje `Moje zestawy`.
2. Pusty stan zachęca do utworzenia zestawu.
3. Utworzenie dwóch ToU z różnymi nazwami działa.
4. Klonowanie kopiuje wybrane DLL/config.
5. Inspector pokazuje zawartość właściwego zestawu.
6. Rename aktualizuje UI i metadata bez reinstalacji.
7. Import kodu modpacka tworzy nową instancję.
8. Udostępnienie lokalnej instancji zwraca kod/link.
9. PL/EN mają wszystkie klucze i zgodne placeholdery.
10. Smoke test Steam i Epic.

---

## Suggested implementation order

### Faza 0 — doprecyzowanie decyzji (0.5 dnia)

- Ustalić nazwę UI: `Moje zestawy` vs `Moje modpacki`.
- Ustalić domyślny folder instancji.
- Ustalić, czy MVP pozwala edytować config ToU w kreatorze, czy tylko przenosi istniejący.

### Faza 1 — SQLite + modele (2-3 dni)

- `ModInstance`, `ModInstanceDll`, `ModInstanceConfig`.
- Repozytorium i migracja.
- Import legacy z `mods.InstallPath` i `.susmodder-install.json`.

### Faza 2 — instalacja jako instancja (3-4 dni)

- `ModInstanceInstaller`.
- Nowa ścieżka instalacji per instance.
- DLL-e instalowane do konkretnej instancji.
- Delete/rename/launch instance.

### Faza 3 — UI `Moje zestawy` (3-4 dni)

- Tab/segment `Moje zestawy`.
- Karty lokalnych zestawów.
- Inspector zestawu.
- Empty state.

### Faza 4 — kreator i klonowanie (2-3 dni)

- `Create Local Pack`.
- `Clone Pack`.
- wybór DLL/config.

### Faza 5 — sharing integration (2 dni)

- `Share Local Pack` -> `ModPackCreateRequest`.
- Import kodu -> `InstallAsNewInstance`.
- Historia packów połączona z lokalną instancją.

### Faza 6 — polish/review (1-2 dni)

- PL/EN.
- Pink/dark contrast.
- AV/security review external DLL i destructive delete.
- Screenshoty README.

### Równoległość

- Core SQLite/repozytoria i UI mock `Moje zestawy` mogą iść równolegle po ustaleniu modelu DTO.
- i18n keys można przygotować równolegle z UI.
- Sharing mapper można robić równolegle po gotowym modelu `ModInstance`.

---

## Kryteria akceptacji

1. Można mieć dwa lokalne zestawy na bazie tego samego moda full.
2. Każdy zestaw ma własną nazwę i osobny folder.
3. DLL-e są instalowane do wybranego zestawu, nie globalnie do moda.
4. Config ToU może różnić się między zestawami.
5. Usunięcie jednego zestawu nie rusza drugiego.
6. Import kodu modpacka tworzy nowy lokalny zestaw.
7. Udostępnienie lokalnego zestawu tworzy kod zgodny z obecnym API.
8. Legacy instalacje są widoczne jako zestawy `Origin = legacy`.
9. PL/EN mają pełne copy, bez hardcoded user-facing strings.
10. Steam i Epic przechodzą smoke test uruchamiania konkretnej instancji.

---

## Otwarte pytania

1. Czy `Moje zestawy` ma być domyślną zakładką od razu w 3.0?
2. Czy `Vanilla` ma być osobną instancją w `Moje zestawy`, czy stałą kartą systemową?
3. Czy `ApplyToExistingInstance` w ogóle dopuszczamy w MVP, czy odkładamy jako P2?
4. Jak nazwać foldery: po `DisplayName`, po `InstanceId`, czy hybrydowo `DisplayName - shortId`?
5. Czy config ToU w `mod_instance_configs` przechowujemy jako snapshot JSON, czy tylko wskaźnik do istniejącego systemu configów?
6. Czy auto-update działa per instancja, czy globalnie dla base moda?

---

## Decyzja rekomendowana

Traktować lokalne modpacki jako fundament SUSModder 3.0. UI powinno być zbudowane wokół **Moich zestawów**, a katalog modów powinien służyć głównie do tworzenia nowych zestawów. Sharing przez kod pozostaje osobnym, społecznościowym rozszerzeniem lokalnej instancji.
