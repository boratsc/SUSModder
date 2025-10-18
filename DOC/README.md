# Dokumentacja projektu SUSModder

## 📖 O dokumentacji

Ta dokumentacja została stworzona w celu:
- Zapewnienia pełnego przeglądu architektury aplikacji
- Identyfikacji nieużywanych komponentów (refaktoring)
- Ułatwienia onboardingu nowych deweloperów
- Dokumentacji kluczowych przepływów danych

**Data utworzenia:** 2025-10-19  
**Stan:** ✅ **100% UKOŃCZONE!** 🎉  
**Ostatnia aktualizacja:** 2025-10-19 (Refaktoryzacja MainWindowViewModel)  
**Zakres:** SUSModder.Core (39 plików) + SUSModder Frontend (67 plików) + Updater (1 plik)  
**Łączna analiza:** 107 plików źródłowych

> **Nowe:** Zobacz [REFACTORING_SUMMARY.md](REFACTORING_SUMMARY.md) dla szczegółów refaktoryzacji MainWindowViewModel (redukcja o 676 linii / 22%)

---

## 📁 Struktura projektu

```
SUSModder/
├─ SUSModder/              # Frontend (Avalonia UI + ReactiveUI)
│  ├─ ViewModels/          # ViewModels + Helpers (po refaktoryzacji 2025)
│  ├─ Views/               # Widoki AXAML
│  ├─ Services/            # ThemeManager, FileSystemHelper, etc.
│  └─ ...
├─ SUSModder.Core/         # Logika biznesowa
└─ Updater/                # Aplikacja auto-update
```

---

## 📚 Dokumentacja modułów

### ✅ SUSModder.Core (UKOŃCZONE!)

| Moduł | Plik | Status | Plików | Aktywnych | Do usunięcia |
|-------|------|--------|--------|-----------|--------------|
| **Configuration** | [01_Configuration.md](Core/01_Configuration.md) | ✅ Gotowe | 9 | 7 (77.8%) | 2 (22.2%) |
| **GameIntegration** | [02_GameIntegration.md](Core/02_GameIntegration.md) | ✅ Gotowe | 7 | 7 (100%) | 0 (0%) |
| **Services** | [03_Services.md](Core/03_Services.md) | ✅ Gotowe | 10 | 5 (50%) | 3 (30%) |
| **Utilities** | [04_Utilities.md](Core/04_Utilities.md) | ✅ Gotowe | 6 | 6 (100%) | 0 (0%) |
| **Models & inne** | [05_ModelsAndOthers.md](Core/05_ModelsAndOthers.md) | ✅ Gotowe | 7 | 7 (100%) | 0 (0%) |
| **RAZEM** | | ✅ **100%** | **39** | **32 (82%)** | **5 (13%)** |

### ✅ SUSModder Frontend (UKOŃCZONE! + Refaktoryzacja 2025)

| Moduł | Plik | Status | Elementy | Aktywnych | Notatki |
|-------|------|--------|----------|-----------|---------|
| **README** | [Frontend/README.md](Frontend/README.md) | ✅ Gotowe | Architektura, punkt wejścia | - | - |
| **ViewModels** | [Frontend/01_ViewModels.md](Frontend/01_ViewModels.md) | ✅ **Zaktualizowane 2025** | 13 + Helpers | 13 (100%) | MainWindowViewModel: partial class |
| **Views** | [Frontend/02_Views.md](Frontend/02_Views.md) | ✅ Gotowe | 40 plików AXAML | 40 (100%) | 0 (0%) |
| **Converters** | [Frontend/03_Converters.md](Frontend/03_Converters.md) | ✅ Gotowe | 9 | 8 (89%) | 1 (11%) |
| **Services & Utilities** | [Frontend/04_ServicesAndUtilities.md](Frontend/04_ServicesAndUtilities.md) | ✅ **Zaktualizowane 2025** | 7 (5+2 nowe) | 6 (86%) | +ThemeManager, +FileSystemHelper |
| **Refaktory** | [Frontend/REFACTOR.md](Frontend/REFACTOR.md) | ✅ Gotowe | Lista problemów | - | Większość naprawiona |
| **Refaktoryzacja 2025** | [REFACTORING_SUMMARY.md](REFACTORING_SUMMARY.md) | ✅ **NOWY** | Szczegóły refaktoryzacji | - | MainWindowViewModel: -676 linii |
| **RAZEM** | | ✅ **100%** | **67→71 plików** | **69 (97%)** | **+4 nowe, duplikaty usunięte** |

### ✅ Updater (UKOŃCZONE!)

