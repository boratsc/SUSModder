# Backend contract: GitHub custom DLL/FULL w modpackach API v2

Data: 2026-06-14  
Status: specyfikacja do wdrożenia w `susmodder-backend`  
Zakres: `susmodder-api` / API v2, modpacks, GitHub release assets, VirusTotal, CDN, PostgreSQL, klient SUSModder 3.x

## Cel

Dodać po stronie backendu obsługę custom modów z GitHuba w modpackach tak, żeby klient SUSModder:

1. **Nie pobierał bezpośrednio z GitHuba**.
2. Deklarował wyłącznie publiczny link GitHub release asset.
3. Backend pobierał artefakt, walidował strukturę, liczył SHA256, skanował VirusTotal i publikował download tylko dla `clean`.
4. `GET /api/v2/modpacks/:code` zwracał `status`, `installable` i `customArtifacts[]` w formacie zgodnym z klientem.
5. Stare modpacki bez custom content działały bez zmian.

## Kontrakt klient ↔ backend

### 1. Deklaracja GitHub custom artifact

`POST /api/v2/modpacks/:code/custom-github-mods`

Auth: jak `POST /api/v2/modpacks` / upload DLL (`Authorization` + `X-User-Hash`/`creatorHash` wg obecnego wzorca).  
Rate limit: grupa modpack create/upload.

Request:

```json
{
  "sourceKind": "github_dll",
  "modType": "dll",
  "displayName": "My Custom DLL",
  "version": "v1.2.3",
  "githubUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
  "dllInstallPath": "BepInEx/plugins"
}
```

Docelowo dla FULL:

```json
{
  "sourceKind": "github_full",
  "modType": "full",
  "displayName": "My Custom Full Mod",
  "version": "v1.2.3",
  "githubUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyFullOverlay.zip"
}
```

Walidacja requestu:

| Pole | Reguła |
|------|--------|
| `sourceKind` | `github_dll` albo `github_full`; musi zgadzać się z `modType` |
| `modType` | `dll` albo `full` |
| `displayName` | 1–100 znaków, trim, bez control chars |
| `version` | opcjonalne, max 50 znaków; rekomendowane tag/release version |
| `githubUrl` | wymagane, `https://github.com/...`, bez shortlinków, bez prywatnych hostów |
| `dllInstallPath` | tylko dla DLL; default `BepInEx/plugins`; relatywne i whitelistowane |

Response `202 Accepted`:

```json
{
  "data": {
    "customArtifact": {
      "artifactId": "uuid-or-id",
      "sourceKind": "github_dll",
      "modType": "dll",
      "displayName": "My Custom DLL",
      "version": "v1.2.3",
      "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
      "fileName": "MyMod.dll",
      "sha256": "",
      "fileSize": 0,
      "status": "pending",
      "vtPermalink": null,
      "downloadUrl": null,
      "dllInstallPath": "BepInEx/plugins",
      "structureWarnings": []
    }
  }
}
```

Po przyjęciu requestu backend tworzy rekord `pending`/`scanning` i uruchamia job:

1. normalizacja GitHub URL,
2. pobranie artefaktu,
3. walidacja struktury,
4. SHA256 + rozmiar,
5. VirusTotal scan/report,
6. publikacja CDN/download tylko jeśli `clean`.

### 2. Status custom artifact

`GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/status`

Response `200`:

```json
{
  "data": {
    "status": "scanning",
    "downloadAvailable": false,
    "customArtifact": {
      "artifactId": "uuid-or-id",
      "sourceKind": "github_dll",
      "modType": "dll",
      "displayName": "My Custom DLL",
      "version": "v1.2.3",
      "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
      "fileName": "MyMod.dll",
      "sha256": "...64 hex when known...",
      "fileSize": 123456,
      "status": "scanning",
      "vtPermalink": null,
      "downloadUrl": null,
      "dllInstallPath": "BepInEx/plugins",
      "structureWarnings": []
    }
  }
}
```

Statusy:

| Status | Znaczenie | `downloadAvailable` |
|--------|-----------|---------------------|
| `pending` | rekord utworzony, job jeszcze nie pobrał artefaktu | false |
| `scanning` | artefakt pobrany/walidowany/skanowany | false |
| `clean` | struktura OK i VT clean | true |
| `suspicious` | VT przekroczył próg suspicious/malicious | false |
| `rejected` | walidacja URL/struktury/rozmiaru nie przeszła | false |
| `expired` | TTL paczki/artefaktu minął | false |

