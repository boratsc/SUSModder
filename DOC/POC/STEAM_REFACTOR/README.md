# Steam Integration — DepotDownloader Migration

**Status:** POC v1.0 (2026-06-01)  
**Poprzedni POC:** 2025-11-07 (ogólna analiza SteamCMD vs DD)

## Problem

SUSModder 2.x pobiera vanillę Among Us jako hostowane paczki `.7z` z CDN (`among-steam/`). To redystrybucja plików gry — koszt CDN, ryzyko prawne, brak elastyczności wersji.

## Rozwiązanie (2026-06)

**DepotDownloader 3.4.0** jako główne źródło vanilli (CDN Valve, manifest pinning z backendu). Paczki `.7z` zostają jako **fallback**.

**Wersja vanilli:** zawsze dokładnie ta z `modConfig.AmongVersion`. **Cache per wersja** — kilka modów na tej samej wersji AU = jedno pobranie, potem kopia z `Among Us - Vanilla/extracted/{storageVersion}/`.

**Auth Steam:** bez formularza login/hasło w aplikacji (odrzucenie modelu z SUSModder 3.0). Kaskada:

1. DepotDownloader z pinned `-manifest` (zapisany token lub QR auth)
2. QR auth (`-qr -remember-password`) — jedyne interaktywne logowanie
3. Fallback 7z z CDN (ta sama `storageVersion`)

## Dokumenty

| Dokument | Opis |
|----------|------|
| **[2026-06-01-depotdownloader-migration-poc.md](2026-06-01-depotdownloader-migration-poc.md)** | **Aktualny plan migracji** — architektura 2.x, backend manifestów, auth, fazy implementacji |
| [STEAM_INTEGRATION_POC.md](STEAM_INTEGRATION_POC.md) | Wcześniejsza analiza (2025-11) — SteamCMD vs DD, hybrydowe podejście |

## Referencje zewnętrzne

- SUSModder 3.0 (porzucony): `D:\Development\Żródła\SUSModder-3.0-main`
- Backend manifestów: endpoint `GET /api/among-us-steam-manifests` (Faza E backendu)
- Among Us: AppId `945360`, depot `945361`

## Następne kroki

1. Faza 0: weryfikacja API manifestów na produkcji + spike QR z DD
2. Decyzja: start Fazy 1 (port Core z 3.0)
3. Implementacja `SteamVanillaProvider` + integracja z `ModManager`
