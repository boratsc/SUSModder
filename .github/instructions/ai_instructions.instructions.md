---
applyTo: '**'
---
# SUSModder – kontekst dla AI (skrót działania aplikacji i wytyczne)

Cel tego pliku: zapewnić stały kontekst działania aplikacji, jej architekturę, kluczowe przepływy, pliki i zasady kodowania. Dzięki temu AI nie musi pytać o podstawy w każdej sesji.

## O aplikacji
- Aplikacja desktopowa .NET 8 (C#) z UI w Avalonia 11 (ReactiveUI).
- Przeznaczenie: zarządzanie modami do gry Among Us (Steam i Epic).
- Funkcje: instalacja pełnych modów (full), instalacja/usuwanie DLL do wybranych modów full, aktualizacje modów i aplikacji, ustawienia, temat UI, szybkie linki do Discordów, narzędzia (np. Fix Black Screen, lobby size).
- Dystrybucja: single-file self-contained (win-x64) z dołączonymi narzędziami (tools/7z.exe) i updaterem (updater/Updater.exe).

## Kluczowe moduły i pliki
- UI/uruchomienie: `SUSModder/Program.cs`, `SUSModder/App.axaml(.cs)`, `SUSModder/ViewModels/MainWindowViewModel.cs`.
- Logika domenowa (Core): `SUSModder.Core/*`.
	- Konfiguracje: `Configuration/ModConfiguration` (model), `Configuration/ConfigManager`, `Services/ConfigService`.
	- Ścieżki i ustawienia: `Utilities/PathSettings.cs` (ModsInstallPath, DefaultModsPath – wczytywane z appsettings.json lub domyślne w %APPDATA%).
	- Lokalizacja gry: `GameIntegration/GameLocator.cs` (auto-detekcja Among Us dla Steam/Epic + rejestracja Vanilla).
	- Instalacja modów: `GameIntegration/ModManager.cs` (Steam full), `Services/ModService.cs` (fasada), `GameIntegration/ModDelete.cs`.
	- Aktualizacje modów: `GameIntegration/ModUpdate*.cs`, `Services/ModUpdateManager.cs`.
	- Mody DLL: `Services/DllModificationService.cs` (+ VM `DllModSelectionViewModel` po stronie UI).
	- Aktualizacje aplikacji: 
		- **Legacy** (v2.0.1 i wcześniej): `Services/AppUpdateService.cs` + `Updater.exe` (ZIP download → extract → replace).
		- **Velopack** (v2.1.0+, obecny): `Services/VelopackUpdateService.cs` + `Services/VelopackApiSource.cs` (delta updates, atomic swaps, Rust-based).
			- Auto-detekcja środowiska Velopack z fallback do legacy w `MainWindowViewModel.TryHandleVelopackAppUpdatesAsync()`.
			- API endpoint: `https://susmodder.app/api/releases?channel=win` (manifest z .nupkg, checksum SHA256).
			- UI: `Views/VelopackUpdateDialog.axaml(.cs)`.
			- Pakiety generowane przez Velopack CLI (`vpk`), format .nupkg (NuGet).
	- Sekrety: `Secrets.cs` (token HTTP i hasło 7z pobierane przez `SecretProvider`).
	- Repozytoria: `Repositories/ConfigRepository.cs` (czytanie/zapis appsettings.json, config.json, komunikacja z API).

## Pliki konfiguracyjne (runtime – obok exe)
- `version.json` – **GŁÓWNY** plik wersji aplikacji (od v2.2.0+):
	```json
	{
	    "currentVersion": "2.2.1",  // lub "2.3.7-beta" dla beta
	    "lastUpdateDate": "2025-11-06",
	    "buildNumber": ""
	}
	```
	- **ZAWSZE** aktualizuj ten plik przed buildem nowej wersji!
	- Dla beta: MUSI zawierać sufix `-beta` (np. "2.3.7-beta")
	- Dla release: BEZ sufixu (np. "2.2.1")
- `appsettings.json` – **DEPRECATED** dla wersji (używany tylko dla innych ustawień):
	- Configuration:
		- Mode: "steam" lub "epic" (bieżący tryb gry; może zostać nadpisany auto-detekcją GameLocator).
		- BaseUrl: bazowy URL API (np. https://susmodder.boracik.pl).
		- UpdateServerUrl, lastLaunchId, Theme.
	- AppSettings:
		- ModsInstallPath: docelowy katalog instalacji modów (jeśli brak – użyj DefaultModsPath).
		- DefaultModsPath: domyślny katalog, zwykle %APPDATA%/Among Us - Mody.
- `config.json` – lista ModConfiguration (mody z polami niżej). Wczytywana/zapisywana przez `ConfigManager`; fallback do API, gdy brak lokalnego pliku.

## Model ModConfiguration (wybór pól)
- Id (int), ModName (string), ModType ("full" | "dll" | "Vanilla"), PngFileName, InstallPath?, GitHubRepoOrLink, EpicGitHubRepoOrLink?, DllInstallPath?, ModVersion, AmongVersion, LastUpdated?, Description.

## Główne przepływy
1) Start aplikacji
- `Program.Main` przywraca kopię ustawień użytkownika po update (AppUpdateService.RestoreUserSettingsIfNeeded – legacy compatibility), uruchamia Avalonię.
- `App.OnFrameworkInitializationCompleted` tworzy `MainWindow` z `MainWindowViewModel`.

2) Inicjalizacja (MainWindowViewModel)
- Wczytuje konfiguracje modów (`ConfigService.LoadConfig`).
- Przeprowadza auto-detekcję gry Among Us (`GameLocator.CheckAndSetupVanillaModAsync`).
	- Jeżeli znaleziono, zapisuje wpis "AmongUs" (ModType=Vanilla) do `config.json`, aktualizuje Configuration:Mode.
	- Jeżeli nie – prosi użytkownika o wskazanie "Among Us.exe" lub pozwala zamknąć aplikację (może skasować `config.json`).
- Następnie sprawdza aktualizacje modów, odświeża listę UI.
- Równolegle próbuje preloadować ikony Discordów, wczytuje motyw i wersję aplikacji.

3) Instalacja moda (full)
- `ModService.InstallModAsync` -> `ModManager.ModifyAsync` (Steam) -> `InstallSteamAsync`:
	- Przygotowanie katalogów: `PathSettings.ModsInstallPath`/"Among Us - Vanilla" i docelowy folder moda.
	- Pobranie archiwum Vanilla 7z po wersji AmongVersion (zabezpieczenia tokenem, hasłem 7z – `SecretProvider`).
	- Rozpakowanie 7z przez narzędzie `tools/7z.exe` do katalogu moda.
	- Pobranie archiwum moda (zip) z `GitHubRepoOrLink` i rozpakowanie, następnie skopiowanie zawartości (BepInEx itp.) do docelowego folderu moda.
	- Zapis do `config.json` (InstallPath, LastUpdated). Obsługa błędów, braków uprawnień, retry dialogi przez `ModManagerUserCallbacks` (potwierdzenia/komunikaty asynchroniczne).

