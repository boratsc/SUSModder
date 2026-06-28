# Architektura Systemu Lokalizacji

## Przegląd architektury

System lokalizacji wykorzystuje wzorzec **Service + Reactive Binding** zapewniający automatyczne odświeżanie interfejsu przy zmianie języka.

```
┌─────────────────────────────────────────────────────────────┐
│                      Warstwa UI (AXAML)                      │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐ │
│  │ MainWindow     │  │ AppSettings    │  │ Dialogs (30+)  │ │
│  │ {Localize ...} │  │ {Localize ...} │  │ {Localize ...} │ │
│  └───────┬────────┘  └───────┬────────┘  └───────┬────────┘ │
└──────────┼───────────────────┼───────────────────┼──────────┘
           │                   │                   │
           └───────────────────┴───────────────────┘
                               │
                     ┌─────────▼──────────┐
                     │ LocalizeExtension  │ ◄─── MarkupExtension
                     │  (MarkupExt)       │
                     └─────────┬──────────┘
                               │
           ┌───────────────────┴───────────────────┐
           │                                       │
    ┌──────▼────────────────┐         ┌───────────▼──────────┐
    │  ViewModels (C#)      │         │ LocalizationService  │
    │  ┌─────────────────┐  │         │ (ReactiveObject)     │
    │  │ MainWindowVM    │  │         ├──────────────────────┤
    │  │ _loc.Get(...)   │──┼────────►│ • CurrentCulture     │
    │  └─────────────────┘  │         │ • Get(key)           │
    │  ┌─────────────────┐  │         │ • GetFormatted(...)  │
    │  │ SettingsVM      │  │         │ • ChangeCulture(...) │
    │  │ _loc.Get(...)   │──┼────────►│                      │
    │  └─────────────────┘  │         └──────────┬───────────┘
    └───────────────────────┘                    │
                                                 │
                                    ┌────────────▼────────────┐
                                    │  Translation Files      │
                                    │  (JSON)                 │
                                    ├─────────────────────────┤
                                    │  • pl.json (default)    │
                                    │  • en.json              │
                                    │  • de.json (future)     │
                                    └─────────────────────────┘
                                                 ▲
                                                 │
                                    ┌────────────┴────────────┐
                                    │  appsettings.json       │
                                    │  Configuration.Language │
                                    └─────────────────────────┘
```

## Komponenty główne

### 1. ILocalizationService (Interface)

**Lokalizacja**: `SUSModder.Core/Services/Localization/ILocalizationService.cs`

```csharp
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Aktualnie wybrany język (pl, en, de, etc.)
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Pobiera przetłumaczony string dla podanego klucza.
    /// Jeśli klucz nie istnieje, zwraca klucz w nawiasach [KEY_NOT_FOUND].
    /// </summary>
    string Get(string key);

    /// <summary>
    /// Pobiera przetłumaczony string z formatowaniem (string.Format).
    /// </summary>
    string GetFormatted(string key, params object[] args);

    /// <summary>
    /// Zmienia aktualny język i odświeża wszystkie bindingi.
    /// </summary>
    void ChangeCulture(string culture);

    /// <summary>
    /// Sprawdza czy dany język jest dostępny (czy istnieje plik JSON).
    /// </summary>
    bool IsCultureAvailable(string culture);

    /// <summary>
    /// Zwraca listę dostępnych języków.
    /// </summary>
    IEnumerable<string> GetAvailableCultures();
}
```

### 2. LocalizationService (Implementacja)

**Lokalizacja**: `SUSModder/Services/Localization/LocalizationService.cs`

**Dziedziczenie**: `ReactiveObject` (ReactiveUI) + `ILocalizationService`

**Kluczowe funkcje:**
- Ładowanie plików JSON z `/Localization/`
- Deserializacja do `Dictionary<string, Dictionary<string, object>>`
- Observable property `CurrentCulture` z `RaisePropertyChanged`
- Rekursywne wyszukiwanie kluczy (obsługa zagnieżdżeń)
- Fallback do pl.json jeśli klucz nie istnieje w wybranym języku
- Cache dla wydajności

