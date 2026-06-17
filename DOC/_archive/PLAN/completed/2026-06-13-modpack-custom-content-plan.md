# Plan: własne DLL i własne mody GitHub w modpackach

Data: 2026-06-13  
Status: **Zakończone. 3-opcyjny kreator wdrożony. Klient + backend gotowe do beta. Jedyny stub: custom FULL overlay validation.**  
Zakres: SUSModder 3.x, API v2, modpack sharing, SUSModder.Core, Avalonia UI, backend `susmodder.app` / `api.susmodder-cdn.ovh`, CDN, VirusTotal

## Status wdrożenia — 2026-06-15 (finalny)

### Faza 0 — kontrakt API v2 ✅
- Uzgodniony format `status`, `installable`, `customArtifacts[]`, `finalize`.
- Backend contract rozpisany w `DOC/PLAN/2026-06-14-modpack-github-custom-content-backend-contract.md`.

### Faza 1 — bezpieczeństwo fundamentów ✅
- `Sha256Verifier` — `SUSModder.Core/Utilities/Sha256Verifier.cs`.
- Weryfikacja SHA256 pobranych external/custom DLL przed zapisem w `ModPackInstaller`.
- Safe write: temp file + replace w katalogu docelowym dla wszystkich DLL.
- Whitleista `DllInstallPath` pod `BepInEx/plugins`; blokada `..`, absolutnych ścieżek, ADS `:`, plików bez `.dll`.
- Safe path validation podpięta do:
  - `DllModificationService.InstallDllToModAsync`
  - `DllModificationService.UninstallDllFromModAsync`
  - `DllModificationService.IsDllInstalledInMod`
  - `ModManager.ModifyDllAsync`

### Faza 2 — local custom DLL + instalator ✅
- **Core**: modele (`ModPackCustomArtifact`, status/installable, wynik finalizacji).
- **Core**: `IModPackService` / `ModPackService` — upload, status DLL/GitHub, finalizacja, parsowanie v2 `data` wrapper.
- **Core**: `ModPackInstaller` instaluje clean DLL `customArtifacts` (`uploaded_dll`, `github_dll`) z backend/CDN.
- **UI**: `ModPackCreatorView` — multi-file picker, upload, polling, finalizacja.
- **UI**: `ModPackPreviewViewModel` — pokazuje `customArtifacts` i blokuje install dla non-clean.
- **UI**: anulowanie długiego flow przez `CancellationTokenSource`.
- **UI**: lokalne tworzenie instancji raportuje partial failure z listą fail DLL.
- PL/EN kompletne: `CustomDlls.*`, `CustomGitHub.*`, `CustomContent.*`.
- `Core.Tests`: testy API v2, SHA256, safe path, custom DLL install z HTTP serverem.
- Skrypt smoke/E2E: `SKRYPTY/Test/test-modpack-custom-content-v2.ps1` (tryb `-ValidateOnly` lokalnie).

### Faza 3 — GitHub custom DLL ✅
- **Core**: `DeclareGitHubCustomModAsync`, `GetCustomArtifactStatusAsync`.
- **UI**: formularz w kreatorze (nazwa, wersja, URL, `DllInstallPath`).
- **UI**: walidacja klienta URL (`github.com/releases/download/...`), safe `DllInstallPath`.
- **UI**: deklaracja → polling → finalizacja w jednym flow.


### Przebudowa UI — 3-opcyjny kreator (2026-06-15)

Zastąpiono ShareOnline dwoma nowymi trybami, dodano CreateAndShare:

| Tryb | Enum | Panel widoczny | Flow |
|------|------|---------------|------|
| **Utwórz nowy zestaw** | InstallLocal | Custom DLL (lokalne), katalogowe DLL | Instaluje lokalnie → gotowe |
| **Udostępnij zestaw** | ShareExisting | Custom DLL (z instancji), GitHub DLL (checkbox), GitHub FULL (checkbox), nazwa/creator/TTL | Wybiera instancję → pre-fill → upload → finalizuj → kod paczki |
| **Utwórz i udostępnij** | CreateAndShare | Wszystkie panele (lokalne + share) | Wybiera mod → dodaje custom DLL + GitHub → instaluje lokalnie → od razu upload → finalizuj → kod paczki |