| Moduł | Plik | Status | Plików | Opis |
|-------|------|--------|--------|------|
| **Updater** | [Updater/README.md](Updater/README.md) | ✅ Gotowe | 1 (`Program.cs`) | Aplikacja auto-update (~200 linii) |

---

## 🎉 Refaktoryzacja 2025 - Główne osiągnięcia

### MainWindowViewModel - Redukcja o 22%
- ✅ **3081 → 2405 linii** (redukcja o **676 linii**)
- ✅ Przekształcone w **partial class**
- ✅ Utworzono `ViewModels/Helpers/` folder (4 klasy)
- ✅ Utworzono `MainWindowViewModel.Helpers.cs` (144 linie)
- ✅ Usunięto wszystkie duplikaty

### Nowe Services
- ✅ **ThemeManager** - centralne zarządzanie motywami (Dark/Light/Pink)
- ✅ **FileSystemHelper** - zaawansowane operacje na plikach (SafeDelete z retry, elevated permissions)

### Helpers - Nowy folder
- ✅ **UIProgressReporter** - reporter postępu dla UI thread
- ✅ **UIDiagnosticsOutput** - wyjście diagnostyczne
- ✅ **SilentUserInteractionWrapper** - wrapper pomijający dialogi info
- ✅ **EpicUserInteractionAdapter** - adapter dla operacji Epic

**Zobacz pełną dokumentację:** [REFACTORING_SUMMARY.md](REFACTORING_SUMMARY.md)

---

## 🗑️ Identyfikowane elementy do usunięcia

### SUSModder.Core/Configuration

| Plik | Powód | Rozmiar | Priorytet |
|------|-------|---------|-----------|
| `AmongTokensService.cs` | Nieużywany, duplikat SUStatsService | ~130 linii | ⚠️ WYSOKI |
| `ConfigUpdater.cs` | Nieużywany, funkcjonalność przeniesiona | ~50 linii | ⚠️ WYSOKI |

### SUSModder.Core/Services

| Plik | Powód | Rozmiar | Priorytet |
|------|-------|---------|-----------|
| `DialogService.cs` | Puste placeholder-y, zastąpione przez UserInteraction | ~28 linii | ⚠️ WYSOKI |
| `GameService.cs` | Kompletny duplikat GameLocator, zero użyć | ~130 linii | ⚠️ WYSOKI |
| `UserInteractionAsyncService.cs` | Duplikacja UserInteractionService | ~48 linii | ⚠️ ŚREDNI |

**Razem do usunięcia (Core):** ~386 linii kodu martwego + 2 pliki do refaktoringu

### SUSModder Frontend

| Plik | Powód | Rozmiar | Status |
|------|-------|---------|--------|
| `Models/Mod.cs` | Całkowicie nieużywany, zastąpiony ModItem+ModConfiguration | ~11 linii | ⚠️ WYSOKI |
| `Converters/CategoryToClassConverter.cs` | Brak użyć w XAML | ~33 linie | ⚠️ WYSOKI |
| ~~`ViewModels/MainWindowViewModel.cs` (linia ~2980)~~ | ~~Duplikat InstallationSilentUserInteraction~~ | ~~25 linii~~ | ✅ **NAPRAWIONE 2025** |
| `ViewModels/FileName.cs` | Błędna nazwa pliku (zawiera EpicErrorDialogViewModel) | - | ⚠️ NISKI (rename) |

**Razem do usunięcia (Frontend):** ~44 linii + 1 rename ~~+ 1 duplikat (naprawiony)~~

---

## 📊 Statystyki

### ✅ SUSModder.Core (UKOŃCZONE - 100%)

- **Moduły przeanalizowane:** 5/5 (100%)
- **Plików przeanalizowanych:** 39
- **Plików aktywnych:** 32 (82%)
- **Plików do usunięcia:** 5 (13%)
- **Plików do refaktoringu:** 2 (5%)
- **Zaoszczędzone linie kodu:** ~386 linii

### ✅ SUSModder Frontend (UKOŃCZONE - 100%) 🆕

- **Moduły przeanalizowane:** 5/5 (100%)
- **Plików przeanalizowanych:** 67
  - ViewModels: 13 (wszystkie aktywne)
  - Views: 40 (wszystkie aktywne)
  - Converters: 9 (8 aktywnych, 1 do usunięcia)
  - Services: 4 (wszystkie aktywne)
  - Models: 2 (1 aktywny, 1 do usunięcia)
