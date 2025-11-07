# Migracja do v2.2.0 - Przewodnik Wydania

## Wprowadzenie

Wersja 2.2.0 wprowadza:
- ✅ Pełne przejście na Velopack
- ✅ System kanałów (release/beta) 
- ✅ Numeracja beta: nieparzyste drugie cyfry (jak kernele Linuxa)
- ✅ Bezpieczna migracja dla użytkowników z v2.0.1

## Strategia Migracji

### Problem
Użytkownicy na v2.0.1 używają **legacy updater** (Updater.exe + ZIP).
Nowy system używa **Velopack** (.nupkg + delta updates).

### Rozwiązanie: Dual Release
```
v2.0.1 (legacy updater)
  │
  ├─→ Legacy update → v2.2.0 ZIP (zawiera Velopack)
  │                     └─→ Kolejne updaty: Velopack
  │
  └─→ Nowe instalacje → v2.2.0 Velopack (bezpośrednio)
```

### Numeracja Wersji (Kernel Style)
- **Stabilne**: `2.2.0`, `2.4.0`, `2.6.0` (parzyste drugie cyfry)
- **Beta**: `2.3.0-beta`, `2.5.0-beta` (nieparzyste drugie cyfry)

**Przykład cyklu:**
```
2.2.0 (release)           ← Stabilna wersja
  ├─→ 2.2.1 (bugfix)      ← Drobne poprawki
  └─→ 2.3.0-beta (beta)   ← Nowe funkcje testowe
       └─→ 2.4.0 (release) ← Nowa stabilna wersja
            └─→ 2.5.0-beta (beta)
```

## Proces Budowania v2.2.0

### Wymagania
1. **.NET 8.0 SDK**
2. **Velopack CLI** (`dotnet tool install -g vpk`)
3. **Certyfikat Code Signing** (opcjonalnie, ale zalecane)
   - Format: PFX/P12
   - Zaufany CA (np. Sectigo, DigiCert)
4. **signtool.exe** (część Windows SDK)

### Instalacja signtool
```powershell
# Pobierz Windows SDK
# https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/

# Lub zainstaluj tylko Build Tools
winget install Microsoft.VisualStudio.2022.BuildTools --silent

# signtool będzie w:
# C:\Program Files (x86)\Windows Kits\10\bin\{version}\x64\signtool.exe

# Dodaj do PATH
$env:Path += ";C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64"
```

### Krok 1: Przygotowanie

```powershell
# Przejdź do katalogu projektu
cd d:\Development\SUSModder

# Upewnij się, że masz czyste środowisko
git status
git pull origin main

# Sprawdź wersję w appsettings.json
Get-Content SUSModder\appsettings.json | Select-String "CurrentVersion"
# Powinno być: "CurrentVersion": "2.2.0"
```

### Krok 2: Build z podpisywaniem

**Obecna konfiguracja SUSModder:**
- Dostawca: Certum
- Certyfikat w Windows Certificate Store
- Thumbprint: `97171de086564a84fa22a72c4260f72ba13096c6`

```powershell
# Metoda 1: Interaktywny helper (REKOMENDOWANE)
.\SKRYPTY\Build\sign-and-build.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta"
# Skrypt zapyta o thumbprint - naciśnij ENTER aby użyć domyślnego Certum

# Metoda 2: Bezpośrednio z thumbprint (Certum)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificateThumbprint "97171de086564a84fa22a72c4260f72ba13096c6"

# Metoda 3: Z plikiem PFX (jeśli używasz innego certyfikatu)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "TwojeHaslo"

# Bez podpisywania (tylko dla testów)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -SkipSigning
```

### Krok 3: Weryfikacja Outputu

**Oczekiwane pliki:**

```
releases-legacy/
├── SUSModder-2.2.0-legacy.zip    (~50-60 MB)

releases-release/
├── SUSModder-2.2.0-release-full.nupkg
├── RELEASES
└── releases.release.json

releases-beta/
├── SUSModder-2.3.0-beta-beta-full.nupkg
├── RELEASES
└── releases.beta.json
```

