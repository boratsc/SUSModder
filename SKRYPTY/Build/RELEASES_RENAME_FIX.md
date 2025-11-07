# Problem z nazwami plików RELEASES w Velopack

## Problem
Velopack generuje pliki z nazwą:
- RELEASES-{channel} (np. RELEASES-beta, RELEASES-release)

Ale API i Velopack oczekują:
- RELEASES (bez sufixu)

## Root Cause
Velopack CLI v0.0.1298+ automatycznie dodaje sufix do nazwy pliku RELEASES gdy używany jest parametr --channel.

## Rozwiązanie
W skrypcie uild-dual-channel.ps1 po generowaniu pakietu trzeba dodać:

```powershell
# Po linii 117 (po "Velopack packaging")
# Dodaj rename logic:

# Fix: Velopack generuje RELEASES-{channel}, ale potrzebujemy RELEASES
$releasesFileWithSuffix = Join-Path $ReleasesDir "RELEASES-$ChannelCode"
$releasesFile = Join-Path $ReleasesDir "RELEASES"

if (Test-Path $releasesFileWithSuffix) {
    Copy-Item $releasesFileWithSuffix $releasesFile -Force
    Write-Host "  INFO: Renamed RELEASES-$ChannelCode -> RELEASES" -ForegroundColor Gray
}
```

## Weryfikacja
Po dodaniu tej zmiany:
```powershell
# Build testowy
.\SKRYPTY\Build\build-dual-channel.ps1 -Version 2.3.7 -SkipRelease

# Sprawdź output
Get-ChildItem releases-beta | Select-Object Name
# Powinny być OBA pliki:
# - RELEASES-beta (backup)
# - RELEASES (główny plik używany przez Velopack)
```

## Status
✅ NAPRAWIONE RĘCZNIE - pliki RELEASES wgrane na serwer
⚠️ DO ZROBIENIA - dodać automatyczną logikę do skryptu budowania