4) Instalacja/odinstalowanie DLL
- `DllModificationService` wybiera link zależnie od platformy (dla Epic preferuje `EpicGitHubRepoOrLink`).
- Domyślna ścieżka docelowa dla DLL: `DllInstallPath` lub `BepInEx\\plugins` w katalogu moda full.
- UI: `DllModSelectionViewModel` pozwala wybrać wiele DLL i zainstaluje je do wskazanego moda full (z korektą ścieżki dla Epic, jeśli potrzeba). Odinstalowanie usuwa plik DLL.

5) Aktualizacje modów
- `ModUpdateManager` sprawdza różnice wersji na podstawie konfiguracji z API i lokalnej. UI pokazuje dialog aktualizacji i wykonuje sekwencję z progresami.

6) Aktualizacja aplikacji (Velopack - v2.1.0+)
- `VelopackUpdateService.CheckForUpdateAsync()` → `VelopackApiSource.GetReleaseFeed()` pobiera manifest z `https://susmodder.app/api/releases`.
- Porównuje `CurrentVersion` (z appsettings.json) z `LatestVersion` z API.
- Jeśli aktualizacja dostępna: pokazuje `VelopackUpdateDialog`.
- `DownloadUpdateAsync()` pobiera pakiet .nupkg z progresem (SHA256 verification).
- `ApplyUpdateAndRestartAsync()` wywołuje `UpdateManager.WaitExitThenApplyUpdatesAsync()` → Velopack's native `Update.exe` (Rust) wykonuje atomic swap plików.
- Aplikacja restartuje się automatycznie.
- **Fallback**: Jeśli Velopack nie jest wykryty (`IsInstalledAsync() == false`), używa legacy `AppUpdateService` (ZIP download → Updater.exe).

7) Aktualizacja aplikacji (Legacy - v2.0.1 i wcześniej, dla kompatybilności wstecznej)
- `AppUpdateService` porównuje `CurrentVersion` z najnowszą wersją z serwera.
- Pobieranie paczki ZIP do %TEMP%, uruchomienie `updater/Updater.exe` z argumentami (kopia ustawień użytkownika – Mode, Theme, lastLaunchId, ModsInstallPath – jest zapisywana i przywracana przy kolejnym starcie).

## Zależności i środowisko
- .NET 8.0; Avalonia 11.3; ReactiveUI; Microsoft.Extensions.Configuration.
- **Velopack** (v0.0.1298+) - system aktualizacji aplikacji (NuGet: `Velopack`).
- Runtime: Windows (publikacja win-x64). Narzędzia zewnętrzne w `tools/7z.exe`.
- Uprawnienia: operacje na plikach w katalogu ModsInstallPath i rozpakowywanie archiwów (foldery muszą być dostępne do zapisu; gra nie może być uruchomiona podczas instalacji).

## Ważne założenia i ograniczenia
- Sekrety (token HTTP, hasło do 7z) pobieraj przez `SecretProvider`. Nie umieszczaj jawnych sekretów w kodzie ani w logach (ModManager maskuje hasło w komunikatach błędów).
- Dla Epic często katalog gry ma strukturę z folderem `AmongUs`; dla Steam klasycznie `steamapps/common/Among Us`.
- `ConfigManager.LoadConfig()` może wywołać pobranie z API i zapisać lokalnie, jeżeli `config.json` nie istnieje.
- UI działa w Avalonia – operacje dłuższe zawsze asynchronicznie, a komunikaty/okna przez dispatcher.

## Standardy kodowania i wzorce w repo
- C#: nullable enable, async/await, brak blokowania UI, try/catch z diagnostyką.
- ReactiveUI: właściwości z RaiseAndSetIfChanged, ReactiveCommand do akcji; aktualizacje UI przez Dispatcher.
- Konfiguracja: korzystaj z `ConfigurationBuilder` i kluczy sekcji ("Configuration:*", "AppSettings:*").
- I/O: ostrożnie z uprawnieniami i plikami zablokowanymi (obsługuj UnauthorizedAccessException, retry z komunikatami użytkownika, bezpieczne kasowanie katalogów – patrz SafeDeleteDirectory).
- Logowanie: używaj `IDiagnosticsOutput` (np. `UIDiagnosticsOutput`) do przekazywania informacji do UI/logu debug.

## Co robić / czego nie robić (AI)
- Rób:
	- Trzymaj się istniejących usług (ConfigService, ModService, DllModificationService, AppUpdateService, GameLocator).
	- Używaj PathSettings.ModsInstallPath zamiast hardcodować ścieżki.
	- Dodawaj nowe akcje UI jako ReactiveCommand i pamiętaj o wątkach UI.
	- Jeżeli dodajesz pobieranie plików – używaj tokenu z SecretProvider i raportuj progres.
- Nie rób:
	- Nie wprowadzaj jawnych sekretów, haseł, tokenów ani URL-i, które nadpisują konfigurację użytkownika bez potrzeby.
	- Nie blokuj wątku UI operacjami I/O.
	- Nie zmieniaj publicznych API bez zaktualizowania wszystkich wywołań.

## Typowe zadania a oczekiwane zachowanie
- Dodanie nowego moda full: 
	- Rozszerz listę w `config.json` (lub w źródle API), ustaw poprawnie `ModType=full`, `GitHubRepoOrLink`, `AmongVersion` itd.
	- Instalacja powinna utworzyć folder w ModsInstallPath, rozpakować Vanilla, następnie zawartość moda.
- Dodanie nowego moda DLL:
	- `ModType=dll`, ustaw `DllInstallPath` (jeśli inne niż BepInEx\\plugins), opcjonalnie `EpicGitHubRepoOrLink`.
	- W UI pojawi się w selektorze DLL i można instalować/odinstalować do modów full.
- Wsparcie dla nowej platformy (hipotetycznie): 
	- Dodaj rozpoznanie w GameLocator i odpowiednie linki pobierania w `ModConfiguration` + obsługa w DllModificationService.

## Weryfikacja (skrót)
- Build: projekt `SUSModder` (net8.0). 
- Release (Legacy single-file): publikacja single-file do `publish/` z dołączonym `tools` i `updater`.
- Release (Velopack - REKOMENDOWANE): 
	```powershell
	# Zainstaluj Velopack CLI (jednorazowo)
	dotnet tool install -g vpk
	
	# Zbuduj i spakuj
	.\build-velopack-test.ps1
	
	# Output: velopack-releases/SUSModder-X.Y.Z-win-full.nupkg + RELEASES + releases.win.json
	# Upload wszystkie pliki na https://susmodder.app/releases/
	```
