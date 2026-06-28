# 🗑️ Przewodnik Deinstalacji SUSModder

**Problem:** Aplikacja nie pojawia się w "Dodaj/usuń programy" w Windows

**Przyczyna:** Velopack rejestruje aplikację w systemie tylko wtedy, gdy została zainstalowana przez `Setup.exe`

---

## ✅ Rozwiązanie 1: Instalacja przez Setup.exe (REKOMENDOWANE)

### Dla nowych użytkowników:

1. **Zbuduj Setup.exe:**
   ```powershell
   cd d:\Development\SUSModder
   .\SKRYPTY\Build\build-release-2.2.0.ps1 -ReleaseVersion "2.2.0" -NextBetaVersion "2.3.0-beta"
   ```

2. **Udostępnij Setup.exe:**
   - **Lokalizacja:** `releases-release\SUSModder-release-Setup.exe`
   - **Upload na serwer:** Użyj skryptu deploy `.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"`
   - **Link do pobrania:** `https://susmodder.app/releases/release/SUSModder-release-Setup.exe`

3. **Użytkownik instaluje aplikację:**
   - Pobiera `SUSModder-release-Setup.exe`
   - Uruchamia instalator (z uprawnieniami administratora jeśli potrzebne)
   - Setup tworzy wpis w rejestrze Windows
   - Aplikacja pojawia się w "Dodaj/usuń programy"

### Co robi Setup.exe?