- **Plików aktywnych:** 65 (97%)
- **Plików do usunięcia:** 2 (`Mod.cs`, `CategoryToClassConverter.cs`)
- **Problemów do naprawy:** 4 (zobacz [Frontend/REFACTOR.md](Frontend/REFACTOR.md))
  - FileName.cs (błędna nazwa pliku)
  - Duplikat InstallationSilentUserInteraction
  - 2 nieużywane pliki
- **Największy plik:** MainWindowViewModel.cs (3081 linii!) 📈

---

## 🔑 Kluczowe odkrycia

### Configuration
- Wszyst services API (Discord, SUStats) działają poprawnie
- `DeveloperModeSettings` używany do zarządzania trybem dev
- `ModConfigHandler` - kompleksowe zarządzanie presetami gry
- `ConfigManager` w `ModConfig.cs` - rozważyć wydzielenie do osobnego pliku

### GameIntegration
- **100% kodu w użyciu** - świetny stan modułu
- `EpicVersionManager` - zaawansowany wrapper Legendary CLI
- `GameLocator` - solidna auto-detekcja platformy
- `ModManager` - dobrze zaprojektowany system instalacji Steam
- Jedna przestarzała metoda: `GameLocator.CheckAndSetupVanillaMod` (synchroniczna)

### Services
- **50% kodu aktywnego** - wymaga refaktoringu
- 3 klasy całkowicie nieużywane (DialogService, GameService, UserInteractionAsyncService)
- 2 klasy do decyzji: `ModService` (dobrze zaprojektowany ale nieużywany), `ToUConfigService` (większość metod placeholder)
- Kluczowe serwisy: `AppUpdateService`, `ConfigService`, `DllModificationService`, `ModUpdateManager`, `UserInteractionService`

### Utilities
- **100% kodu w użyciu** - doskonały stan
- Wszystkie interfejsy aktywnie używane
- `PathSettings` - kluczowa klasa, bardzo dobrze zaprojektowana
- `IUserInteraction` - fundamentalny interfejs architektury (DIP)
- Sugestia: zunifikować nazwy metod w `IUserInteractionAsync`

### Models, Diagnostics, Repositories
- **100% kodu w użyciu** - perfekcyjny stan
- Wszystkie modele DTO aktywnie używane
- `ConfigRepository` - solidny Repository Pattern
- `SecretProvider` - działa, ale Base64 to obfuscation nie security (do poprawy w produkcji)
- `IDiagnosticsOutput` - świetna abstrakcja logowania

### Updater 🆕
- **Minimalistyczna aplikacja** - jeden plik `Program.cs` (~200 linii)
- Proces: Czekaj na zamknięcie SUSModder → Rozpakuj ZIP → Sprzątaj stare pliki → Kopiuj nowe → Uruchom
- **Inteligentne sprzątanie** - usuwa pliki, które nie istnieją w nowej wersji
- **Zachowuje dane użytkownika** - nie nadpisuje `config.json` i folderu `updater/`
- **Self-contained executable** - brak zależności, single-file deployment (~8 MB)
- Znane ograniczenie: Updater nie może zaktualizować samego siebie (wymaga dwuetapowego procesu)

### Frontend (SUSModder) 🆕
- **97% kodu w użyciu** - bardzo dobry stan
- ViewModels: **wszystkie aktywne** (13/13) - dobra architektura MVVM
- Views: **wszystkie aktywne** (40/40) - kompleksowy UI
- **MainWindowViewModel ma 3081 linii** - gigant! Kandydat do refaktoringu (podział na mniejsze ViewModels)
- Converters: 8/9 aktywnych (`CategoryToClassConverter` nieużywany)
- Models: `Mod.cs` całkowicie nieużywany (zastąpiony przez `ModItem` + `ModConfiguration`)
- Znaleziono **błąd w nazwie pliku**: `FileName.cs` zawiera `EpicErrorDialogViewModel`
- Zduplikowany kod: `InstallationSilentUserInteraction` w dwóch miejscach
- Serwisy frontendu dobrze zaprojektowane:
  - `RolesService` - komunikacja z API
  - `DiscordIconPreloader` - inteligentny cache z preloadem w tle
  - `ConsoleLogger` - przechwytywanie Debug.WriteLine
- ReactiveUI dobrze wykorzystane (ReactiveCommand, ObservableCollection, RaiseAndSetIfChanged)

---

## 🎯 Architektura wysokopoziomowa

