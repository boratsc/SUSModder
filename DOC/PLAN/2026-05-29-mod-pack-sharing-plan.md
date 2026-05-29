# Plan Wdrożenia: Mod Pack Sharing

**Data:** 2026-05-29  
**Status:** ⏳ Gotowy do implementacji  
**Priorytet:** P1  
**Effort:** ~8-12 dni (backend + core + UI + testy)  
**Specyfikacja:** [`DOC/2026-05-25 - frontend-ideas/16-mod-pack-sharing.md`](../2026-05-25%20-%20frontend-ideas/16-mod-pack-sharing.md)

---

## Cel

System dzielenia się **zestawami modów** (full mod + wersja + DLL + opcjonalnie config ToU + Discord + integration.dll) przez krótki kod i link `https://susmodder.app/pack/{code}`.

**Analogia:** Config ToU (hash → zapisany config), ale dla całego stacku modów.

---

## Non-goals (MVP)

- ❌ Marketplace / publiczny katalog paczek
- ❌ Komentarze, oceny, rankingi
- ❌ Wersjonowanie paczek po utworzeniu (snapshot only)
- ❌ Integracja CurseForge / Modrinth
- ❌ Edycja paczki po utworzeniu (post-MVP P2)

---

## Decyzje (rozstrzygnięte)

| # | Temat | Decyzja |
|---|-------|---------|
| D1 | TTL paczki | Użytkownik wybiera: **7 / 30 / 90 dni** (default 30) |
| D2 | Limit paczek | **10 aktywnych** na `creatorHash` (SHA256 HWID) |
| D3 | Anonimowość | `creatorName` **opcjonalny** (default anonim) |
| D4 | Wersja full mod | `"latest"` lub konkretna wersja (np. `"5.4.0"`) |
| D5 | Config ToU | **Pełny JSON** na serwerze (`mod_pack_tou_configs`), nie sam hash |
| D6 | Zewnętrzne DLL | Max **3** pliki, max **10 MB**, VirusTotal backend-side |
| D7 | Deep link | `susmodder://pack/{code}` + fallback web |
| D8 | integration.dll | Tylko jeśli istnieje w katalogu / plan V2 bridge |
| D9 | Auth tworzenia | Bearer `SecretProvider.GetDownloadToken()` + `creatorHash` |
| D10 | Auth odczytu | Publiczny `GET` (bez tokenu) |

---

## Architektura

```
Twórca (SUSModder)
  → ModPackCreatorDialog
  → ModPackService.CreatePackAsync()
  → POST /api/mod-packs (susmodder-backend)
  → zwraca packId + shareUrl

Odbiorca
  → klik susmodder://pack/ABC-XYZ-123  LUB  wklejenie kodu
  → ModPackService.GetPackAsync()
  → ModPackPreviewDialog (+ ostrzeżenia external DLL)
  → ModPackService.InstallPackAsync()
      → ModManager (full mod)
      → DllModificationService (DLL katalogowe)
      → download external DLL (jeśli są)
      → ToUConfigService (config z serwera)
```

---

## Część 1: Backend API (susmodder-backend) — ~3-4 dni

### 1.1 Schemat PostgreSQL

Pełny SQL w specyfikacji 16. Tabele:

| Tabela | Opis |
|--------|------|
| `mod_packs` | Metadane paczki, TTL, creator_hash |
| `mod_pack_dlls` | DLL katalogowe + zewnętrzne |
| `mod_pack_tou_configs` | Pełny config ToU (JSONB) |

Indeksy: `expires_at`, `creator_hash`, `pack_id`.

### 1.2 Endpointy

| Metoda | Path | Auth | Status docelowy |
|--------|------|------|-----------------|
| POST | `/api/mod-packs` | Bearer | 201 + pack |
| GET | `/api/mod-packs/:packId` | — | 200 / 404 / 410 |
| POST | `/api/mod-packs/:packId/external-dll` | Bearer | multipart upload |
| GET | `/api/mod-packs/:packId/dlls/:hash` | — | plik DLL |
| DELETE | `/api/mod-packs/:packId` | Bearer + creatorHash | opcjonalnie MVP |

**Kody HTTP:**
- `410 Gone` — paczka wygasła
- `429` — limit 10 paczek / creatorHash
- `413` — DLL > 10 MB

### 1.3 VirusTotal (backend)

- Klucz już na backendzie (skanowanie wersji SUSModder)
- Flow: upload → SHA256 → VT cache → `pending` → `clean` / `suspicious`
- Background job: poll pending co 5 min
- `suspicious` → usunięcie z CDN + flaga w API response

### 1.4 Cron cleanup

```sql
DELETE FROM mod_packs WHERE expires_at < NOW();
-- CASCADE usuwa dlls + tou_configs
```

