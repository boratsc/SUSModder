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
- `appsettings.json` – sekcje ważne dla AI:
	- Configuration:
		- Mode: "steam" lub "epic" (bieżący tryb gry; może zostać nadpisany auto-detekcją GameLocator).
		- BaseUrl: bazowy URL API (np. https://susmodder.boracik.pl).
		- UpdateServerUrl, CurrentVersion, lastLaunchId, Theme.
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

**Automatyczny deployment (REKOMENDOWANE):**
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
│       ├── release/           ← Release channel (.nupkg, RELEASES)
│       └── beta/              ← Beta channel (.nupkg, RELEASES)
├── susmodder-velopack/
│   ├── releases.release.json  ← Manifest dla API
│   └── releases.beta.json     ← Manifest dla API
└── susmodder-versions/
    └── SUSModder-2.2.0.zip     ← Legacy dla użytkowników 2.0.2 i starszych
```

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

## Publikacja Legacy (dla kompatybilności wstecznej - depreciated)

**Uwaga:** Ta metoda jest przestarzała. Używaj `build-release-2.2.0.ps1` zamiast tego.

# Publikacja Updater (jeśli potrzebny osobno)
cd d:\Development\SUSModder\Updater
dotnet publish -c Release

# Publikacja głównej aplikacji (single-file - tylko dev)
cd d:\Development\SUSModder\SUSModder
dotnet publish -c Release