**Weryfikacja podpisów:**
```powershell
# Sprawdź podpis EXE w legacy ZIP
Expand-Archive releases-legacy\SUSModder-2.2.0-legacy.zip -DestinationPath temp
Get-AuthenticodeSignature temp\SUSModder.exe | Format-List

# Oczekiwane:
# Status: Valid
# SignerCertificate: CN=Your Company Name
# TimeStamperCertificate: CN=Certum Trusted Network CA
```

### Krok 4: Deployment na Serwer

**Automatyczny upload (REKOMENDOWANE):**
```powershell
# Zainstaluj PuTTY tools (jednorazowo)
winget install PuTTY.PuTTY

# Upload wszystkich plików
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"
# Skrypt zapyta o hasło SSH

# Dry run (test bez uploadu)
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -DryRun

# Tylko release channel (pomiń legacy i beta)
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -SkipLegacy -SkipBeta
```

**Co robi skrypt:**
1. Sprawdza wymagania (pscp, plink z PuTTY)
2. Weryfikuje katalogi z plikami release
3. Pyta o hasło SSH (bezpiecznie przez prompt)
4. Testuje połączenie
5. Pokazuje plan deployu i pyta o potwierdzenie
6. Uploaduje pliki:
   - Legacy ZIP → `/srv/.../susmodder/releases/legacy/`
   - Release files → `/srv/.../susmodder/releases/release/`
   - Beta files → `/srv/.../susmodder/releases/beta/`
   - Manifesty JSON → `/srv/.../susmodder-velopack/`
7. Weryfikuje upload

**Manualne upload (alternatywa):**
```bash
# Przez SSH/SCP (Linux/WSL)
scp releases-legacy/* debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/
scp releases-release/* debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/release/
scp releases-beta/* debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/

# Manifesty do osobnego katalogu
scp releases-release/releases.release.json debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder-velopack/
scp releases-beta/releases.beta.json debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder-velopack/

# Legacy ZIP do susmodder-versions/ z wersjonowaną nazwą (ważne!)
scp releases-legacy/SUSModder-*-legacy.zip debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder-versions/SUSModder-2.2.0.zip
```

### Backend Configuration

```javascript
// 1. Legacy endpoint (dla użytkowników z v2.0.1)
app.get('/api/susmodder-current-version', (req, res) => {
    const userVersion = req.query.current;
    
    // Dla starych użytkowników
    if (userVersion === '2.0.1' || userVersion < '2.2.0') {
        return res.json({
            currentVersion: '2.2.0',
            downloadUrl: '/api/download-latest',
            updateType: 'legacy'
        });
    }
    
    // Dla nowych użytkowników
    return res.json({
        currentVersion: '2.2.0',
        downloadUrl: null, // Velopack używa własnego API
        updateType: 'velopack'
    });
});

// 2. Legacy download endpoint
app.get('/api/download-latest', (req, res) => {
    const file = path.join(__dirname, 'releases/legacy/SUSModder-2.2.0-legacy.zip');
    res.download(file);
});

// 3. Velopack manifest endpoint
app.get('/api/releases', async (req, res) => {
    const channel = req.query.channel || 'release';
    
    // Walidacja
    if (channel !== 'release' && channel !== 'beta') {
        return res.status(400).json({ error: 'Invalid channel' });
    }
    
    // Wczytaj manifest
    const manifestPath = path.join(__dirname, `releases/${channel}/releases.${channel}.json`);
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    
    return res.json({
        success: true,
        channel: channel,
        latestVersion: manifest.LatestVersion,
        manifest: manifest,
        downloadBaseUrl: `https://susmodder.app/releases/${channel}`
    });
});
```

### Upload Plików

```bash
# 1. Legacy ZIP (dla użytkowników z v2.0.1)
scp releases-legacy/SUSModder-2.2.0-legacy.zip \
    server:/var/www/susmodder/releases/legacy/