- Tworzy katalog instalacji (np. `C:\Users\{User}\AppData\Local\SUSModder\`)
- Rozpakowuje pliki aplikacji do `{InstallDir}\current\`
- Tworzy skrót w Menu Start
- **Rejestruje aplikację w systemie Windows** (klucz rejestru `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\SUSModder`)
- Tworzy `Update.exe` dla przyszłych aktualizacji

---

## ⚠️ Rozwiązanie 2: Ręczna deinstalacja (dla użytkowników bez Setup.exe)

Jeśli aplikacja była zainstalowana jako portable ZIP (bez Setup.exe):

### Krok 1: Usuń pliki aplikacji

```powershell
# 1. Zamknij aplikację
# 2. Usuń katalog aplikacji
Remove-Item "D:\Development\SUSModder\publish" -Recurse -Force

# 3. Usuń dane użytkownika (opcjonalnie)
$appData = "$env:APPDATA\Among Us - Mody"
if (Test-Path $appData) {
    Remove-Item $appData -Recurse -Force
}
```

### Krok 2: Wyczyść ustawienia (opcjonalnie)

```powershell
# Usuń user-settings.json
$exeDir = "D:\Development\SUSModder\publish"
$userSettings = Join-Path $exeDir "user-settings.json"
if (Test-Path $userSettings) {
    Remove-Item $userSettings -Force
}
```

### Krok 3: Usuń skróty (jeśli istnieją)

```powershell
# Menu Start
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\SUSModder.lnk" -ErrorAction SilentlyContinue

# Pulpit
Remove-Item "$env:USERPROFILE\Desktop\SUSModder.lnk" -ErrorAction SilentlyContinue
```

---

## 🔧 Rozwiązanie 3: Dodanie manualnego uninstallera

Możemy dodać prosty skrypt PowerShell do katalogu aplikacji:

### `uninstall.ps1`:

```powershell
# SUSModder Uninstaller
# Uruchom jako administrator jeśli potrzebne

$ErrorActionPreference = "Stop"

Write-Host "SUSModder Uninstaller" -ForegroundColor Cyan
Write-Host "=====================" -ForegroundColor Cyan
Write-Host ""

# 1. Sprawdź czy aplikacja jest uruchomiona
$processName = "SUSModder"
$runningProcesses = Get-Process -Name $processName -ErrorAction SilentlyContinue

if ($runningProcesses) {
    Write-Host "Zamykanie aplikacji..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# 2. Usuń pliki aplikacji
$appDir = Split-Path -Parent $PSScriptRoot
Write-Host "Usuwanie plików aplikacji z: $appDir" -ForegroundColor Yellow

if (Test-Path $appDir) {
    Remove-Item $appDir -Recurse -Force
    Write-Host "✅ Pliki aplikacji usunięte" -ForegroundColor Green
}

# 3. Zapytaj o dane użytkownika
$modsPath = "$env:APPDATA\Among Us - Mody"
if (Test-Path $modsPath) {
    $response = Read-Host "Czy usunąć zainstalowane mody z $modsPath? (T/N)"
    
    if ($response -eq "T" -or $response -eq "t") {
        Remove-Item $modsPath -Recurse -Force
        Write-Host "✅ Dane użytkownika usunięte" -ForegroundColor Green
    } else {
        Write-Host "⏭️ Dane użytkownika zachowane" -ForegroundColor Yellow
    }
}

# 4. Usuń skróty
Write-Host "Usuwanie skrótów..." -ForegroundColor Yellow
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\SUSModder.lnk" -ErrorAction SilentlyContinue
Remove-Item "$env:USERPROFILE\Desktop\SUSModder.lnk" -ErrorAction SilentlyContinue
Write-Host "✅ Skróty usunięte" -ForegroundColor Green

Write-Host ""
Write-Host "✅ Deinstalacja zakończona" -ForegroundColor Green
Write-Host ""
Write-Host "Naciśnij dowolny klawisz aby zamknąć..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
```

### Dodaj do build-release-2.2.0.ps1:

```powershell
# Kopiuj uninstall.ps1 do katalogu aplikacji
$uninstallScript = Join-Path $ProjectRoot "SKRYPTY\Utilities\uninstall.ps1"
if (Test-Path $uninstallScript) {
    Copy-Item $uninstallScript -Destination $releasePublishDir -Force
}
```

---

## 📊 Porównanie metod

| Metoda | Rejestracja w Windows | Automatyczna deinstalacja | Aktualizacje delta |
|--------|----------------------|---------------------------|-------------------|
| **Setup.exe** | ✅ Tak | ✅ Tak | ✅ Tak |
| **Portable ZIP** | ❌ Nie | ❌ Nie | ❌ Nie |
| **+ uninstall.ps1** | ❌ Nie | ⚠️ Manualna | ❌ Nie |

---

## 🎯 Rekomendacje

### Dla produkcji (v2.2.0+):

1. **Główna metoda dystrybucji:** `Setup.exe` (Velopack installer)
   - Pełna integracja z systemem Windows
   - Automatyczna rejestracja
   - Wsparcie dla delta updates

2. **Alternatywna metoda (legacy):** ZIP package
   - Tylko dla kompatybilności wstecznej
   - Dla użytkowników migrujących z v2.0.1
   - Po pierwszej aktualizacji przejdą na Velopack

3. **Dodatkowe narzędzie:** `uninstall.ps1`
   - Dla użytkowników portable ZIP
   - Manual cleanup script

### Dla użytkowników:

**Nowa instalacja:**
- ✅ Pobierz i uruchom `SUSModder-release-Setup.exe`
- ✅ Aplikacja pojawi się w "Dodaj/usuń programy"

**Migracja z v2.0.1:**
- 🔄 Zaktualizuj przez aplikację (pobierze legacy ZIP)
- 🔄 Po restarcie aplikacja będzie używać Velopack
- ✅ Następne aktualizacje będą przez Velopack (delta)

**Deinstalacja:**
- ✅ Jeśli zainstalowano przez Setup.exe: "Dodaj/usuń programy" → SUSModder → Odinstaluj
- ⚠️ Jeśli zainstalowano z ZIP: Uruchom `uninstall.ps1` lub usuń folder ręcznie

---

## 📝 Checklist dla release v2.2.0

- [ ] Zbudować Setup.exe (`build-release-2.2.0.ps1`)
- [ ] Przetestować instalację przez Setup.exe
- [ ] Sprawdzić czy aplikacja pojawia się w "Dodaj/usuń programy"
- [ ] Przetestować proces deinstalacji przez panel Windows
- [ ] Dodać `uninstall.ps1` do portable ZIP (dla legacy users)
- [ ] Zaktualizować dokumentację na stronie
- [ ] Zaktualizować FAQ o proces deinstalacji

---

## 🔗 Przydatne linki

- [Velopack Documentation - Setup.exe](https://docs.velopack.io/packaging/creating-packages)
- [Windows Registry Uninstall Keys](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)
- [SUSModder Release Strategy](./STRATEGY_SUMMARY.md)
