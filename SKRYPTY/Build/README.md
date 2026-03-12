# Build Scripts - Quick Reference

## 📋 Skrypty Budowania

## ✅ Czego używać teraz

**Aktualna rekomendacja dla tego repo:**

- `build-with-signing.ps1` - podstawowy skrypt do realnych buildów release/beta z podpisywaniem
- `build-release-2.2.0.ps1` - tylko jeśli naprawdę potrzebujesz starego legacy ZIP; w obecnym repo wymaga starego `Updater.csproj`, więc domyślnie go nie używaj
- `sign-and-build.ps1` - stary helper interaktywny do `build-release-2.2.0.ps1`; traktuj jako legacy helper
- `build-dual-channel.ps1` - szybki build developerski bez podpisywania

**Najczęstsze scenariusze:**

```powershell
# Podpisana beta
.\build-with-signing.ps1 -Version "X.Y.Z" -BetaVersion "X.Y.Z-beta" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE" -SkipRelease

# Podpisany release + beta (bez legacy ZIP)
.\build-with-signing.ps1 -Version "X.Y.Z" -BetaVersion "A.B.C-beta" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Szybki build developerski bez podpisywania
.\build-dual-channel.ps1 -Version X.Y.Z
```

### Production Release (v2.2.0+)

#### `build-with-signing.ps1` ⭐ REKOMENDOWANE
Najbardziej praktyczny skrypt w obecnym repo. Buduje kanał release i/lub beta, podpisuje EXE przed i po pakowaniu oraz nie zależy od starego `Updater.csproj`.

**Użycie:**
```powershell
# Release + beta
.\build-with-signing.ps1 -Version "X.Y.Z" -BetaVersion "A.B.C-beta" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Tylko beta
.\build-with-signing.ps1 -Version "X.Y.Z" -BetaVersion "X.Y.Z-beta" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE" -SkipRelease

# Tylko release
.\build-with-signing.ps1 -Version "X.Y.Z" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE" -SkipBeta
```

**Output:**
- `releases-release/` - Velopack release channel
- `releases-beta/` - Velopack beta channel

**Parametry:**
- `-Version` - wersja release bazowa
- `-BetaVersion` - wersja beta; jeśli pusta, skrypt wygeneruje `${Version}-beta`
- `-CertThumbprint` - SHA1 thumbprint certyfikatu z Windows Store
- `-SkipBeta` - pomiń kanał beta
- `-SkipRelease` - pomiń kanał release

---

#### `sign-and-build.ps1`
Interaktywny helper do starego flow z `build-release-2.2.0.ps1`.

**Status:** używaj tylko jeśli świadomie chcesz wejść w stary proces release z legacy ZIP.

**Użycie:**
```powershell
.\sign-and-build.ps1 -ReleaseVersion "2.2.0" -NextBetaVersion "2.3.0-beta"
```

**Co robi:**
1. Sprawdza dostępność `signtool.exe`
2. Lista certyfikatów w Windows Store
3. Pyta o thumbprint (domyślny Certum: `YOUR_CERTIFICATE_THUMBPRINT_HERE`)
4. Pokazuje podsumowanie i pyta o potwierdzenie
5. Uruchamia `build-release-2.2.0.ps1` z właściwymi parametrami

**Parametry:**
- `-ReleaseVersion` - Wersja release (default: 2.2.0)
- `-NextBetaVersion` - Wersja beta (default: 2.3.0-beta)
- `-SkipLegacyZip` - Pomiń legacy ZIP

---

#### `build-release-2.2.0.ps1`
Główny skrypt budowania - 3 formaty w jednym.

**Status:** legacy release script. W obecnym repo krok legacy ZIP zależy od `Updater\Updater.csproj`. Jeśli ten projekt nie istnieje, uruchamiaj tylko z `-SkipLegacyZip` albo użyj `build-with-signing.ps1`.

**Użycie:**
```powershell
# Aktualnie bezpieczniejszy wariant w tym repo
.\build-release-2.2.0.ps1 `
    -ReleaseVersion "X.Y.Z" `
    -NextBetaVersion "A.B.C-beta" `
    -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE" `
    -SkipLegacyZip

# Z Certum (Windows Store)
.\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Z plikiem PFX
.\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\cert.pfx" `
    -CertificatePassword "Password"

# Bez podpisywania
.\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -SkipSigning
```

**Output:**
- `releases-legacy/` - ZIP dla użytkowników z v2.0.1
- `releases-release/` - Velopack release channel (stable)
- `releases-beta/` - Velopack beta channel (testing)

**Parametry:**
- `-ReleaseVersion` - Wersja release (np. "2.2.0")
- `-NextBetaVersion` - Wersja beta (np. "2.3.0-beta")
- `-CertificateThumbprint` - SHA1 thumbprint certyfikatu z Windows Store
- `-CertificatePath` - Ścieżka do pliku PFX
- `-CertificatePassword` - Hasło do pliku PFX
- `-SkipSigning` - Pomiń podpisywanie
- `-SkipLegacyZip` - Pomiń legacy ZIP

---

### Development/Testing

#### `build-dual-channel.ps1`
Szybki build obu kanałów (bez legacy ZIP, bez signing).

**Użycie:**
```powershell
# Oba kanały
.\build-dual-channel.ps1 -Version 2.2.0