# 2. Release channel
scp releases-release/* \
    server:/var/www/susmodder/releases/release/

# 3. Beta channel
scp releases-beta/* \
    server:/var/www/susmodder/releases/beta/

# 4. Ustaw uprawnienia
ssh server "chmod 644 /var/www/susmodder/releases/*/*.{nupkg,json,zip}"
```

### Struktura na Serwerze

```
/var/www/susmodder/
├── releases/
│   ├── legacy/
│   │   └── SUSModder-2.2.0-legacy.zip
│   ├── release/
│   │   ├── SUSModder-2.2.0-release-full.nupkg
│   │   ├── RELEASES
│   │   └── releases.release.json
│   └── beta/
│       ├── SUSModder-2.3.0-beta-beta-full.nupkg
│       ├── RELEASES
│       └── releases.beta.json
```

## Testowanie

### Test 1: Migracja z v2.0.1 → v2.2.0

```powershell
# 1. Zainstaluj v2.0.1 (stara wersja produkcyjna)
# 2. Uruchom aplikację
# 3. Kliknij "Sprawdź aktualizacje"
```

**Oczekiwany rezultat:**
- ✅ Dialog: "Dostępna aktualizacja do wersji 2.2.0"
- ✅ Pobieranie ZIP (~50MB)
- ✅ Uruchomienie Updater.exe
- ✅ Restart aplikacji
- ✅ Wersja w UI: 2.2.0
- ✅ Plik `Update.exe` istnieje w katalogu nadrzędnym (Velopack)

### Test 2: Aktualizacja Velopack (v2.2.0 → przyszła wersja)

```powershell
# Symulacja kolejnej wersji
# 1. Zbuduj fake v2.2.1
.\SKRYPTY\Build\build-dual-channel.ps1 -Version 2.2.1

# 2. Upload do testowego serwera
# 3. W aplikacji v2.2.0 kliknij "Sprawdź aktualizacje"
```

**Oczekiwany rezultat:**
- ✅ Dialog: "Dostępna aktualizacja do wersji 2.2.1"
- ✅ Pobieranie delta package (~5-10MB)
- ✅ Automatyczny restart
- ✅ Wersja w UI: 2.2.1
- ✅ Brak Updater.exe (tylko Velopack)

### Test 3: Przełączanie Kanałów

```powershell
# 1. W aplikacji v2.2.0 (release)
# 2. Otwórz Ustawienia → Zaawansowane
# 3. Zmień "Kanał aktualizacji" na "Beta"
# 4. Zapisz
# 5. Kliknij "Sprawdź aktualizacje"
```

**Oczekiwany rezultat:**
- ✅ Dialog: "Dostępna aktualizacja do wersji 2.3.0-beta"
- ✅ Pobieranie full package (~50MB)
- ✅ Restart
- ✅ Wersja w UI: 2.3.0-beta

### Test 4: Nowa Instalacja (Velopack)

```powershell
# 1. Pobierz Setup.exe z releases-release/
# (Velopack generuje to automatycznie)

# 2. Uruchom Setup.exe
# 3. Wybierz katalog instalacji
# 4. Poczekaj na instalację
```

**Oczekiwany rezultat:**
- ✅ Aplikacja zainstalowana w `{InstallDir}\current\SUSModder.exe`
- ✅ `Update.exe` w `{InstallDir}\`
- ✅ Start menu shortcut utworzony
- ✅ Automatyczne uruchomienie po instalacji

## Podpisywanie Cyfrowe

### Dlaczego Podpisywanie?

1. **Zaufanie użytkowników**: Windows nie pokazuje ostrzeżenia "Unknown publisher"
2. **SmartScreen**: Mniejsza szansa na blokowanie
3. **Antywirus**: Lepszy reputation score
4. **Profesjonalizm**: Pokazuje autentyczność oprogramowania

### Pozyskanie Certyfikatu

**Rekomendowane CA:**
- **Sectigo (Comodo)**: ~$75-150/rok
- **DigiCert**: ~$200-400/rok
- **GlobalSign**: ~$150-300/rok

**Proces:**
1. Kup certyfikat Code Signing
2. Zweryfikuj firmę/tożsamość (OV - Organization Validation)
3. Pobierz certyfikat w formacie PFX/P12
4. Zabezpiecz hasłem

### Konfiguracja CI/CD (GitHub Actions)

```yaml
name: Release Build

