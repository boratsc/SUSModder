# Plan zmian: SUSModder client — synchronizacja configu i kompatybilności

**Data:** 2026-06-04  
**Status:** Częściowo wdrożone — migracja HTTP na **API v2** ukończona; optymalizacja sync/cache (Fazy 1–5 poniżej) **w toku**  
**Priorytet:** P1  
**Status wdrożenia API v2:** [`2026-06-07-api-v2-rollout-status.md`](2026-06-07-api-v2-rollout-status.md) ← **aktualny stan**  
**Powiązany POC:** [`DOC/POC/2026-06-04-api-config-compatibility-sync-poc.md`](../POC/2026-06-04-api-config-compatibility-sync-poc.md)  
**Backend plan:** [`2026-06-04-susmodder-backend-api-sync-plan.md`](2026-06-04-susmodder-backend-api-sync-plan.md)  
**Audyt E2E:** [`DOC/POC/API v2/2026-06-07-api-e2e-audit.md`](../POC/API%20v2/2026-06-07-api-e2e-audit.md)

---

## 0. Stan na 2026-06-07 (skrót)

| Obszar | Status |
|--------|--------|
| Centralny klient HTTP `ISUSModderApiClient` + `ApiV2BaseUrl` | ✅ |
| Katalog, pobrania, role, Discord, telemetria, releases, lobby, modpacki → v2 | ✅ |
| Backend: CDN, ikony, warianty, `2025-3-31` | ✅ (remediacja 2026-06-07) |
| E2E produkcja (`test-api-v2-client.ps1`) | ✅ 24/24 |
| `CatalogSyncService`, ETag/`sync_state`, `compatibility_cache` | ⏳ plan poniżej |
| Weryfikacja SHA256 przy pobieraniu | ⏳ |

Szczegóły: [`2026-06-07-api-v2-rollout-status.md`](2026-06-07-api-v2-rollout-status.md).

---

## 1. Cel klienta

Ujednolicić pobieranie katalogu modów i tabeli kompatybilności po stronie aplikacji desktop tak, aby:

- nie pobierać pełnego configu jako ping API co 30 sekund,
- nie robić kilku niezależnych requestów do `/api/susmodder-config` w jednym cyklu,
- korzystać z `ETag`/`304 Not Modified`, gdy backend to udostępni,
- zachować ostatnie poprawne dane w SQLite przy awarii API,
- sprawdzać kompatybilność względem faktycznych wersji FULL/DLL,
- nie łamać istniejących flow instalacji, aktualizacji, Steam/Epic i fallbacków JSON.

---

## 2. Zakres techniczny

### Główne pliki do modyfikacji

| Obszar | Pliki |
|---|---|
| Synchronizacja katalogu | `SUSModder.Core/Data/ModRepository.cs`, `SUSModder.Core/Configuration/ModConfig.cs`, `SUSModder.Core/Services/ConfigService.cs` |
| Nowy serwis sync | `SUSModder.Core/Services/CatalogSyncService.cs` *(nowy)* |
| Stan synchronizacji | `SUSModder.Core/Data/DatabaseService.cs`, nowe repozytoria/tabele SQLite |
| Kompatybilność | `SUSModder.Core/Services/CompatibilityService.cs`, `CompatibilityMerger.cs`, modele `Compatibility*` |
| UI/status bar | `SUSModder/ViewModels/MainWindowViewModel.StatusBar.cs` |
| DLL manager/inspektor | `DllModSelectionViewModel.cs`, `MainWindowViewModel.CatalogInspector.cs`, `MainWindowViewModel.InspectorCompat.cs` |
| Testy | `SUSModder.Core.Tests/Services/*`, ewentualnie nowe testy SQLite/cache |

---

## 3. Faza 1 — ograniczenie zbędnych requestów bez zależności od backendu

**Cel:** poprawa natychmiastowa, możliwa przed wdrożeniem zmian API.

### Zadania

- [ ] W `MainWindowViewModel.StatusBar.cs` przestać używać `UpdateServerUrl` jako pełnego pingu co 30 s.
  - Opcja A: użyć lekkiego istniejącego endpointu, jeśli jest dostępny.
  - Opcja B: pingować `BaseUrl`/health endpoint dopiero po wdrożeniu backendu, a do tego czasu zwiększyć interwał albo rozdzielić ping od sync configu.
- [ ] Po udanym sprawdzeniu API nie wykonywać natychmiast drugiego pełnego GET, jeśli pierwszy request już był do configu.
- [ ] Dodać single-flight dla pobierania remote configu w Core: jeden aktywny fetch na proces.
- [ ] Dodać krótki memory cache remote configu na potrzeby jednego cyklu update checkerów (np. 60-120 s).
- [ ] Zmniejszyć bezpośrednie użycia `ConfigRepository.LoadConfigFromApiAsync()` w nowych ścieżkach; preferować `ConfigService`/nowy `CatalogSyncService`.