**Przykładowa struktura wewnętrzna:**
```csharp
public class LocalizationService : ReactiveObject, ILocalizationService
{
    private string _currentCulture = "pl";
    private Dictionary<string, Dictionary<string, object>> _translations;
    private readonly string _localizationPath;

    public string CurrentCulture
    {
        get => _currentCulture;
        private set => this.RaiseAndSetIfChanged(ref _currentCulture, value);
    }

    public string Get(string key)
    {
        // Split key: "UI.Buttons.Install" -> ["UI", "Buttons", "Install"]
        // Traverse nested dictionaries
        // Fallback to pl.json if not found
        // Return "[KEY_NOT_FOUND: {key}]" if completely missing
    }

    public void ChangeCulture(string culture)
    {
        if (_currentCulture == culture) return;

        CurrentCulture = culture;

        // Trigger property changed for ALL translated strings
        // ReactiveUI will auto-update bindings
        this.RaisePropertyChanged(string.Empty); // Forces update of all bindings
    }
}
```

### 3. LocalizeExtension (MarkupExtension dla AXAML)

**Lokalizacja**: `SUSModder/Services/Localization/LocalizeExtension.cs`

**Cel**: Umożliwienie użycia `{local:Localize Key}` w AXAML

**Jak działa:**
1. AXAML parser spotyka `{local:Localize UI.Buttons.Install}`
2. LocalizeExtension.ProvideValue() jest wywoływane
3. Tworzy binding do `LocalizationService.Get("UI.Buttons.Install")`
4. Zwraca IBinding który nasłuchuje zmian w LocalizationService
5. Gdy język się zmieni, binding automatycznie odświeża wartość

**Przykładowa implementacja:**
```csharp
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocalizeExtension() { }
    public LocalizeExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Pobierz LocalizationService z DI
        var locService = App.GetService<ILocalizationService>();

        // Stwórz binding który reaguje na zmiany
        var binding = new Binding
        {
            Source = locService,
            Path = $"Item[{Key}]", // Indexer property
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
```

**Alternatywna implementacja (prostsze, bez indexera):**
Każde użycie `{local:Localize}` tworzy helper property który obserwuje `CurrentCulture`.

### 4. ConfigManager Integration

**Zmiany w**: `SUSModder.Core/Configuration/ConfigManager.cs`

**Nowe metody:**
```csharp
public static class ConfigManager
{
    // ... istniejące metody ...

    /// <summary>
    /// Pobiera wybrany język z appsettings.json
    /// </summary>
    public static string GetLanguageSetting()
    {
        return _configuration?["Configuration:Language"] ?? "pl";
    }

    /// <summary>
    /// Zapisuje wybrany język do appsettings.json
    /// </summary>
    public static void SaveLanguageSetting(string language)
    {
        var appSettings = LoadAppSettings();
        if (appSettings["Configuration"] is JObject config)
        {
            config["Language"] = language;
        }
        SaveAppSettings(appSettings);
    }
}
```

**Aktualizacja appsettings.json:**
```json
{
  "Configuration": {
    "UpdateServerUrl": "https://susmodder.app/api/susmodder-config",
    "CurrentVersion": "1.1.2",
    "BaseUrl": "https://susmodder.app/",
    "ApiPort": "3001",
    "Mode": "steam",
    "lastLaunchId": 0,
    "Theme": "dark",
    "Language": "pl"  // ← NOWE
  }
}
```

## Przepływ danych

### 1. Inicjalizacja aplikacji (App.axaml.cs)

```
Start aplikacji
    │
    ├─► Rejestracja LocalizationService w DI (singleton)
    │
    ├─► Odczyt Language z appsettings.json
    │       └─► ConfigManager.GetLanguageSetting() → "pl"
    │
    ├─► LocalizationService.Initialize("pl")
    │       ├─► Ładuje pl.json
    │       ├─► Ładuje en.json (do cache)
    │       └─► Ustawia CurrentCulture = "pl"
    │
    └─► Uruchomienie MainWindow
            └─► Bindingi {local:Localize ...} automatycznie działają
```

### 2. Zmiana języka przez użytkownika

