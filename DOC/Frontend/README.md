# Frontend SUSModder – Dokumentacja

## Spis treści
1. [O frontendzie](#o-frontendzie)
2. [Architektura](#architektura)
3. [Technologie](#technologie)
4. [Struktura katalogów](#struktura-katalogów)
5. [Punkt wejścia aplikacji](#punkt-wejścia-aplikacji)
6. [Dokumenty szczegółowe](#dokumenty-szczegółowe)

---

## O frontendzie

Frontend SUSModder to aplikacja desktopowa zbudowana w technologii **Avalonia UI 11** z wykorzystaniem wzorca **MVVM** (Model-View-ViewModel) oraz biblioteki **ReactiveUI** do reaktywnego programowania.

Aplikacja dostarcza interfejs użytkownika do:
- Zarządzania modami Among Us (instalacja, aktualizacja, usuwanie)
- Konfiguracji aplikacji (tryb gry Steam/Epic, ścieżki instalacji)
- Zarządzania modami DLL
- Konfiguracji narzędzi (SUStats, AmongToken)
- Dostępu do społeczności (linki Discord, social media)
- Przeglądania ról i umiejętności w modach

---

## Architektura

### Wzorzec MVVM
Aplikacja stosuje wzorzec **Model-View-ViewModel**:

- **Models** (`SUSModder/Models/`): Klasy reprezentujące dane domenowe (np. `Role`)
- **Views** (`SUSModder/Views/`): Pliki AXAML + code-behind definiujące interfejs użytkownika
- **ViewModels** (`SUSModder/ViewModels/`): Warstwa logiki prezentacji, wiązanie danych, komendy

### ReactiveUI
ReactiveUI zapewnia:
- **Reaktywne właściwości**: `RaiseAndSetIfChanged` do automatycznej notyfikacji o zmianach
- **Komendy**: `ReactiveCommand` do obsługi akcji użytkownika
- **Obserwowalne kolekcje**: `ObservableCollection<T>` do dynamicznych list

### Komunikacja z logiką biznesową
Frontend komunikuje się z warstwą Core (`SUSModder.Core`) poprzez serwisy:
- `ConfigService` – zarządzanie konfiguracjami modów
- `ModService` – operacje na modach (instalacja, usuwanie)
- `DllModificationService` – zarządzanie DLL
- `AppUpdateService` – aktualizacje aplikacji
- `ToUConfigService` – konfiguracja Town of Us
- `GameLocator` – lokalizacja gry Among Us

---

## Technologie

### Główne zależności
- **.NET 8.0** – platforma runtime
- **Avalonia 11.3** – framework UI (cross-platform XAML)
- **ReactiveUI 20.x** – framework MVVM + reaktywne programowanie
- **Microsoft.Extensions.Configuration** – zarządzanie konfiguracją

### Biblioteki pomocnicze
- **Avalonia.Themes.Fluent** – motyw Fluent Design
- **Avalonia.ReactiveUI** – integracja ReactiveUI z Avalonią
- **System.Reactive** – reactive extensions (LINQ to Events)

---

## Struktura katalogów

```
SUSModder/
├── App.axaml(.cs)                    # Główna aplikacja, inicjalizacja
├── Program.cs                         # Punkt wejścia, konfiguracja AppBuilder
├── ViewLocator.cs                     # Lokalizacja View na podstawie ViewModel
│
├── ViewModels/                        # Warstwa logiki prezentacji
│   ├── MainWindowViewModel.cs        # Główny ViewModel aplikacji (3081 linii!)
│   ├── AppSettingsViewModel.cs       # Ustawienia aplikacji
│   ├── DllModSelectionViewModel.cs   # Selekcja modów DLL
│   ├── SUStatsConfigViewModel.cs     # Konfiguracja SUStats
│   ├── RecommendedDiscordsViewModel.cs # Lista polecanych serwerów Discord
│   ├── DiscordServerViewModel.cs     # Pojedynczy serwer Discord
│   ├── AmongTokenViewModel.cs        # Token AmongUs
│   ├── ModItem.cs                    # Element moda w UI (adapter)
│   ├── ModItemAdapter.cs             # Konwersja ModConfiguration <-> ModItem
│   ├── PresetFileItem.cs             # Plik presetu (ToU)
│   ├── SavedConfigItem.cs            # Zapisana konfiguracja
│   ├── ViewModelBase.cs              # Bazowa klasa dla ViewModels
│   └── FileName.cs                   # ⚠️ BŁĘDNA NAZWA – zawiera EpicErrorDialogViewModel
│
├── Views/                             # Interfejs użytkownika (AXAML + code-behind)
│   ├── MainWindow.axaml(.cs)         # Główne okno aplikacji
│   ├── InfoPanel.axaml(.cs)          # Panel informacyjny (social media)
│   ├── AdditionalActionsPanel.axaml(.cs) # Panel dodatkowych akcji (ToU)
│   │
│   ├── Dialogi ogólne:
│   │   ├── ConfirmDialog.axaml(.cs)     # Dialog potwierdzenia (Tak/Nie)
│   │   ├── MessageDialog.axaml(.cs)     # Dialog informacyjny (OK)
│   │   ├── PromptDialog.axaml(.cs)      # Dialog z polem tekstowym
│   │   ├── ErrorDialog.axaml(.cs)       # Dialog błędu
│   │   └── EpicErrorDialog.axaml(.cs)   # Dialog błędu Epic z logiem
│   │
│   ├── Dialogi aktualizacji:
│   │   ├── UpdateDialog.axaml(.cs)      # Lista aktualizacji modów
│   │   └── AppUpdateDialog.axaml(.cs)   # Aktualizacja aplikacji
│   │
│   ├── Okna konfiguracyjne:
│   │   ├── AppSettingsWindow.axaml(.cs) # Ustawienia aplikacji
│   │   ├── SUStatsConfigWindow.axaml(.cs) # Konfiguracja SUStats
│   │   ├── DllModSelectionView.axaml(.cs) # Wybór modów DLL
│   │   └── RecommendedDiscordsWindow.axaml(.cs) # Polecane serwery Discord
│   │
│   ├── Okna specjalistyczne:
│   │   ├── RolesWindow.axaml(.cs)       # Lista ról w modzie
│   │   ├── RoleDetailWindow.axaml(.cs)  # Szczegóły roli
│   │   ├── ConsoleWindow.axaml(.cs)     # Konsola debug (tryb developerski)
│   │   ├── HashDisplayDialog.axaml(.cs) # Wyświetlanie hashu pliku
│   │   └── LobbySetDialog.axaml(.cs)    # Ustawienie wielkości lobby
│   │
│   └── Dialogi ToU:
│       ├── LoadServerConfigDialog.axaml(.cs) # Wczytanie konfiguracji z serwera
│       ├── SUStatsConfirmDialog.axaml(.cs)   # Potwierdzenie SUStats
│       └── ChangePresetNamesDialog.axaml(.cs) # Zmiana nazw presetów
│
├── Converters/                        # Konwertery wartości dla bindingów XAML
│   ├── AsyncUrlToBitmapConverter.cs   # Async ładowanie obrazków z URL
│   ├── StringToBitmapConverter.cs     # String (nazwa pliku) -> Bitmap
│   ├── PathShorteningConverter.cs     # Skracanie długich ścieżek
│   ├── InstallStatusToOpacityConverter.cs # InstallPath -> Opacity (badge)
│   ├── GreaterThanConverter.cs        # Liczba > Parameter -> bool
│   ├── StringNotNullOrEmptyToBoolConverter.cs # String -> bool (visibility)
│   ├── UrlToCommandConverter.cs       # URL -> ReactiveCommand (otwórz link)
│   ├── ThemeColorConverter.cs         # Kolor motywu
│   └── CategoryToClassConverter.cs    # ⚠️ NIEUŻYWANY – do usunięcia
│
├── Services/                          # Serwisy pomocnicze frontendu
│   ├── RolesService.cs                # Pobieranie ról z API
│   ├── DiscordIconPreloader.cs        # Preloadowanie ikon Discord
│   ├── ConsoleLogger.cs               # Logger do okna konsoli (dev mode)
│   └── InstallationSilentUserInteraction.cs # ⚠️ ZDUPLIKOWANY (również w MainWindowViewModel)
│
├── Models/                            # Modele danych UI
│   ├── Role.cs                        # Model roli i umiejętności
│   └── Mod.cs                         # ⚠️ NIEUŻYWANY – do usunięcia
│
├── Styles/                            # Style XAML
│   ├── AnimationStyles.axaml          # Animacje (fade in/out)
│   ├── LinkButtonStyle.axaml          # Style przycisków-linków
│   └── ListBoxStyles.axaml            # Style list i ListBoxów
│
├── Themes/                            # Motywy kolorystyczne
│   ├── DarkTheme.axaml                # Ciemny motyw
│   ├── LightTheme.axaml               # Jasny motyw
│   └── PinkTheme.axaml                # Różowy motyw
│
├── Assets/                            # Zasoby graficzne (ikony, obrazki)
├── Graphics/UI/                       # Grafiki UI
└── tools/                             # Narzędzia (np. 7z.exe)
```

---

## Punkt wejścia aplikacji

### 1. `Program.cs`
```csharp
public static void Main(string[] args)
{
    // Przywracanie ustawień użytkownika po aktualizacji
    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    AppUpdateService.RestoreUserSettingsIfNeeded(appSettingsPath, null);

    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .UseReactiveUI();
```

**Zadania:**
- Przywrócenie ustawień użytkownika po aktualizacji (`AppUpdateService.RestoreUserSettingsIfNeeded`)
- Konfiguracja buildera Avalonia (platform detection, ReactiveUI)
- Uruchomienie aplikacji w trybie desktop

### 2. `App.axaml.cs`
```csharp
public override void Initialize()
{
    AvaloniaXamlLoader.Load(this);
}

public override void OnFrameworkInitializationCompleted()
{
    ConsoleLogger.Initialize();
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };
    }

    base.OnFrameworkInitializationCompleted();
}
```

**Zadania:**
- Ładowanie zasobów XAML (`AvaloniaXamlLoader.Load`)
- Inicjalizacja loggera konsoli (`ConsoleLogger.Initialize`)
- Utworzenie głównego okna `MainWindow` z `MainWindowViewModel`

### 3. `MainWindow` + `MainWindowViewModel`
- `MainWindow.axaml` definiuje strukturę UI
- `MainWindowViewModel` (3081 linii!) zarządza stanem aplikacji, komendami, listami modów

**MainWindowViewModel – kluczowe odpowiedzialności:**
- Ładowanie i odświeżanie listy modów
- Auto-detekcja gry Among Us (`GameLocator`)
- Instalacja/usuwanie modów (full i DLL)
- Sprawdzanie aktualizacji modów i aplikacji
- Zarządzanie motywami UI
- Obsługa dodatkowych akcji (ToU: Fix Black Screen, Lobby Size)
- Wyświetlanie dialogów i paneli pomocniczych

---

## Dokumenty szczegółowe

1. **[01_ViewModels.md](01_ViewModels.md)** – szczegółowy opis wszystkich ViewModels, ich właściwości, komend i odpowiedzialności
2. **[02_Views.md](02_Views.md)** – dokumentacja widoków (okna, dialogi, panele), struktura XAML, bindingi
3. **[03_Converters.md](03_Converters.md)** – opis konwerterów wartości używanych w bindingach XAML
4. **[04_ServicesAndUtilities.md](04_ServicesAndUtilities.md)** – serwisy pomocnicze frontendu (RolesService, DiscordIconPreloader, ConsoleLogger, ViewLocator)
5. **[REFACTOR.md](REFACTOR.md)** – lista przestarzałych elementów do usunięcia/refaktoryzacji

---

## Ważne uwagi

### ⚠️ Problemy do naprawy (zobacz [REFACTOR.md](REFACTOR.md)):
1. **`Models/Mod.cs`** – klasa nieużywana, do usunięcia
2. **`Converters/CategoryToClassConverter.cs`** – konwerter nieużywany, do usunięcia
3. **`ViewModels/FileName.cs`** – błędna nazwa pliku (powinno być `EpicErrorDialogViewModel.cs`)
4. **Zduplikowana klasa `InstallationSilentUserInteraction`** – w `Services/` oraz na końcu `MainWindowViewModel.cs` (linia ~2980)

### 🎯 Best practices
- **Async/await**: wszystkie operacje I/O są asynchroniczne
- **UI thread safety**: używaj `Dispatcher.UIThread.InvokeAsync` dla operacji UI
- **Bindingi**: preferuj bindingi XAML zamiast bezpośredniej manipulacji UI w code-behind
- **ReactiveCommand**: używaj do obsługi akcji użytkownika (zamiast event handlerów)
- **Nullability**: `#nullable enable` w całym projekcie

---

## Statystyki projektu

| Kategoria | Liczba | Opis |
|-----------|--------|------|
| **ViewModels** | 13 | Główny: `MainWindowViewModel` (3081 linii!) |
| **Views** | 40 | Okna, dialogi, panele |
| **Converters** | 9 | 8 używanych + 1 do usunięcia |
| **Services** | 3 | RolesService, DiscordIconPreloader, ConsoleLogger |
| **Models** | 2 | Role (używany), Mod (nieużywany) |
| **Themes** | 3 | Dark, Light, Pink |

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Wersja:** 1.0  
**Status:** Wersja robocza – weryfikacja w toku