### 3. Finalizacja paczki

`POST /api/v2/modpacks/:code/finalize`

Backend ustawia paczkę jako:

- `ready`, `installable=true` — tylko jeśli wszystkie custom artifacts są `clean`,
- `scanning`, `installable=false` — jeśli co najmniej jeden artifact jest `pending/scanning`,
- `blocked`, `installable=false` — jeśli co najmniej jeden artifact jest `suspicious/rejected`.

Response `200`:

```json
{
  "data": {
    "status": "ready",
    "installable": true,
    "shareUrl": "https://susmodder.app/pack/ABCD-EFGH-JKLM",
    "deepLink": "susmodder://pack/ABCD-EFGH-JKLM"
  }
}
```

Jeśli niegotowe:

```json
{
  "error": {
    "code": "PACK_NOT_READY",
    "message": "Custom content is still being scanned"
  }
}
```

Rekomendacja HTTP: `409 Conflict` albo `425 Too Early` dla pending scan.

### 4. Publiczny preview paczki

`GET /api/v2/modpacks/:code`

Musi zachować dotychczasowe pola i dodać opcjonalne:

```json
{
  "data": {
    "packCode": "ABCD-EFGH-JKLM",
    "status": "ready",
    "installable": true,
    "fullMod": { "id": 1, "version": "5.4.0" },
    "dllMods": [],
    "externalDlls": [],
    "customArtifacts": [
      {
        "artifactId": "uuid-or-id",
        "sourceKind": "github_dll",
        "modType": "dll",
        "displayName": "My Custom DLL",
        "version": "v1.2.3",
        "originalSourceUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll",
        "fileName": "MyMod.dll",
        "sha256": "...64 hex...",
        "fileSize": 123456,
        "status": "clean",
        "vtPermalink": "https://www.virustotal.com/gui/file/...",
        "downloadUrl": "https://api.susmodder-cdn.ovh/v2/modpacks/ABCD-EFGH-JKLM/custom-artifacts/uuid-or-id/download",
        "dllInstallPath": "BepInEx/plugins",
        "structureWarnings": []
      }
    ]
  }
}
```

Kompatybilność:

- `customArtifacts` opcjonalne; brak pola = stary klient działa jak wcześniej.
- `externalDlls` zostaje dla local uploaded DLL legacy/adapterów.
- Dla nowych uploadowanych DLL można mapować też alias do `customArtifacts` z `sourceKind=uploaded_dll`, ale nie usuwać `externalDlls`.

### 5. Download artifactu

`GET /api/v2/modpacks/:code/custom-artifacts/:artifactId/download`

Dozwolone tylko dla `status=clean` i aktywnej paczki.

Response:

- `302` do CDN albo `200` stream z nagłówkami:
  - `X-SUSModder-SHA256: <sha256>`
  - `X-SUSModder-File-Size: <bytes>`
  - `Content-Disposition: attachment; filename="..."`

Błędy:

| HTTP | code | Kiedy |
|------|------|-------|
| 404 | `ARTIFACT_NOT_FOUND` | brak artefaktu/paczki |
| 410 | `PACK_EXPIRED` | TTL minął |
| 425 | `CUSTOM_CONTENT_PENDING_SCAN` | pending/scanning |
| 451 | `CUSTOM_CONTENT_SUSPICIOUS` | suspicious |
| 451 | `CUSTOM_CONTENT_REJECTED` | rejected |

## GitHub URL normalization

Akceptować MVP:

1. `https://github.com/{owner}/{repo}/releases/download/{tag}/{asset}`
2. Opcjonalnie później: `https://github.com/{owner}/{repo}/releases/tag/{tag}` tylko jeśli backend jednoznacznie wybierze jeden asset.

Odrzucać:

- `http://`, host inny niż `github.com`, userinfo w URL,
- redirect poza `github.com`/`objects.githubusercontent.com`/`github-releases.githubusercontent.com` kontrolowany przez GitHub,
- branch links (`/tree/main`, `/archive/refs/heads/main.zip`),
- repo homepage bez release assetu,
- shortlinki,
- prywatne repo wymagające tokena użytkownika.