```
User wybiera "English" w AppSettingsView
    │
    ├─► AppSettingsViewModel.SelectedLanguage = "en"
    │
    ├─► LocalizationService.ChangeCulture("en")
    │       ├─► CurrentCulture = "en"
    │       └─► RaisePropertyChanged(string.Empty)  ← WAŻNE!
    │
    ├─► ReactiveUI propaguje zmianę do wszystkich bindingów
    │
    ├─► Wszystkie {local:Localize ...} odświeżają wartości
    │       └─► Pobierają stringi z en.json zamiast pl.json
    │
    ├─► ConfigManager.SaveLanguageSetting("en")
    │       └─► Zapisuje do appsettings.json
    │
    └─► UI natychmiast wyświetla angielskie teksty
```

### 3. Odczyt stringa w ViewModel

```
ViewModel wywołuje: _localization.Get("Dialogs.Error.Title")
    │
    ├─► LocalizationService.Get("Dialogs.Error.Title")
    │
    ├─► Split key: ["Dialogs", "Error", "Title"]
    │
    ├─► Sprawdź w _translations[CurrentCulture] ("en")
    │       ├─► Szukaj: translations["en"]["Dialogs"]["Error"]["Title"]
    │       └─► Znaleziono: "Error"
    │
    └─► Return "Error"
```

**Jeśli klucz nie istnieje w wybranym języku:**
```
LocalizationService.Get("Dialogs.NewFeature.Message")
    │
    ├─► Szukaj w translations["en"] → NIE ZNALEZIONO
    │
    ├─► FALLBACK: Szukaj w translations["pl"] → ZNALEZIONO
    │       └─► Return "Nowa funkcja dostępna!" (po polsku)
    │
    └─► (Opcjonalnie: log warning o brakującym tłumaczeniu)
```

## Dependency Injection

### Rejestracja serwisów (App.axaml.cs)

```csharp
public partial class App : Application
{
    private static ServiceProvider? _serviceProvider;

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // ... istniejące serwisy ...
        services.AddSingleton<IUserInteraction, UserInteraction>();
        services.AddSingleton<ConsoleLogger>();

        // NOWY SERWIS LOKALIZACJI
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var locService = new LocalizationService();
            var currentLang = ConfigManager.GetLanguageSetting();
            locService.ChangeCulture(currentLang);
            return locService;
        });

        // ViewModels z dependency injection
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AppSettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // ... reszta inicjalizacji ...
    }

    public static T GetService<T>() where T : class
    {
        return _serviceProvider?.GetService<T>()
            ?? throw new InvalidOperationException($"Service {typeof(T)} not found");
    }
}
```

### Injection w ViewModels

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly IUserInteraction _userInteraction;

    public MainWindowViewModel(
        ILocalizationService localization,
        IUserInteraction userInteraction)
    {
        _localization = localization;
        _userInteraction = userInteraction;
    }

    private async Task ShowError()
    {
        await _userInteraction.ShowErrorAsync(
            _localization.Get("Dialogs.Error.Title"),
            _localization.Get("Dialogs.Error.InstallFailed")
        );
    }
}
```

## Struktura plików JSON

### Hierarchia kluczy

```
UI                          ← Elementy interfejsu
├── Buttons                 ← Przyciski
│   ├── Install
│   ├── Launch
│   └── Update
├── Labels                  ← Etykiety
│   ├── InstalledMods
│   └── ModVersion
├── Menu                    ← Menu
│   ├── Settings
│   └── About
└── Status                  ← Statusy

Dialogs                     ← Dialogi
├── Error
│   ├── Title
│   ├── InstallFailed
│   └── UpdateFailed
├── Confirm
│   ├── Title
│   └── UninstallMessage
└── Info
    ├── Title
    └── UpdateAvailable

Settings                    ← Ustawienia
├── Title
├── Language
│   ├── Label
│   ├── Polish
│   └── English
└── Paths
    ├── ModsFolder
    └── GameFolder

Messages                    ← Wiadomości systemowe
├── RestartRequired
├── UpdateAvailable
└── InstallComplete

