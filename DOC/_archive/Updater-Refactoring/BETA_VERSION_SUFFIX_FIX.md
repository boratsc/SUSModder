# Fix: Beta Channel Version Problem (2025-11-06)

## Problem
1. **Beta wersje nie miały sufixu -beta w manifeście**
   - Plik: \SUSModder-2.3.6-beta-full.nupkg\ ✅
   - Wersja w manifeście: \2.3.6\ ❌
   - Powinno być: \2.3.6-beta\ ✅

2. **Release channel próbował aktualizować do beta**
   - Release: 2.2.0
   - Beta: 2.3.6 (bez sufixu)
   - Velopack porównywał numerycznie: 2.3.6 > 2.2.0 → upgrade
   - To jest BŁĘDNE - różne kanały nie powinny się krzyżować

## Root Cause
W \SKRYPTY\Build\build-dual-channel.ps1\ (linia 180):
\\\powershell
# STARY KOD (błędny):
Build-Channel -ChannelName "BETA (Testowe)" -ChannelVersion \ -ChannelCode "beta"
# Jeśli Version = "2.3.6", to manifest zawierał Version: "2.3.6"
\\\

**Błędne założenie:** Komentarz mówił "Velopack sam doda nazwę kanału" - to była nieprawda!
- Velopack dodaje \-{channel}\ do **NAZWY PLIKU**
- ALE NIE do **wersji w manifeście**!

## Solution
\\\powershell
# NOWY KOD (poprawny):
if (-not \) {
    # MUSIMY dodać sufix -beta do wersji, inaczej Velopack wpisze czystą wersję
    \ = if (\.Contains("-beta")) { \ } else { "\-beta" }
    Build-Channel -ChannelName "BETA (Testowe)" -ChannelVersion \ -ChannelCode "beta"
}
\\\

**Wynik:**
- Jeśli buildujemy \-Version 2.3.6\ → Velopack dostaje \2.3.6-beta\
- Manifest: \{"Version": "2.3.6-beta"}\ ✅
- Plik: \SUSModder-2.3.6-beta-beta-full.nupkg\ ✅ (podwójne "beta" jest OK)

## Velopack Version Comparison Logic

Velopack używa \SemanticVersion\ (SemVer):
- \2.2.0\ (release) vs \2.3.6-beta\ (prerelease)
- Reguły SemVer:
  - Wersja z prerelease sufiksem jest MNIEJSZA niż bez sufixu
  - \2.3.6-beta\ < \2.3.6\
  - ALE \2.3.6\ > \2.2.0\ (numerycznie)

**Z sufiksem -beta:**
- Release channel (2.2.0) widzi beta (2.3.6-beta) jako prerelease → **NIE aktualizuje** ✅
- Beta channel (2.2.0) widzi beta (2.3.6-beta) jako nowszą → **aktualizuje** ✅

**Bez sufixu -beta (stary błąd):**
- Release channel (2.2.0) widzi (2.3.6) jako stable newer → **błędnie aktualizuje** ❌
- Beta channel (2.2.0) widzi (2.3.6) jako nowszą → aktualizuje ✅

## Deployment
1. **Build z poprawionego skryptu:**
   \\\powershell
   .\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.3.6" -SkipRelease
   # Wersja w manifeście: 2.3.6-beta ✅
   \\\

2. **Upload na serwer:**
   - \/susmodder/releases/beta/RELEASES\ (plik Velopack)
   - \/susmodder/releases/beta/releases.beta.json\ (manifest z pakietami)
   - \/susmodder-velopack/releases.beta.json\ (manifest dla API) ← KLUCZOWE!
   - \/susmodder/releases/beta/SUSModder-2.3.6-beta-beta-full.nupkg\

3. **Weryfikacja:**
   \\\ash
   curl https://susmodder.app/api/releases?channel=beta | jq '.manifest.Releases[0].Version'
   # Powinno zwrócić: "2.3.6-beta"
   \\\

## Konsekwencje dla użytkowników

### Przed fixem:
- User na release (2.2.0) → widzi update do 2.3.6 (beta bez sufixu) → instaluje beta ❌
- User na beta (2.2.0) → widzi update do 2.3.6 → OK ✅
- User chce wrócić z beta na release → downgrade z 2.3.6 do 2.2.0 → ciągłe powiadomienia o "nowej" 2.3.6 ❌

### Po fixie:
- User na release (2.2.0) → NIE widzi beta (2.3.6-beta) jako update ✅
- User na beta (2.2.0) → widzi update do 2.3.6-beta → OK ✅
- User przełącza z beta na release → downgrade z 2.3.6-beta do 2.2.0 → brak fałszywych powiadomień ✅

## Files Changed
- \SKRYPTY\Build\build-dual-channel.ps1\ - dodano automatyczne \-beta\ sufix (linia ~180)
- Server: \/susmodder-velopack/releases.beta.json\ - zaktualizowano manifest z prawidłową wersją

## Status
✅ **NAPRAWIONE** - beta wersje mają teraz prawidłowy sufix \-beta\
✅ **ZWERYFIKOWANE** - manifest na serwerze zawiera \2.3.6-beta\
✅ **TESTED** - lokalny build generuje prawidłowe manifesty

Data: 2025-11-06 18:52:00