**Kluczowe zmiany:**
- GitHub DLL panel: **checkbox toggle** + **lista wielu wpisów** (dodaj/usuń) — nie jeden stały formularz.
- GitHub FULL panel: checkbox toggle (analogicznie).
- Custom DLL panel: widoczny we wszystkich trybach (lokalnie kopiuje do BepInEx/plugins, przy share uploaduje).
- CreateAndShare łączy oba flowy w jeden przycisk — nie trzeba już przeskakiwać między dwoma dialogami.
- ShareExisting pre-fill z wybranej instancji (mod, DLL, custom DLL).
- ModPackCreatorMode enum: InstallLocal, ShareExisting, CreateAndShare (usunięto ShareOnline).
- MainWindowViewModel: nowy CreateAndSharePackCommand, ShareExistingPackCommand.
- PL/EN: CreateAndShareTitle, CustomGitHub.Toggle, CustomGitHub.AddDll, CustomGitHub.SelectedCount, CustomDlls.LocalNotice.
### Testy i weryfikacja
- 179 testów jednostkowych Core — PASS.
- `dotnet build SUSModder.sln` — PASS.
- `pwsh -NoProfile -File SKRYPTY/Test/test-modpack-custom-content-v2.ps1 -ValidateOnly` — PASS.

### Review
- Core: code/security/goal/context PASS, blocker poprawiony.
- UI local DLL: goal/security/i18n PASS, cancellation gap poprawiony.
- UI GitHub DLL: goal/i18n PASS; code review bez blockerów.
- Starsze QA background taski timeoutowały (nie dotyczyły ostatnich iteracji).

### Co jeszcze przed końcem MVP custom content
1. **Custom FULL GitHub overlay** ✅ — klient gotowy (UI toggle, formularz, declaracja/polling/finalizacja, installer).
2. Smoke/E2E ✅ — wszystkie endpointy v2 działają (create, upload DLL, finalize, preview, custom-github-mods).
3. Backend worker VT scan + GitHub download + walidacja struktury ✅ — wszystkie endpointy produkcyjne.
4. **Custom FULL overlay validation** 🔧 — celowy stub backlogu per kontrakt („FULL artifact – akceptowane później, po DLL”). Backend zwraca `pending`/`scanning` do czasu wdrożenia walidacji struktury overlay.
5. Polish/QA: ręczny smoke UI kreatora, przegląd i18n copy — ostatni krok przed beta.

### Backend status (2026-06-15)
| Endpoint | Status |
|----------|--------|
| `POST /v2/modpacks` | ✅ |
| `POST /v2/modpacks/:code/dlls` (multipart + creatorHash) | ✅ |
| `GET /v2/modpacks/:code` (preview z customArtifacts[], externalDlls, status/installable) | ✅ |
| `POST /v2/modpacks/:code/custom-github-mods` | ✅ |
| `GET /v2/modpacks/:code/custom-artifacts/:artifactId/status` | ✅ |
| `POST /v2/modpacks/:code/finalize` | ✅ |
| `GET /v2/modpacks/:code/custom-artifacts/:artifactId/download` | ✅ |
| Worker GitHub download + walidacja struktury + VT scan | ✅ |
| Custom FULL overlay validation | 🔧 stub (celowo po DLL)

### Backend contract
`DOC/PLAN/2026-06-14-modpack-github-custom-content-backend-contract.md`

## Kontekst

Obecny plan modpacków zakładał katalogowy mod FULL, katalogowe DLL oraz zewnętrzne DLL, ale w praktyce zabrakło pełnego workflow dla użytkownika, który chce:

1. Dodać do modpacka wiele własnych lokalnych plików `.dll`.
2. Dodać własny mod FULL albo DLL jako link do GitHuba.
3. Udostępnić taki zestaw dopiero po stronie serwera, po walidacji struktury i skanowaniu przez VirusTotal.

Istniejące elementy, które trzeba rozszerzyć zamiast pisać od zera:

- `DOC/2026-05-25 - frontend-ideas/16-mod-pack-sharing.md` ma pierwotny koncept external DLL i ostrzeżeń.
- `DOC/PLAN/2026-05-29-mod-pack-sharing-plan.md` mówi, że klient Core + UI modpacków jest w dużej części gotowy, a backend/testy zostają do domknięcia.
- `DOC/PLAN/MODPACK_API.md` opisuje v1 kontrakt external DLL, ale część ścieżek różni się od v2.
- `DOC/POC/API v2/consumer-susmodder-3x.md` wskazuje aktualny v2 flow: `POST /api/v2/modpacks`, `POST /api/v2/modpacks/:code/dlls`, `GET /api/v2/modpacks/:code/dlls/:sha256/status`.
- `SUSModder.Core/Models/ModPack.cs` ma już `ExternalDlls`, `ExternalDllFilePaths`, `ModPackExternalDllDeclaration`.
- `SUSModder.Core/Services/ModPackService.cs` po utworzeniu paczki wywołuje upload pending external DLL, ale bez pełnego UI statusu i bez finalizacji paczki.
- `SUSModder.Core/Services/InstanceToModPackMapper.cs` potrafi wykryć external DLL w lokalnej instancji i dodać ścieżki do uploadu.
- `SUSModder.Core/Services/ModPackInstaller.cs` pobiera external DLL i zapisuje do `BepInEx/plugins`, ale plan SHA256 wskazuje, że nadal brakuje weryfikacji pobranego hasha przed zapisem.
- `DOC/2026-05-25 - frontend-ideas/17-sha256-verification.md` wskazuje brak SHA256 verification w downloaderach jako lukę bezpieczeństwa.

