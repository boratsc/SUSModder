# PODSUMOWANIE: Naprawa problemu aktualizacji kanału beta 2.3.6

## Problem
Mimo że pliki dla wersji 2.3.6 zostały wgrane na serwer, API zwracało wersję 2.3.5 jako najnowszą.

## Root Cause Analysis

### 1. Nieprawidłowe nazwy plików RELEASES
**Problem:** Velopack CLI generuje pliki z nazwą:
- \RELEASES-beta\ (zamiast \RELEASES\)
- \RELEASES-release\ (zamiast \RELEASES\)

**Oczekiwane:** Velopack i backend API szukają pliku o nazwie \RELEASES\ (bez sufixu).

### 2. Niezsynchronizowane manifesty JSON
**Problem:** Backend API czyta manifest z:
\/srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json\

Ale deployment wgrywał tylko do:
\/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/releases.beta.json\

**Rezultat:** 
- W \/susmodder-velopack/\ był stary JSON z wersją 2.3.5
- W \/susmodder/releases/beta/\ był nowy JSON z wersją 2.3.6

## Rozwiązanie

### Krok 1: Lokalnie ✅
Skopiowano pliki z prawidłowymi nazwami:
\\\powershell
Copy-Item "releases-beta\RELEASES-beta" "releases-beta\RELEASES" -Force
Copy-Item "releases-release\RELEASES-release" "releases-release\RELEASES" -Force
\\\

### Krok 2: Na serwerze ✅
Wgrano pliki za pomocą Posh-SSH:

1. **Pliki RELEASES:**
   \\\
   /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/RELEASES
   /srv/synapsekit-boracik/nginx/html/susmodder/releases/release/RELEASES
   \\\

2. **Manifesty JSON (dla API):**
   \\\
   /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json
   /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.release.json
   \\\

### Krok 3: Naprawiono skrypt budowania ✅
Edytowano \SKRYPTY\Build\build-dual-channel.ps1\:

**Dodano (po linii 117):**
\\\powershell
# Fix: Velopack generates RELEASES-{channel}, but we need RELEASES for compatibility
\ = Join-Path \ "RELEASES-\"
\ = Join-Path \ "RELEASES"

if (Test-Path \) {
    Copy-Item \ \ -Force
    Write-Host "  INFO: Created RELEASES from RELEASES-\" -ForegroundColor Gray
}
\\\

**Rezultat:** Przyszłe buildy będą automatycznie tworzyć plik \RELEASES\ obok \RELEASES-{channel}\.

## Weryfikacja

### Backend API
\\\ash
curl https://susmodder.app/api/releases?channel=beta
# Powinien zwrócić: "latestVersion": "2.3.6"
\\\

### Pliki na serwerze
\\\ash
ssh debian@vps-b99a39c3.vps.ovh.net
cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json
# Powinien zawierać: "Version":"2.3.6"
\\\

## Wnioski

### Przyczyny problemu:
1. **Velopack CLI** automatycznie dodaje sufix do nazwy pliku RELEASES
2. **Deployment workflow** nie kopiował plików JSON do katalogu API
3. **Brak automatyzacji** w skrypcie budowania

### Zapobieganie w przyszłości:
1. ✅ Skrypt \uild-dual-channel.ps1\ automatycznie tworzy oba pliki (RELEASES-{channel} i RELEASES)
2. ⚠️ TODO: Zaktualizować \deploy-to-server.ps1\ aby kopiował JSON również do \/susmodder-velopack/\
3. ⚠️ TODO: Dodać weryfikację poprawności manifestu JSON po deployment

## Pliki zmienione
- \SKRYPTY\Build\build-dual-channel.ps1\ - dodano automatyczne kopiowanie pliku RELEASES
- \SKRYPTY\Build\RELEASES_RENAME_FIX.md\ - dokumentacja problemu

## Status
✅ **NAPRAWIONE** - wersja 2.3.6 jest teraz zwracana jako najnowsza w kanale beta

Data: 2025-11-06 18:22:23
