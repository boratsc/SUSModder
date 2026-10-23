# Build scripts — quick reference

## Polityka podpisów (docelowa)

| Kanał | Authenticode | Uwagi |
|-------|--------------|--------|
| `beta` | **SignPath** (domyślnie) | `signingMode=signpath` w RC; escape hatch: `none` |
| `release` | **SignPath** (domyślnie) | to samo — oba kanały mogą być podpisane |

Publiczne buildy Velopack **wymagają** `PublishSingleFile=false` (unpacked). Gate: `Assert-NotSingleFile.ps1`.

### Przypięta wersja Velopack

CLI `vpk` jest przypięte w `.config/dotnet-tools.json` do **1.2.161**.
Skrypty pakowania korzystają z `Invoke-Velopack.ps1`: przywracają lokalne
narzędzie, sprawdzają zgodność wersji z biblioteką w UI/Core i nie używają
globalnego `vpk`. Nie trzeba aktualizować ani usuwać globalnej instalacji.

```powershell
.\SKRYPTY\Build\Invoke-Velopack.ps1 -RestoreOnly
.\SKRYPTY\Build\Invoke-Velopack.ps1 -Arguments @('pack', '--help')
# Test offline, bez instalacji/publikacji paczek:
.\SKRYPTY\Test\test-velopack-toolchain.ps1
```

Velopack 1.x używa własnego `SemanticVersion`, synchronicznego
`WaitExitThenApplyUpdates` i trybu generowania w `--delta` (nie ścieżki pliku).
Nowy updater nie obsługuje starych delt bsdiff; przed publikacją trzeba sprawdzić
przejście ze starego klienta, pełną paczkę, kanały beta/release oraz restart.
Nie traktuj samego poprawnego builda jako testu rzeczywistej aktualizacji.

### SignPath / Authenticode (CI)

Domyślny `signingMode` w `release-candidate.yml`: **`signpath`** (beta i release).

Wymagane GitHub configuration:

| Typ | Nazwa | Opis |
|-----|-------|------|
| Secret | `SIGNPATH_API_TOKEN` | API token submittera SignPath |
| Variable | `SIGNPATH_ORGANIZATION_ID` | UUID organizacji |
| Variable | `SIGNPATH_PROJECT_SLUG` | slug projektu |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | np. `test-signing` / `release-signing` |
| Variable | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | opcjonalnie; domyślnie `velopack-channel` |

W portalu SignPath utwórz Artifact Configuration o slug `velopack-channel` na podstawie XML:
`.signpath/artifact-configurations/velopack-channel.xml`

Flow CI: pack → upload unsigned bundle (Setup/Update/Portable/nupkg) → SignPath → apply → `Assert-AuthenticodeSigned.ps1` → deploy/draft.

Skrypty: `Prepare-SignPathBundle.ps1`, `Apply-SignPathBundle.ps1`, `Assert-AuthenticodeSigned.ps1`.

## Sekrety (`Secrets.cs`)

`SUSModder.Core/Secrets.cs` jest **gitignored** i nigdy nie trafia do repo.

| Środowisko | Źródło |
|------------|--------|
| Lokalnie | istniejący `Secrets.cs` albo `generate-secrets.ps1` z env |
| CI (GitHub Actions) | GitHub Secrets → `generate-secrets.ps1` przed build |

Wymagane GitHub Secrets:

- `SUSMODDER_DOWNLOAD_TOKEN` — plaintext token Authorization
- `SUSMODDER_7Z_PASSWORD` — plaintext hasło legacy vanilla 7z
- `DEPLOY_SSH_PRIVATE_KEY` — klucz do uploadu na VPS (release workflow). **Preferuj Base64** całego pliku klucza (jedna linia); workflow akceptuje też surowy PEM.
- `DC_WEBHOOK` — Discord webhook URL; po udanym deployu na serwer (`release-candidate.yml`) wysyła ogłoszenie beta/release.
- `SIGNPATH_API_TOKEN` — token SignPath (gdy `signingMode=signpath`)

Opcjonalne Variables: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_PATH`.

```powershell
$env:SUSMODDER_DOWNLOAD_TOKEN = "..."
$env:SUSMODDER_7Z_PASSWORD = "..."
.\generate-secrets.ps1
```

Uwaga: GH Secrets chronią źródło w CI; wartości i tak trafiają do binarki klienta. Pełne usunięcie sekretów z desktopa = osobny POC.

### Discord announce (RC)

Po `Deploy to susmodder.app` workflow woła `Send-DiscordReleaseAnnouncement.ps1`.

Inputy `workflow_dispatch`:

- `changelogMarkdown` — user-facing punkty zmian (markdown). Puste = post bez bloku zmian.
- `discordTeaser` — opcjonalna linia na końcu (np. „Niedługo 3.0.13-beta”).

### Uruchamianie wydania z zachowaniem polskich znaków

Changelog zapisuj jako **UTF-8**. Do uruchamiania RC używaj helpera
`Start-ReleaseCandidate.ps1` (Windows PowerShell 5.1 i PowerShell 7):

```powershell
# Najpierw podgląd JSON — bez publikacji i bez wysyłania ogłoszenia
.\SKRYPTY\Build\Start-ReleaseCandidate.ps1 -Version 3.0.14 -Channel beta `
  -ChangelogPath .release-changelog.txt -DryRun

# Właściwe uruchomienie workflow: SignPath, draft GitHub, serwer i Discord
.\SKRYPTY\Build\Start-ReleaseCandidate.ps1 -Version 3.0.14 -Channel beta `
  -Ref beta -ChangelogPath .release-changelog.txt