- Smoke test: start aplikacji, auto-detekcja gry (lub wybór exe), lista modów, instalacja DLL do istniejącego moda full, sprawdzenie aktualizacji (Velopack w installed env, legacy w dev mode).

## Słowniczek
- Full mod: modyfikacja z pełnym kompletem plików (BepInEx itd.) – instalowana do osobnego folderu w ModsInstallPath.
- DLL mod: pojedyncza biblioteka .dll kopiowana do folderu BepInEx\\plugins (lub innego z DllInstallPath) w modzie full.
- Vanilla: czysta kopia plików gry Among Us, rozpakowywana z zaszyfrowanego archiwum 7z.
- Velopack: nowoczesny framework do instalacji i auto-update (następca Squirrel.Windows), używany od v2.1.0. Delta updates, atomic swaps, napisany w Rust.
- .nupkg: format pakietu Velopack (NuGet package, czyli ZIP z manifestem), zawiera pliki aplikacji + metadane do delta updates.

## Testowanie i debugowanie aktualizacji (Velopack)

### Szybki test w dev mode:
```powershell
# 1. Wygeneruj dummy release (testowy pakiet)
.\SKRYPTY\Testing\generate-dummy-release.ps1

# 2. Sprawdź API
.\SKRYPTY\Testing\test-velopack-api.ps1

# 3. Symuluj środowisko Velopack
cd publish
mkdir packages
echo. > ..\Update.exe

# 4. Zmień wersję w appsettings.json na niższą (np. "2.0.1")
# 5. Uruchom aplikację i kliknij "Sprawdź aktualizacje"
```

### Debugging:
- Breakpoint w `VelopackUpdateService.CheckForUpdateAsync()`.
- Logi `[Velopack]` w `IDiagnosticsOutput`.
- Sprawdź `velopackEnvironmentDetected` w `MainWindowViewModel.TryHandleVelopackAppUpdatesAsync()`.

### Typowe problemy:
- "No updates available" → `CurrentVersion` w appsettings.json >= wersja z API.
- "Velopack not detected" → Normalne w dev mode, używa legacy updater.
- "Invalid checksum" → Backend musi zwracać prawdziwy SHA256 z pliku `RELEASES`.
- "Failed to download" → Sprawdź dostępność URL, format API response.

### Dokumentacja:
- `DOC/Updater-Refactoring/VELOPACK_TESTING_GUIDE.md` - pełny przewodnik testowania.
- `DOC/Updater-Refactoring/VELOPACK_STATUS.md` - obecny status implementacji.
- `DOC/Updater-Refactoring/` - szczegółowa dokumentacja architektury i migracji.

## Struktura projektu

### Katalogi dokumentacji (DOC/)
```
DOC/
├── Updater-Refactoring/           # Dokumentacja systemu aktualizacji
│   ├── VELOPACK_READY.md          # Status gotowości Velopack
│   ├── VELOPACK_STATUS.md         # Bieżący status implementacji
│   ├── VELOPACK_TESTING_GUIDE.md  # Przewodnik testowania
│   └── MIGRATION_PLAN.md          # Plan migracji z legacy do Velopack
├── 2025-10-22 - susmodder-api rozbudowa/  # Rozbudowa API backendu
│   └── BACKEND_UPDATE_NEEDED.md   # Wymagania dla backendu
├── Localization/                  # Lokalizacja i tłumaczenia
│   └── Reports/
│       └── HARDCODED_POLISH_TEXTS_REPORT.md  # Raport hardcodowanych tekstów
└── DOCUMENTATION_UPDATE_SUMMARY.md  # Podsumowanie aktualizacji dokumentacji
```

### Katalogi skryptów (SKRYPTY/)
```
SKRYPTY/
├── Build/                         # Skrypty buildowania i pakowania
│   ├── build-dual-channel.ps1     # Build dla obu kanałów (release + beta)
│   ├── build-release-velopack.ps1 # Build release Velopack
│   └── build-velopack-test.ps1    # Build testowy Velopack
├── Testing/                       # Skrypty testowe
│   ├── generate-dummy-release.ps1 # Generowanie testowego release
│   └── test-velopack-api.ps1      # Testowanie API Velopack
└── Utilities/                     # Narzędzia pomocnicze
    └── temp_inspect.ps1           # Inspekcja tymczasowa
```

### Ważne uwagi o strukturze:
- Wszystkie skrypty buildowania w `SKRYPTY/Build/` - używaj `.\SKRYPTY\Build\build-dual-channel.ps1`
- Dokumentacja Velopack w `DOC/Updater-Refactoring/` - zawsze sprawdzaj tam aktualne informacje
- Skrypty testowe w `SKRYPTY/Testing/` - do lokalnego testowania przed produkcją
- Raport lokalizacji w `DOC/Localization/Reports/` - informacje o tekstach do przetłumaczenia

# Publikacja (Velopack - REKOMENDOWANE od v2.2.0)

## Strategia Wydania v2.2.0+

### Przegląd
Od wersji 2.2.0 aplikacja używa:
- **Dual release**: Legacy ZIP (dla migracji z v2.0.1) + Velopack packages (dla nowych instalacji i aktualizacji)
- **System kanałów**: `release` (stable) i `beta` (testing)
- **Numeracja kernel-style**: Parzyste drugie cyfry = stable (2.2.0, 2.4.0), nieparzyste = beta (2.3.0-beta, 2.5.0-beta)
- **Code signing**: Automatyczne podpisywanie .exe (opcjonalne, ale zalecane)

### Quick Start - Budowanie Release

```powershell
# 1. Zainstaluj wymagane narzędzia (jednorazowo)
dotnet tool install -g vpk

# 2. Zbuduj kompletny release (3 formaty w jednym)
cd d:\Development\SUSModder
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "YourPassword"

# Bez podpisywania (tylko dla testów)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -SkipSigning

# Pomiń legacy ZIP (jeśli wszyscy już na Velopack)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -SkipLegacyZip `
    -SkipSigning
```

### Output Struktury

**Skrypt generuje 3 katalogi:**
```
releases-legacy/
└── SUSModder-2.2.0-legacy.zip           (~50 MB) - dla użytkowników z v2.0.1

releases-release/
├── SUSModder-2.2.0-release-full.nupkg   (~50 MB) - stable channel
├── RELEASES
└── releases.release.json

