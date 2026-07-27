# Build scripts — quick reference

## Polityka podpisów (docelowa)

| Kanał | Authenticode | Uwagi |
|-------|--------------|--------|
| `beta` | **SignPath** (domyślnie) | `signingMode=signpath` w RC; escape hatch: `none` |
| `release` | **SignPath** (domyślnie) | to samo — oba kanały mogą być podpisane |

Publiczne buildy Velopack **wymagają** `PublishSingleFile=false` (unpacked). Gate: `Assert-NotSingleFile.ps1`.

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