# Tylko release
.\build-dual-channel.ps1 -Version 2.2.0 -SkipBeta

# Tylko beta
.\build-dual-channel.ps1 -Version 2.2.0 -SkipRelease

# Custom beta suffix
.\build-dual-channel.ps1 -Version 2.2.0 -BetaSuffix "rc1"
```

**Output:**
- `releases-release/` - Release channel
- `releases-beta/` - Beta channel

---

#### `build-release-velopack.ps1` / `build-velopack-test.ps1`
Starsze skrypty - używaj `build-dual-channel.ps1` zamiast tego.

---

### Deployment

#### `deploy-to-server.ps1` ⭐
Automatyczny upload plików na serwer produkcyjny przez SSH/SCP.

**Użycie:**
```powershell
# Zainstaluj PuTTY tools (jednorazowo)
winget install PuTTY.PuTTY

# Upload wszystkich plików
.\deploy-to-server.ps1 -ReleaseVersion "2.2.0"
# Skrypt zapyta o hasło SSH

# Dry run (test bez uploadu)
.\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -DryRun

# Tylko release channel
.\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -SkipLegacy -SkipBeta

# Tylko legacy
.\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -SkipRelease -SkipBeta
```

**Co robi:**
1. Sprawdza wymagania (pscp, plink)
2. Weryfikuje katalogi z plikami
3. Pyta o hasło SSH (debian@vps-b99a39c3.vps.ovh.net)
4. Testuje połączenie
5. Pokazuje plan i pyta o potwierdzenie
6. Uploaduje pliki do właściwych lokalizacji
7. Weryfikuje poprawność

**Upload destinations:**
- Legacy (backup): `/srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/`
- **Legacy (versions)**: `/srv/synapsekit-boracik/nginx/html/susmodder-versions/SUSModder-X.Y.Z.zip`
- Release: `/srv/synapsekit-boracik/nginx/html/susmodder/releases/release/`
- Beta: `/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/`
- Manifests: `/srv/synapsekit-boracik/nginx/html/susmodder-velopack/`

**Parametry:**
- `-ReleaseVersion` - Wersja release (default: 2.2.0)
- `-Server` - Serwer (default: vps-b99a39c3.vps.ovh.net)
- `-Username` - Użytkownik SSH (default: debian)
- `-SkipLegacy` - Pomiń upload legacy ZIP
- `-SkipRelease` - Pomiń upload release channel
- `-SkipBeta` - Pomiń upload beta channel
- `-DryRun` - Test bez faktycznego uploadu

---

## 🔐 Code Signing

**Obecna konfiguracja:**
- Dostawca: Certum (http://time.certum.pl)
- Metoda: Windows Certificate Store
- Thumbprint: `YOUR_CERTIFICATE_THUMBPRINT_HERE`

**Znajdź certyfikaty:**
```powershell
Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | 
    Where-Object { $_.NotAfter -gt (Get-Date) } |
    Format-Table Thumbprint, Subject, NotAfter
```

**Manualne podpisywanie:**
```powershell
signtool sign /sha1 "YOUR_CERTIFICATE_THUMBPRINT_HERE" `
    /tr http://time.certum.pl `
    /td sha256 /fd sha256 /v "SUSModder.exe"
```

**Szczegóły:** `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`

---

## 📊 Numeracja Wersji (Kernel Style)

**Stable (parzyste drugie cyfry):**
```
2.0.0 → 2.2.0 → 2.4.0 → 2.6.0
```

**Beta (nieparzyste drugie cyfry):**
```
2.1.0-beta → 2.3.0-beta → 2.5.0-beta
```

**Przykład:**
```
2.2.0 (release) → 2.2.1 (bugfix) → 2.3.0-beta → 2.4.0 (release)
```

---

## ✅ Quick Checklist

**Przed release:**
```
[ ] Zainstaluj vpk: dotnet tool install -g vpk
[ ] Sprawdź certyfikat: Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert
[ ] Upewnij się że signtool jest w PATH
```

**Build:**
```powershell
# Zalecane obecnie
.\build-with-signing.ps1 -Version "X.Y.Z" -BetaVersion "A.B.C-beta" -CertThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"
```

**Po build:**
```
[ ] Zweryfikuj podpisy: Get-AuthenticodeSignature releases-*/*/SUSModder.exe
[ ] Sprawdź rozmiary pakietów (~50MB każdy)
[ ] Przetestuj lokalnie
```

**Deploy:**
```powershell
.\deploy-to-server.ps1 -ReleaseVersion "2.2.0"
```

**Po deploy:**
```
[ ] Test API: curl https://susmodder.app/api/releases?channel=release
[ ] Test download: curl -I https://susmodder.app/releases/release/RELEASES
[ ] Test update w aplikacji (v2.0.1 → v2.2.0)
[ ] Monitor logs: ssh debian@vps-b99a39c3.vps.ovh.net 'tail -f /var/log/nginx/access.log'
```

---

## 📚 Więcej Informacji

- **Pełny przewodnik**: `DOC/Updater-Refactoring/STRATEGY_SUMMARY.md`
- **Szczegóły migracji**: `DOC/Updater-Refactoring/RELEASE_220_MIGRATION.md`
- **Code signing**: `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`
- **Kanały**: `DOC/Updater-Refactoring/UPDATE_CHANNELS.md`