releases-beta/
├── SUSModder-2.3.0-beta-beta-full.nupkg (~50 MB) - testing channel
├── RELEASES
└── releases.beta.json
```

### Deployment na Serwer

**WAŻNE: Wymagane narzędzia**
Zamiast PuTTY tools, używamy **Posh-SSH** (bardziej niezawodny):
```powershell
# 1. Zainstaluj Posh-SSH module (jednorazowo)
Install-Module -Name Posh-SSH -Force -Scope CurrentUser

# Sprawdź instalację
Get-Module -ListAvailable Posh-SSH
```

**Deployment (Manual - zalecane dla kontroli):**
```powershell
# 1. Import modułu
Import-Module Posh-SSH

# 2. Przygotuj credentials
$password = ConvertTo-SecureString "PASSWORD" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ("debian", $password)

# 3. Upload plików RELEASES (CRITICAL!)
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "D:\Development\SUSModder\releases-beta\RELEASES" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 4. Upload plików JSON do API location (CRITICAL!)
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "D:\Development\SUSModder\releases-beta\releases.beta.json" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder-velopack/" -AcceptKey

# 5. Upload pakietu .nupkg
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "D:\Development\SUSModder\releases-beta\SUSModder-X.Y.Z-beta-full.nupkg" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 6. Weryfikacja
$session = New-SSHSession -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential -AcceptKey
Invoke-SSHCommand -SessionId $session.SessionId `
    -Command "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"
Remove-SSHSession -SessionId $session.SessionId
```

**Automatyczny deployment (Legacy - wymaga PuTTY):**
```powershell
# 1. Zainstaluj PuTTY tools (jednorazowo)
winget install PuTTY.PuTTY

# 2. Deploy wszystkich plików
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"
# Skrypt zapyta o hasło SSH: debian@vps-b99a39c3.vps.ovh.net

# Dry run (test bez uploadu)
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -DryRun

# Tylko release channel (pomiń legacy i beta)
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -SkipLegacy -SkipBeta
```

**UWAGA**: Skrypt `deploy-to-server.ps1` może nie kopiować plików JSON do `/susmodder-velopack/`.
Zawsze weryfikuj ręcznie!

**Co robi skrypt:**
1. Sprawdza wymagania (pscp, plink z PuTTY)
2. Weryfikuje katalogi z release files
3. Pyta o hasło SSH
4. Testuje połączenie
5. Pokazuje plan uploadu i pyta o potwierdzenie
6. Uploaduje pliki do właściwych lokalizacji na serwerze
7. Weryfikuje poprawność uploadu

**Struktura na serwerze:**
```
/srv/synapsekit-boracik/nginx/html/
├── susmodder/
│   └── releases/
│       ├── legacy/            ← Legacy ZIP (backup)
│       ├── release/           ← Release channel
│       │   ├── SUSModder-X.Y.Z-release-full.nupkg
│       │   ├── RELEASES       ← MUSI być bez sufixu!
│       │   ├── RELEASES-release (backup)
│       │   └── releases.release.json
│       └── beta/              ← Beta channel
│           ├── SUSModder-X.Y.Z-beta-full.nupkg
│           ├── RELEASES       ← MUSI być bez sufixu!
│           ├── RELEASES-beta (backup)
│           └── releases.beta.json
├── susmodder-velopack/        ← KLUCZOWY katalog dla API!
│   ├── releases.release.json  ← API czyta stąd! (nie z /susmodder/releases/)
│   └── releases.beta.json     ← API czyta stąd! (nie z /susmodder/releases/)
└── susmodder-versions/
    └── SUSModder-2.2.0.zip     ← Legacy dla użytkowników 2.0.2 i starszych
```

**CRITICAL NOTES:**
- Plik `RELEASES` MUSI być bez sufixu `-beta` lub `-release` (Velopack go tak wymaga)
- Pliki JSON MUSZĄ być w obu lokalizacjach:
  1. `/susmodder/releases/{channel}/` - dla spójności z pakietami
  2. `/susmodder-velopack/` - **to tu API czyta manifesty!**
- Skrypt `build-dual-channel.ps1` (v2025-11-06+) automatycznie tworzy plik `RELEASES` z `RELEASES-{channel}`

**Uwaga:** Legacy ZIP trafia do 3 miejsc:
1. `/susmodder/releases/legacy/` - backup/archiwum
2. `/susmodder-versions/SUSModder-2.2.0.zip` - główny link dla starych użytkowników

**Wymagane endpointy backend:**
1. **Legacy version check**: `/api/susmodder-current-version?current=X.Y.Z`
   - Dla v2.0.1: zwraca JSON z `downloadUrl: "/api/download-latest"`
   - Dla v2.2.0+: zwraca JSON z info o Velopack

2. **Legacy download**: `/api/download-latest`
   - Zwraca: `SUSModder-2.2.0-legacy.zip`

3. **Velopack manifest**: `/api/releases?channel={release|beta}`
   - Zwraca manifest JSON z `downloadBaseUrl` i listą pakietów

**Upload plików:**
```bash
# 1. Legacy ZIP (dla użytkowników z v2.0.1)
scp releases-legacy/SUSModder-2.2.0-legacy.zip \
    server:/var/www/susmodder/releases/legacy/

# 2. Release channel (stable)
scp releases-release/* \
    server:/var/www/susmodder/releases/release/

# 3. Beta channel (testing)
scp releases-beta/* \
    server:/var/www/susmodder/releases/beta/
```

**Struktura na serwerze:**
```
/var/www/susmodder/
└── releases/
    ├── legacy/
    │   └── SUSModder-2.2.0-legacy.zip
    ├── release/
    │   ├── SUSModder-2.2.0-release-full.nupkg
    │   ├── RELEASES
    │   └── releases.release.json
    └── beta/
        ├── SUSModder-2.3.0-beta-beta-full.nupkg
        ├── RELEASES
        └── releases.beta.json
```

### Code Signing (Opcjonalne, ale Zalecane)

