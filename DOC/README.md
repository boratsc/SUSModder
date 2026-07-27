# Dokumentacja projektu SUSModder

**Źródło prawdy (SSOT):** AFFiNE — strona `SUSModder / INDEX` (MCP `affine`).

Treść `DOC/` **nie jest w gicie**. Lokalny katalog może istnieć jako prywatny mirror (gitignored); agent i ludzie czytają docs z Affine.

## Agent

1. Skill: `affine-knowledge`
2. `keyword_search` / `semantic_search` → `read_document`
3. Indeksy: `INDEX / DOC / PLAN`, `INDEX / SKRYPTY / Build`, …
4. Start: `SUSModder / INDEX` (`ZTnFn0_HoNHhWut1zwG9D`)

## Sync (opcjonalny lokalny mirror → Affine)

```powershell
.\SKRYPTY\Utilities\sync-to-affine.ps1 -All -SkipReadback -DelayMs 1200
.\SKRYPTY\Utilities\generate-affine-folder-indexes.ps1
```

Mapa (gitignored): `.affine-migration/map.json`

## CI w repo (executable)

- `SKRYPTY/Build/generate-secrets.ps1`
- `SKRYPTY/Build/build-dual-channel.ps1`
- `SKRYPTY/Build/Assert-NotSingleFile.ps1`
- `SKRYPTY/Build/deploy-to-server.ps1`