## Goal

Dodać bezpieczny, jawny i weryfikowalny model custom content w modpackach:

1. **Własne lokalne DLL** — użytkownik może wybrać wiele plików `.dll`; każdy plik jest uploadowany na backend, liczony po SHA256, skanowany przez VirusTotal i dopiero po statusie `clean` staje się instalowalny.
2. **Własne mody jako GitHub links** — użytkownik może podać wyłącznie link do GitHuba dla własnego moda `full` albo `dll`; backend pobiera dokładnie wskazany artefakt, waliduje strukturę, liczy hash, skanuje artefakt i pliki wykonywalne wewnątrz, a następnie udostępnia go przez kontrolowany download API/CDN.
3. **Modpack nie jest „gotowy do instalacji”, dopóki custom content nie przejdzie walidacji i skanowania**.
4. **Klient instaluje tylko artefakty z backendu/API**, nie bezpośrednio z losowego linku, nawet jeśli źródłem był GitHub.
5. Zachować kompatybilność z istniejącymi katalogowymi modpackami bez custom content.

## Non-goals

- Nie tworzymy publicznego marketplace'a ani wyszukiwarki custom modów.
- Nie akceptujemy hostów innych niż GitHub (`github.com` i API GitHuba) dla linkowanych custom modów.
- Nie instalujemy plików, których backend nie przeskanował albo oznaczył jako `pending`, `suspicious`, `rejected`, `expired`.
- Nie ufamy nazwie pliku, rozszerzeniu ani deklaracji użytkownika bez walidacji backendowej.
- Nie pobieramy runtime tłumaczeń z serwera.
- Nie zapisujemy ustawień runtime w `appsettings.json`.
- Nie automatyzujemy aktualizacji custom GitHub modów w MVP; modpack jest snapshotem konkretnego artefaktu/hashu.
- Nie wspieramy CurseForge, Modrinth, Discord CDN, Google Drive ani prywatnych repozytoriów GitHub w MVP.

## Decyzje projektowe

### D1. Pack może mieć status roboczy

Dodać stan paczki po stronie backendu:

| Status | Znaczenie | Publiczny install? |
|--------|-----------|--------------------|
| `draft` | paczka utworzona, ale custom content nie został jeszcze dodany/finalizowany | nie |
| `scanning` | są artefakty w trakcie walidacji albo VT scan | nie |
| `ready` | wszystkie wymagane artefakty są `clean` i strukturalnie poprawne | tak |
| `blocked` | co najmniej jeden artefakt jest `suspicious`/`rejected` | nie |
| `expired` | TTL minął | nie |

Dla paczek bez custom content backend może od razu zwracać `ready`, żeby nie psuć obecnego flow.

### D2. Backend jest źródłem prawdy dla skanu i downloadu

Klient może liczyć SHA256 lokalnie tylko pomocniczo, ale ostateczne wartości (`sha256`, `vtStatus`, `artifactStatus`, `downloadAvailable`) pochodzą z backendu. Instalator używa download URL z API albo endpointu `GET /api/v2/modpacks/:code/...`, a nie bezpośredniego GitHub URL.

### D3. GitHub link musi być pinowany do konkretnego artefaktu

Akceptujemy tylko linki, które backend potrafi znormalizować do konkretnego release assetu albo archiwum źródłowego w konkretnym tagu/commicie. Preferowane dla MVP:

- `https://github.com/{owner}/{repo}/releases/download/{tag}/{asset}`
- `https://github.com/{owner}/{repo}/releases/tag/{tag}` + wybór assetu po stronie backend/UI, jeśli API GitHuba zwróci jednoznaczny plik

Odrzucić w MVP:

- link do gałęzi `main/master` bez taga/commita,
- link do strony repo bez release assetu,
- shortlinki i redirecty poza GitHub,
- prywatne repozytoria wymagające tokena użytkownika.

### D4. Skanujemy artefakt i pliki wykonywalne

Dla `.dll` skanowany jest dokładny plik DLL.  
Dla `.zip`/release assetu skanowany jest:

1. cały artefakt jako plik,
2. każdy istotny plik wykonywalny wypakowany w sandboxie (`.dll`, `.exe`, ewentualnie `.bat`, `.cmd`, `.ps1` od razu reject w MVP).

`ready` tylko jeśli wszystkie wymagane skany są `clean` oraz walidacja struktury przeszła.

## User workflow