**Obecna konfiguracja:**
- Dostawca: **Certum** (http://time.certum.pl)
- Metoda: Certyfikat w **Windows Certificate Store**
- Thumbprint: `97171de086564a84fa22a72c4260f72ba13096c6`

**Dlaczego?**
- Windows nie pokazuje "Unknown publisher"
- Mniejsza szansa na blokowanie przez SmartScreen
- Lepszy reputation w antywirusach
- Profesjonalny wizerunek

**Jak podpisać? (3 metody)**

**Metoda 1: Interaktywny helper (REKOMENDOWANE)** ⭐
```powershell
# Skrypt zapyta o thumbprint certyfikatu
.\SKRYPTY\Build\sign-and-build.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta"

# Domyślny thumbprint Certum: 97171de086564a84fa22a72c4260f72ba13096c6
# Wystarczy nacisnąć ENTER aby użyć domyślnego
```

**Metoda 2: Z thumbprint (Certum - obecna)**
```powershell
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificateThumbprint "97171de086564a84fa22a72c4260f72ba13096c6"

# Znajdź dostępne certyfikaty w systemie:
Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | 
    Where-Object { $_.NotAfter -gt (Get-Date) } |
    Format-Table Thumbprint, Subject, NotAfter
```

**Metoda 3: Z plikiem PFX (alternatywna)**
```powershell
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "YourPassword"
```

**Manualne podpisywanie (signtool):**
```powershell
# Pojedynczy plik
signtool sign /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" `
    /tr http://time.certum.pl `
    /td sha256 /fd sha256 /v "SUSModder.exe"

# Wiele plików
signtool sign /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" `
    /tr http://time.certum.pl `
    /td sha256 /fd sha256 /v `
    "SUSModder.exe" "Updater.exe"
```

**Szczegóły:** Zobacz `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`

### Numeracja Wersji (Kernel Style)

**Stable (parzyste drugie cyfry):**
```
2.0.0 → 2.2.0 → 2.4.0 → 2.6.0
```

**Beta (nieparzyste drugie cyfry):**
```
2.1.0-beta → 2.3.0-beta → 2.5.0-beta
```

**Przykładowy cykl wydawniczy:**
```
2.2.0 (release)           ← Stabilna wersja
  ├─→ 2.2.1 (bugfix)      ← Drobne poprawki
  ├─→ 2.2.2 (bugfix)
  └─→ 2.3.0-beta (beta)   ← Nowe funkcje testowe
       ├─→ 2.3.1-beta     ← Beta bugfix
       └─→ 2.4.0 (release) ← Nowa stabilna wersja
            └─→ 2.5.0-beta (beta) ← Kolejny cykl testowy
```

### Migracja z v2.0.1 na v2.2.0

**Proces:**
1. Użytkownik na v2.0.1 klika "Sprawdź aktualizacje"
2. Backend zwraca v2.2.0 przez `/api/download-latest`
3. Pobiera legacy ZIP (~50MB)
4. `Updater.exe` aplikuje aktualizację
5. Restart aplikacji do v2.2.0
6. **Velopack framework jest teraz zainstalowany** (`Update.exe` w katalogu nadrzędnym)
7. Kolejne aktualizacje używają Velopack (delta updates, ~5-10MB)

### Testowanie Przed Release

**Test 1: Migracja v2.0.1 → v2.2.0**
- Zainstaluj v2.0.1, kliknij "Sprawdź aktualizacje"
- Powinien pobrać ZIP i zaktualizować przez Updater.exe
- Po restarcie: v2.2.0 z Velopack zainstalowanym

**Test 2: Update Velopack (v2.2.0 → v2.2.1)**
- Zbuduj fake v2.2.1, upload na serwer testowy
- W aplikacji v2.2.0: "Sprawdź aktualizacje"
- Powinien pobrać DELTA package (~5-10MB, nie full)

**Test 3: Przełączanie kanałów**
- W v2.2.0 (release): Ustawienia → Zaawansowane → Kanał: Beta
- "Sprawdź aktualizacje" → powinien znaleźć v2.3.0-beta

**Test 4: Nowa instalacja**
- Pobierz Setup.exe z releases-release/
- Zainstaluj → aplikacja w `{InstallDir}\current\SUSModder.exe`

### Dokumentacja Szczegółowa

**Kompletne przewodniki:**
- `DOC/Updater-Refactoring/STRATEGY_SUMMARY.md` - Strategia wydania v2.2.0 (quick reference)
- `DOC/Updater-Refactoring/RELEASE_220_MIGRATION.md` - Pełna instrukcja wydania
- `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md` - Automatyzacja podpisywania
- `DOC/Updater-Refactoring/UPDATE_CHANNELS.md` - System kanałów release/beta
- `DOC/Updater-Refactoring/MIGRATION_PLAN.md` - Strategia migracji użytkowników

### Monitoring Po Release

**Metryki sukcesu:**
- Po 1 tygodniu: >60% użytkowników na v2.2.0+, <5% błędów aktualizacji
- Po 2 tygodniach: >80% użytkowników na v2.2.0+, brak krytycznych bugów
- Po 4 tygodniach: >95% użytkowników na v2.2.0+

**Rollback Plan:**
Jeśli krytyczny bug w v2.2.0:
1. Wycofaj manifest Velopack (przywróć backup)
2. Zmień `/api/susmodder-current-version` do zwracania v2.0.1
3. Komunikat dla użytkowników
4. Hotfix v2.2.1 → przetestuj → deploy

### Development/Testing Builds

```powershell
# Szybkie buildy do testowania (bez legacy ZIP, bez signing)
.\SKRYPTY\Build\build-dual-channel.ps1 -Version 2.2.0              # Oba kanały
.\SKRYPTY\Build\build-dual-channel.ps1 -Version 2.2.0 -SkipBeta    # Tylko release
.\SKRYPTY\Build\build-dual-channel.ps1 -Version 2.2.0 -SkipRelease # Tylko beta

# Legacy single-file (tylko dla dev)
dotnet publish SUSModder\SUSModder.csproj -c Release -r win-x64 --self-contained
```

---

## 📦 DEPLOYMENT - Instrukcja Wdrażania na Serwer Produkcyjny

### Wymagania wstępne:

1. **PuTTY Tools** (pscp, plink):
   ```powershell
   # Instalacja przez winget
   winget install PuTTY.PuTTY
   
   # Lub chocolatey
   choco install putty
   
   # Dodaj do PATH
   $env:Path += ';C:\Program Files\PuTTY'
   ```

2. **Dane dostępowe:**
   - Serwer: `vps-b99a39c3.vps.ovh.net`
   - User: `debian`
   - Hasło: (dostępne w bezpiecznym repozytorium)

3. **Struktura na serwerze:**
   ```
   /srv/synapsekit-boracik/nginx/html/susmodder/releases/
   ├── legacy/      ← Legacy ZIP (dla migracji z v2.0.1)
   ├── release/     ← Release channel (stable)
   └── beta/        ← Beta channel (testing)
   ```

### Proces Deployment - Krok po Kroku

#### 1. BUILD RELEASE

**A. Beta Channel (najczęściej):**
```powershell
cd D:\Development\SUSModder

# Sprawdź aktualną wersję
Get-Content .\SUSModder\version.json

# Build beta (np. 2.3.5-beta)
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.3.5-beta" -SkipRelease

# Output: releases-beta\
#   - RELEASES-beta
#   - releases.beta.json
#   - SUSModder-X.Y.Z-beta-beta-full.nupkg
#   - SUSModder-beta-Setup.exe
#   - SUSModder-beta-Portable.zip
```

**B. Release Channel (stable):**
```powershell
# Build release (np. 2.2.0)
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.2.0" -SkipBeta

# Output: releases-release\
```

**C. Oba kanały jednocześnie:**
```powershell
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.2.0"
```

#### 2. UPLOAD NA SERWER

**Metoda A: Automatyczny deploy (REKOMENDOWANE):**
```powershell
# Deploy wszystkich kanałów
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"

# Tylko beta
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.3.5-beta" -SkipLegacy -SkipRelease

# Tylko release
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -SkipLegacy -SkipBeta

# Dry run (test bez uploadu)
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0" -DryRun
```

**Metoda B: Manualne uploadowanie (jeśli auto nie działa):**
```powershell
# Dodaj PuTTY do PATH
$env:Path += ';C:\Program Files\PuTTY'

cd D:\Development\SUSModder

# Ścieżki i zmienne
$betaDir = ".\releases-beta"
$server = "debian@vps-b99a39c3.vps.ovh.net"
$remotePath = "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/"
$password = "TUTAJ_WPISZ_HASLO"

# Upload plików beta
$files = @(
    "RELEASES-beta",
    "releases.beta.json",
    "SUSModder-2.3.5-beta-beta-beta-full.nupkg"
)

foreach ($file in $files) {
    $localFile = Join-Path $betaDir $file
    if (Test-Path $localFile) {
        Write-Host "Uploading: $file" -ForegroundColor Yellow
        
        $pscpArgs = "-pw", $password, $localFile, "${server}:${remotePath}"
        & pscp @pscpArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  OK: $file uploaded" -ForegroundColor Green
        } else {
            Write-Host "  ERROR: Failed to upload $file" -ForegroundColor Red
        }
    }
}
```

**Dla release channel:**
```powershell
$releaseDir = ".\releases-release"
$remotePath = "/srv/synapsekit-boracik/nginx/html/susmodder/releases/release/"

$files = @(
    "RELEASES-release",
    "releases.release.json",
    "SUSModder-2.2.0-release-full.nupkg"
)

# ... (ten sam kod uploadowania)
```

#### 3. WERYFIKACJA DEPLOYMENT

**Sprawdź API endpoint:**
```powershell
# Test beta API
$response = Invoke-RestMethod -Uri "https://susmodder.app/api/releases?channel=beta" -Method Get
$response | ConvertTo-Json -Depth 10

# Sprawdź kluczowe wartości
Write-Host "Success: $($response.success)" -ForegroundColor $(if($response.success){"Green"}else{"Red"})
Write-Host "Latest Version: $($response.latestVersion)" -ForegroundColor Yellow
Write-Host "Channel: $($response.channel)" -ForegroundColor Yellow

# Test release API
$response = Invoke-RestMethod -Uri "https://susmodder.app/api/releases?channel=release" -Method Get
$response | ConvertTo-Json -Depth 10
```

**Sprawdź dostępność plików:**
```powershell
# Test czy plik jest dostępny do pobrania
$fileUrl = "https://susmodder.app/releases/beta/SUSModder-2.3.5-beta-beta-beta-full.nupkg"

try {
    $response = Invoke-WebRequest -Uri $fileUrl -Method Head -ErrorAction Stop
    Write-Host "✅ File accessible!" -ForegroundColor Green
    Write-Host "   Status: $($response.StatusCode)" -ForegroundColor Gray
    Write-Host "   Size: $([math]::Round($response.Headers.'Content-Length'[0]/1MB, 2)) MB" -ForegroundColor Gray
} catch {
    Write-Host "❌ File NOT accessible: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
}
```

#### 4. POST-DEPLOYMENT TESTING

**Test aktualizacji w aplikacji:**
1. Uruchom aplikację z kanałem beta
2. Kliknij "Sprawdź aktualizacje"
3. Sprawdź czy wykrywa nową wersję
4. Przetestuj download i instalację

**Test zmiany kanału:**
1. Zainstaluj wersję beta
2. Otwórz Ustawienia → Kanał aktualizacji → "Release"
3. Kliknij "Zapisz"
4. **Oczekiwane**: Dialog aktualizacji pokazuje się automatycznie
5. Sprawdź czy proponuje wersję z kanału release

### Troubleshooting

**Problem: PuTTY tools not found**
```powershell
# Sprawdź czy PuTTY jest zainstalowany
Get-ChildItem "C:\Program Files\PuTTY" -ErrorAction SilentlyContinue

# Jeśli istnieje, dodaj do PATH
$env:Path += ';C:\Program Files\PuTTY'

# Sprawdź ponownie
Get-Command pscp -ErrorAction SilentlyContinue
Get-Command plink -ErrorAction SilentlyContinue
```

**Problem: Access denied podczas uploadu**
- Sprawdź uprawnienia na serwerze
- Upewnij się że użytkownik `debian` ma write access do `/srv/synapsekit-boracik/nginx/html/susmodder/releases/`

**Problem: API zwraca starą wersję**
- Sprawdź czy pliki zostały faktycznie wgrane na serwer
- **CRITICAL**: Sprawdź czy istnieje plik `RELEASES` (bez sufixu) w katalogach beta/release
  - Velopack generuje `RELEASES-beta` i `RELEASES-release`
  - API wymaga `RELEASES` (bez sufixu)
  - Skrypt `build-dual-channel.ps1` automatycznie kopiuje, ale starsze buildy tego nie robiły
- **CRITICAL**: Sprawdź czy manifesty JSON są w obu lokalizacjach:
  - `/susmodder/releases/beta/releases.beta.json` (pliki release)
  - `/susmodder-velopack/releases.beta.json` (czytane przez API) ← to jest KLUCZOWE
  - To samo dla release channel
- Poczekaj ~30 sekund (cache API)
- Sprawdź logi backendu

**Problem: Checksum mismatch**
- Backend automatycznie generuje SHA256 z pliku RELEASES
- Jeśli błąd, sprawdź czy plik .nupkg jest kompletny
- Porównaj rozmiar pliku lokalnie vs na serwerze

---

## Quick Reference: Budowanie i Publikacja Nowej Wersji

**KROK 1: Aktualizuj version.json** (ZAWSZE PIERWSZY!)
```powershell
# Edytuj: D:\Development\SUSModder\SUSModder\version.json
# Beta: "currentVersion": "2.3.7-beta"
# Release: "currentVersion": "2.2.1"
```

**KROK 2: Build**
```powershell
cd D:\Development\SUSModder

# Beta
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.3.7" -SkipRelease

# Release  
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.2.1" -SkipBeta

# Skrypt automatycznie dodaje -beta dla beta channel
```

**KROK 3: Podpisz Setup.exe**
```powershell
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
& $signtool sign /sha1 "97171de086564a84fa22a72c4260f72ba13096c6" `
    /tr http://time.certum.pl /td sha256 /fd sha256 /v `
    "releases-beta\SUSModder-beta-Setup.exe"
```

**KROK 4: Weryfikuj lokalnie**
```powershell
$manifest = Get-Content "releases-beta\releases.beta.json" -Raw | ConvertFrom-Json
$manifest.Assets[0].Version  # MUSI być "X.Y.Z-beta" dla beta!
```

**KROK 5: Upload (WSZYSTKIE pliki!)**
```powershell
Import-Module Posh-SSH
$password = ConvertTo-SecureString "PASSWORD" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ("debian", $password)

# 1. RELEASES
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\RELEASES" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 2. JSON do /releases/beta/
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\releases.beta.json" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 3. JSON do /susmodder-velopack/ (KLUCZOWE dla API!)
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\releases.beta.json" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder-velopack/" -AcceptKey

# 4. Package .nupkg
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\SUSModder-X.Y.Z-beta-beta-full.nupkg" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 5. Setup.exe (podpisany!)
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\SUSModder-beta-Setup.exe" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# 6. Portable.zip
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\SUSModder-beta-Portable.zip" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey
```

**KROK 6: Weryfikuj na serwerze**
```powershell
$session = New-SSHSession -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential -AcceptKey

# Sprawdź manifest API (NAJWAŻNIEJSZE!)
Invoke-SSHCommand -SessionId $session.SessionId `
    -Command "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"

# Sprawdź wszystkie pliki
Invoke-SSHCommand -SessionId $session.SessionId `
    -Command "ls -lh /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/"

Remove-SSHSession -SessionId $session.SessionId
```

**Najczęstsze błędy:**
- ❌ Zapomnienie aktualizacji `version.json` przed buildem
- ❌ Brak `-beta` w manifeście (skrypt dodaje automatycznie, ale sprawdź!)
- ❌ Nie wgranie JSON do `/susmodder-velopack/` → API zwraca starą wersję
- ❌ Nie wgranie Setup.exe/Portable.zip → użytkownicy nie mogą pobrać
- ❌ Setup.exe niepodpisany → Windows SmartScreen

---

### Quick Reference - Najczęstsze Komendy (LEGACY)

```powershell
# === BUILD ===
# Beta
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.3.5-beta" -SkipRelease

# Release
.\SKRYPTY\Build\build-dual-channel.ps1 -Version "2.2.0" -SkipBeta

# === DEPLOY ===
# Beta (po zbudowaniu)
$env:Path += ';C:\Program Files\PuTTY'
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.3.5-beta" -SkipLegacy -SkipRelease

# === VERIFY ===
# Test API
Invoke-RestMethod -Uri "https://susmodder.app/api/releases?channel=beta" | ConvertTo-Json

# Test file
Invoke-WebRequest -Uri "https://susmodder.app/releases/beta/SUSModder-X.Y.Z-beta-beta-full.nupkg" -Method Head
```

### Checklist Deployment

**Przed deployment:**
- [ ] Kod skompilowany bez błędów (`dotnet build`)
- [ ] Testy przeszły pomyślnie
- [ ] Wersja zaktualizowana w `SUSModder\version.json`
- [ ] Changelog przygotowany
- [ ] PuTTY tools zainstalowane

**Po build:**
- [ ] Sprawdzono pliki w `releases-beta/` lub `releases-release/`
- [ ] Rozmiar pakietu .nupkg ok (~50-60 MB)
- [ ] RELEASES i releases.json wygenerowane
- [ ] **CRITICAL**: Sprawdź czy istnieje plik `RELEASES` (bez sufixu `-beta` lub `-release`)
  - Jeśli brak: `Copy-Item "releases-beta\RELEASES-beta" "releases-beta\RELEASES" -Force`
  - Jeśli brak: `Copy-Item "releases-release\RELEASES-release" "releases-release\RELEASES" -Force`

**Po upload:**
- [ ] **CRITICAL**: Sprawdź czy pliki JSON są w katalogu API:
  ```bash
  ssh debian@vps-b99a39c3.vps.ovh.net \
    "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"
  # Powinien zawierać najnowszą wersję!
  ```
- [ ] **CRITICAL**: Sprawdź czy plik RELEASES istnieje na serwerze:
  ```bash
  ssh debian@vps-b99a39c3.vps.ovh.net \
    "ls -lh /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/RELEASES"
  # Musi być RELEASES, nie RELEASES-beta!
  ```
- [ ] API endpoint zwraca nową wersję (`/api/releases?channel=beta`)
- [ ] Plik .nupkg dostępny do pobrania
- [ ] SHA256 checksum poprawny
- [ ] Test aktualizacji w aplikacji OK

**Po weryfikacji:**
- [ ] Komunikat dla użytkowników wysłany
- [ ] Changelog opublikowany
- [ ] Dokumentacja zaktualizowana

---

## Publikacja Legacy (dla kompatybilności wstecznej - depreciated)

**Uwaga:** Ta metoda jest przestarzała. Używaj `build-release-2.2.0.ps1` zamiast tego.

# Publikacja Updater (jeśli potrzebny osobno)
cd d:\Development\SUSModder\Updater
dotnet publish -c Release

# Publikacja głównej aplikacji (single-file - tylko dev)
cd d:\Development\SUSModder\SUSModder
dotnet publish -c Release

---

## Known Issues & Solutions

### Issue: API zwraca starą wersję mimo uploadu nowych plików

**Symptom:** Po wgraniu nowych plików (np. 2.3.6), API nadal zwraca starą wersję (np. 2.3.5).

**Root Cause (2025-11-06):**
1. **Brak pliku RELEASES** - Velopack generuje `RELEASES-{channel}`, ale API wymaga `RELEASES` (bez sufixu)
2. **JSON w złej lokalizacji** - Pliki JSON wgrane tylko do `/susmodder/releases/{channel}/`, ale API czyta z `/susmodder-velopack/`

**Solution:**
```powershell
# Krok 1: Fix lokalnie (jeśli używasz starszej wersji skryptu)
Copy-Item "releases-beta\RELEASES-beta" "releases-beta\RELEASES" -Force
Copy-Item "releases-release\RELEASES-release" "releases-release\RELEASES" -Force

# Krok 2: Upload plików RELEASES na serwer
Import-Module Posh-SSH
$password = ConvertTo-SecureString "PASSWORD" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ("debian", $password)

Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\RELEASES" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/" -AcceptKey

# Krok 3: Upload JSON do API location (KLUCZOWE!)
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-beta\releases.beta.json" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder-velopack/" -AcceptKey

# Krok 4: Weryfikacja
$session = New-SSHSession -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential -AcceptKey
Invoke-SSHCommand -SessionId $session.SessionId `
    -Command "cat /srv/synapsekit-boracik/nginx/html/susmodder-velopack/releases.beta.json"
Remove-SSHSession -SessionId $session.SessionId
```

**Prevention:**
- Używaj `build-dual-channel.ps1` z datą 2025-11-06 lub nowszą (automatycznie kopiuje RELEASES)
- Zawsze weryfikuj po upload: `ls /srv/.../susmodder-velopack/releases.*.json`
- Dokumentacja: `DOC/Updater-Refactoring/BETA_236_FIX.md`

### Issue: Velopack generuje RELEASES-{channel} zamiast RELEASES

**Fixed in:** `build-dual-channel.ps1` (2025-11-06)

Skrypt automatycznie kopiuje `RELEASES-{channel}` → `RELEASES` po pakietowaniu.

**Manual fix (jeśli używasz starszej wersji):**
```powershell
Copy-Item "releases-beta\RELEASES-beta" "releases-beta\RELEASES" -Force
```

### Issue: Beta wersje bez sufixu -beta w manifeście

**Symptom:** 
- Release channel (2.2.0) próbuje aktualizować do beta (2.3.6)
- Beta wersja pokazuje się jako "2.3.6" zamiast "2.3.6-beta"
- Przełączanie z beta na release powoduje ciągłe powiadomienia o aktualizacji

**Root Cause (2025-11-06):**
Velopack dodaje `-{channel}` do **nazwy pliku**, ale NIE do **wersji w manifeście**.
Stary kod przekazywał czystą wersję (np. `2.3.6`) → manifest miał `Version: "2.3.6"` bez sufixu.

**Problem z porównywaniem wersji:**
- `2.3.6` (bez sufixu) > `2.2.0` → Velopack widzi jako nowszą stable wersję
- `2.3.6-beta` (z sufiksem) jest prerelease → Velopack NIE aktualizuje release channel

**Fixed in:** `build-dual-channel.ps1` (2025-11-06, linia ~180)

Skrypt automatycznie dodaje `-beta` do wersji dla beta channel:
```powershell
if (-not $SkipBeta) {
    $betaVersion = if ($Version.Contains("-beta")) { $Version } else { "$Version-beta" }
    Build-Channel -ChannelName "BETA (Testowe)" -ChannelVersion $betaVersion -ChannelCode "beta"
}
```

**Verification:**
```powershell
# Po build sprawdź manifest
$manifest = Get-Content "releases-beta\releases.beta.json" -Raw | ConvertFrom-Json
$manifest.Assets[0].Version  # Powinno być: "2.3.6-beta"

# Na serwerze sprawdź API
curl https://susmodder.app/api/releases?channel=beta | jq '.manifest.Releases[0].Version'
# Powinno zwrócić: "2.3.6-beta"
```

**Dokumentacja:** `DOC/Updater-Refactoring/BETA_VERSION_SUFFIX_FIX.md`

### Issue: Przełączanie kanałów nie działa (channel switching)

**Symptom:**
- Zmiana kanału z beta na release pokazuje "masz najnowszą wersję" zamiast oferować odpowiednią aktualizację
- Po zmianie kanału w ustawieniach, sprawdzenie aktualizacji używa starego kanału
- VelopackUpdateService nie reinicjalizuje się z nowym kanałem

**Root Cause (2025-11-06):**
`VelopackUpdateService` był tworzony jako **lokalna zmienna** w `TryHandleVelopackAppUpdatesAsync()` i dispose'owany po każdym sprawdzeniu.
Zmiana kanału zapisywała ustawienie, ale nie reinicjalizowała UpdateManager.

**Fixed in:** 
- `MainWindowViewModel.cs` - dodano pole `_velopackUpdateService`
- `MainWindowViewModel.Initialization.cs` - używa pola zamiast lokalnej zmiennej
- `MainWindowViewModel.AppSettings.cs` - wywołanie `ReinitializeAsync()` po zmianie kanału

**Kluczowe zmiany:**
```csharp
// MainWindowViewModel.cs
private VelopackUpdateService? _velopackUpdateService;

// MainWindowViewModel.Initialization.cs
if (_velopackUpdateService == null)
{
    _velopackUpdateService = new VelopackUpdateService(...);
}
// NIE dispose - używane przez cały cykl życia aplikacji

// MainWindowViewModel.AppSettings.cs
private async void OnUpdateChannelChanged(string newChannel)
{
    // CRITICAL: Reinicjalizuj z nowym kanałem
    if (_velopackUpdateService != null)
    {
        await _velopackUpdateService.ReinitializeAsync();
    }
    await CheckForAppUpdatesCoreAsync(notifyWhenNoUpdates: true, showErrorsToUser: true);
}
```

**Verification:**
1. Ustaw kanał na 'beta', sprawdź aktualizacje → powinien znaleźć wersję beta
2. Zmień kanał na 'release', sprawdź aktualizacje → powinien znaleźć wersję release (lub brak aktualizacji)
3. Przełączanie tam i z powrotem działa bez restartu aplikacji

**Dokumentacja:** `DOC/Updater-Refactoring/CHANNEL_SWITCH_FIX.md`

### Issue: Brak Setup.exe i Portable.zip po deploymencie

**Symptom:**
Po wydaniu nowej wersji (np. 2.2.1), na serwerze są tylko pliki:
- `.nupkg` (pakiet Velopack)
- `RELEASES` (manifest)
- `releases.*.json` (API manifest)

Ale brakuje:
- `SUSModder-*-Setup.exe` (instalator)
- `SUSModder-*-Portable.zip` (wersja portable)

**Root Cause:**
Skrypt uploadu wgrywał tylko kluczowe pliki dla Velopack (`.nupkg`, `RELEASES`, JSON), pomijając Setup i Portable.

**Solution:**
```powershell
# Upload wszystkich plików z katalogu releases
Import-Module Posh-SSH
$password = ConvertTo-SecureString "PASSWORD" -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential ("debian", $password)

# Upload Setup.exe
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-release\SUSModder-release-Setup.exe" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/release/" -AcceptKey

# Upload Portable.zip
Set-SCPItem -ComputerName "vps-b99a39c3.vps.ovh.net" -Credential $credential `
    -Path "releases-release\SUSModder-release-Portable.zip" `
    -Destination "/srv/synapsekit-boracik/nginx/html/susmodder/releases/release/" -AcceptKey
```

**Prevention:**
- Zawsze sprawdź listę plików na serwerze po deploymencie
- Upewnij się że Setup.exe jest **podpisany** przed uploadem
- Weryfikacja: `ls -lh /srv/.../susmodder/releases/{channel}/ | grep -E 'Setup|Portable'`

**Note:** Setup.exe i Portable.zip mają nazwę z sufiksem kanału (np. `SUSModder-release-Setup.exe`, `SUSModder-beta-Setup.exe`), NIE z numerem wersji.