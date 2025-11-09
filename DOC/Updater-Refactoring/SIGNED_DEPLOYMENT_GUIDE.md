# Przewodnik: Deploy Wersji Beta z Pełnym Podpisywaniem

## Quick Reference - Krok po kroku

### KROK 1: Aktualizuj version.json (ZAWSZE PIERWSZY!)
```powershell
# Edytuj: D:\Development\SUSModder\SUSModder\version.json
# Zmień currentVersion na kolejną wersję beta (np. 2.3.12-beta -> 2.3.13-beta)
# Zmień lastUpdateDate na dzisiejszą datę
```

Przykład:
```json
{
  "buildNumber": "",
  "currentVersion": "2.3.13-beta",
  "lastUpdateDate": "2025-11-09"
}
```

### KROK 2: Build aplikacji
```powershell
cd D:\Development\SUSModder

# Wyczyść poprzednie buildy
if (Test-Path 'publish-beta') { Remove-Item 'publish-beta' -Recurse -Force }
if (Test-Path 'releases-beta') { Remove-Item 'releases-beta' -Recurse -Force }
New-Item -ItemType Directory -Path 'releases-beta' -Force

# Zbuduj aplikację
dotnet publish "SUSModder\SUSModder.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o "publish-beta" `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false
```

### KROK 3: Podpisz wszystkie pliki .exe PRZED pakowaniem
```powershell
cd publish-beta

# Utwórz skrypt podpisywania
@'
@echo off
echo Signing SUSModder.exe...
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "SUSModder.exe"

echo.
echo Signing 7z.exe...
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "tools\7z.exe"

echo.
echo Signing createdump.exe...
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "createdump.exe"

echo.
echo All files signed successfully!
'@ | Out-File -FilePath "sign-all.bat" -Encoding ASCII

# Uruchom podpisywanie
.\sign-all.bat
```

**WAŻNE:** Podpisanie plików PRZED pakowaniem zapewnia, że:
- ✅ SUSModder.exe jest podpisany
- ✅ 7z.exe jest podpisany
- ✅ createdump.exe jest podpisany
- ✅ Update.exe będzie podpisany przez Velopack (parametr --signTemplate)

### KROK 4: Pakowanie z Velopack + podpisywanie Update.exe
```powershell
cd D:\Development\SUSModder

# Spakuj z opcją podpisywania (podpisze Update.exe i Setup.exe)
vpk pack `
    --packId SUSModder `
    --packVersion "2.3.13-beta" `
    --packDir "publish-beta" `
    --outputDir "releases-beta" `
    --channel beta `
    --packTitle "SUSModder" `
    --packAuthors "SUSModder Team" `
    --icon "SUSModder\Assets\icon.ico" `
    --splashImage "SUSModder\Assets\splashscreen.jpg" `
    --signTemplate "signtool sign /fd sha256 /sha1 97171de086564a84fa22a72c4260f72ba13096c6 /tr http://time.certum.pl /td sha256 {{file}}"
```

**Co robi --signTemplate:**
- Podpisuje wszystkie 41 plików .exe wewnątrz pakietu (które już były podpisane w kroku 3)
- Podpisuje Update.exe (część Velopack)
- Podpisuje Setup.exe (instalator)

**Wynik:** 41 podpisanych plików + Update.exe + Setup.exe = wszystko podpisane! ✅

### KROK 5: Skopiuj plik RELEASES
```powershell
cd releases-beta
Copy-Item 'RELEASES-beta' 'RELEASES' -Force

# Sprawdź wygenerowane pliki
Get-ChildItem | Select-Object Name, @{Name='Size';Expression={
    if ($_.Length -gt 1MB) {'{0:N2} MB' -f ($_.Length/1MB)}
    else {'{0:N2} KB' -f ($_.Length/1KB)}
}}
```

**Powinny być:**
- ✅ RELEASES (bez sufixu - CRITICAL!)
- ✅ RELEASES-beta (backup)
- ✅ releases.beta.json
- ✅ SUSModder-X.Y.Z-beta-beta-full.nupkg
- ✅ SUSModder-beta-Setup.exe (PODPISANY!)
- ✅ SUSModder-beta-Portable.zip

### KROK 6: Upload na serwer
```powershell
cd D:\Development\SUSModder\releases-beta

# 1. RELEASES
pscp -pw "HASLO" RELEASES debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/

# 2. JSON do /releases/beta/
pscp -pw "HASLO" releases.beta.json debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/

# 3. JSON do /susmodder-velopack/ (KLUCZOWE dla API!)
pscp -pw "HASLO" releases.beta.json debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder-velopack/