### A. Twórca: własne lokalne DLL

1. Użytkownik otwiera kreator „Udostępnij zestaw modów”.
2. W sekcji `Mody DLL` wybiera katalogowe DLL jak dziś.
3. W nowej sekcji `Własne pliki DLL` klika `Dodaj pliki DLL` i wybiera wiele plików.
4. UI pokazuje tabelę:
   - nazwa pliku,
   - rozmiar,
   - lokalny SHA256 po stronie klienta,
   - status uploadu/skanu: `oczekuje`, `upload`, `skanowanie`, `bezpieczny`, `zablokowany`, `błąd`.
5. Po kliknięciu `Utwórz zestaw` klient:
   - tworzy pack draft albo ready-with-pending-artifacts zależnie od API,
   - uploaduje każdy DLL na backend,
   - polluje statusy skanu,
   - dopiero po `ready` pokazuje link do udostępnienia.
6. Jeśli któryś DLL jest `suspicious/rejected`, UI pokazuje wynik i nie generuje gotowego linku instalacyjnego dla tej wersji paczki.

### B. Twórca: własny mod z GitHuba

1. W kreatorze dodajemy sekcję `Własny mod z GitHuba`.
2. Użytkownik wybiera typ:
   - `FULL mod` — alternatywa dla katalogowego moda głównego,
   - `DLL mod` — dodatkowy DLL instalowany do wybranego/full moda.
3. Użytkownik podaje:
   - nazwę moda,
   - wersję/tag,
   - URL GitHub release/release asset,
   - dla DLL opcjonalnie docelową ścieżkę instalacji, domyślnie `BepInEx/plugins`.
4. Backend waliduje URL, pobiera artefakt, sprawdza strukturę i skanuje.
5. UI pokazuje wynik walidacji i ewentualne instrukcje naprawy: „asset musi być `.dll` albo `.zip` z `BepInEx/plugins`”, „link musi wskazywać GitHub release”, itd.

### C. Odbiorca paczki

1. Odbiorca wkleja kod albo otwiera deep link.
2. Preview pokazuje osobno:
   - mod główny katalogowy albo custom GitHub full,
   - DLL katalogowe,
   - lokalne custom DLL przesłane na backend,
   - custom GitHub DLL,
   - statusy VT i permalink, jeśli backend zwraca.
3. Jeśli jakikolwiek custom content nie jest `clean`, instalacja jest zablokowana.
4. Jeśli content jest `clean`, nadal wymagamy checkboxa zgody na zewnętrzne/custom DLL/mod full: użytkownik rozumie, że to nie jest oficjalny katalog SUSModder.
5. Instalator pobiera każdy artefakt przez API/CDN, sprawdza SHA256 i dopiero zapisuje do instancji.

## Language / i18n impact

Wszystkie teksty PL/EN jako klucze, bez hardcodowania w Views/ViewModels/Core user-facing errors. Core zwraca stabilne error codes + techniczny fallback.

Proponowane klucze:

- `ModPacks.CustomDlls.Title`
- `ModPacks.CustomDlls.AddFiles`
- `ModPacks.CustomDlls.RemoveFile`
- `ModPacks.CustomDlls.ScanPending`
- `ModPacks.CustomDlls.ScanClean`
- `ModPacks.CustomDlls.ScanSuspicious`
- `ModPacks.CustomDlls.UploadFailed`
- `ModPacks.CustomDlls.LimitReached`
- `ModPacks.CustomGitHub.Title`
- `ModPacks.CustomGitHub.TypeFull`
- `ModPacks.CustomGitHub.TypeDll`
- `ModPacks.CustomGitHub.UrlLabel`
- `ModPacks.CustomGitHub.UrlPlaceholder`
- `ModPacks.CustomGitHub.GitHubOnlyWarning`
- `ModPacks.CustomGitHub.StructureInvalid`
- `ModPacks.CustomGitHub.ScanRequired`
- `ModPacks.CustomContent.InstallConsent`
- `ModPacks.CustomContent.InstallBlockedPendingScan`
- `ModPacks.CustomContent.InstallBlockedSuspicious`

Zasady:

- fallback locale: `pl`,
- komplet PL/EN dla każdego klucza,
- placeholdery identyczne, np. `{fileName}`, `{modName}`, `{status}`, `{count}`,
- przy liczbach używać ICU MessageFormat lub istniejącego mechanizmu pluralizacji,
- przyszły locale ma być dodawalny przez plik locale/metadata, bez zmian w logice,
- telemetry wysyła tylko kanoniczne `pl`/`en`, nie URL ani nazwy lokalnych plików.

## Core business logic responsibilities

### Modele

Rozszerzyć `SUSModder.Core/Models/ModPack.cs` albo dodać nowe typy:

```csharp
public sealed class ModPackCustomArtifact
{
    public string ArtifactId { get; set; } = string.Empty;
    public string SourceKind { get; set; } = "uploaded_dll"; // uploaded_dll | github_full | github_dll
    public string ModType { get; set; } = "dll";             // full | dll
    public string DisplayName { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? OriginalSourceUrl { get; set; }            // only safe GitHub URL, optional in public preview
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "pending";          // pending | scanning | clean | suspicious | rejected | expired
    public string? VtPermalink { get; set; }
    public string? DownloadUrl { get; set; }
    public string? DllInstallPath { get; set; }
    public IReadOnlyList<string> StructureWarnings { get; set; } = Array.Empty<string>();
}
```

Możliwa kompatybilność: `ModPackExternalDll` może zostać jako alias/adapter dla `SourceKind = uploaded_dll`.

### Serwisy

Rozszerzyć `IModPackService` / `ModPackService`:

- `CreateDraftPackAsync(...)` albo rozszerzone `CreatePackAsync(...)` z obsługą `status`.
- `UploadCustomDllAsync(packCode, filePath, ct)` — powtarzalne dla wielu plików.
- `DeclareGitHubCustomModAsync(packCode, request, ct)` — deklaracja linku GitHub i uruchomienie backendowej walidacji/skanu.
- `GetCustomArtifactStatusAsync(packCode, artifactIdOrSha, ct)`.
- `FinalizePackAsync(packCode, ct)` — opcjonalnie, jeśli backend wymaga jawnej finalizacji.
- `ValidatePack(...)` musi blokować instalację, gdy jakikolwiek custom artifact nie jest `clean`.

### Instalator

- `ModPackInstaller` musi obsłużyć custom full mod jako źródło moda głównego. W praktyce powinien utworzyć instancję jak dla katalogowego full moda, ale z downloadem artefaktu z backendu i walidacją struktury overlaya.
- Custom DLL instalować jak external DLL, ale:
  - pobrać przez API/CDN,
  - sprawdzić `Content-Length` i limit,
  - sprawdzić SHA256 pobranych bajtów przed zapisem,
  - użyć safe replace temp -> target,
  - zapisać w `InstallationMap` / `ModInstanceDll` jako `Source = "custom_upload"` albo `"custom_github"`, z `Sha256`, `VtStatus`, `SourcePackCode`.
- Zablokować path traversal w `DllInstallPath` i nazwie pliku.

### Weryfikacja SHA256

To powinno wejść przed lub razem z tym planem:

- dodać wspólny `Sha256Verifier`, zgodnie z `DOC/2026-05-25 - frontend-ideas/17-sha256-verification.md`,
- zastosować minimum w `ModPackInstaller.InstallExternalDllAsync()` i nowych custom artifact downloaderach,
- test: zmieniony hash = brak zapisu do `BepInEx/plugins`.

## UI / Avalonia responsibilities

### Kreator modpacka

Rozszerzyć `SUSModder/Views/ModPackCreatorView.axaml` i `.cs`:

- Sekcja `Własne pliki DLL`:
  - multi file picker `.dll`,
  - lista z możliwością usuwania,
  - limit liczby/rozmiaru zgodny z backendiem,
  - status uploadu/skanu per plik,
  - informacja, że pliki idą na serwer i są skanowane przez VirusTotal.
- Sekcja `Własny mod z GitHuba`:
  - typ `FULL`/`DLL`,
  - pole URL,
  - nazwa, wersja/tag,
  - dla DLL `DllInstallPath` z whitelistą/sugerowanym `BepInEx/plugins`,
  - przycisk `Sprawdź link` uruchamia walidację backendową przed utworzeniem finalnego share linku.
- Przycisk `Utwórz zestaw` pokazuje etapowanie: `tworzenie`, `upload`, `skanowanie`, `finalizacja`, `gotowe`.
- Link do udostępniania pokazywać dopiero gdy pack `ready` albo jasno oznaczyć „link roboczy — instalacja zablokowana do czasu skanu”. Preferowane MVP: link gotowy dopiero po `ready`.

### Preview / install dialog

- Pokazywać custom artifacts osobno od katalogowych modów.
- Dla każdego custom artifact: `clean/pending/suspicious/rejected`, hash, rozmiar, źródło (`GitHub`/`uploaded DLL`), opcjonalny VT permalink.
- Gdy status inny niż `clean`: przycisk instalacji disabled.
- Gdy status `clean`: wymagany checkbox zgody dla custom content.

## Backend / API responsibilities

### Minimalne nowe endpointy v2

Zachować istniejące endpointy i dodać rozszerzenia:

| Method | Path | Cel |
|--------|------|-----|
| `POST` | `/api/v2/modpacks` | Tworzy pack; przy custom content może zwracać `status=draft/scanning` |
| `POST` | `/api/v2/modpacks/:code/dlls` | Upload jednego DLL; można wywołać wiele razy albo dodać batch multipart później |
| `GET` | `/api/v2/modpacks/:code/dlls/:sha256/status` | Status skanu DLL — istniejący v2 koncept |
| `POST` | `/api/v2/modpacks/:code/custom-github-mods` | Deklaruje custom FULL/DLL z GitHub URL |
| `GET` | `/api/v2/modpacks/:code/custom-artifacts/:artifactId/status` | Status walidacji/skanu GitHub artifact |
| `POST` | `/api/v2/modpacks/:code/finalize` | Finalizuje pack, jeśli wszystkie artifacty są `clean` |
| `GET` | `/api/v2/modpacks/:code` | Zwraca `status`, `installable`, `customArtifacts[]` |
| `GET` | `/api/v2/modpacks/:code/custom-artifacts/:artifactId/download` | Download/302 tylko dla `clean` |

### Error codes

Stabilne kody do lokalizacji w UI:

- `CUSTOM_CONTENT_PENDING_SCAN`
- `CUSTOM_CONTENT_SUSPICIOUS`
- `CUSTOM_CONTENT_REJECTED`
- `CUSTOM_CONTENT_EXPIRED`
- `GITHUB_URL_REQUIRED`
- `GITHUB_URL_NOT_ALLOWED`
- `GITHUB_RELEASE_ASSET_REQUIRED`
- `GITHUB_ASSET_TOO_LARGE`
- `GITHUB_ASSET_NOT_FOUND`
- `GITHUB_RATE_LIMITED`
- `MOD_STRUCTURE_INVALID`
- `MOD_STRUCTURE_UNSUPPORTED_FILES`
- `MOD_STRUCTURE_PATH_TRAVERSAL`
- `DLL_LIMIT_REACHED`
- `DLL_FILE_TOO_LARGE`
- `VT_SCAN_PENDING`
- `VT_SCAN_FAILED`
- `PACK_NOT_READY`

### Struktura baz danych

Przykładowe tabele/migracje w backendzie:

- `mod_pack_custom_artifacts`
  - `id`, `pack_id`, `source_kind`, `mod_type`, `display_name`, `version`, `original_url`, `normalized_github_url`, `github_owner`, `github_repo`, `github_tag`, `github_asset_id`, `file_name`, `sha256`, `file_size`, `cdn_path`, `status`, `vt_status`, `vt_permalink`, `structure_status`, `structure_report_json`, `dll_install_path`, `created_at`, `updated_at`.
- `mod_pack_artifact_scans`
  - jeden artefakt może mieć wiele skanowanych plików wewnętrznych: `artifact_id`, `file_path`, `sha256`, `file_size`, `vt_status`, `vt_permalink`, `verdict`.
- Opcjonalnie wspólna tabela `file_scan_cache` po `sha256`, żeby nie skanować ponownie tego samego pliku.

### Walidacja struktury

#### DLL mod

Akceptowane:

- pojedynczy `.dll`, albo
- `.zip` z jednym lub wieloma `.dll` w dozwolonej ścieżce.

Wymagania:

- domyślna instalacja do `BepInEx/plugins`,
- `DllInstallPath` tylko relatywny i z whitelistą (`BepInEx/plugins`, znane podfoldery pluginów),
- brak `..`, absolutnych ścieżek, symlinków, ADS, ukrytych ścieżek specjalnych,
- brak `.exe`, `.ps1`, `.bat`, `.cmd` w MVP.

#### FULL mod

Akceptowany artefakt powinien być overlayem moda, nie pełną kopią gry:

- musi zawierać rozpoznawalną strukturę BepInEx / loadera, np. `BepInEx/`, `BepInEx/plugins/`, `doorstop_config.ini`, `winhttp.dll` lub równoważne pliki używane przez obecne katalogowe mody,
- nie powinien zawierać pirackiej kopii gry (`Among Us.exe`, duże pliki gry InnerSloth) — w MVP reject albo ręczna lista wyjątków tylko jeśli obecny katalog faktycznie tego wymaga,
- wszystkie wpisy ZIP muszą po normalizacji mieścić się w katalogu docelowym,
- maksymalna liczba plików i rozmiar po rozpakowaniu limitowane, żeby uniknąć zip bomb.

## Config and migration implications

### Klient SQLite

- Nie zmieniać `appsettings.json` runtime.
- `user_settings` raczej bez nowych flag; istniejące `mod_packs_enabled` i `mod_packs_auto_install` wystarczą. `mod_packs_auto_install` nie może omijać consentu dla custom content.
- Rozważyć cache statusów custom artifactów w historii modpacków, ale źródłem prawdy zostaje backend.
- `mod_instances` / `mod_instance_dlls` powinny zapisywać nowe źródła:
  - `custom_upload`,
  - `custom_github`,
  - `source_pack_code`,
  - `sha256`,
  - `vt_status`,
  - `original_source_url` tylko jeśli nie narusza prywatności i jest GitHub URL.

