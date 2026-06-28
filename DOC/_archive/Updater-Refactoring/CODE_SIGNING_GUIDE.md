# Code Signing - Przewodnik Automatyzacji

## Przegląd

Automatyczne podpisywanie plików .exe zwiększa zaufanie użytkowników i redukuje fałszywe alarmy antywirusów.

**Obecna konfiguracja SUSModder:**
- Dostawca: **Certum** (http://time.certum.pl)
- Metoda: Certyfikat w **Windows Certificate Store**
- Thumbprint: `YOUR_CERTIFICATE_THUMBPRINT_HERE`

## Opcje Podpisywania

### 0. Obecna Metoda (Certum - Windows Store) ⭐ REKOMENDOWANE

**Zalety:**
- Certyfikat już zainstalowany w systemie
- Nie trzeba podawać hasła przy każdym buildzie
- Bezpieczne (certyfikat nie jest w pliku)

**Użycie - Pomocnik:**
```powershell
# Interaktywny helper - zapyta o thumbprint
.\SKRYPTY\Build\sign-and-build.ps1 -ReleaseVersion "2.2.0" -NextBetaVersion "2.3.0-beta"

# Bez legacy ZIP
.\SKRYPTY\Build\sign-and-build.ps1 -ReleaseVersion "2.2.0" -NextBetaVersion "2.3.0-beta" -SkipLegacyZip
```

**Użycie - Bezpośrednie:**
```powershell
# Z thumbprint (domyślny Certum)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Znajdź dostępne certyfikaty
Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | 
    Where-Object { $_.NotAfter -gt (Get-Date) } |
    Format-Table Thumbprint, Subject, NotAfter -AutoSize
```

**Manualne podpisywanie (signtool):**
```powershell
# Pojedynczy plik
signtool sign /sha1 "YOUR_CERTIFICATE_THUMBPRINT_HERE" `
    /tr http://time.certum.pl `
    /td sha256 `
    /fd sha256 `
    /v "SUSModder.exe"

# Wiele plików
signtool sign /sha1 "YOUR_CERTIFICATE_THUMBPRINT_HERE" `
    /tr http://time.certum.pl `
    /td sha256 `
    /fd sha256 `
    /v "SUSModder.exe" "Updater.exe"

# Weryfikacja
signtool verify /pa "SUSModder.exe"
Get-AuthenticodeSignature "SUSModder.exe" | Format-List
```

### 1. Lokalne Podpisywanie (PFX File)

**Wymagania:**
- Windows SDK (signtool.exe)
- Certyfikat PFX/P12
- Hasło do certyfikatu

**Instalacja signtool:**
```powershell
# Sprawdź czy jest zainstalowany
Get-Command signtool -ErrorAction SilentlyContinue

# Jeśli nie, zainstaluj Windows SDK
winget install Microsoft.VisualStudio.2022.BuildTools

# Lub pobierz SDK bezpośrednio:
# https://developer.microsoft.com/windows/downloads/windows-sdk/

# Dodaj do PATH (przykład)
$sdkPath = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64"
$env:Path += ";$sdkPath"
[Environment]::SetEnvironmentVariable("Path", $env:Path, "User")
```

**Użycie w skrypcie build:**
```powershell
# Z plikiem PFX
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "HasłoDoCertyfikatu"
```

**Manualne podpisywanie:**
```powershell
# Podpisz plik
signtool sign `
    /f "C:\Certs\susmodder.pfx" `
    /p "HasłoDoCertyfikatu" `
    /fd SHA256 `
    /tr http://timestamp.digicert.com `
    /td SHA256 `
    "SUSModder.exe"

# Weryfikacja
signtool verify /pa "SUSModder.exe"
Get-AuthenticodeSignature "SUSModder.exe" | Format-List
```

### 2. CI/CD (GitHub Actions)

**Konfiguracja secrets:**
```powershell
# Konwertuj certyfikat do Base64
$certPath = "C:\Certs\susmodder.pfx"
$bytes = [IO.File]::ReadAllBytes($certPath)
$base64 = [Convert]::ToBase64String($bytes)

# Wypisz do pliku (żeby nie przepełnić terminala)
Set-Content -Path "cert-base64.txt" -Value $base64

# Skopiuj zawartość cert-base64.txt do GitHub Secrets
```

**GitHub Secrets (Settings → Secrets and variables → Actions):**
- `CODE_SIGN_CERT_BASE64` - certyfikat w base64
- `CODE_SIGN_CERT_PASSWORD` - hasło do certyfikatu

**Workflow file (.github/workflows/release.yml):**
```yaml
name: Release Build and Sign

on:
  push:
    tags:
      - 'v*.*.*'
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to build (e.g. 2.2.0)'
        required: true

jobs:
  build-and-sign:
    runs-on: windows-latest
    
    steps:
      - name: Checkout
        uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Install Velopack CLI
        run: dotnet tool install -g vpk
      
      - name: Decode signing certificate
        shell: pwsh
        run: |
          $certBytes = [Convert]::FromBase64String("${{ secrets.CODE_SIGN_CERT_BASE64 }}")
          $certPath = Join-Path $env:TEMP "cert.pfx"
          [IO.File]::WriteAllBytes($certPath, $certBytes)
          Write-Output "CERT_PATH=$certPath" >> $env:GITHUB_ENV
      
      - name: Build and sign releases
        shell: pwsh
        run: |
          $version = "${{ github.ref_name }}"
          if ($version.StartsWith("v")) {
            $version = $version.Substring(1)
          }
          
          .\SKRYPTY\Build\build-release-2.2.0.ps1 `
            -ReleaseVersion $version `
            -NextBetaVersion "$version-beta" `
            -CertificatePath $env:CERT_PATH `
            -CertificatePassword "${{ secrets.CODE_SIGN_CERT_PASSWORD }}"
      
      - name: Upload Legacy ZIP
        uses: actions/upload-artifact@v4
        with:
          name: legacy-zip
          path: releases-legacy/*.zip
      
      - name: Upload Release Channel
        uses: actions/upload-artifact@v4
        with:
          name: velopack-release
          path: releases-release/*
      
      - name: Upload Beta Channel
        uses: actions/upload-artifact@v4
        with:
          name: velopack-beta
          path: releases-beta/*
      
      - name: Create GitHub Release
        uses: softprops/action-gh-release@v1
        if: startsWith(github.ref, 'refs/tags/')
        with:
          files: |
            releases-legacy/*.zip
            releases-release/*.nupkg
            releases-release/RELEASES
            releases-beta/*.nupkg
            releases-beta/RELEASES
          draft: false
          prerelease: false
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Cleanup certificate
        if: always()
        shell: pwsh
        run: |
          if (Test-Path $env:CERT_PATH) {
            Remove-Item $env:CERT_PATH -Force
          }
```

**Trigger release:**
```bash
# Automatyczny (przy tagu)
git tag v2.2.0
git push origin v2.2.0

# Manualny (workflow_dispatch)
# GitHub → Actions → Release Build and Sign → Run workflow
# Wpisz: 2.2.0
```

### 3. Azure Key Vault (Enterprise)

**Zalety:**
- Certyfikat nigdy nie opuszcza cloud
- Centralne zarządzanie
- Audit log wszystkich podpisów
- Automatyczna rotacja certyfikatów

**Konfiguracja:**
```bash
# Zainstaluj Azure CLI
winget install Microsoft.AzureCLI

# Zaloguj się
az login

# Utwórz Key Vault
az keyvault create \
  --name susmodder-keyvault \
  --resource-group susmodder-rg \
  --location westeurope

# Import certyfikatu
az keyvault certificate import \
  --vault-name susmodder-keyvault \
  --name code-sign-cert \
  --file susmodder.pfx \
  --password "HasłoDoCertyfikatu"

# Nadaj uprawnienia dla service principal
az keyvault set-policy \
  --name susmodder-keyvault \
  --spn <app-id> \
  --certificate-permissions get list \
  --secret-permissions get list
```

**AzureSignTool (open source):**
```powershell
# Instalacja
dotnet tool install --global AzureSignTool

# Podpisywanie
AzureSignTool sign `
  --azure-key-vault-url "https://susmodder-keyvault.vault.azure.net/" `
  --azure-key-vault-client-id "<client-id>" `
  --azure-key-vault-client-secret "<client-secret>" `
  --azure-key-vault-certificate "code-sign-cert" `
  --timestamp-rfc3161 "http://timestamp.digicert.com" `
  --timestamp-digest sha256 `
  --file-digest sha256 `
  "SUSModder.exe"
```

**GitHub Actions z Azure Key Vault:**
```yaml
- name: Sign with Azure Key Vault
  run: |
    AzureSignTool sign `
      --azure-key-vault-url "${{ secrets.AZURE_KEYVAULT_URL }}" `
      --azure-key-vault-tenant-id "${{ secrets.AZURE_TENANT_ID }}" `
      --azure-key-vault-client-id "${{ secrets.AZURE_CLIENT_ID }}" `
      --azure-key-vault-client-secret "${{ secrets.AZURE_CLIENT_SECRET }}" `
      --azure-key-vault-certificate "code-sign-cert" `
      --timestamp-rfc3161 "http://timestamp.digicert.com" `
      --timestamp-digest sha256 `
      --file-digest sha256 `
      "publish\SUSModder.exe"
```

## Pozyskanie Certyfikatu

### Rekomendowani Dostawcy

| Dostawca | Cena/rok | OV | EV | Czas weryfikacji |
|----------|----------|----|----|------------------|
| **Sectigo** | $75-150 | ✅ | ✅ | 1-3 dni |
| **DigiCert** | $200-400 | ✅ | ✅ | 1-5 dni |
| **GlobalSign** | $150-300 | ✅ | ✅ | 2-4 dni |
| **SSL.com** | $100-250 | ✅ | ✅ | 1-3 dni |

### Typy Certyfikatów

**OV (Organization Validation):**
- Weryfikacja firmy przez dokumenty
- Certyfikat w formacie pliku (PFX)
- Dobry dla większości aplikacji
- **Rekomendowane dla SUSModder**

**EV (Extended Validation):**
- Rygorystyczna weryfikacja (wywiad z firmą)
- Wymaga Hardware Security Module (USB token)
- Natychmiastowa reputacja w Windows SmartScreen
- Droższe (~$300-500/rok)
- Najlepsze dla high-profile aplikacji

### Proces Zakupu (Sectigo - przykład)

1. **Wybierz certyfikat:**
   - https://sectigo.com/ssl-certificates-tls/code-signing
   - Standard Code Signing (OV)

2. **Weryfikacja firmy:**
   ```
   Wymagane dokumenty:
   - Zaświadczenie z KRS/CEIDG (jeśli firma w PL)
   - Dokument tożsamości (właściciel)
   - Potwierdzenie numeru telefonu
   - Potwierdzenie adresu email
   ```

3. **Generacja CSR (Certificate Signing Request):**
   ```powershell
   # Windows: użyj certmgr.msc
   # Start → Run → certmgr.msc
   # Personal → All Tasks → Advanced Operations → Create Custom Request
   # Wybierz: Code Signing
   # Klucz: RSA 2048-bit
   # Zapisz CSR do pliku
   
   # Lub OpenSSL:
   openssl req -new -newkey rsa:2048 -nodes `
     -keyout private.key `
     -out request.csr `
     -subj "/C=PL/ST=Mazowieckie/L=Warszawa/O=SUSModder/CN=SUSModder Team"
   ```

4. **Odebranie certyfikatu:**
   - Sectigo wyśle certyfikat emailem (format: PEM/CRT)
   - Zaimportuj do Windows Keystore lub konwertuj do PFX:
   ```powershell
   openssl pkcs12 -export `
     -in certificate.crt `
     -inkey private.key `
     -out susmodder.pfx `
     -passout pass:TwojeHasło
   ```

## Timestamp Servers

**Dlaczego timestamp?**
- Podpis pozostaje ważny po wygaśnięciu certyfikatu
- Dowód, że plik był podpisany gdy certyfikat był aktywny

**Rekomendowane serwery:**
```
http://timestamp.digicert.com          (DigiCert)
http://timestamp.sectigo.com           (Sectigo)
http://timestamp.globalsign.com/tsa/r6cca1  (GlobalSign)
http://timestamp.apple.com/ts01        (Apple)
```

**Użycie w signtool:**
```powershell
signtool sign `
  /f cert.pfx `
  /p password `
  /fd SHA256 `
  /tr http://timestamp.digicert.com `  # RFC 3161 timestamp
  /td SHA256 `                         # Timestamp digest algorithm
  file.exe
```

## Weryfikacja Podpisu

### PowerShell
```powershell
# Podstawowa weryfikacja
Get-AuthenticodeSignature "SUSModder.exe" | Select-Object Status, SignerCertificate

# Szczegółowe info
Get-AuthenticodeSignature "SUSModder.exe" | Format-List *

# Oczekiwane:
# Status: Valid
# SignerCertificate:
#   Subject: CN=Your Company Name, O=Your Company, C=PL
#   Issuer: CN=Sectigo Public Code Signing CA R36
#   Thumbprint: ...
#   NotBefore: ...
#   NotAfter: ...
# TimeStamperCertificate:
#   Subject: CN=DigiCert Timestamp 2023
```

### Windows Explorer
```
1. Prawy klik na SUSModder.exe
2. Properties → Digital Signatures
3. Sprawdź:
   - Name of signer: Your Company Name
   - Digest algorithm: sha256
   - Timestamp: [data]
```

### signtool
```powershell
# Weryfikacja
signtool verify /pa "SUSModder.exe"

# Output jeśli OK:
# Successfully verified: SUSModder.exe
```

## Troubleshooting

### Problem: "No certificates were found that met all criteria"

**Przyczyna:** signtool nie znajduje certyfikatu

**Rozwiązanie:**
```powershell
# Sprawdź certyfikaty w keystore
certutil -store -user My

# Jeśli certyfikat jest w pliku, użyj /f zamiast /sha1
signtool sign /f cert.pfx /p password ...
```

### Problem: "SignerSign() failed (-2147024891/0x80070005)"

**Przyczyna:** Brak uprawnień lub certyfikat jest protected

**Rozwiązanie:**
```powershell
# Uruchom PowerShell jako Administrator
# Lub sprawdź uprawnienia do pliku certyfikatu
icacls cert.pfx
```

### Problem: Timestamp server timeout

**Przyczyna:** Serwer timestamp jest niedostępny

**Rozwiązanie:**
```powershell
# Spróbuj innego serwera
signtool sign `
  /f cert.pfx `
  /tr http://timestamp.sectigo.com `  # Zamiast digicert
  /td SHA256 `
  file.exe

# Lub dodaj retry w skrypcie
for ($i = 1; $i -le 3; $i++) {
    signtool sign ... file.exe
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep -Seconds 5
}
```

### Problem: "The specified timestamp server could not be reached"

**Przyczyna:** Firewall/proxy blokuje połączenie

**Rozwiązanie:**
```powershell
# Sprawdź połączenie
Test-NetConnection timestamp.digicert.com -Port 80

# Jeśli jest proxy, skonfiguruj:
netsh winhttp set proxy proxy-server="proxy.company.com:8080"
```

## Best Practices

1. **Nigdy nie commituj certyfikatu do Git**
   ```gitignore
   # .gitignore
   *.pfx
   *.p12
   cert-base64.txt
   ```

2. **Używaj środowiskowych zmiennych dla haseł**
   ```powershell
   $env:CERT_PASSWORD = Read-Host -AsSecureString "Certificate password"
   ```

3. **Timestamp zawsze**
   - Bez timestamp: podpis wygasa gdy certyfikat wygaśnie
   - Z timestamp: podpis ważny na zawsze

4. **Backup certyfikatu**
   - Zaszyfruj PFX i trzymaj w bezpiecznym miejscu
   - Jeśli zgubisz, musisz kupić nowy certyfikat

5. **Rotacja certyfikatów**
   - Ustaw przypomnienie 30 dni przed wygaśnięciem
   - Odnów certyfikat zawczasu
   - Update GitHub secrets z nowym certyfikatem

6. **Używaj SHA256, nie SHA1**
   - SHA1 jest deprecated od 2016
   - Windows może odrzucić pliki podpisane SHA1

## Koszty Roczne (Przykład)

```
Certyfikat Code Signing (Sectigo OV): $120/rok
Timestamp service: Darmowy
signtool: Darmowy (część Windows SDK)
GitHub Actions minutes: Darmowe (public repo)

TOTAL: ~$120/rok
```

**Alternatywnie (EV + Azure):**
```
Certyfikat EV: $350/rok
USB Token (YubiKey 5 FIPS): $70 (jednorazowo)
Azure Key Vault: $0.03/10k operacji (~$1-2/rok)
Azure Key Vault storage: $0.05/miesiąc (~$0.60/rok)

TOTAL: ~$422 pierwszy rok, ~$352 kolejne lata
```

## Automatyzacja Odnowienia

```yaml
# .github/workflows/cert-renewal-reminder.yml
name: Certificate Renewal Reminder

on:
  schedule:
    - cron: '0 9 * * 1'  # Każdy poniedziałek o 9:00

jobs:
  check-expiry:
    runs-on: ubuntu-latest
    steps:
      - name: Decode certificate
        run: |
          echo "${{ secrets.CODE_SIGN_CERT_BASE64 }}" | base64 -d > cert.pfx
      
      - name: Check expiry date
        run: |
          expiry=$(openssl pkcs12 -in cert.pfx -passin pass:"${{ secrets.CODE_SIGN_CERT_PASSWORD }}" -nokeys | openssl x509 -noout -enddate | cut -d= -f2)
          expiry_epoch=$(date -d "$expiry" +%s)
          now_epoch=$(date +%s)
          days_left=$(( ($expiry_epoch - $now_epoch) / 86400 ))
          
          echo "Certificate expires in $days_left days"
          
          if [ $days_left -lt 30 ]; then
            echo "⚠️ Certificate expires soon! Renew now." >> $GITHUB_STEP_SUMMARY
            # Wyślij notyfikację (Discord, email, etc.)
          fi
```

---

**Ostatnia aktualizacja:** 2025-11-06
**Wersja:** 1.0