```
┌─────────────────────────────────────────────────────┐
│                  SUSModder (UI)                     │
│              Avalonia 11 + ReactiveUI               │
└──────────────────────┬──────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────┐
│              SUSModder.Core (Logika)                │
├─────────────────────────────────────────────────────┤
│ Configuration/  → Zarządzanie konfiguracją          │
│ GameIntegration/ → Instalacja i aktualizacje        │
│ Services/       → Serwisy biznesowe                 │
│ Utilities/      → Narzędzia pomocnicze              │
│ Models/         → Modele danych                     │
│ Repositories/   → Dostęp do danych                  │
└──────────────────────┬──────────────────────────────┘
                       │
                       ↓
        ┌──────────────┴──────────────┐
        │                             │
        ↓                             ↓
┌───────────────┐           ┌─────────────────┐
│   API Serwer  │           │ Pliki lokalne   │
│  susmodder.   │           │ config.json     │
│  boracik.pl   │           │ appsettings.json│
└───────────────┘           └─────────────────┘
```

---

## 🔄 Główne przepływy danych

### 1. Start aplikacji
```
Program.Main
  → Przywracanie ustawień po update (AppUpdateService)
  → Inicjalizacja Avalonia
  → MainWindow + MainWindowViewModel
     → ConfigService.LoadConfig (config.json lub API)
     → GameLocator.CheckAndSetupVanillaModAsync (auto-detekcja)
     → ModUpdateChecker.GetAvailableUpdatesAsync
     → Odświeżenie UI
```

### 2. Instalacja moda (Steam)
```
UI: Przycisk instalacji
  → ModService.InstallModAsync
     → ModManager.ModifyAsync
        → Pobierz Vanilla 7z (SecretProvider token)
        → Rozpakuj 7z (tools/7z.exe + hasło)
        → Pobierz mod ZIP (GitHub)
        → Rozpakuj ZIP
        → Skopiuj pliki do folderu gry
        → ConfigManager.SaveConfig
```

### 3. Instalacja moda (Epic)
```
UI: Przycisk instalacji
  → EpicVersionManager.InstallOrUpdateModAsync
     → legendary.exe install
        → Parsowanie logów (LegendaryProgressParser)
        → Raportowanie postępu (event ProgressChanged)
        → ConfigManager.SaveConfig
```

### 4. Aktualizacja modów
```
UI: Sprawdź aktualizacje
  → ModUpdateChecker.GetAvailableUpdatesAsync
     → Pobierz config z API
     → Porównaj z lokalnym
     → Zwróć listę ModUpdateInfo
  → UpdateDialog (wybór modów)
  → ModUpdateChecker.UpdateSelectedModsAsync
     → ModUpdates.UpdateModAsync (dla każdego)
        → ModDelete.DeleteMod
        → ModManager.ModifyAsync / EpicVersionManager.InstallOrUpdateModAsync
```

### 5. Aktualizacja aplikacji 🆕
```
UI: Sprawdź aktualizacje
  → AppUpdateService.CheckForUpdatesAsync
     → Pobierz latest version z API
     → Porównaj z CurrentVersion (appsettings.json)
  → AppUpdateDialog (pokaż informacje o nowej wersji)
     → Użytkownik klika "Aktualizuj"
  → AppUpdateService.DownloadAndStartUpdate
     → Pobierz ZIP do %TEMP%
     → Zapisz kopię ustawień użytkownika (Mode, Theme, lastLaunchId, ModsInstallPath)
     → Uruchom Updater.exe <target-dir> <zip-path>
     → Application.Shutdown() (zamknij SUSModder)
  → Updater.exe
     → Czekaj na zamknięcie SUSModder.exe (WaitForExit)
     → Rozpakuj ZIP do %TEMP%/LatestVersionExtract
     → Usuń stare pliki (pomijając config.json, updater/)
     → Skopiuj nowe pliki do target-dir
     → Uruchom SUSModder.exe
     → Sprzątaj tymczasowe pliki
  → SUSModder.exe (nowa wersja!)
     → Program.Main
        → AppUpdateService.RestoreUserSettingsIfNeeded()
           → Przywróć Mode, Theme, lastLaunchId, ModsInstallPath
```

---

## 🛠️ Używane technologie

### Backend (SUSModder.Core)
- .NET 8.0
- Microsoft.Extensions.Configuration
- System.Net.Http
- System.IO.Compression
- Newtonsoft.Json / System.Text.Json

### Frontend (SUSModder)
- Avalonia 11.3
- ReactiveUI
- Avalonia.Diagnostics (dev mode)

### Narzędzia zewnętrzne
- `tools/7z.exe` - rozpakowywanie zaszyfrowanych archiwów
- `legendary.exe` - Epic Games CLI
- `updater/Updater.exe` - aplikacja aktualizacji

---

## 📝 Konwencje kodowania