Co 24h + cleanup plików CDN powiązanych z wygasłymi packId.

### 1.5 Strona web fallback

`https://susmodder.app/pack/{packId}` — statyczna strona:
- Nazwa paczki, lista modów (bez external download URLs)
- Przycisk „Pobierz SUSModder”
- Meta tag / JS redirect do `susmodder://pack/{packId}` jeśli protocol handler

### 1.6 Kolejność backend

| # | Zadanie | Effort |
|---|---------|--------|
| 1.1 | Migracja PostgreSQL | ~2h |
| 1.2 | POST + GET mod-packs | ~6h |
| 1.3 | Limit 10/creatorHash | ~1h |
| 1.4 | external-dll upload + CDN | ~4h |
| 1.5 | VirusTotal integration | ~3h |
| 1.6 | Cron cleanup | ~1h |
| 1.7 | Strona web /pack/:id | ~3h |
| 1.8 | Testy API + Swagger | ~2h |

---

## Część 2: SUSModder.Core — ~3-4 dni

### 2.1 Modele

**Plik:** `SUSModder.Core/Models/ModPack.cs`

```csharp
public sealed class ModPack { /* packId, name, fullModId, dllMods, touConfig, ... */ }
public sealed class ModPackDllEntry { /* dllModId, isExternal, sha256, virusTotalStatus */ }
public sealed class ModPackInstallResult { /* Success, Installed, Skipped, Failed */ }
```

### 2.2 Serwisy

| Serwis | Metody |
|--------|--------|
| `ModPackService` | `CreatePackAsync`, `GetPackAsync`, `ValidatePackAsync`, `InstallPackAsync` |
| `DeepLinkService` | `ParseDeepLink`, `RegisterProtocolHandlerAsync`, `HandleDeepLinkAsync` |
| `VirusTotalDisplayService` | Mapowanie statusu VT na UI (backend robi scan) |

### 2.3 InstallPackAsync — integracja

| Krok | Delegacja |
|------|-----------|
| 1. Full mod | `ModService.InstallModAsync(fullModId, version)` |
| 2. DLL katalogowe | `DllModificationService` per mod |
| 3. External DLL | Download z `/api/mod-packs/.../dlls/` → folder moda |
| 4. Config ToU | `ToUConfigService.ApplyConfigAsync(configData)` |
| 5. integration.dll | Kopiuj jeśli `includeIntegrationDll` + plik istnieje |
| 6. Discord | Otwórz invite / zapisz fav (opcjonalnie) |

**Walidacja przed instalacją:**
- Full mod istnieje w katalogu API
- External DLL → wymagany checkbox „Rozumiem ryzyko”
- VT `suspicious` → blokada instalacji external DLL

### 2.4 Deep link protocol

Rejestracja w `HKEY_CURRENT_USER\Software\Classes\susmodder` (bez UAC):

```
susmodder://pack/ABC-XYZ-123
susmodder://pack/ABC-XYZ-123?install=1  (wymaga potwierdzenia UI)
```

**Plik:** `Program.cs` — parsowanie args `%1` przy starcie.

### 2.5 SQLite lokalne

Migracja PRAGMA user_version:

```sql
CREATE TABLE IF NOT EXISTS mod_pack_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pack_id TEXT NOT NULL,
    name TEXT NOT NULL,
    creator_name TEXT,
    installed_at TEXT NOT NULL,
    mods_installed TEXT NOT NULL  -- JSON
);
```

**User settings:** `modPacksEnabled` (bool, default true), `modPacksAutoInstall` (bool, default false).

### 2.6 appsettings.json

```json
"ModPacksEndpoint": "/api/mod-packs"
```

### 2.7 Kolejność Core

| # | Zadanie | Effort | Zależności |
|---|---------|--------|------------|
| 2.1 | Modele ModPack | ~1h | — |
| 2.2 | ModPackService create/get | ~4h | Backend 1.2 |
| 2.3 | ModPackService install | ~6h | ModManager, DllMod |
| 2.4 | DeepLinkService | ~3h | — |
| 2.5 | SQLite history + settings | ~1h | — |
| 2.6 | Testy jednostkowe | ~4h | 2.2–2.4 |

---

## Część 3: UI (Avalonia) — ~3-4 dni

### 3.1 Nowe widoki

| Widok | Opis |
|-------|------|
| `ModPackCreatorDialog` | Kreator: mod, wersja, DLL, ToU config, TTL |
| `ModPackResultDialog` | Kod + link + kopiuj / share Discord |
| `ModPackCodeEntryDialog` | Wklejenie kodu |
| `ModPackPreviewDialog` | Podgląd + ostrzeżenia external DLL + checkbox ryzyka |
| `ModPackInstallProgressDialog` | Progress instalacji multi-step |

