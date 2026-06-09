# Status wdrożenia API v2 — SUSModder 3.x

**Data aktualizacji:** 2026-06-07  
**Produkcja API:** `https://api.susmodder-cdn.ovh/v2`  
**Powiązane dokumenty:**
- [`DOC/PLAN/2026-06-04-susmodder-client-api-sync-plan.md`](2026-06-04-susmodder-client-api-sync-plan.md) — optymalizacja sync/cache (częściowo nakłada się na v2)
- [`DOC/POC/API v2/2026-06-07-api-e2e-audit.md`](../POC/API%20v2/2026-06-07-api-e2e-audit.md) — audyt E2E + remediacja backendu
- [`DOC/POC/API v2/consumer-susmodder-3x.md`](../POC/API%20v2/consumer-susmodder-3x.md) — kontrakt konsumencki
- [`DOC/POC/API v2/endpoint-checklist.md`](../POC/API%20v2/endpoint-checklist.md) — checklist endpointów backendu

**Testy regresji:** `SKRYPTY/Test/test-api-v2-client.ps1` (ostatni run: **24/24 OK**, lobby 401 bez tokena = oczekiwane)

---

## Podsumowanie

| Warstwa | Status | Uwagi |
|---------|--------|-------|
| **Backend API v2 (public)** | ✅ Gotowe | Remediacja 2026-06-07, 20/20 pobierań modów |
| **Klient — migracja HTTP na v2** | ✅ Gotowe | Jedna brama: `ISUSModderApiClient` |
| **Klient — instalacja / ikony / lobby** | ✅ Działa | Po fixie CDN + `Secrets.cs` + `CdnAssetUrlResolver` |
| **Klient — CatalogSyncService / ETag SQLite** | ⏳ Nie wdrożone | Z oryginalnego planu sync (Faza 2–5) |
| **Klient — `compatibility_cache` SQLite** | ⏳ Nie wdrożone | Snapshot API działa, brak lokalnej tabeli |
| **Klient — weryfikacja SHA256 przy pobieraniu** | ⏳ Nie wdrożone | Nagłówki `X-SUSModder-SHA256` dostępne z API |
| **Legacy v1 na susmodder.app** | ⚠️ Częściowo | `/api/susmodder-config` OK; reszta → v2 |
| **SUStats `/api/among-tokens`** | ❌ 404 | Migracja na Clair OAuth (osobny track) |

---

## Backend — co naprawiono (2026-06-07)

| Problem | Fix | Weryfikacja |
|---------|-----|-------------|
| CDN 404 na pobraniach | Usunięcie prefiksu `/mods/` z `download_url` (135 wierszy) | 20/20 modów → HTTP 200 |
| V1 `/api/mod-download/` 404 | Reguła nginx `location /api/mod-download/` | 302 → CDN 200 |
| `iconUrl: null` | `normalizeCatalogRow` + `GET /v2/icons/:filename` | 20/20 modów ma `iconUrl` |
| `versions/2025-3-31` 404 | INSERT do `among_us_versions` | GET 200 |
| 8 modów bez wariantów | `cdn-bulk-upload.js` + wpisy w `mod_variants` | Wszystkie mody mają warianty |
| Architektura Syzyfowy (x32→x86) | UPDATE `mod_variants` | Pobrania steam OK |

Commity backendu: `cedd5fb` … `567aae4` (branch `api-v2`).

---

## Klient — zaimplementowane

### Warstwa API (`SUSModder.Core/Api/`)

| Komponent | Status |
|-----------|--------|
| `ISUSModderApiClient` / `SUSModderApiClient` | ✅ |
| `SUSModderApiClientProvider` (singleton dla kodu statycznego) | ✅ |
| `CatalogMapper`, modele v2, `CdnAssetUrlResolver` | ✅ |
| `GetCatalogModDetailAsync` + `CatalogModVariantDto` | ✅ |
| `JsonNumberHandling.AllowReadingFromString` (`fileSizeBytes`) | ✅ |
| `StaticAssetsBaseUrl` + resolve `/api/v2/icons/...` | ✅ |

### Konfiguracja

| Element | Status |
|---------|--------|
| `appsettings.json` → `Configuration:ApiV2BaseUrl` | ✅ |
| Rejestracja DI w `App.axaml.cs` | ✅ |
| `SecretProvider.GetDownloadToken()` (`Secrets.cs`, gitignored) | ✅ |

### Zmigrowane wywołania (v2 zamiast rozproszonych URL-i)

| Obszar | Endpoint(y) v2 | Pliki |
|--------|----------------|-------|
| Katalog modów | `/catalog`, upsert SQLite | `ModRepository.cs`, `CatalogMapper.cs` |
| Pobieranie modów | `/downloads/mod/:id/:version` | `ModDownloadUrlBuilder.cs`, `ModManager.cs` |
| Kompatybilność | `/compatibility` | `CompatibilityService.cs` |
| Wersje Among Us | `/versions`, `/versions/:dbValue` | `AmongUsManifestService.cs`, `ModVersionService.cs` |
| Role | `/roles` | `RolesService.cs` |
| Discord | `/discord/favs/public`, `/discord/server-counts` | `DiscordFavoritesService.cs` |
| Telemetria | `/telemetry/heartbeat` | `TelemetryService.cs` |
| Aktualizacje app | `/releases` | `VelopackUpdateService`, `VelopackApiSource` |
| Lobby board | `/lobby` (+ `Authorization`) | `LobbyBoardService.cs` |
| Modpacki | `/modpacks/*` | `ModPackService.cs`, `ModPackInstaller.cs` |
| Online (status bar) | `/online` | `MainWindowViewModel.StatusBar.cs` |

