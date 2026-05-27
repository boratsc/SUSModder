# Quick Start - Publikacja Wersji SUSModder

Skrócona ściągawka dla doświadczonych użytkowników.

---

## Dane Wejściowe
```powershell
$releaseVer = "2.2.5"                           # Nowa wersja release
$betaVer = "2.3.13-beta"                        # Nowa wersja beta
$thumbprint = "YOUR_CERT_THUMBPRINT"            # SHA1 certyfikatu (40 hex)
$sshPass = "YOUR_SSH_PASSWORD"                  # Hasło SSH
$sshHost = "user@your-server.com"               # SSH host
```

---

## Krok 1: version.json
```powershell
cd D:\Development\SUSModder
# Edytuj SUSModder/version.json:
# - currentVersion: "2.2.5"
# - lastUpdateDate: "2025-11-XX"
```

---

## Krok 2: Release Channel
```powershell
# Clean
Remove-Item 'publish-velopack-release' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'releases-release' -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path 'releases-release' -Force | Out-Null

# Build
dotnet publish "SUSModder\SUSModder.csproj" `
    -c Release -r win-x64 --self-contained `
    -o "publish-velopack-release" `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false

# Pack + Sign
vpk pack `
    --packId SUSModder `
    --packVersion $releaseVer `
    --packDir "publish-velopack-release" `
    --outputDir "releases-release" `
    --channel release `
    --packTitle "SUSModder" `
    --packAuthors "SUSModder Team" `
    --icon "SUSModder\Assets\icon.ico" `
    --splashImage "SUSModder\Assets\splashscreen.jpg" `
    --signTemplate "signtool sign /sha1 $thumbprint /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"

# Copy RELEASES
cd releases-release
Copy-Item 'RELEASES-release' -Destination 'RELEASES' -Force
cd ..
```

---

## Krok 3: Beta Channel
```powershell
# Clean
Remove-Item 'publish-velopack-beta' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'releases-beta' -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path 'releases-beta' -Force | Out-Null

# Build
dotnet publish "SUSModder\SUSModder.csproj" `
    -c Release -r win-x64 --self-contained `
    -o "publish-velopack-beta" `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false

# Pack + Sign
vpk pack `
    --packId SUSModder `
    --packVersion $betaVer `
    --packDir "publish-velopack-beta" `
    --outputDir "releases-beta" `
    --channel beta `
    --packTitle "SUSModder Beta" `
    --packAuthors "SUSModder Team" `
    --icon "SUSModder\Assets\icon.ico" `
    --splashImage "SUSModder\Assets\splashscreen.jpg" `
    --signTemplate "signtool sign /sha1 $thumbprint /tr http://time.certum.pl /td sha256 /fd sha256 /v {{file}}"

# Copy RELEASES
cd releases-beta
Copy-Item 'RELEASES-beta' -Destination 'RELEASES' -Force
cd ..
```

---

## Krok 4: Upload Release
```powershell
cd releases-release
pscp -pw "$sshPass" RELEASES ${sshHost}:/srv/path/susmodder/releases/release/
pscp -pw "$sshPass" releases.release.json ${sshHost}:/srv/path/susmodder/releases/release/
pscp -pw "$sshPass" "SUSModder-${releaseVer}-release-full.nupkg" ${sshHost}:/srv/path/susmodder/releases/release/
pscp -pw "$sshPass" "SUSModder-${releaseVer}-release-delta.nupkg" ${sshHost}:/srv/path/susmodder/releases/release/
pscp -pw "$sshPass" "SUSModder-release-Setup.exe" ${sshHost}:/srv/path/susmodder/releases/release/
pscp -pw "$sshPass" "SUSModder-release-Portable.zip" ${sshHost}:/srv/path/susmodder/releases/release/
```

---

## Krok 5: Upload Beta
```powershell
cd ..\releases-beta
pscp -pw "$sshPass" RELEASES ${sshHost}:/srv/path/susmodder/releases/beta/
pscp -pw "$sshPass" releases.beta.json ${sshHost}:/srv/path/susmodder/releases/beta/
pscp -pw "$sshPass" "SUSModder-${betaVer}-beta-full.nupkg" ${sshHost}:/srv/path/susmodder/releases/beta/
pscp -pw "$sshPass" "SUSModder-beta-Setup.exe" ${sshHost}:/srv/path/susmodder/releases/beta/
pscp -pw "$sshPass" "SUSModder-beta-Portable.zip" ${sshHost}:/srv/path/susmodder/releases/beta/
```

---

## Krok 6: Upload Manifests (CRITICAL!)
```powershell
cd ..\releases-release
pscp -pw "$sshPass" releases.release.json ${sshHost}:/srv/path/susmodder-velopack/

cd ..\releases-beta
pscp -pw "$sshPass" releases.beta.json ${sshHost}:/srv/path/susmodder-velopack/
```

---

## Krok 7: Weryfikacja
```powershell
# Check API
Invoke-RestMethod 'https://susmodder.app/api/releases?channel=release' | ConvertTo-Json -Depth 5
Invoke-RestMethod 'https://susmodder.app/api/releases?channel=beta' | ConvertTo-Json -Depth 5

# Check HTTP
Invoke-WebRequest -Method Head "https://susmodder.app/releases/release/SUSModder-${releaseVer}-release-full.nupkg"
Invoke-WebRequest -Method Head "https://susmodder.app/releases/beta/SUSModder-${betaVer}-beta-full.nupkg"
```

---

## Checklist
- [ ] version.json zaktualizowany
- [ ] Release: RELEASES (bez sufixu) istnieje
- [ ] Beta: RELEASES (bez sufixu) istnieje
- [ ] Release: Setup.exe podpisany
- [ ] Beta: Setup.exe podpisany
- [ ] Delta package wygenerowany
- [ ] Wszystkie pliki wgrane na serwer
- [ ] **CRITICAL:** Manifesty JSON w `/susmodder-velopack/`
- [ ] API zwraca nową wersję
- [ ] HTTP 200 dla wszystkich plików

---

**⚠️ PAMIĘTAJ:**
1. `RELEASES` (bez sufixu) MUSI istnieć!
2. Manifesty JSON MUSZĄ być w `/susmodder-velopack/`!
3. Poprzednia wersja w `releases-release/` → automatyczna delta!

---

**Czas:** ~15-20 minut
**Szczegóły:** Zobacz [`RELEASE_GUIDE.md`](RELEASE_GUIDE.md)