Errors                      ← Błędy
├── NetworkError
├── FileAccessError
└── ConfigError

Tooltips                    ← Podpowiedzi
├── InstallButton
└── SettingsButton
```

### Przykładowy pl.json (fragment)

```json
{
  "UI": {
    "Buttons": {
      "Install": "Instaluj",
      "Launch": "Uruchom",
      "Update": "Aktualizuj",
      "Delete": "Usuń",
      "Cancel": "Anuluj",
      "Browse": "Przeglądaj...",
      "OpenFolder": "Otwórz folder",
      "CreateShortcut": "Stwórz skrót"
    },
    "Labels": {
      "InstalledMods": "Zainstalowanych modów",
      "InstalledIn": "Zainstalowano w",
      "Version": "Wersja",
      "NoMods": "Brak zainteresowanych modów",
      "SpaceDetails": "Szczegóły przestrzeni"
    },
    "Menu": {
      "ToUConfigs": "Konfiguracje ToU",
      "DllMods": "Modyfikacje DLL",
      "SUStats": "SUStats - konfiguracje",
      "RepairGame": "Napraw Amonga",
      "Settings": "Ustawienia aplikacji"
    }
  },
  "Dialogs": {
    "Error": {
      "Title": "Błąd",
      "InstallFailed": "Nie udało się zainstalować moda",
      "UpdateFailed": "Nie udało się zaktualizować moda",
      "ConfigNotFound": "Nie znaleziono konfiguracji moda",
      "NetworkError": "Błąd połączenia sieciowego"
    },
    "Confirm": {
      "Title": "Potwierdzenie",
      "UninstallMessage": "Czy na pewno chcesz odinstalować {0}?",
      "DeleteMessage": "Ta operacja jest nieodwracalna. Kontynuować?"
    },
    "Info": {
      "Title": "Informacja",
      "UpdateAvailable": "Dostępna jest nowa wersja",
      "RestartRequired": "Restart wymagany. Zmiana wymaga ponownego uruchomienia aplikacji."
    }
  },
  "Settings": {
    "Title": "Ustawienia aplikacji",
    "Language": {
      "Label": "Język",
      "Polish": "Polski",
      "English": "English"
    },
    "Theme": {
      "Label": "Motyw",
      "Dark": "Ciemny",
      "Pink": "Różowy"
    },
    "Paths": {
      "ModsFolder": "Folder instalacji modów",
      "Browse": "Przeglądaj",
      "Reset": "Przywróć domyślne"
    }
  }
}
```

## Optymalizacje i wydajność

### 1. Cache tłumaczeń
- Wszystkie języki ładowane przy starcie aplikacji
- Brak powtórnego czytania JSON przy zmianie języka
- Dictionary lookup: O(1)

### 2. Lazy loading (opcjonalnie)
- Ładowanie tylko aktualnie wybranego języka
- Inne języki ładowane on-demand

### 3. Compiled bindings (Avalonia 11+)
```xml
<Window xmlns:local="clr-namespace:SUSModder.Services.Localization"
        x:DataType="local:ILocalizationService">
    <Button Content="{CompiledBinding Get('UI.Buttons.Install')}"/>
</Window>
```

## Rozszerzalność

### Dodanie nowego języka

1. Stwórz nowy plik: `Localization/de.json`
2. Skopiuj strukturę z `pl.json`
3. Przetłumacz wszystkie wartości
4. Język automatycznie dostępny (wykryty przez `GetAvailableCultures()`)
5. Dodaj do UI wyboru języka

### Dodanie nowej kategorii stringów

1. Dodaj nową sekcję w `pl.json` i `en.json`:
```json
{
  "NewFeature": {
    "Title": "Nowa funkcja",
    "Description": "Opis nowej funkcji"
  }
}
```

2. Użyj w kodzie:
```csharp
_localization.Get("NewFeature.Title")
```

---

**Podsumowanie**: System zaprojektowany z myślą o prostocie, wydajności i elastyczności. ReactiveUI zapewnia automatyczne odświeżanie UI, a struktura JSON ułatwia zarządzanie tłumaczeniami.