Normalization output zapisywany do DB:

```json
{
  "githubOwner": "owner",
  "githubRepo": "repo",
  "githubTag": "v1.2.3",
  "githubAssetName": "MyMod.dll",
  "normalizedGithubUrl": "https://github.com/owner/repo/releases/download/v1.2.3/MyMod.dll"
}
```

## Walidacja struktury

### DLL artifact

Akceptowane:

- pojedynczy `.dll`, albo
- `.zip` zawierający `.dll` wyłącznie pod dozwoloną ścieżką.

MVP rekomendacja: najpierw przyjąć pojedyncze `.dll`; ZIP DLL można dodać później.

Reguły:

- max size konfigurowalny, startowo 10 MB dla DLL,
- `dllInstallPath` tylko `BepInEx/plugins` albo podfoldery `BepInEx/plugins/*`,
- nazwa pliku bez separatorów, bez `..`, bez `:`, rozszerzenie `.dll`,
- skanować dokładny plik `.dll`.

### FULL artifact

Akceptowane później, po DLL:

- ZIP overlay moda, nie pełna gra,
- musi zawierać rozpoznawalny loader/BepInEx layout: `BepInEx/`, `BepInEx/plugins/`, `doorstop_config.ini`, `winhttp.dll` albo ekwiwalent uzgodniony z realnymi modami,
- reject jeśli zawiera `Among Us.exe` albo duże pliki gry,
- reject dla `.ps1`, `.bat`, `.cmd`; `.exe` tylko jeśli świadomie dopuszczone późniejszą decyzją,
- zip entries po normalizacji muszą mieścić się pod katalogiem docelowym,
- limity liczby plików, total uncompressed size i depth.

## Tabele / migracje PostgreSQL

Minimalnie:

### `mod_pack_custom_artifacts`

Kolumny:

- `id uuid primary key default gen_random_uuid()`
- `pack_id uuid not null references mod_packs(id) on delete cascade`
- `source_kind text not null` — `uploaded_dll | github_dll | github_full`
- `mod_type text not null` — `dll | full`
- `display_name text not null`
- `version text null`
- `original_url text null`
- `normalized_github_url text null`
- `github_owner text null`
- `github_repo text null`
- `github_tag text null`
- `github_asset_name text null`
- `file_name text not null default ''`
- `sha256 char(64) null`
- `file_size bigint null`
- `cdn_path text null`
- `status text not null default 'pending'`
- `vt_status text null`
- `vt_permalink text null`
- `structure_status text null`
- `structure_report_json jsonb null`
- `dll_install_path text null`
- `error_code text null`
- `error_message text null`
- `created_at timestamptz not null default now()`
- `updated_at timestamptz not null default now()`

Indexes:

- `(pack_id)`
- `(sha256)`
- `(status)`
- unique optional `(pack_id, source_kind, sha256)` when `sha256 is not null`.

### `mod_pack_artifact_scans`

For FULL/ZIP internal files:

- `id uuid primary key default gen_random_uuid()`
- `artifact_id uuid references mod_pack_custom_artifacts(id) on delete cascade`
- `file_path text not null`
- `sha256 char(64) not null`
- `file_size bigint not null`
- `vt_status text not null`
- `vt_permalink text null`
- `verdict text not null`
- `created_at timestamptz not null default now()`

### `file_scan_cache` (optional but recommended)

- `sha256 char(64) primary key`
- `file_size bigint not null`
- `vt_status text not null`
- `vt_permalink text null`
- `stats_json jsonb null`
- `last_checked_at timestamptz not null default now()`

## Worker / async processing

MVP bez kolejki zewnętrznej może działać przez prosty background job loop w Node, ale musi przetrwać restart:

1. Endpoint tworzy rekord `pending`.
2. Worker wybiera `pending` artifacts (`FOR UPDATE SKIP LOCKED` jeśli dostępne).
3. Ustawia `scanning`.
4. Pobiera asset do temp storage.
5. Waliduje strukturę i size.
6. Liczy SHA256.
7. Sprawdza `file_scan_cache`.
8. Jeśli brak cache: upload/poll VirusTotal.
9. Jeśli clean: przenosi do CDN path po hashach, ustawia `status=clean`, `downloadUrl`/`cdn_path`.
10. Jeśli fail: `status=rejected/suspicious`, `error_code`.

