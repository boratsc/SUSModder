# SKRYPTY

Katalog z utrzymywanymi skryptami pomocniczymi.

Snapshoty treści są w AFFiNE (`SUSModder / INDEX`). **Executable CI/build zostają w repo.**

- `Build/` — build, pakowanie, deploy gotowych artefaktów.
- `Test/` — smoke testy API, testy Velopack i inne ręczne testy integracyjne.
- `Utilities/` — diagnostyka/naprawy + `sync-to-affine.ps1` / `generate-affine-folder-indexes.ps1`.

## CI (wymagane w gicie)

- `Build/generate-secrets.ps1`
- `Build/build-dual-channel.ps1`
- `Build/Assert-NotSingleFile.ps1`
- `Build/deploy-to-server.ps1`

## Pliki generowane

Wyniki testów API (`api-*-results-*.json`) oraz tymczasowe payloady (`_*.json`) są ignorowane przez `.gitignore` i nie powinny być commitowane.