on:
  push:
    tags:
      - 'v*'

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    
    - name: Install Velopack CLI
      run: dotnet tool install -g vpk
    
    - name: Decode certificate
      run: |
        $certBytes = [Convert]::FromBase64String("${{ secrets.CODE_SIGN_CERT_BASE64 }}")
        [IO.File]::WriteAllBytes("cert.pfx", $certBytes)
    
    - name: Build and sign
      run: |
        .\SKRYPTY\Build\build-release-2.2.0.ps1 `
          -ReleaseVersion "${{ github.ref_name }}" `
          -NextBetaVersion "${{ github.ref_name }}-beta" `
          -CertificatePath "cert.pfx" `
          -CertificatePassword "${{ secrets.CODE_SIGN_CERT_PASSWORD }}"
    
    - name: Upload artifacts
      uses: actions/upload-artifact@v3
      with:
        name: releases
        path: |
          releases-legacy/
          releases-release/
          releases-beta/
```

**Sekrety w GitHub:**
```bash
# Konwertuj certyfikat do Base64
$bytes = [IO.File]::ReadAllBytes("susmodder-code-signing.pfx")
$base64 = [Convert]::ToBase64String($bytes)
Write-Output $base64

# Dodaj w GitHub Settings → Secrets:
# CODE_SIGN_CERT_BASE64 = <base64 string>
# CODE_SIGN_CERT_PASSWORD = <hasło do certyfikatu>
```

## Monitoring Migracji

### Telemetry Endpoints

```javascript
// Track updater type
app.post('/api/telemetry/app-start', (req, res) => {
    const { version, updaterType, userId } = req.body;
    
    // Zapisz do bazy
    db.insertTelemetry({
        event: 'app_start',
        version: version,
        updater: updaterType, // 'legacy' lub 'velopack'
        userId: userId,
        timestamp: new Date()
    });
    
    res.json({ success: true });
});
```

### Metryki Sukcesu

**Po 1 tygodniu:**
- ✅ >60% użytkowników na v2.2.0+
- ✅ <5% błędów aktualizacji
- ✅ <10% zgłoszeń problemów

**Po 2 tygodniach:**
- ✅ >80% użytkowników na v2.2.0+
- ✅ Brak krytycznych bugów
- ✅ Pozytywny feedback

**Po 4 tygodniach:**
- ✅ >95% użytkowników na v2.2.0+
- ✅ Można usunąć legacy endpoint (opcjonalnie zostawić dla grace period)

### Zapytania SQL (przykłady)

```sql
-- Dystrybucja wersji
SELECT
    version,
    updater_type,
    COUNT(*) as user_count,
    COUNT(*) * 100.0 / SUM(COUNT(*)) OVER () as percentage
FROM telemetry
WHERE event = 'app_start'
  AND timestamp >= NOW() - INTERVAL '7 days'
GROUP BY version, updater_type
ORDER BY version DESC;

-- Wskaźnik sukcesu aktualizacji
SELECT
    DATE(timestamp) as date,
    COUNT(*) FILTER (WHERE event = 'update_success') as successes,
    COUNT(*) FILTER (WHERE event = 'update_failed') as failures,
    ROUND(
        COUNT(*) FILTER (WHERE event = 'update_success') * 100.0 / 
        NULLIF(COUNT(*), 0), 
        2
    ) as success_rate
FROM telemetry
WHERE event IN ('update_success', 'update_failed')
  AND timestamp >= NOW() - INTERVAL '30 days'