### 3.2 Integracja UI

| Miejsce | Akcja |
|---------|-------|
| FAB menu | „Udostępnij zestaw modów” |
| Menu kontekstowe moda | „Udostępnij jako zestaw” (pre-fill full mod) |
| MainWindow | Pole „Wklej kod zestawu” (obok search lub w FAB) |
| `App.axaml.cs` | Obsługa `susmodder://` URI przy starcie |

### 3.3 Bezpieczeństwo UI (wymagane)

Przed instalacją paczki z `isExternal: true`:
1. Czerwone ostrzeżenie (nie do pominięcia)
2. Status VirusTotal (clean / suspicious / unknown)
3. Przycisk „Zainstaluj” **ukryty** do czasu checkbox „Rozumiem ryzyko”
4. VT `suspicious` → instalacja external DLL **zablokowana**

### 3.4 i18n

~25 kluczy sekcji `ModPacks.*` — pełna lista w specyfikacji 16 (PL + EN).

### 3.5 Kolejność UI

| # | Zadanie | Effort |
|---|---------|--------|
| 3.1 | ModPackCreatorDialog | ~6h |
| 3.2 | ModPackPreviewDialog + warnings | ~4h |
| 3.3 | ModPackResultDialog + CodeEntry | ~4h |
| 3.4 | Install progress + error handling | ~4h |
| 3.5 | FAB + context menu + deep link | ~3h |
| 3.6 | i18n PL/EN | ~2h |

---

## Część 4: Testy i wdrożenie — ~2 dni

### 4.1 Testy jednostkowe (Core)

| Test | Opis |
|------|------|
| CreatePack_Valid | Poprawne dane → packId |
| CreatePack_InvalidModId | 400 |
| GetPack_Expired | null / 410 |
| InstallPack_FullSuccess | Wszystkie mody |
| InstallPack_SkipInstalled | Pomija zainstalowane |
| InstallPack_ExternalDll_Warns | Wymaga checkbox |
| DeepLink_Parse_Valid | `susmodder://pack/ABC` |

### 4.2 Testy E2E (manualne)

| Scenariusz | Kroki |
|------------|-------|
| Happy path | Utwórz → skopiuj kod → druga instancja → zainstaluj |
| Deep link | Klik link → preview → instalacja |
| External DLL | Upload → VT pending → clean → instalacja z checkbox |
| Wygasła paczka | Komunikat „nie istnieje lub wygasła” |
| Limit 10 paczek | 11. paczka → błąd + sugestia usunięcia |

### 4.3 Telemetria

| Event | Pola (anonimowe) |
|-------|------------------|
| `pack_created` | full_mod_id, dll_count, has_external, ttl |
| `pack_installed` | full_mod_id, dll_count, partial |
| `pack_install_failed` | error_code |

**NIE wysyłać:** nazw external DLL, download URLs.

---

## Kolejność implementacji (master)

```
Tydzień 1
├── Backend 1.1–1.6 (równolegle z Core 2.1–2.2)
└── Core 2.3–2.5 po gotowym POST/GET

Tydzień 2
├── UI 3.1–3.6
├── Backend 1.7 (strona web)
└── Testy 4.1–4.2 + release
```

**Możliwy MVP bez external DLL:** skrócenie o ~2 dni (pominąć 1.4, 1.5, external UI) — decyzja produktowa.

---

## Zależności między feature'ami

| Feature | Zależy od |
|---------|-----------|
| integration.dll w paczce | `D:\Development\susmodder-integration` — E2E ✅, brak dystrybucji katalogowej; plan V2.0.3 |
| Discord server w paczce | [`2026-05-29-voice-chat-integration-plan.md`](2026-05-29-voice-chat-integration-plan.md) Faza 1 |
| ToU config | Istniejący `ToUConfigService` + SQLite `tou_configs` |

---

## Ryzyka

| Ryzyko | Mitygacja |
|--------|-----------|
| Malware w external DLL | VT + checkbox + limit rozmiaru |
| AV false positive | Transparentność, SHA256 display |
| „latest” vs pinned version | Preview pokazuje co zostanie zainstalowane |
| Deep link na Windows bez admin | HKCU registry only |
| CDN koszty | TTL 30 dni default, cleanup cron |

---

## Definition of Done

- [ ] Twórca tworzy paczkę → dostaje kod + link
- [ ] Odbiorca instaluje zestaw jednym flow (full + DLL + ToU)
- [ ] External DLL wymaga świadomej zgody + VT status
- [ ] Deep link `susmodder://pack/` działa po instalacji app
- [ ] Web fallback `/pack/` działa bez app
- [ ] Paczki wygasają i są usuwane z backendu
- [ ] PL + EN i18n kompletne
- [ ] `dotnet build` + testy manualne E2E OK