### Backend migracje

- Nowe tabele wg sekcji Backend.
- Cleanup CDN musi usuwać custom artifacty po TTL/hard-delete paczki.
- `GET /modpacks/:code` musi zachować kompatybilność z obecnymi klientami: nowe pola opcjonalne, stare `externalDlls` nadal mapowane.

## Platform, packaging, updater, telemetry, privacy, AV constraints

### Platform

- Windows paths: wszystkie ścieżki instalacji muszą przejść safe path normalization.
- Steam/Epic: custom full mod musi być instalowany tak jak overlay katalogowego full moda na odpowiednią bazę vanilla, bez zakładania jednej struktury katalogów Epic/Steam.
- Brak zależności od systemowego Git/GitHub CLI.

### Packaging/updater

- Brak zmian w Velopack.
- Nie bundle'ować dodatkowych skanerów antywirusowych w kliencie.
- Nie pobierać narzędzi runtime do walidacji; walidacja ciężka po stronie backendu.

### Telemetry/privacy

- Nie wysyłać lokalnych pełnych ścieżek plików.
- Telemetry może zawierać tylko agregaty: liczba custom DLL, typ custom GitHub artifactu, status scan outcome, locale `pl/en`, error code.
- Nie logować prywatnych tokenów ani pełnych lokalnych ścieżek; GitHub URL może być logowany tylko po normalizacji i tylko publiczny.

### AV / bezpieczeństwo

- Każdy custom artifact idzie przez backend i VirusTotal.
- Klient instaluje tylko po `clean` + zgoda użytkownika.
- Dla `pending` zwracać HTTP 425 / `CUSTOM_CONTENT_PENDING_SCAN`.
- Dla `suspicious/rejected` zwracać HTTP 451 / `CUSTOM_CONTENT_SUSPICIOUS` lub `CUSTOM_CONTENT_REJECTED`.
- Zachować VT permalink dla transparentności, ale nie traktować go jako jedynego dowodu bezpieczeństwa.
- Progi VT powinny być konfigurowalne po stronie backendu; początkowo spójne z istniejącym planem (`malicious >= 3` blokuje), ale decyzja do potwierdzenia po testach false positive.

## Verification plan

### Backend unit/integration

- Walidacja URL:
  - `github.com` release asset accepted,
  - redirect poza GitHub rejected,
  - branch link rejected,
  - invalid URL rejected.
- Upload DLL:
  - wiele DLL w jednej paczce przez powtarzalne uploady,
  - limit liczby/rozmiaru,
  - duplicate hash dedupe,
  - pending -> clean -> download enabled,
  - suspicious -> download blocked.
- Struktura:
  - DLL direct accepted,
  - ZIP z path traversal rejected,
  - FULL bez BepInEx rejected,
  - FULL z podejrzanymi skryptami rejected,
  - zip bomb / za dużo plików rejected.
- Cleanup TTL usuwa CDN + DB rows.

### Core tests

- `ModPackService` serializuje nowe pola / obsługuje statusy.
- `UploadCustomDllAsync` nie wysyła lokalnej ścieżki jako danych publicznych.
- `ValidatePack` blokuje pending/suspicious/rejected.
- `ModPackInstaller` weryfikuje SHA256 przed zapisem.
- Custom DLL safe path test: `..\\evil.dll` nie przechodzi.
- Partial failure nie niszczy istniejącej DLL.

### UI tests / smoke

- Multi-file picker pokazuje listę wybranych DLL.
- Usuwanie pliku przed uploadem działa.
- Pending scan blokuje przycisk finalizacji / link.
- Suspicious pokazuje lokalizowany komunikat PL/EN i blokuje instalację.
- Clean wymaga checkboxa zgody.
- GitHub form blokuje non-GitHub URL.

### E2E

1. Pack katalogowy bez custom content nadal działa jak dziś.
2. Pack z 2 lokalnymi DLL:
   - upload obu,
   - scan clean,
   - public preview,
   - install do nowej instancji,
   - hash zgadza się z API.
3. Pack z custom GitHub DLL:
   - link release asset,
   - validate + scan,
   - install do `BepInEx/plugins`.
4. Pack z custom GitHub FULL:
   - validate overlay,
   - install jako nowa instancja,
   - katalogowe DLL nadal mogą zostać doinstalowane po full.
5. Pack z rejected artifact nie da się zainstalować przez UI ani przez ręczne API download.

## Suggested implementation order