GROUP BY DATE(timestamp)
ORDER BY date DESC;
```

## Rollback Plan

### Scenariusz: Krytyczny Bug w v2.2.0

**Natychmiastowe działania:**

1. **Wycofaj manifest Velopack:**
   ```bash
   ssh server "cp /var/www/susmodder/releases/release/releases.release.json.backup \
               /var/www/susmodder/releases/release/releases.release.json"
   ```

2. **Przywróć starą wersję w API:**
   ```javascript
   app.get('/api/susmodder-current-version', (req, res) => {
       return res.json({
           currentVersion: '2.0.1', // Rollback
           downloadUrl: '/api/download-latest',
           updateType: 'legacy'
       });
   });
   ```

3. **Komunikat dla użytkowników:**
   ```
   W aplikacji: "Wykryto problem z najnowszą wersją. 
   Pracujemy nad poprawką. Zalecamy pozostanie na obecnej wersji."
   ```

4. **Hotfix release (v2.2.1):**
   - Napraw bug
   - Zbuduj v2.2.1
   - Przetestuj dokładnie
   - Deploy jako emergency update

## FAQ

**Q: Czy mogę pominąć legacy ZIP i wydać tylko Velopack?**
A: Nie zalecane. Użytkownicy na v2.0.1 nie będą mogli zaktualizować się.

**Q: Co jeśli użytkownik zignoruje update do v2.2.0?**
A: Będzie mógł zaktualizować się później. Legacy endpoint powinien być aktywny przez 3-6 miesięcy.

**Q: Czy delta updates działają z legacy ZIP?**
A: Nie. Pierwsza aktualizacja (v2.0.1 → v2.2.0) jest pełnym pakietem. Delta updates zaczynają działać od v2.2.0 → kolejne wersje.

**Q: Jak długo trzymać legacy endpoint?**
A: Minimum 3 miesiące. Po tym okresie <5% użytkowników powinno być na starych wersjach.

**Q: Czy mogę zmienić numerację beta?**
A: Tak, ale utrzymuj spójność. Nieparzyste = beta, parzyste = release.

**Q: Co z podpisywaniem dla beta?**
A: Beta również powinny być podpisane tym samym certyfikatem.

## Checklist Wydania

### Przed Build
- [ ] Zaktualizuj `CurrentVersion` w `appsettings.json` do `2.2.0`
- [ ] Zaktualizuj `CHANGELOG.md`
- [ ] Sprawdź, czy wszystkie testy przechodzą
- [ ] Przygotuj certyfikat code signing

### Build
- [ ] Uruchom `build-release-2.2.0.ps1` z podpisywaniem
- [ ] Zweryfikuj podpisy wszystkich plików .exe
- [ ] Sprawdź rozmiary pakietów (legacy ~50MB, Velopack ~50MB)
- [ ] Przetestuj instalację z legacy ZIP lokalnie
- [ ] Przetestuj instalację z Velopack Setup.exe lokalnie

### Backend
- [ ] Zaktualizuj endpoint `/api/susmodder-current-version`
- [ ] Zaktualizuj endpoint `/api/releases`
- [ ] Upload plików na serwer
- [ ] Sprawdź dostępność wszystkich endpointów
- [ ] Przetestuj odpowiedzi API (curl/Postman)

### Testy
- [ ] Test migracji: v2.0.1 → v2.2.0 (legacy)
- [ ] Test Velopack update: v2.2.0 → fake v2.2.1
- [ ] Test przełączania kanałów: release ↔ beta
- [ ] Test nowej instalacji (Setup.exe)
- [ ] Test na czystej maszynie Windows 10/11

### Deployment
- [ ] Deploy backend na produkcję
- [ ] Upload wszystkich release plików
- [ ] Ogłoszenie na Discordzie/social media
- [ ] Monitoruj logi serwera przez pierwsze 24h
- [ ] Monitoruj telemetrię przez pierwszy tydzień

### Post-Release
- [ ] Zbierz feedback od użytkowników
- [ ] Monitoruj błędy w telemetrii
- [ ] Przygotuj hotfix jeśli potrzebny (v2.2.1)
- [ ] Planuj następny cycle (v2.3.0-beta)

## Kontakt w Razie Problemów

**Discord:** [Link do serwera SUSModder]
**Email:** support@susmodder.app
**GitHub Issues:** https://github.com/susmodder/susmodder/issues

---

**Data utworzenia:** 2025-11-06
**Wersja dokumentu:** 1.0
**Autor:** SUSModder Team
