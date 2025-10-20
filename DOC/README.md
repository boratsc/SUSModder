# Dokumentacja projektu SUSModder

## 📖 O dokumentacji

Ta dokumentacja została stworzona w celu:
- Zapewnienia pełnego przeglądu architektury aplikacji
- Identyfikacji nieużywanych komponentów (refaktoring)
- Ułatwienia onboardingu nowych deweloperów
- Dokumentacji kluczowych przepływów danych

**Data utworzenia:** 2025-10-19
**Stan:** ✅ **100% UKOŃCZONE + ZOPTYMALIZOWANE!** 🎉
**Ostatnia aktualizacja:** 2025-10-21 (Optymalizacje Frontend + HttpClient Fix)
**Zakres:** SUSModder.Core (32 plików aktywnych) + SUSModder Frontend (71 plików) + Updater (1 plik)
**Łączna analiza:** 104 pliki źródłowe

> **Najnowsze (2025-10-21):**
> - ✅ Usunięto martwy kod (10 plików, ~500 linii)
> - ✅ HttpClient Anti-pattern Fix (4 serwisy)
> - ✅ MainWindowViewModel: 2799 → 371 linii (-86.7%)
> - ✅ Nowe style XAML (3 pliki, 158 linii)
> - Zobacz [CORE_REFACTORING_SUMMARY.md](CORE_REFACTORING_SUMMARY.md) i [FRONTEND_REFACTORING_SUMMARY.md](FRONTEND_REFACTORING_SUMMARY.md)

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

### MainWindowViewModel - Redukcja o 86.7%! 🚀
- ✅ **2799 → 371 linii** (redukcja o **2428 linii / 86.7%**)
- ✅ Podzielone na **11 partial classes**
- ✅ Separated Concerns - każda klasa ma jasno określoną odpowiedzialność
- ✅ Utworzono `ViewModels/Helpers/` folder (4 klasy)
- ✅ Usunięto wszystkie duplikaty

**Partial classes:**
1. `MainWindowViewModel.cs` - główna klasa (properties, commands)
2. `MainWindowViewModel.AppSettings.cs` - zarządzanie ustawieniami
3. `MainWindowViewModel.Dialogs.cs` - okna dialogowe
4. `MainWindowViewModel.DllManagement.cs` - zarządzanie DLL modami
5. `MainWindowViewModel.ExternalActions.cs` - akcje zewnętrzne
6. `MainWindowViewModel.GameLaunch.cs` - uruchamianie gry
7. `MainWindowViewModel.Helpers.cs` - metody pomocnicze
8. `MainWindowViewModel.Initialization.cs` - inicjalizacja
9. `MainWindowViewModel.ModOperations.cs` - operacje na modach
10. `MainWindowViewModel.ThemeManagement.cs` - zarządzanie motywami
11. `MainWindowViewModel.Updates.cs` - aktualizacje modów

### HttpClient Anti-pattern Fix
- ✅ **RolesService** - HttpClient instance → static readonly
- ✅ **DllModificationService** - HttpClient instance → static readonly
- ✅ **SUStatsService** - HttpClient instance → static readonly + usunięto IDisposable
- ✅ **DiscordFavoritesService** - HttpClient instance → static readonly + usunięto IDisposable
- ✅ Zgodne z Microsoft best practices
- ✅ Brak socket exhaustion

### Usunięty martwy kod
- ✅ **Core:** 8 plików usunięte (~500 linii)
  - AmongTokensService.cs, ConfigUpdater.cs, DialogService.cs
  - GameService.cs, ModService.cs, ToUConfigService.cs
  - UserInteractionAsyncService.cs, IUserInteractionAsync.cs
- ✅ **Frontend:** 2 pliki usunięte (44 linie)
  - Models/Mod.cs, Converters/CategoryToClassConverter.cs
- ✅ **Rename:** FileName.cs → EpicErrorDialogViewModel.cs

### Nowe Style XAML (158 linii)
- ✅ **ModCardStyle.axaml** - style kart modów + selected state + hover
- ✅ **MenuButtonStyle.axaml** - style przycisków menu
- ✅ **PanelStyles.axaml** - style paneli szczegółów

### Nowe Utility (Core)
- ✅ **FileSystemUtilities** - zaawansowane operacje na plikach (SafeDelete z elevated permissions, retry logic)