### Kryteria akceptacji

- Status bar nie pobiera pełnego JSON configu co 30 sekund.
- `ModUpdateManager` i `DllUpdateManager` nie robią dwóch osobnych fetchy tego samego configu w jednym cyklu.
- Przy awarii API lokalny katalog nie jest czyszczony ani nadpisywany pustą listą.

---

## 4. Faza 2 — `CatalogSyncService` i conditional GET

**Cel:** jedna warstwa synchronizacji z obsługą backendowych `ETag`/`304`.

### Nowy komponent

`SUSModder.Core/Services/CatalogSyncService.cs`

Odpowiedzialność:

- `RefreshCatalogIfDueAsync(force: false)` — conditional GET configu,
- `GetLatestRemoteConfigAsync()` — zwraca ostatni pobrany lub lokalny snapshot,
- obsługa `ETag`, `If-None-Match`, `304 Not Modified`, timeoutów,
- walidacja odpowiedzi przed zapisem,
- merge lokalnych pól instalacji (`InstallPath`, `LastUpdated`, Vanilla),
- status wyniku: `Updated`, `NotModified`, `OfflineUsingCache`, `InvalidResponse`, `Failed`.

### Nowe modele pomocnicze

- `CatalogSyncResult`
- `CatalogSyncStatus`
- `CatalogSnapshotMetadata`

### SQLite: `sync_state`

Dodać migrację w `DatabaseService.ApplyMigrations()`:

```sql
CREATE TABLE IF NOT EXISTS sync_state (
  key TEXT PRIMARY KEY,
  etag TEXT NULL,
  last_modified TEXT NULL,
  last_success_utc TEXT NULL,
  last_attempt_utc TEXT NULL,
  last_error_code TEXT NULL,
  failure_count INTEGER NOT NULL DEFAULT 0,
  next_allowed_attempt_utc TEXT NULL
);
```

Klucz dla katalogu: `catalog.config`.

### Implementacja HTTP

- [ ] Przy znanym `etag` wysyłać `If-None-Match`.
- [ ] Dla `304` nie deserializować body i nie przepisywać tabeli `mods`.
- [ ] Dla `200` zapisać nowy `ETag`/`Last-Modified` i dopiero po walidacji zapisać `mods`.
- [ ] Dla błędów 5xx/timeout użyć backoffu i ostatniego lokalnego snapshotu.
- [ ] Nie dodawać nowych zależności, chyba że osobno zaakceptowane. Na start wystarczy ręczny backoff + istniejący `HttpClient`/DI.

### Walidacja odpowiedzi configu

- lista nie jest pusta,
- `Id` unikalne i dodatnie dla zdalnych modów,
- `ModName` niepuste,
- `ModType` w `full`, `dll`, `Vanilla`,
- dla zdalnych `full`/`dll` istnieje link pobierania i `ModVersion`,
- podejrzany spadek liczby modów nie nadpisuje lokalnej bazy bez wyraźnego sukcesu/rewizji.

---

## 5. Faza 3 — trwały cache kompatybilności w SQLite

**Cel:** przy offline/API failure aplikacja nadal zna ostatnie statusy kompatybilności, szczególnie `NW`.

### SQLite: `compatibility_cache`

Dodać migrację:

```sql
CREATE TABLE IF NOT EXISTS compatibility_cache (
  full_mod_id INTEGER NOT NULL,
  full_mod_version TEXT NOT NULL,
  dll_mod_id INTEGER NOT NULL,
  dll_mod_version TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('F','W','NT','NW')),
  is_exact_version INTEGER NOT NULL DEFAULT 1,
  warning TEXT NULL,
  source_updated_at TEXT NULL,
  fetched_at_utc TEXT NOT NULL,
  PRIMARY KEY (full_mod_id, full_mod_version, dll_mod_id, dll_mod_version)
);
```

### Repozytorium

Nowe pliki:

- `SUSModder.Core/Data/ICompatibilityCacheRepository.cs`
- `SUSModder.Core/Data/CompatibilityCacheRepository.cs`

Metody minimalne:

- `GetForFullMod(fullModId, fullVersion)`
- `GetForDllMod(dllModId, dllVersion)`
- `GetPair(fullModId, fullVersion, dllModId, dllVersion)`
- `SaveSnapshot(entries, metadata)`
- `ClearStaleForRevision(...)` *(opcjonalnie)*

### Zmiany w `CompatibilityService`

- [ ] Cache key musi zawierać wersje: `fullId/fullVersion/dllId/dllVersion`.
- [ ] Dodać przeciążenia metod z wersjami:
  - `CheckCompatibilityAsync(dllModId, dllVersion, fullModId, fullVersion, ...)`
  - `GetCompatibilityMatrixForFullModAsync(fullModId, fullVersion, ...)`
  - `GetCompatibilityMatrixAsync(dllModId, dllVersion, ...)`