### Faza 0 — kontrakt i decyzje API (równolegle backend + klient)

1. Uzgodnić finalny kontrakt v2 dla `status`, `installable`, `customArtifacts[]`, `finalize`.
2. Uzgodnić limity: max DLL per pack, max DLL size, max GitHub artifact size, max unpacked size/full mod.
3. Uzgodnić próg VT i stany `pending/scanning/clean/suspicious/rejected`.

### Faza 1 — bezpieczeństwo fundamentów

1. Core: wdrożyć `Sha256Verifier` i użyć w external/custom DLL downloadach.
2. Backend: wspólny `file_scan_cache` / adapter VT (`GET /api/v3/files/{sha256}`, upload gdy brak raportu, polling analysis).
3. Backend: safe artifact storage i CDN path po hashach.

### Faza 2 — local custom DLL files

1. Backend: wielokrotne uploady DLL + status endpoint + cleanup.
2. Core: `UploadCustomDllAsync`, status polling, modele wyników.
3. UI: multi-file picker, tabela statusów, finalizacja/link po `ready`.
4. Testy: pack z wieloma DLL.

### Faza 3 — GitHub custom DLL

1. Backend: GitHub URL normalization, release asset fetch, structure validation dla DLL/ZIP.
2. Backend: VT scan artifactu i DLL wewnętrznych.
3. Core/UI: formularz custom GitHub DLL + status.
4. Instalator: install do whitelisted `DllInstallPath`.

### Faza 4 — GitHub custom FULL

1. Backend: walidacja struktury FULL overlay, reject pełnej gry i unsafe plików.
2. Core: model custom full jako alternatywa dla `FullModId`.
3. Installer: custom full download + SHA256 + overlay na vanilla jak katalogowy full.
4. UI: wybór „mod główny z katalogu” vs „mod główny z GitHuba”.

### Faza 5 — polish, docs, rollout

1. PL/EN copy i i18n review.
2. `MODPACK_API.md` aktualizacja do rzeczywistego v2 kontraktu.
3. E2E scripts w `SKRYPTY/Test`.
4. Beta rollout z telemetry aggregate i monitorowaniem VT false positives.
5. Dopiero po beta: release channel.

## Parallelizable tasks

- Backend DB/API kontrakt i Core modele mogą iść równolegle po ustaleniu JSON schema.
- UI multi-file picker może powstać na fake service/mock statusach równolegle z backendem.
- SHA256 verifier jest niezależny i powinien wejść najpierw.
- GitHub URL validator i structure validator mogą być robione równolegle z VT adapterem.
- i18n copy checker może pracować równolegle po zamrożeniu listy kluczy.

## Open questions

1. Czy dla MVP link GitHub ma wskazywać wyłącznie release asset, czy dopuszczamy release page z automatycznym wyborem assetu?
2. Jaki maksymalny rozmiar custom FULL ZIP jest akceptowalny dla backendu i VT quota?
3. Czy custom FULL może zawierać `winhttp.dll`/loader native DLL, czy wymagamy tylko BepInEx/plugins? Trzeba porównać z realną strukturą obecnych katalogowych modów.
4. Czy `suspicious` ma blokować tworzenie paczki całkowicie, czy paczka zostaje jako `blocked` do diagnostyki autora?
5. Czy publiczny preview pokazuje pełny GitHub URL, czy tylko repo + tag, żeby ograniczyć nadużycia i tracking?
6. Czy obecny limit 3 external DLL z v1 zostaje, czy podnosimy go dla wielu własnych DLL? Propozycja: MVP 5 plików, limit konfigurowalny backendowo.

## Sources used

- `DOC/2026-05-25 - frontend-ideas/16-mod-pack-sharing.md`
- `DOC/PLAN/2026-05-29-mod-pack-sharing-plan.md`
- `DOC/PLAN/MODPACK_API.md`
- `DOC/POC/API v2/consumer-susmodder-3x.md`
- `DOC/POC/API v2/2026-06-07-api-e2e-audit.md`
- `DOC/2026-05-25 - frontend-ideas/17-sha256-verification.md`
- `DOC/PLAN/2026-06-13-dll-auto-update-modpack-update-notifications-plan.md`
- `SUSModder.Core/Models/ModPack.cs`
- `SUSModder.Core/Services/ModPackService.cs`
- `SUSModder.Core/Services/InstanceToModPackMapper.cs`
- `SUSModder.Core/Services/ModPackInstaller.cs`
- `SUSModder.Core/Services/DllModificationService.cs`
- `SUSModder/Views/ModPackCreatorView.axaml(.cs)`
- `mcp-rag` lookup for modpack/API patterns
- `sus-free-doc-scout` broad documentation scan
- VirusTotal API docs: file object and `GET /api/v3/files/{id}` report