### Poprawki po audycie E2E (klient)

| Problem | Fix |
|---------|-----|
| Bezpośredni CDN URL z `variant.downloadUrl` → 404 | `ModDownloadUrlBuilder.ResolveAsync()` zawsze przez `/v2/downloads/mod/...` |
| Ikony na `susmodder-cdn.ovh/icons/` | `CdnAssetUrlResolver` → `susmodder.app` lub `/api/v2/icons/` |
| Lobby JSON `data: [...]` | `LobbyBoardService.DeserializeLobbyPayload` — obsługa tablicy |
| Mody znikały z UI przy sync | Upsert zamiast replace; snapshot przed filtrem przeglądarki |
| Role `HasRoles = false` | `Role.IsAssociatedWithMod()` po polu `mods[]` z API v2 |

### Testy

| Zakres | Wynik |
|--------|-------|
| `SUSModder.Core.Tests` | ✅ 97/97 |
| E2E `test-api-v2-client.ps1` | ✅ 24/24 (+ 1 EXPECTED lobby) |

---

## Klient — do zrobienia (z planu sync + v2)

Pozycje z [`2026-06-04-susmodder-client-api-sync-plan.md`](2026-06-04-susmodder-client-api-sync-plan.md), **niezależne** od samej migracji URL na v2:

### Faza 1 — ograniczenie requestów
- [ ] Status bar: ping przez `/catalog-meta` zamiast pełnego configu
- [ ] Single-flight + memory cache remote configu (częściowo: `_refreshLock` w `ModRepository`)
- [ ] Brak duplikatów GET configu w jednym cyklu update checkerów

### Faza 2 — `CatalogSyncService` + `sync_state`
- [ ] Nowy serwis `CatalogSyncService.cs`
- [ ] Tabela SQLite `sync_state` (ETag, backoff)
- [ ] Conditional GET z `If-None-Match` → `304`

### Faza 3–5 — kompatybilność offline
- [ ] Tabela SQLite `compatibility_cache`
- [ ] `CompatibilityService` z wersjonowanym cache key
- [ ] Pobieranie `/compatibility/snapshot` do SQLite
- [ ] UI: wersje FULL/DLL z `InstallationMap` w zapytaniach compat

### v2 — jakość pobierania
- [ ] Weryfikacja SHA256 po pobraniu (nagłówki redirectu lub `variant.sha256`)
- [ ] Obsługa `legacyFallbackAvailable` — klient 3.x **nie** instaluje przez legacy fallback

---

## Mapowanie endpointów — stan produkcji

### ✅ Działa (używane przez klienta)

`GET /catalog`, `/catalog/:id`, `/catalog/:id/versions`, `/catalog-meta`, `/versions`, `/versions/:dbValue`, `/versions/:dbValue/steam|epic`, `/downloads/mod/:id/:version`, `/compatibility`, `/compatibility/snapshot`, `/roles`, `/releases`, `/telemetry/*`, `/discord/*`, `/online`, `/virustotal/report`, `/icons/:filename`, `POST /telemetry/heartbeat`

### ⚠️ Wymaga auth

`GET /lobby` — `Authorization: Bearer <HTTP_TOKEN>` (token w `Secrets.cs`)

### ✅ Modpacki (pełny flow od 2026-06-07)

`POST /modpacks`, `GET /modpacks/:code`, `DELETE /modpacks/:code`, `/modpacks/:code/dlls`, `/modpacks/:code/dlls/:sha256/status` — zweryfikowane na produkcji (fix backendu: [`2026-06-07-modpacks-post-backend-bug.md`](../POC/API%20v2/2026-06-07-modpacks-post-backend-bug.md))

### ❌ Poza v2 / legacy

| Endpoint | Status | Działanie klienta |
|----------|--------|-------------------|
| `/api/among-tokens` | 404 | SUStats → Clair OAuth (plan Discord) |
| `/api/susmodder-config` | 200 | Fallback ikon w `ModRepository` (do usunięcia gdy `iconUrl` stabilne) |
| `/api/lobby-board`, `/api/mod-download`, … | 404 | Zastąpione przez v2 |

---

## Następne kroki (rekomendacja)

1. **Smoke test w aplikacji** — instalacja Town of Us + Syzyfowy ToU, lobby board z tokenem.
2. **Client PR:** `CatalogSyncService` + `sync_state` + `/catalog-meta` w status barze.
3. **Client PR:** `compatibility_cache` + snapshot do SQLite.
4. **Client PR:** SHA256 verify przy `ModManager.DownloadFileWithMemoryManagementAsync`.
5. **Cleanup:** usunąć merge ikon z legacy `/api/susmodder-config` gdy `iconUrl` w v2 jest stabilne przez 2+ tygodnie.

---

## Uruchamianie testów

```powershell
# Pełna weryfikacja v2 (dopasowana do klienta)
.\SKRYPTY\Test\test-api-v2-client.ps1

# Z tokenem (lobby)
.\SKRYPTY\Test\test-api-v2-client.ps1 -AuthToken "Bearer <HTTP_TOKEN>"

# Build + unit testy
dotnet build SUSModder.sln
dotnet test SUSModder.Core.Tests\SUSModder.Core.Tests.csproj
```
