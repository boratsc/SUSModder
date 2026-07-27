# Build scripts — quick reference

## Polityka podpisów (docelowa)

| Kanał | Authenticode | Uwagi |
|-------|--------------|--------|
| `beta` | **unsigned** | Domyślny kanał testowy CI (`release-candidate.yml`) |
| `release` | **signed** (SignPath/OSSign) | Pending integracji; lokalnie nadal unsigned |

Publiczne buildy Velopack **wymagają** `PublishSingleFile=false` (unpacked). Gate: `Assert-NotSingleFile.ps1`.

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

Opcjonalne Variables: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_PATH`.

```powershell
$env:SUSMODDER_DOWNLOAD_TOKEN = "..."
$env:SUSMODDER_7Z_PASSWORD = "..."
.\generate-secrets.ps1
```

Uwaga: GH Secrets chronią źródło w CI; wartości i tak trafiają do binarki klienta. Pełne usunięcie sekretów z desktopa = osobny POC.

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
- `build-release-2.2.0.ps1`, `sign-and-build.ps1`, `build-with-signing.ps1`, `post-sign-packages.ps1` — legacy/reference. Nie używać domyślnie.

## Zasady

1. Nie commituj katalogów `publish*`, `releases-*`, `velopack-releases`, ani logów z buildów.
2. Nie zapisuj haseł, tokenów ani prywatnych kluczy w skryptach; nie uploaduj `Secrets.cs` jako artifact CI.
3. Publiczny release: `PublishSingleFile=false`, jeden kanał na run CI (storage).
4. Jeśli stary skrypt nie jest już potrzebny jako referencja, przenieś go do archiwum albo usuń osobnym cleanupem.