Przy restarcie worker ma podnosić stare `scanning` starsze niż np. 30 minut do retry albo `rejected` z `VT_SCAN_FAILED` po przekroczeniu limitu retry.

## Error codes

Stabilne kody dla klienta:

- `GITHUB_URL_REQUIRED`
- `GITHUB_URL_NOT_ALLOWED`
- `GITHUB_RELEASE_ASSET_REQUIRED`
- `GITHUB_ASSET_TOO_LARGE`
- `GITHUB_ASSET_NOT_FOUND`
- `GITHUB_RATE_LIMITED`
- `MOD_STRUCTURE_INVALID`
- `MOD_STRUCTURE_UNSUPPORTED_FILES`
- `MOD_STRUCTURE_PATH_TRAVERSAL`
- `DLL_INSTALL_PATH_INVALID`
- `VT_SCAN_PENDING`
- `VT_SCAN_FAILED`
- `CUSTOM_CONTENT_PENDING_SCAN`
- `CUSTOM_CONTENT_SUSPICIOUS`
- `CUSTOM_CONTENT_REJECTED`
- `PACK_NOT_READY`

## Security / privacy

- Nie logować tokenów, Authorization headers ani pełnych lokalnych ścieżek klienta.
- GitHub URL może być logowany tylko po normalizacji i tylko publiczny release asset.
- CDN path nie może bazować na user filename bez sanitizacji; preferowane `<packCode>/<sha256>/<safeFileName>`.
- Każdy download clean artifactu musi mieć SHA256 w DB i w nagłówku.
- `originalSourceUrl` w public preview: MVP może zwracać pełny GitHub release asset, ale rozważyć później ograniczenie do `owner/repo@tag`.
- Rate-limit deklarację GitHub assetów i polling statusów.

## Backend tests required

### Unit

- GitHub URL validator:
  - release asset accepted,
  - repo homepage rejected,
  - branch/archive main rejected,
  - host other than `github.com` rejected,
  - URL with userinfo rejected.
- `dllInstallPath` validator:
  - `BepInEx/plugins` accepted,
  - `BepInEx/plugins/Sub` accepted,
  - `../`, absolute path, `:` rejected.
- Structure validator:
  - direct DLL accepted,
  - non-DLL rejected for DLL mode,
  - FULL without BepInEx rejected,
  - ZIP traversal rejected,
  - unsupported scripts rejected.

### Integration

- `POST /modpacks/:code/custom-github-mods` creates pending artifact.
- Worker transitions `pending -> scanning -> clean` with mocked GitHub/VT.
- `GET /status` returns clean artifact with SHA256/fileSize/downloadAvailable.
- `POST /finalize` returns `PACK_NOT_READY` while pending and `ready/installable=true` after clean.
- `GET /modpacks/:code` includes `customArtifacts[]` and legacy fields still work.
- `GET /download` returns 425/451 for non-clean and 302/headers for clean.

### E2E staging/prod

1. Pack bez custom content: unchanged.
2. Pack z local uploaded DLL: unchanged v2 upload/status/finalize.
3. Pack z GitHub DLL release asset:
   - declare,
   - scan clean,
   - preview has `customArtifacts[]`,
   - client installs to `BepInEx/plugins` and SHA256 matches.
4. Rejected GitHub URL cannot be finalized/installable.

## Rollout order for backend

1. DB migration + repository/query layer.
2. GitHub URL validator + Joi schema.
3. `POST /custom-github-mods` storing `pending` only.
4. Worker with mocked VT/GitHub in tests.
5. Real GitHub download + size limits.
6. VT scan/cache integration.
7. `GET /status`, `POST /finalize`, `GET /modpacks/:code` custom fields.
8. `GET /download` gated by `clean`.
9. Staging E2E with SUSModder client.

## Client dependency

SUSModder client already has Core methods prepared:

- `DeclareGitHubCustomModAsync(packCode, request, ct)`
- `GetCustomArtifactStatusAsync(packCode, artifactId, ct)`
- `FinalizePackAsync(packCode, ct)`
- preview parsing for `customArtifacts[]`
- installer support for clean DLL artifacts with SHA256 verification

UI for GitHub custom DLL is the next client task and should follow this backend contract.