# 4. Package .nupkg
pscp -pw "HASLO" "SUSModder-X.Y.Z-beta-beta-full.nupkg" debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/

# 5. Setup.exe (podpisany!)
pscp -pw "HASLO" "SUSModder-beta-Setup.exe" debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/

# 6. Portable.zip
pscp -pw "HASLO" "SUSModder-beta-Portable.zip" debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/
```

**CRITICAL:** Plik `releases.beta.json` MUSI być w DWÓCH miejscach:
1. `/susmodder/releases/beta/` - dla spójności z pakietami
2. `/susmodder-velopack/` - **API czyta stąd!**

### KROK 7: Weryfikacja
```powershell
# Sprawdź API
Invoke-RestMethod -Uri 'https://susmodder.app/api/releases?channel=beta' | ConvertTo-Json -Depth 5

# Powinno zwrócić:
# - success: true
# - latestVersion: "X.Y.Z-beta"
# - SHA256: checksum pakietu

# Sprawdź dostępność plików
$urls = @(
    "https://susmodder.app/releases/beta/SUSModder-X.Y.Z-beta-beta-full.nupkg",
    "https://susmodder.app/releases/beta/SUSModder-beta-Setup.exe",
    "https://susmodder.app/releases/beta/SUSModder-beta-Portable.zip",
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

# Sprawdź pliki na serwerze
plink -pw "HASLO" debian@vps-b99a39c3.vps.ovh.net "ls -lh /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/ | tail -5"

# Sprawdź manifest API (NAJWAŻNIEJSZE!)
plink -pw "HASLO" debian@vps-b99a39c3.vps.ovh.net "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"
```

## Certyfikat Podpisywania

**Obecny certyfikat:**
- Dostawca: Certum Code Signing 2021 CA
- Właściciel: Open Source Developer, Bartosz Gradzik
- Thumbprint: `97171de086564a84fa22a72c4260f72ba13096c6`
- Timestamp server: `http://time.certum.pl`
- Ważny do: 2026-05-21

**Składnia signtool:**
```bash
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "plik.exe"
```

**Parametry:**
- `/fd sha256` - algorytm digest pliku
- `/sha1 "thumbprint"` - identyfikator certyfikatu
- `/tr URL` - timestamp server (RFC 3161)
- `/td sha256` - algorytm digest timestamp
- `/v` - verbose output

## Pliki Wymagające Podpisu

### W publish-beta/ (przed pakowaniem):
1. ✅ **SUSModder.exe** - główna aplikacja
2. ✅ **tools/7z.exe** - narzędzie do rozpakowywania
3. ✅ **createdump.exe** - .NET diagnostic tool

### Przez Velopack (podczas pakowania):
4. ✅ **Update.exe** - updater Velopack (podpisany przez --signTemplate)
5. ✅ **Setup.exe** - instalator (podpisany przez --signTemplate)
6. ✅ **Wszystkie pozostałe .exe w pakiecie** - re-signed przez Velopack

**Razem:** ~41 plików .exe + Update.exe + Setup.exe = **wszystko podpisane!**

## Najczęstsze Błędy i Rozwiązania

### ❌ Błąd: "No file digest algorithm specified"
**Problem:** Stara składnia signtool lub złe ułożenie parametrów

**Rozwiązanie:**
```bash
# POPRAWNIE:
signtool sign /fd sha256 /sha1 "thumbprint" /tr URL /td sha256 /v "file.exe"

# ŹLE:
signtool sign /sha1 "thumbprint" /tr URL /td sha256 /fd sha256 /v "file.exe"
# (brak /fd przed innymi parametrami powoduje błąd w nowszych wersjach)
```

### ❌ Błąd: API zwraca starą wersję
**Przyczyny:**
1. JSON nie wgrany do `/susmodder-velopack/`
2. Brak pliku `RELEASES` (bez sufixu)
3. Cache API (poczekaj 30 sekund)

**Rozwiązanie:**
```powershell
# Sprawdź czy JSON jest w lokalizacji API
plink -pw "HASLO" debian@vps-b99a39c3.vps.ovh.net "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"

# Sprawdź czy RELEASES istnieje
plink -pw "HASLO" debian@vps-b99a39c3.vps.ovh.net "ls -l /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/RELEASES"
```

### ❌ Błąd: Pliki niepodpisane w pakiecie
**Problem:** Podpisano tylko Setup.exe, ale nie pliki wewnątrz

**Rozwiązanie:** Podpisz pliki .exe W KROKU 3 (przed pakowaniem), następnie użyj --signTemplate w kroku 4

### ❌ Błąd: Update.exe niepodpisany
**Problem:** Brak parametru --signTemplate w vpk pack

**Rozwiązanie:** Użyj --signTemplate z pełną składnią signtool

## Checklist Deployment

**Przed buildem:**
- [ ] Zaktualizowano `SUSModder/version.json` do nowej wersji
- [ ] Data w `lastUpdateDate` jest dzisiejsza
- [ ] Sprawdzono signtool w PATH (`where signtool`)

**Po buildzie (publish-beta/):**
- [ ] Podpisano `SUSModder.exe`
- [ ] Podpisano `tools/7z.exe`
- [ ] Podpisano `createdump.exe`

**Po pakowaniu (releases-beta/):**
- [ ] Istnieje plik `RELEASES` (bez sufixu!)
- [ ] Rozmiar .nupkg ~50-60 MB
- [ ] Setup.exe jest podpisany (sprawdź: klik prawym → Właściwości → Podpisy cyfrowe)

**Po uploadzie:**
- [ ] `RELEASES` wgrany do `/releases/beta/`
- [ ] `releases.beta.json` wgrany do `/releases/beta/`
- [ ] `releases.beta.json` wgrany do `/susmodder-velopack/` ← **CRITICAL!**
- [ ] `.nupkg` wgrany i dostępny
- [ ] `Setup.exe` wgrany
- [ ] `Portable.zip` wgrany

**Po weryfikacji:**
- [ ] API zwraca nową wersję (`/api/releases?channel=beta`)
- [ ] Wszystkie pliki dostępne przez HTTP
- [ ] Manifest na serwerze zawiera nową wersję
- [ ] SHA256 checksum się zgadza

## Skróty i Aliasy (opcjonalne)

```powershell
# Dodaj do $PROFILE dla szybkiego dostępu

function Deploy-Beta {
    param([string]$Version)

    Write-Host "=== DEPLOYMENT v$Version ===" -ForegroundColor Cyan

    # 1. Update version.json
    $versionPath = "D:\Development\SUSModder\SUSModder\version.json"
    $json = Get-Content $versionPath | ConvertFrom-Json
    $json.currentVersion = "$Version-beta"
    $json.lastUpdateDate = (Get-Date).ToString("yyyy-MM-dd")
    $json | ConvertTo-Json | Set-Content $versionPath
    Write-Host "✅ version.json updated" -ForegroundColor Green

    # 2. Clean
    cd D:\Development\SUSModder
    Remove-Item 'publish-beta' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item 'releases-beta' -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path 'releases-beta' -Force | Out-Null
    Write-Host "✅ Directories cleaned" -ForegroundColor Green

    # 3. Build
    dotnet publish "SUSModder\SUSModder.csproj" -c Release -r win-x64 --self-contained -o "publish-beta" -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false
    Write-Host "✅ Build completed" -ForegroundColor Green

    # 4. Sign files before packaging
    cd publish-beta
    @'
@echo off
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "SUSModder.exe"
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "tools\7z.exe"
signtool sign /fd sha256 /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" /tr http://time.certum.pl /td sha256 /v "createdump.exe"
'@ | Out-File "sign-all.bat" -Encoding ASCII
    .\sign-all.bat
    Write-Host "✅ Files signed" -ForegroundColor Green

    # 5. Package with signing
    cd ..
    vpk pack --packId SUSModder --packVersion "$Version-beta" --packDir "publish-beta" --outputDir "releases-beta" --channel beta --packTitle "SUSModder" --packAuthors "SUSModder Team" --icon "SUSModder\Assets\icon.ico" --splashImage "SUSModder\Assets\splashscreen.jpg" --signTemplate "signtool sign /fd sha256 /sha1 97171de086564a84fa22a72c4260f72ba13096c6 /tr http://time.certum.pl /td sha256 {{file}}"
    Write-Host "✅ Package created" -ForegroundColor Green

    # 6. Copy RELEASES
    cd releases-beta
    Copy-Item 'RELEASES-beta' 'RELEASES' -Force
    Write-Host "✅ RELEASES copied" -ForegroundColor Green

    Write-Host ""
    Write-Host "=== READY TO UPLOAD ===" -ForegroundColor Yellow
    Write-Host "Manually upload files from releases-beta/ using pscp" -ForegroundColor Gray
}

# Użycie:
# Deploy-Beta "2.3.13"
```

## Historia Zmian

- **2025-11-09:** Utworzono przewodnik na podstawie udanego deploymentu v2.3.12-beta
- Pełne podpisywanie wszystkich plików .exe (41 + Update.exe + Setup.exe)
- Weryfikacja deployment z checklistą

---

**Autor:** AI Assistant + Bartosz Gradzik
**Data:** 2025-11-09
**Wersja przewodnika:** 1.0