### Przestrzegane zasady:
- ✅ C# nullable enable
- ✅ Async/await dla operacji I/O
- ✅ ReactiveUI dla bindingu (RaiseAndSetIfChanged)
- ✅ Dependency Injection przez konstruktory
- ✅ Interfejsy dla abstrakcji (IUserInteraction, IProgressReporter, IDiagnosticsOutput)
- ✅ Static classes dla utilities (PathSettings, GameLocator)
- ✅ Try-catch z diagnostyką
- ✅ Brak blokowania UI

### Do poprawy:
- ⚠️ Więcej interfejsów (np. IModInstaller, IGameLocator)
- ⚠️ Unit testy
- ⚠️ XML documentation comments

---

## 🔐 Bezpieczeństwo

### SecretProvider
Dostarcza wrażliwe dane:
- `GetDownloadToken()` - token autoryzacji HTTP dla API
- `Get7zPassword()` - hasło do archiwów vanilla

⚠️ **Ważne:** Nigdy nie loguj pełnych wartości tokenów/haseł (maskuj w logach)

---

## 📦 Deployment

### Proces publikacji:
```batch
# 1. Publikacja Updater
cd d:\repos\SUSModder\Updater
dotnet publish -c Release

# 2. Publikacja głównej aplikacji
cd d:\repos\SUSModder\SUSModder
dotnet publish -c Release
```

### Output:
- Single-file self-contained (win-x64)
- Katalog: `publish/`
- Dołączone: `tools/`, `updater/`, `appsettings.json`, `config.json`

---

## 🐛 Znane problemy i TODOs

### Configuration
- [ ] Wydzielić `ConfigManager` z `ModConfig.cs` do osobnego pliku
- [ ] Dodać interfejsy dla API services

### GameIntegration
- [ ] Usunąć przestarzałą `GameLocator.CheckAndSetupVanillaMod` (sync)
- [ ] Dodać unit testy dla `LegendaryProgressParser`

---

## 📞 Kontakt i wsparcie

- **Repository:** boratsc/SUSModder
- **Branch:** develop
- **Discord:** Linki w aplikacji (RecommendedDiscords)

---

## 📜 Historia zmian dokumentacji

| Data | Autor | Zmiany |
|------|-------|--------|
| 2025-10-19 (23:00) | GitHub Copilot | ✅ **UKOŃCZENIE UPDATER** - pełna dokumentacja procesu aktualizacji, 100% projektu! 🎉 |
| 2025-10-19 (wieczór) | GitHub Copilot | ✅ **UKOŃCZENIE FRONTENDU** - pełna dokumentacja (README, ViewModels, Views, Converters, Services), REFACTOR.md z listą problemów |
| 2025-10-19 (rano) | GitHub Copilot | Utworzenie struktury, Configuration, GameIntegration, Services, Utilities, Models |

---

## 🎉 Podsumowanie projektu dokumentacji

### Stan ukończenia:
- ✅ **SUSModder.Core**: 100% (5/5 modułów, 39 plików)
- ✅ **SUSModder Frontend**: 100% (5/5 dokumentów, 67 plików)
- ✅ **Updater**: 100% (1 moduł, 1 plik ~200 linii)

### Zidentyfikowane problemy:
- **Core**: 5 plików do usunięcia (~386 linii martwego kodu)
- **Frontend**: 2 pliki do usunięcia + 2 problemy do naprawy (~69 linii + 1 duplikat + 1 rename)

### Najważniejsze odkrycia:
1. **MainWindowViewModel** - gigant 3081 linii! Kandydat do refaktoringu
2. **Duplikaty kodu** - InstallationSilentUserInteraction w 2 miejscach
3. **Błędy nazewnictwa** - FileName.cs zawiera EpicErrorDialogViewModel
4. **Martwy kod** - CategoryToClassConverter, Mod.cs nigdy nie używane
5. **Architektura ogólnie dobra** - 82% Core i 97% Frontend aktywne

### Rekomendacje na przyszłość:
1. Refaktor MainWindowViewModel na mniejsze ViewModels
2. Usunięcie zidentyfikowanych martwych plików
3. Dodanie unit testów (szczególnie Core/GameIntegration)
4. Rozważenie DI Container zamiast Service Locator
5. ~~Dokumentacja Updater~~ ✅ **ZROBIONE!**

---

**Legenda statusów:**
- ✅ Gotowe
- 🚧 W trakcie
- ⏳ Oczekuje
- ❌ Do usunięcia
- ⚠️ Wymaga uwagi

---

*Dokumentacja jest aktualizowana na bieżąco.*