```

`-SigningMode none` wymaga świadomej decyzji o wydaniu bez podpisu.
`-PublishTarget github-draft` pomija serwer i Discord; `none` pomija publikację.
`-Ref` wskazuje gałąź/commit budowany przez workflow (domyślnie `beta`).

**Nie przekazuj JSON przez `ConvertTo-Json | gh api ... --input -` w Windows
PowerShell 5.1**: domyślny `$OutputEncoding` to ASCII, więc polskie litery stają
się `?` już w inputach GitHub Actions (również w changelogu draftu GitHub).
Helper czyta changelog i zapisuje JSON jako UTF-8 bez BOM, a `gh` odczytuje plik
przez `--input <plik>` — bez zależności od kodowania konsoli/potoku.
Nie naprawia to tekstu uszkodzonego wcześniej; źródłowy changelog musi być poprawny.
Wysyłka webhooka Discord już używa bajtów UTF-8 i `charset=utf-8`.

Testy offline (bez `gh`, webhooka, sekretów i publikacji):

```powershell
powershell -NoProfile -File .\SKRYPTY\Test\test-release-text-encoding.ps1
pwsh -NoProfile -File .\SKRYPTY\Test\test-release-text-encoding.ps1
```

Workflow RC uruchamia oba testy przed buildem/publikacją. Test PowerShell 5.1
sprawdza JSON dispatchu, a PowerShell 7 także pełną treść i bajty webhooka
dla kanałów beta i release (polskie litery, emoji, cudzysłowy i nowe linie).

**GitHub draft assets** (nie mylić z deployem na serwer):

- zawsze: `SUSModderInstaller.exe` (pobierany z `https://susmodder.app/releases/SUSModderInstaller.exe`)
- zawsze (beta i release): `*Portable.zip` + zbudowany `*Setup.exe`
- nupkg / RELEASES / json → tylko serwer

Lokalny dry-run (wymaga `gh` + istniejącego draftu z Portable dla beta):

```powershell
.\SKRYPTY\Build\Send-DiscordReleaseAnnouncement.ps1 `
  -Channel beta `
  -Version 3.0.12 `
  -ReleaseTag v3.0.12-beta `
  -ChangelogMarkdown "* [3.0.12] Przykładowa zmiana" `
  -DryRun
```

## Aktualne użycie (lokalnie)

```powershell
# Release + beta
.\build-dual-channel.ps1 -Version 3.0.0

# Tylko release
.\build-dual-channel.ps1 -Version 3.0.0 -SkipBeta

# Tylko beta
.\build-dual-channel.ps1 -Version 3.1.0 -SkipRelease
```

Output:

- `releases-release/` — kanał release Velopack
- `releases-beta/` — kanał beta Velopack

## Skrypty

- `generate-secrets.ps1` — generuje `Secrets.cs` z env (CI + opcjonalnie lokalnie).
- `Assert-NotSingleFile.ps1` — fail jeśli publish wygląda na single-file.
- `build-dual-channel.ps1` — rekomendowany build developerski/produkcyjny.
- `build-release-velopack.ps1` — helper dla pojedynczego kanału Velopack.
- `build-velopack-test.ps1` — lokalne testy paczkowania Velopack.
- `build-bootstrapper.ps1` — build bootstrappera/instalatora, jeśli jest potrzebny w release flow.
- `deploy-to-server.ps1` — ręczny upload gotowych artefaktów; wymaga jawnych parametrów/klucza SSH i ostrożności.
- `Send-DiscordReleaseAnnouncement.ps1` — post na Discord (webhook) po RC; szablony beta/release.
- `Prepare-SignPathBundle.ps1` / `Apply-SignPathBundle.ps1` / `Assert-AuthenticodeSigned.ps1` — SignPath Authenticode w CI.
- `build-release-2.2.0.ps1`, `sign-and-build.ps1`, `build-with-signing.ps1`, `post-sign-packages.ps1` — legacy/reference. Nie używać domyślnie.

## Zasady

1. Nie commituj katalogów `publish*`, `releases-*`, `velopack-releases`, ani logów z buildów.
2. Nie zapisuj haseł, tokenów ani prywatnych kluczy w skryptach; nie uploaduj `Secrets.cs` jako artifact CI.
3. Publiczny release: `PublishSingleFile=false`, jeden kanał na run CI (storage).
4. Jeśli stary skrypt nie jest już potrzebny jako referencja, przenieś go do archiwum albo usuń osobnym cleanupem.
