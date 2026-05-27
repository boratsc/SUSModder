# Przewodnik Publikacji Wersji SUSModder (Velopack)

**Data utworzenia:** 2025-11-13
**Wersja dokumentu:** 1.0
**Autor:** Bartosz Gradzik / AI Assistant

---

## 📋 Spis Treści

1. [Wymagania](#wymagania)
2. [Przegląd Procesu](#przegląd-procesu)
3. [Krok 1: Aktualizacja version.json](#krok-1-aktualizacja-versionjson)
4. [Krok 2: Build i Pakowanie Release Channel](#krok-2-build-i-pakowanie-release-channel)
5. [Krok 3: Build i Pakowanie Beta Channel](#krok-3-build-i-pakowanie-beta-channel)
6. [Krok 4: Upload na Serwer](#krok-4-upload-na-serwer)
7. [Krok 5: Weryfikacja](#krok-5-weryfikacja)
8. [Checklist Publikacji](#checklist-publikacji)
9. [Troubleshooting](#troubleshooting)

---

## Wymagania

### Narzędzia
- ✅ .NET 8 SDK
- ✅ Velopack CLI (`vpk`)
- ✅ signtool (Windows SDK)
- ✅ PuTTY (pscp, plink)
- ✅ Certyfikat Code Signing w Windows Certificate Store

### Weryfikacja Środowiska
```powershell
# Sprawdź dostępność narzędzi
dotnet --version          # Powinno zwrócić 8.x
vpk --version             # Powinno zwrócić 0.0.1298+
where signtool            # Powinno pokazać ścieżkę
where pscp                # Powinno pokazać ścieżkę

# Sprawdź certyfikat
Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.NotAfter -gt (Get-Date) } |
    Format-Table Thumbprint, Subject, NotAfter -AutoSize
```

### Dane Konfiguracyjne
Przygotuj:
- **Thumbprint certyfikatu** (SHA1, 40 znaków hex)
- **SSH credentials** (user@host, hasło)
- **Numer wersji release** (np. 2.2.4)
- **Numer wersji beta** (np. 2.3.12-beta)

---

## Przegląd Procesu

```
┌─────────────────────────────────────────────────────────────┐
│ FAZA 1: Przygotowanie                                       │
│ ├─ Aktualizacja version.json                               │
│ └─ Wyczyść katalogi publish/releases                       │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 2: Build Release Channel (2.2.4)                       │
│ ├─ dotnet publish (PublishSingleFile=false)                │
│ ├─ vpk pack --channel release --signTemplate               │
│ │  ├─ Podpisuje 42 pliki .exe                              │
│ │  ├─ Tworzy deltę 2.2.3 → 2.2.4 (AUTOMATYCZNIE!)          │
│ │  └─ Podpisuje Setup.exe                                  │
│ └─ Kopiuj RELEASES-release → RELEASES                      │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 3: Build Beta Channel (2.3.12-beta)                    │
│ ├─ dotnet publish (PublishSingleFile=false)                │
│ ├─ vpk pack --channel beta --signTemplate                  │
│ │  ├─ Podpisuje 42 pliki .exe                              │
│ │  └─ Podpisuje Setup.exe                                  │
│ └─ Kopiuj RELEASES-beta → RELEASES                         │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 4: Upload na Serwer SSH                                │
│ ├─ /releases/release/                                       │
│ │  ├─ RELEASES                                             │
│ │  ├─ releases.release.json                                │
│ │  ├─ SUSModder-X.Y.Z-release-full.nupkg                   │
│ │  ├─ SUSModder-X.Y.Z-release-delta.nupkg                  │
│ │  ├─ SUSModder-release-Setup.exe                          │
│ │  └─ SUSModder-release-Portable.zip                       │
│ ├─ /releases/beta/                                          │
│ │  ├─ RELEASES                                             │
│ │  ├─ releases.beta.json                                   │
│ │  ├─ SUSModder-X.Y.Z-beta-beta-full.nupkg                 │
│ │  ├─ SUSModder-beta-Setup.exe                             │
│ │  └─ SUSModder-beta-Portable.zip                          │
│ └─ /susmodder-velopack/ (CRITICAL!)                        │
│    ├─ releases.release.json                                │
│    └─ releases.beta.json                                   │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 5: Weryfikacja                                         │
│ ├─ Sprawdź API: /api/releases?channel=release              │
│ ├─ Sprawdź API: /api/releases?channel=beta                 │
│ └─ Sprawdź dostępność plików przez HTTP                    │
└─────────────────────────────────────────────────────────────┘
```

**Czas trwania:** ~15-20 minut (w zależności od prędkości uploadu)

---

## Krok 1: Aktualizacja version.json

**Lokalizacja:** `SUSModder/version.json`

### 1.1 Edytuj Plik
```json
{
  "buildNumber": "",
  "currentVersion": "2.2.4",
  "lastUpdateDate": "2025-11-13"
}
```

**⚠️ WAŻNE:**
- `currentVersion` - numer wersji release (nie beta!)
- `lastUpdateDate` - dzisiejsza data w formacie YYYY-MM-DD
- Ten plik jest kopiowany do buildu i odczytywany przez aplikację

---

## Krok 2: Build i Pakowanie Release Channel

### 2.1 Przygotowanie Środowiska
```powershell
cd D:\Development\SUSModder

# Wyczyść poprzednie buildy
if (Test-Path 'publish-velopack-release') {
    Remove-Item 'publish-velopack-release' -Recurse -Force
}
if (Test-Path 'releases-release') {
    Remove-Item 'releases-release' -Recurse -Force
}
New-Item -ItemType Directory -Path 'releases-release' -Force | Out-Null
```

### 2.2 Build Aplikacji
```powershell
dotnet publish "SUSModder\SUSModder.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o "publish-velopack-release" `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false
```

**⚠️ UWAGA:** `-p:PublishSingleFile=false` jest wymagane przez Velopack (delta updates)

### 2.3 Pakowanie z Velopack + Podpisywanie
```powershell
# Podstawowa komenda (zastąp YOUR_THUMBPRINT własnym thumbprintem)
vpk pack `
    --packId SUSModder `
    --packVersion 2.2.4 `
    --packDir "publish-velopack-release" `
    --outputDir "releases-release" `
    --channel release `
    --packTitle "SUSModder" `
    --packAuthors "SUSModder Team" `
    --icon "SUSModder\Assets\icon.ico" `
    --splashImage "SUSModder\Assets\splashscreen.jpg" `
    --signTemplate "signtool sign /sha1 YOUR_THUMBPRINT /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"
```

**Co robi `--signTemplate`:**
- Podpisuje wszystkie 42 pliki .exe w pakiecie
- Podpisuje Update.exe (część Velopack)
- Podpisuje Setup.exe (instalator)

**Co robi Velopack automatycznie:**
- Wykrywa poprzednią wersję (2.2.3) w `releases-release/`
- Tworzy plik delta: `SUSModder-2.2.4-release-delta.nupkg` (~2.5 MB zamiast ~52 MB!)
- Generuje manifesty: `releases.release.json`, `RELEASES-release`

**Przykładowy output:**
```
[INF] 42 file(s) will be signed, 209 will be skipped.
[INF] Code-signed 42/42 files
[INF] Building delta 2.2.3 -> 2.2.4
[INF] Delta processed 0255 files. 0222 patched, 0033 unchanged, 0002 new, 0002 removed
[INF] Finished in 00:00:52
```

### 2.4 Kopiuj Plik RELEASES
```powershell
cd releases-release
Copy-Item 'RELEASES-release' -Destination 'RELEASES' -Force
```

**⚠️ KRYTYCZNE:** Plik `RELEASES` (bez sufixu) musi istnieć! Velopack wymaga tego pliku.

### 2.5 Weryfikacja Wygenerowanych Plików
```powershell
Get-ChildItem releases-release | Select-Object Name, @{Name='Size';Expression={'{0:N2} MB' -f ($_.Length/1MB)}}
```

**Powinny być:**
- ✅ `RELEASES` (bez sufixu)
- ✅ `RELEASES-release` (backup)
- ✅ `releases.release.json`
- ✅ `assets.release.json`
- ✅ `SUSModder-2.2.4-release-full.nupkg` (~52 MB)
- ✅ `SUSModder-2.2.4-release-delta.nupkg` (~2.5 MB)
- ✅ `SUSModder-2.2.3-release-full.nupkg` (poprzednia wersja)
- ✅ `SUSModder-release-Setup.exe` (~55 MB, PODPISANY)
- ✅ `SUSModder-release-Portable.zip` (~52 MB)

---

## Krok 3: Build i Pakowanie Beta Channel

### 3.1 Przygotowanie Środowiska
```powershell
cd D:\Development\SUSModder

# Wyczyść poprzednie buildy
if (Test-Path 'publish-velopack-beta') {
    Remove-Item 'publish-velopack-beta' -Recurse -Force
}
if (Test-Path 'releases-beta') {
    Remove-Item 'releases-beta' -Recurse -Force
}
New-Item -ItemType Directory -Path 'releases-beta' -Force | Out-Null
```

### 3.2 Build Aplikacji
```powershell
dotnet publish "SUSModder\SUSModder.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o "publish-velopack-beta" `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false
```

### 3.3 Pakowanie z Velopack + Podpisywanie
```powershell
vpk pack `
    --packId SUSModder `
    --packVersion 2.3.12-beta `
    --packDir "publish-velopack-beta" `
    --outputDir "releases-beta" `
    --channel beta `
    --packTitle "SUSModder Beta" `
    --packAuthors "SUSModder Team" `
    --icon "SUSModder\Assets\icon.ico" `
    --splashImage "SUSModder\Assets\splashscreen.jpg" `
    --signTemplate "signtool sign /sha1 YOUR_THUMBPRINT /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"
```

### 3.4 Kopiuj Plik RELEASES
```powershell
cd releases-beta
Copy-Item 'RELEASES-beta' -Destination 'RELEASES' -Force
```

### 3.5 Weryfikacja Wygenerowanych Plików
```powershell
Get-ChildItem releases-beta | Select-Object Name, @{Name='Size';Expression={'{0:N2} MB' -f ($_.Length/1MB)}}
```

**Powinny być:**
- ✅ `RELEASES` (bez sufixu)
- ✅ `RELEASES-beta` (backup)
- ✅ `releases.beta.json`
- ✅ `assets.beta.json`
- ✅ `SUSModder-2.3.12-beta-beta-full.nupkg` (~52 MB)
- ✅ `SUSModder-beta-Setup.exe` (~55 MB, PODPISANY)
- ✅ `SUSModder-beta-Portable.zip` (~52 MB)

---

## Krok 4: Upload na Serwer

### 4.1 Konfiguracja
```powershell
$sshPassword = "YOUR_SSH_PASSWORD"
$sshHost = "user@your-server.com"
$basePath = "/srv/your-path/susmodder"
```

### 4.2 Upload Release Channel
```powershell
cd D:\Development\SUSModder\releases-release

# 1. RELEASES
pscp -pw "$sshPassword" RELEASES ${sshHost}:${basePath}/releases/release/

# 2. JSON manifest do /releases/release/
pscp -pw "$sshPassword" releases.release.json ${sshHost}:${basePath}/releases/release/

# 3. Full package
pscp -pw "$sshPassword" "SUSModder-2.2.4-release-full.nupkg" ${sshHost}:${basePath}/releases/release/

# 4. Delta package (WAŻNE!)
pscp -pw "$sshPassword" "SUSModder-2.2.4-release-delta.nupkg" ${sshHost}:${basePath}/releases/release/

# 5. Setup.exe (podpisany)
pscp -pw "$sshPassword" "SUSModder-release-Setup.exe" ${sshHost}:${basePath}/releases/release/

# 6. Portable.zip
pscp -pw "$sshPassword" "SUSModder-release-Portable.zip" ${sshHost}:${basePath}/releases/release/
```

### 4.3 Upload Beta Channel
```powershell
cd D:\Development\SUSModder\releases-beta

# 1. RELEASES
pscp -pw "$sshPassword" RELEASES ${sshHost}:${basePath}/releases/beta/

# 2. JSON manifest do /releases/beta/
pscp -pw "$sshPassword" releases.beta.json ${sshHost}:${basePath}/releases/beta/

# 3. Full package
pscp -pw "$sshPassword" "SUSModder-2.3.12-beta-beta-full.nupkg" ${sshHost}:${basePath}/releases/beta/

# 4. Setup.exe (podpisany)
pscp -pw "$sshPassword" "SUSModder-beta-Setup.exe" ${sshHost}:${basePath}/releases/beta/

# 5. Portable.zip
pscp -pw "$sshPassword" "SUSModder-beta-Portable.zip" ${sshHost}:${basePath}/releases/beta/
```

### 4.4 Upload Manifestów do /susmodder-velopack/ ⚠️ CRITICAL!
```powershell
# Ten krok jest ABSOLUTNIE KLUCZOWY - API czyta z tego katalogu!

cd D:\Development\SUSModder\releases-release
pscp -pw "$sshPassword" releases.release.json ${sshHost}:/srv/your-path/susmodder-velopack/

cd D:\Development\SUSModder\releases-beta
pscp -pw "$sshPassword" releases.beta.json ${sshHost}:/srv/your-path/susmodder-velopack/
```

**⚠️ DLACZEGO TO JEST KRYTYCZNE:**
API `/api/releases` czyta manifesty z `/susmodder-velopack/`, NIE z `/releases/`!
Bez tego kroku API zwróci starą wersję, mimo że pliki są na serwerze.

---

## Krok 5: Weryfikacja

### 5.1 Sprawdź API Release Channel
```powershell
Invoke-RestMethod -Uri 'https://susmodder.app/api/releases?channel=release' | ConvertTo-Json -Depth 5
```

**Oczekiwany output:**
```json
{
  "success": true,
  "channel": "release",
  "latestVersion": "2.2.4",
  "manifest": {
    "LatestVersion": "2.2.4",
    "Releases": [
      {
        "Version": "2.2.4",
        "File": "SUSModder-2.2.4-release-full.nupkg",
        "SHA256": "...",
        "Size": 54229240
      },
      {
        "Version": "2.2.4",
        "File": "SUSModder-2.2.4-release-delta.nupkg",
        "SHA256": "...",
        "Size": 2601391
      },
      {
        "Version": "2.2.3",
        "File": "SUSModder-2.2.3-release-full.nupkg",
        ...
      }
    ]
  }
}
```

### 5.2 Sprawdź API Beta Channel
```powershell
Invoke-RestMethod -Uri 'https://susmodder.app/api/releases?channel=beta' | ConvertTo-Json -Depth 5
```

**Oczekiwany output:**
```json
{
  "success": true,
  "channel": "beta",
  "latestVersion": "2.3.12-beta",
  "manifest": {
    "LatestVersion": "2.3.12-beta",
    "Releases": [
      {
        "Version": "2.3.12-beta",
        "File": "SUSModder-2.3.12-beta-beta-full.nupkg",
        "SHA256": "...",
        "Size": 54229014
      }
    ]
  }
}
```

### 5.3 Sprawdź Dostępność Plików HTTP
```powershell
$urls = @(
    "https://susmodder.app/releases/release/SUSModder-2.2.4-release-full.nupkg",
    "https://susmodder.app/releases/release/SUSModder-2.2.4-release-delta.nupkg",
    "https://susmodder.app/releases/release/SUSModder-release-Setup.exe",
    "https://susmodder.app/releases/release/RELEASES",
    "https://susmodder.app/releases/beta/SUSModder-2.3.12-beta-beta-full.nupkg",
    "https://susmodder.app/releases/beta/SUSModder-beta-Setup.exe",
    "https://susmodder.app/releases/beta/RELEASES"
)

foreach ($url in $urls) {
    try {
        $response = Invoke-WebRequest -Uri $url -Method Head -ErrorAction Stop
        $fileName = Split-Path $url -Leaf
        Write-Host "✅ OK: $fileName" -ForegroundColor Green
    } catch {
        Write-Host "❌ ERROR: $url" -ForegroundColor Red
    }
}
```

### 5.4 Test Aktualizacji (Opcjonalny)
```powershell
# Zainstaluj release channel i sprawdź czy aktualizacja działa
# Setup.exe zainstaluje aplikację w C:\Users\<user>\AppData\Local\SUSModder\

# Uruchom aplikację - powinna wykryć i pobrać aktualizację 2.2.4
# Sprawdź logi aplikacji dla komunikatów Velopack
```

---

## Checklist Publikacji

### Przed Buildem
- [ ] Zaktualizowano `SUSModder/version.json` do nowej wersji
- [ ] Data w `lastUpdateDate` jest dzisiejsza
- [ ] Sprawdzono dostępność signtool (`where signtool`)
- [ ] Sprawdzono thumbprint certyfikatu
- [ ] Sprawdzono dane SSH

### Po Buildzie Release Channel (releases-release/)
- [ ] Istnieje plik `RELEASES` (bez sufixu!)
- [ ] Istnieje `SUSModder-X.Y.Z-release-full.nupkg` (~52 MB)
- [ ] Istnieje `SUSModder-X.Y.Z-release-delta.nupkg` (~2-5 MB)
- [ ] Istnieje `SUSModder-release-Setup.exe` (podpisany!)
- [ ] Setup.exe ma podpis cyfrowy (Właściwości → Podpisy cyfrowe)

### Po Buildzie Beta Channel (releases-beta/)
- [ ] Istnieje plik `RELEASES` (bez sufixu!)
- [ ] Istnieje `SUSModder-X.Y.Z-beta-beta-full.nupkg` (~52 MB)
- [ ] Istnieje `SUSModder-beta-Setup.exe` (podpisany!)
- [ ] Setup.exe ma podpis cyfrowy

### Po Uploadzie
- [ ] `RELEASES` wgrany do `/releases/release/`
- [ ] `releases.release.json` wgrany do `/releases/release/`
- [ ] `.nupkg` (full + delta) wgrane do `/releases/release/`
- [ ] `Setup.exe` i `Portable.zip` wgrane do `/releases/release/`
- [ ] `RELEASES` wgrany do `/releases/beta/`
- [ ] `releases.beta.json` wgrany do `/releases/beta/`
- [ ] `.nupkg` wgrany do `/releases/beta/`
- [ ] `Setup.exe` i `Portable.zip` wgrane do `/releases/beta/`
- [ ] **CRITICAL:** `releases.release.json` wgrany do `/susmodder-velopack/`
- [ ] **CRITICAL:** `releases.beta.json` wgrany do `/susmodder-velopack/`

### Po Weryfikacji
- [ ] API zwraca nową wersję release (`/api/releases?channel=release`)
- [ ] API zwraca nową wersję beta (`/api/releases?channel=beta`)
- [ ] Wszystkie pliki dostępne przez HTTP (status 200)
- [ ] SHA256 checksums się zgadzają
- [ ] Delta package jest wykrywana przez API

### Po Publikacji
- [ ] Przetestowano aktualizację z poprzedniej wersji (release)
- [ ] Przetestowano instalację Setup.exe (release)
- [ ] Przetestowano instalację Setup.exe (beta)
- [ ] Sprawdzono czy aplikacja uruchamia się poprawnie
- [ ] Sprawdzono logi aplikacji dla błędów Velopack

---

## Troubleshooting

### Problem: API zwraca starą wersję
**Objawy:**
```json
{
  "latestVersion": "2.2.3"  // Stara wersja!
}
```

**Przyczyny:**
1. JSON nie wgrany do `/susmodder-velopack/`
2. Brak pliku `RELEASES` (bez sufixu)
3. Cache API (poczekaj 30 sekund)

**Rozwiązanie:**
```powershell
# Sprawdź czy JSON jest w lokalizacji API
plink -pw "$sshPassword" ${sshHost} "cat /srv/your-path/susmodder-velopack/releases.release.json"

# Sprawdź czy RELEASES istnieje
plink -pw "$sshPassword" ${sshHost} "ls -l /srv/your-path/susmodder/releases/release/RELEASES"

# Jeśli brak - wgraj ponownie
pscp -pw "$sshPassword" releases.release.json ${sshHost}:/srv/your-path/susmodder-velopack/
```

### Problem: signtool error "No file digest algorithm specified"
**Objaw:**
```
SignTool Error: No file digest algorithm specified. Please specify the digest algorithm with the /fd flag.
```

**Rozwiązanie:**
Użyj prawidłowej kolejności parametrów w `--signTemplate`:
```powershell
--signTemplate "signtool sign /sha1 THUMBPRINT /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"
```

Parametr `/fd sha256` MUSI być przed `{{file}}`!

### Problem: Delta package nie jest tworzony
**Objaw:**
Brak pliku `SUSModder-X.Y.Z-release-delta.nupkg`

**Przyczyny:**
1. Brak poprzedniej wersji w `releases-release/`
2. Poprzednia wersja ma inny channel

**Rozwiązanie:**
Upewnij się, że poprzednia wersja (np. 2.2.3) jest w katalogu `releases-release/`:
```powershell
Get-ChildItem releases-release -Filter "*-release-full.nupkg"
# Powinno pokazać SUSModder-2.2.3-release-full.nupkg
```

Velopack automatycznie wykryje poprzednią wersję i stworzy deltę.

### Problem: Setup.exe nie jest podpisany
**Objaw:**
Właściwości → Podpisy cyfrowe: brak

**Przyczyna:**
Brak parametru `--signTemplate` w `vpk pack`

**Rozwiązanie:**
Zawsze używaj `--signTemplate` z pełną składnią signtool:
```powershell
vpk pack ... --signTemplate "signtool sign /sha1 THUMBPRINT /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"
```

### Problem: pscp connection refused
**Objaw:**
```
Network error: Connection refused
```

**Przyczyny:**
1. Nieprawidłowy host/port
2. Firewall blokuje połączenie SSH
3. Serwer SSH nie działa

**Rozwiązanie:**
```powershell
# Test połączenia SSH
plink -pw "$sshPassword" ${sshHost} "echo 'Connection OK'"

# Jeśli nie działa - sprawdź serwer
# ssh user@host (lub użyj PuTTY GUI)
```

### Problem: Pliki wgrane ale HTTP 404
**Objaw:**
```
❌ ERROR: https://susmodder.app/releases/release/SUSModder-2.2.4-release-full.nupkg
```

**Przyczyny:**
1. Nieprawidłowa ścieżka na serwerze
2. Uprawnienia plików (brak read)
3. Nginx nie serwuje katalogu

**Rozwiązanie:**
```powershell
# Sprawdź czy plik istnieje
plink -pw "$sshPassword" ${sshHost} "ls -lh /srv/your-path/susmodder/releases/release/SUSModder-2.2.4-release-full.nupkg"

# Sprawdź uprawnienia (powinno być -rw-r--r--)
plink -pw "$sshPassword" ${sshHost} "chmod 644 /srv/your-path/susmodder/releases/release/*.nupkg"
```

### Problem: Aplikacja nie wykrywa aktualizacji
**Objawy:**
Aplikacja pokazuje "Brak aktualizacji" mimo nowej wersji w API

**Przyczyny:**
1. `version.json` w aplikacji ma wyższą lub równą wersję
2. Cache aplikacji
3. Błąd w logice `VelopackUpdateService`

**Rozwiązanie:**
1. Sprawdź `version.json` w zainstalowanej aplikacji
2. Sprawdź logi aplikacji (Console Output)
3. Sprawdź czy `VelopackUpdateService.IsInstalledAsync()` zwraca `true`

---

## Automatyzacja (Opcjonalna)

### Skrypt PowerShell
Możesz stworzyć zautomatyzowany skrypt:

```powershell
# publish-release.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$ReleaseVersion,

    [Parameter(Mandatory=$true)]
    [string]$BetaVersion,

    [Parameter(Mandatory=$true)]
    [string]$CertThumbprint,

    [Parameter(Mandatory=$true)]
    [string]$SshPassword
)

# Import funkcji z tego przewodnika
# ... (kod automatyzujący kroki 1-4)
```

Użycie:
```powershell
.\publish-release.ps1 -ReleaseVersion "2.2.5" -BetaVersion "2.3.13-beta" -CertThumbprint "ABC123..." -SshPassword "..."
```

---

## Historia Zmian

| Data | Wersja | Zmiany |
|------|--------|--------|
| 2025-11-13 | 1.0 | Utworzono na podstawie udanego deploymentu v2.2.4 + v2.3.12-beta |

---

**Autor:** Bartosz Gradzik / AI Assistant
**Ostatnia aktualizacja:** 2025-11-13
**Kontakt:** [GitHub Issues](https://github.com/your-repo/issues)