**Zobacz pełną dokumentację:**
- [CORE_REFACTORING_SUMMARY.md](CORE_REFACTORING_SUMMARY.md)
- [FRONTEND_REFACTORING_SUMMARY.md](FRONTEND_REFACTORING_SUMMARY.md)

---

## 🗑️ ~~Identyfikowane elementy do usunięcia~~ ✅ USUNIĘTE (2025-10-21)

### ✅ SUSModder.Core/Configuration - USUNIĘTE

| Plik | Powód | Rozmiar | Status |
|------|-------|---------|--------|
| ~~`AmongTokensService.cs`~~ | ~~Nieużywany, duplikat SUStatsService~~ | ~~~130 linii~~ | ✅ **USUNIĘTY** |
| ~~`ConfigUpdater.cs`~~ | ~~Nieużywany, funkcjonalność przeniesiona~~ | ~~~50 linii~~ | ✅ **USUNIĘTY** |

### ✅ SUSModder.Core/Services - USUNIĘTE

| Plik | Powód | Rozmiar | Status |
|------|-------|---------|--------|
| ~~`DialogService.cs`~~ | ~~Puste placeholder-y, zastąpione przez UserInteraction~~ | ~~~28 linii~~ | ✅ **USUNIĘTY** |
| ~~`GameService.cs`~~ | ~~Kompletny duplikat GameLocator, zero użyć~~ | ~~~130 linii~~ | ✅ **USUNIĘTY** |
| ~~`ModService.cs`~~ | ~~Duplikacja logiki, przeniesione do ViewModels~~ | ~~~200 linii~~ | ✅ **USUNIĘTY** |
| ~~`ToUConfigService.cs`~~ | ~~Przeniesione do ViewModels~~ | ~~~100 linii~~ | ✅ **USUNIĘTY** |
| ~~`UserInteractionAsyncService.cs`~~ | ~~Duplikacja UserInteractionService~~ | ~~~48 linii~~ | ✅ **USUNIĘTY** |

### ✅ SUSModder.Core/Utilities - USUNIĘTE

| Plik | Powód | Rozmiar | Status |
|------|-------|---------|--------|
| ~~`IUserInteractionAsync.cs`~~ | ~~Nieużywany interface~~ | ~~~15 linii~~ | ✅ **USUNIĘTY** |

**Usunięte (Core):** ~700 linii martwego kodu ✅

### ✅ SUSModder Frontend - USUNIĘTE

| Plik | Powód | Rozmiar | Status |
|------|-------|---------|--------|
| ~~`Models/Mod.cs`~~ | ~~Całkowicie nieużywany, zastąpiony ModItem+ModConfiguration~~ | ~~~11 linii~~ | ✅ **USUNIĘTY** |
| ~~`Converters/CategoryToClassConverter.cs`~~ | ~~Brak użyć w XAML~~ | ~~~33 linie~~ | ✅ **USUNIĘTY** |
| ~~`ViewModels/FileName.cs`~~ | ~~Błędna nazwa pliku~~ | - | ✅ **ZMIENIONO → EpicErrorDialogViewModel.cs** |

**Usunięte (Frontend):** ~44 linii + 1 rename ✅

---

## 📊 Statystyki

### ✅ SUSModder.Core (UKOŃCZONE + ZOPTYMALIZOWANE - 100%)

- **Moduły przeanalizowane:** 5/5 (100%)
- **Plików przeanalizowanych:** 39
- **Plików aktywnych:** 32 (82%)
- **Plików usunięte:** 8 (21%) ✅
- **Nowe pliki:** 1 (FileSystemUtilities.cs)
- **Zaoszczędzone linie kodu:** ~700 linii ✅
- **HttpClient Anti-pattern:** Naprawiono 3 serwisy ✅

### ✅ SUSModder Frontend (UKOŃCZONE + ZOPTYMALIZOWANE - 100%) 🆕

- **Moduły przeanalizowane:** 5/5 (100%)
- **MainWindowViewModel:** 2799 → 371 linii (-86.7%) ✅
- **Partial classes:** 11 ✅
- **Plików usunięte:** 2 ✅
- **Pliki zmienione nazwy:** 1 ✅
- **Nowe pliki stylów:** 3 (158 linii) ✅
- **HttpClient Anti-pattern:** Naprawiono 1 serwis ✅
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