- [ ] Najpierw czytać SQLite cache, potem memory cache/API.
- [ ] Przy API failure zwrócić cache jako `stale`, jeśli istnieje.
- [ ] Nie traktować braku API jako „działa”. Brak danych = unknown/NT zgodnie z UI.

---

## 6. Faza 4 — dokładne wersje FULL/DLL w UI

**Cel:** nie pytać backendu o kompatybilność „najnowszych” wersji, gdy użytkownik ma starszą instancję.

### Źródła wersji

Kolejność dla FULL:

1. `InstallationMap.FullMod.ModVersion` z folderu instancji,
2. `ModInstance.FullModVersion`, jeśli dotyczy instancji,
3. `ModConfiguration.ModVersion` z SQLite.

Kolejność dla DLL:

1. wersja DLL z installation map / mapy zainstalowanych DLL, jeśli dostępna,
2. `ModConfiguration.ModVersion` z katalogu.

### Pliki UI/ViewModel

- [ ] `DllModSelectionViewModel` — load compatibility z wersją docelowego FULL.
- [ ] `MainWindowViewModel.CatalogInspector.cs` — macierz dla wybranego FULL z wersją.
- [ ] `MainWindowViewModel.InspectorCompat.cs` — wyświetlanie tylko dokładnych albo bezpiecznie oznaczonych danych.

### Semantyka

- dokładne `F/W` — kompatybilne,
- dokładne `NW` — ukryj/blokuj/ostrzegaj jak obecnie,
- brak dokładnego wpisu — unknown/nieprzetestowane,
- historyczne `F/W` nie może być pokazane jako bieżące.

---

## 7. Faza 5 — integracja z backendowym snapshotem compatibility

**Zależność:** backend plan, endpoint `/api/compatibility/snapshot`.

### Zadania

- [ ] `CatalogSyncService.RefreshCompatibilityIfDueAsync()` pobiera snapshot z `If-None-Match`.
- [ ] `304` zostawia SQLite cache bez zmian.
- [ ] `200` zapisuje snapshot w transakcji.
- [ ] Widoki DLL/inspektora używają lokalnej tabeli, bez requestów per otwarcie widoku, jeśli snapshot jest świeży.
- [ ] Per-mod endpoint `/api/compatibility` zostaje fallbackiem dla cache miss albo przed wdrożeniem snapshotu.

---

## 8. i18n / UI copy

Jeśli implementacja pokaże użytkownikowi nowe stany, dodać klucze PL/EN, np.:

- `Sync.Status.OfflineUsingCache`
- `Sync.Status.LastUpdated`
- `Sync.Error.ConfigRefreshFailed`
- `Sync.Error.CompatibilityRefreshFailed`
- `DllModSelection.CompatibilityDataStale`

Nie dodawać hardcoded user-facing copy w ViewModelach. Statusy `F/W/NT/NW` są kodami danych, nie tekstami UI.

---

## 9. Testy i weryfikacja

### Testy jednostkowe

- [ ] `CatalogSyncService`:
  - `200 OK` zapisuje zwalidowany config,
  - `304` nie zapisuje ponownie tabeli,
  - timeout/5xx używa cache i ustawia backoff,
  - pusty/niepoprawny JSON nie nadpisuje lokalnych modów.
- [ ] `CompatibilityService` / repo cache:
  - cache key uwzględnia wersje,
  - `NW` z cache wraca przy offline,
  - historyczne `F/W` nie jest wybierane jako aktualne.
- [ ] Migracje SQLite tworzą nowe tabele idempotentnie.

### Manual QA

- [ ] Start online: config aktualizuje się raz, widoki działają.
- [ ] Start offline: widoczny ostatni katalog i compatibility cache.
- [ ] Przełączenie modów w inspektorze nie generuje lawiny requestów.
- [ ] Instalacja DLL przy znanym `NW` pokazuje ostrzeżenie/blokadę.
- [ ] Steam/Epic instalacje pełnych modów bez regresji.

### Komendy

Po implementacji kodu:

```powershell
dotnet build SUSModder.sln
dotnet test SUSModder.Core.Tests\SUSModder.Core.Tests.csproj
```

---

## 10. Kolejność PR-ów

1. **Client PR 1:** status bar + single-flight remote config + brak duplikatów requestów.
2. **Client PR 2:** `CatalogSyncService`, `sync_state`, ETag/304 dla configu.
3. **Client PR 3:** `compatibility_cache`, wersjonowane compatibility query/cache.
4. **Client PR 4:** integracja ze snapshotem compatibility po wdrożeniu backendu.

Każdy PR powinien być mały i możliwy do zbudowania/testowania niezależnie.
