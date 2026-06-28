# Szczegóły Implementacji

## Spis treści
1. [ILocalizationService Interface](#1-ilocalizationservice-interface)
2. [LocalizationService Class](#2-localizationservice-class)
3. [LocalizeExtension (MarkupExtension)](#3-localizeextension-markupextension)
4. [ConfigManager Integration](#4-configmanager-integration)
5. [App.axaml.cs Initialization](#5-appaxamlcs-initialization)
6. [AppSettingsViewModel - UI wyboru języka](#6-appsettingsviewmodel---ui-wyboru-języka)
7. [Helper Classes (opcjonalnie)](#7-helper-classes-opcjonalnie)

---

## 1. ILocalizationService Interface

**Lokalizacja**: `SUSModder.Core/Services/Localization/ILocalizationService.cs`

```csharp
using System.Collections.Generic;
using System.ComponentModel;

namespace SUSModder.Core.Services.Localization;

/// <summary>
/// Serwis zarządzania wielojęzycznością aplikacji.
/// Implementacja powinna wspierać INotifyPropertyChanged dla live switching.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Aktualnie wybrany język (np. "pl", "en", "de").
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Pobiera przetłumaczony string dla podanego klucza.
    /// </summary>
    /// <param name="key">Klucz w formacie "Category.Subcategory.Key" (np. "UI.Buttons.Install")</param>
    /// <returns>Przetłumaczony string lub klucz w nawiasach jeśli nie znaleziono</returns>
    string Get(string key);

    /// <summary>
    /// Pobiera przetłumaczony string z formatowaniem (string.Format).
    /// </summary>
    /// <param name="key">Klucz tłumaczenia</param>
    /// <param name="args">Parametry do formatowania</param>
    /// <returns>Sformatowany przetłumaczony string</returns>
    string GetFormatted(string key, params object[] args);

    /// <summary>
    /// Zmienia aktualny język i odświeża wszystkie bindingi.
    /// </summary>
    /// <param name="culture">Kod języka (np. "pl", "en")</param>
    void ChangeCulture(string culture);

    /// <summary>
    /// Sprawdza czy dany język jest dostępny (czy istnieje plik JSON).
    /// </summary>
    /// <param name="culture">Kod języka do sprawdzenia</param>
    /// <returns>True jeśli język jest dostępny</returns>
    bool IsCultureAvailable(string culture);

    /// <summary>
    /// Zwraca listę wszystkich dostępnych języków.
    /// </summary>
    /// <returns>Kody języków (np. ["pl", "en", "de"])</returns>
    IEnumerable<string> GetAvailableCultures();
}
```

---

## 2. LocalizationService Class

**Lokalizacja**: `SUSModder/Services/Localization/LocalizationService.cs`

### 2.1 Kompletna implementacja

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ReactiveUI;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Services.Localization;

/// <summary>
/// Implementacja serwisu lokalizacji z obsługą live switching.
/// </summary>
public class LocalizationService : ReactiveObject, ILocalizationService
{
    private string _currentCulture = "pl";
    private Dictionary<string, Dictionary<string, object>> _translations = new();
    private readonly string _localizationPath;
    private const string DefaultCulture = "pl";

    /// <summary>
    /// Aktualnie wybrany język.
    /// </summary>
    public string CurrentCulture
    {
        get => _currentCulture;
        private set => this.RaiseAndSetIfChanged(ref _currentCulture, value);
    }

    public LocalizationService()
    {
        // Domyślnie szukamy w folderze aplikacji/Localization
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _localizationPath = Path.Combine(appDir, "Localization");

        // Ładujemy wszystkie dostępne języki przy starcie
        LoadAllTranslations();

        // Ustawiamy domyślny język
        CurrentCulture = DefaultCulture;
    }

    /// <summary>
    /// Ładuje wszystkie pliki JSON z folderu Localization.
    /// </summary>
    private void LoadAllTranslations()
    {
        if (!Directory.Exists(_localizationPath))
        {
            // Log warning lub stwórz folder
            Directory.CreateDirectory(_localizationPath);
            return;
        }

        var jsonFiles = Directory.GetFiles(_localizationPath, "*.json");

        foreach (var file in jsonFiles)
        {
            var culture = Path.GetFileNameWithoutExtension(file); // pl, en, de
            LoadTranslationFile(culture, file);
        }
    }

    /// <summary>
    /// Ładuje pojedynczy plik JSON z tłumaczeniami.
    /// </summary>
    private void LoadTranslationFile(string culture, string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var translations = JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (translations != null)
            {
                _translations[culture] = translations;
            }
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"[LocalizationService] Błąd ładowania {culture}.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Pobiera przetłumaczony string dla podanego klucza.
    /// </summary>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "[EMPTY_KEY]";

        // Próbuj pobrać z aktualnego języka
        var value = GetFromCulture(CurrentCulture, key);
        if (value != null)
            return value;

        // Fallback do języka domyślnego (pl)
        if (CurrentCulture != DefaultCulture)
        {
            value = GetFromCulture(DefaultCulture, key);
            if (value != null)
            {
                // Opcjonalnie: log warning o brakującym tłumaczeniu
                return value;
            }
        }

        // Nie znaleziono w żadnym języku
        return $"[{key}]";
    }

    /// <summary>
    /// Pobiera wartość z konkretnego języka, obsługując zagnieżdżone klucze.
    /// </summary>
    private string? GetFromCulture(string culture, string key)
    {
        if (!_translations.ContainsKey(culture))
            return null;

        var parts = key.Split('.');
        object? current = _translations[culture];

        foreach (var part in parts)
        {
            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(part, out var next))
                    return null;

                current = next;
            }
            else if (current is JsonElement element)
            {
                // Obsługa JsonElement z System.Text.Json
                if (element.ValueKind == JsonValueKind.Object)
                {
                    if (!element.TryGetProperty(part, out var next))
                        return null;

                    current = next;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        // Konwersja wyniku do string
        if (current is string str)
            return str;

        if (current is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
            return jsonElement.GetString();

        return current?.ToString();
    }

    /// <summary>
    /// Pobiera przetłumaczony string z formatowaniem.
    /// </summary>
    public string GetFormatted(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // Jeśli formatowanie się nie uda, zwróć szablon
            return template;
        }
    }

    /// <summary>
    /// Zmienia aktualny język i odświeża wszystkie bindingi.
    /// </summary>
    public void ChangeCulture(string culture)
    {
        if (_currentCulture == culture)
            return;

        if (!IsCultureAvailable(culture))
        {
            Console.WriteLine($"[LocalizationService] Język {culture} nie jest dostępny");
            return;
        }

        CurrentCulture = culture;

        // KLUCZOWE: Powiadom wszystkie bindingi o zmianie
        // RaisePropertyChanged z pustym stringiem oznacza "wszystkie property się zmieniły"
        this.RaisePropertyChanged(string.Empty);
    }

    /// <summary>
    /// Sprawdza czy język jest dostępny.
    /// </summary>
    public bool IsCultureAvailable(string culture)
    {
        return _translations.ContainsKey(culture);
    }

    /// <summary>
    /// Zwraca listę dostępnych języków.
    /// </summary>
    public IEnumerable<string> GetAvailableCultures()
    {
        return _translations.Keys.OrderBy(c => c);
    }

    /// <summary>
    /// Indexer dla łatwiejszego bindingu w AXAML.
    /// Umożliwia użycie: Binding Path="Item[UI.Buttons.Install]"
    /// </summary>
    public string this[string key] => Get(key);
}
```

### 2.2 Kluczowe aspekty implementacji

#### ReactiveUI i Live Switching
```csharp
// ReactiveObject dostarcza RaiseAndSetIfChanged i RaisePropertyChanged
public class LocalizationService : ReactiveObject

// Przy zmianie języka:
this.RaisePropertyChanged(string.Empty); // ← Odświeża WSZYSTKIE bindingi
```

#### Obsługa JsonElement (System.Text.Json)
Deserializacja JSON może zwrócić `JsonElement` zamiast `Dictionary<string, object>`, dlatego potrzebna jest obsługa obu typów.

#### Fallback mechanism
```csharp
1. Szukaj w CurrentCulture (np. "en")
2. Jeśli nie znaleziono → szukaj w DefaultCulture ("pl")
3. Jeśli nadal nie znaleziono → zwróć "[Key]"
```

---

## 3. LocalizeExtension (MarkupExtension)

**Lokalizacja**: `SUSModder/Services/Localization/LocalizeExtension.cs`

### 3.1 Implementacja z Indexerem

```csharp
using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Services.Localization;

/// <summary>
/// MarkupExtension umożliwiająca użycie {local:Localize Key} w AXAML.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocalizeExtension()
    {
        Key = string.Empty;
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Pobierz LocalizationService z DI
        var locService = App.GetService<ILocalizationService>();

        if (locService == null)
            return $"[LOC_SERVICE_NOT_FOUND: {Key}]";

        // Stwórz binding do indexera: LocalizationService[Key]
        // Dzięki temu zmiana CurrentCulture automatycznie odświeży wartość
        var binding = new ReflectionBindingExtension($"[{Key}]")
        {
            Source = locService,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
```

### 3.2 Alternatywna implementacja (bez indexera)

Jeśli indexer sprawia problemy, można użyć metody `Get()` bezpośrednio:

```csharp
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocalizeExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var locService = App.GetService<ILocalizationService>();

        // Stwórz wrapper który obserwuje CurrentCulture
        return new LocalizedString(locService, Key);
    }
}

/// <summary>
/// Helper class który reaguje na zmianę języka.
/// </summary>
internal class LocalizedString : INotifyPropertyChanged
{
    private readonly ILocalizationService _locService;
    private readonly string _key;

    public string Value => _locService.Get(_key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizedString(ILocalizationService locService, string key)
    {
        _locService = locService;
        _key = key;

        // Nasłuchuj zmiany CurrentCulture
        _locService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || string.IsNullOrEmpty(e.PropertyName))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        };
    }
}
```

### 3.3 Użycie w AXAML

```xml
<!-- Namespace definition w Window -->
<Window xmlns:local="using:SUSModder.Services.Localization">

    <!-- Użycie z key jako parametr -->
    <Button Content="{local:Localize UI.Buttons.Install}"/>

    <!-- Użycie z explicit property -->
    <TextBlock Text="{local:Localize Key=Dialogs.Error.Title}"/>

    <!-- W tooltip -->
    <Button ToolTip.Tip="{local:Localize Tooltips.InstallButton}">
        <PathIcon Data="{StaticResource InstallIcon}"/>
    </Button>
</Window>
```

---

## 4. ConfigManager Integration

**Lokalizacja**: `SUSModder.Core/Configuration/ConfigManager.cs`

### Dodanie metod do istniejącej klasy

```csharp
// W klasie ConfigManager dodaj:

/// <summary>
/// Pobiera wybrany język z appsettings.json.
/// </summary>
/// <returns>Kod języka (np. "pl", "en") lub "pl" jako domyślny</returns>
public static string GetLanguageSetting()
{
    try
    {
        return _configuration?["Configuration:Language"] ?? "pl";
    }
    catch
    {
        return "pl";
    }
}

/// <summary>
/// Zapisuje wybrany język do appsettings.json.
/// </summary>
/// <param name="language">Kod języka do zapisania</param>
public static void SaveLanguageSetting(string language)
{
    try
    {
        var appSettings = LoadAppSettings();

        if (appSettings["Configuration"] is JObject config)
        {
            config["Language"] = language;
            SaveAppSettings(appSettings);
        }
    }
    catch (Exception ex)
    {
        // Log error
        Console.WriteLine($"[ConfigManager] Błąd zapisu języka: {ex.Message}");
    }
}
```

### Aktualizacja appsettings.json schema

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
    "Language": "pl"
  },
  "AppSettings": {
    "ModsInstallPath": "",
    "DefaultModsPath": "%APPDATA%\\Among Us - Mody",
    "DeveloperMode": false
  }
}
```

---

## 5. App.axaml.cs Initialization

**Lokalizacja**: `SUSModder/App.axaml.cs`

### Rejestracja w DI i inicjalizacja

```csharp
public partial class App : Application
{
    private static ServiceProvider? _serviceProvider;

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // ========================================
        // REJESTRACJA SERWISÓW
        // ========================================

        // Istniejące serwisy
        services.AddSingleton<IUserInteraction, UserInteraction>();
        services.AddSingleton<ConsoleLogger>();
        services.AddSingleton<RolesService>();
        services.AddSingleton<DiscordIconPreloader>();

        // *** NOWY SERWIS LOKALIZACJI ***
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var locService = new LocalizationService();

            // Odczytaj zapisany język z appsettings.json
            var savedLanguage = ConfigManager.GetLanguageSetting();

            // Ustaw język (jeśli dostępny, w przeciwnym razie zostanie "pl")
            if (locService.IsCultureAvailable(savedLanguage))
            {
                locService.ChangeCulture(savedLanguage);
            }

            return locService;
        });

        // ViewModels z dependency injection
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AppSettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // ========================================
        // INICJALIZACJA APLIKACJI
        // ========================================

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Reszta inicjalizacji...
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Pobiera serwis z DI container.
    /// </summary>
    public static T GetService<T>() where T : class
    {
        return _serviceProvider?.GetService<T>()
            ?? throw new InvalidOperationException($"Service {typeof(T)} not registered in DI container");
    }
}
```

---

## 6. AppSettingsViewModel - UI wyboru języka

**Lokalizacja**: `SUSModder/ViewModels/AppSettingsViewModel.cs`

### 6.1 Dodanie properties i logiki

```csharp
public class AppSettingsViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private string _selectedLanguage;

    public AppSettingsViewModel(ILocalizationService localization)
    {
        _localization = localization;
        _selectedLanguage = _localization.CurrentCulture;
    }

    /// <summary>
    /// Dostępne języki do wyboru.
    /// </summary>
    public List<LanguageOption> AvailableLanguages => new()
    {
        new LanguageOption { Code = "pl", DisplayName = "Polski" },
        new LanguageOption { Code = "en", DisplayName = "English" }
    };

    /// <summary>
    /// Aktualnie wybrany język.
    /// </summary>
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _selectedLanguage, value))
            {
                OnLanguageChanged();
            }
        }
    }

    /// <summary>
    /// Obsługa zmiany języka.
    /// </summary>
    private void OnLanguageChanged()
    {
        // Zmień język w serwisie (live switch)
        _localization.ChangeCulture(SelectedLanguage);

        // Zapisz wybór do appsettings.json
        ConfigManager.SaveLanguageSetting(SelectedLanguage);

        // Opcjonalnie: Pokaż info że język został zmieniony
        // await ShowInfoAsync("Język zmieniony", $"Interfejs został przełączony na: {SelectedLanguage}");
    }
}

/// <summary>
/// Model dla opcji języka w ComboBox.
/// </summary>
public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
```

### 6.2 Aktualizacja AppSettingsView.axaml

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="using:SUSModder.Services.Localization"
             x:Class="SUSModder.Views.AppSettingsView">

    <!-- W sekcji ustawień dodaj: -->

    <StackPanel Spacing="10">
        <!-- Istniejące ustawienia... -->

        <!-- *** NOWA SEKCJA: Wybór języka *** -->
        <Border Background="{DynamicResource PanelBackgroundBrush}"
                CornerRadius="8"
                Padding="15">
            <StackPanel Spacing="10">
                <TextBlock Text="{local:Localize Settings.Language.Label}"
                           FontWeight="Bold"
                           FontSize="14"/>

                <ComboBox ItemsSource="{Binding AvailableLanguages}"
                          SelectedItem="{Binding SelectedLanguage}"
                          DisplayMemberPath="DisplayName"
                          SelectedValuePath="Code"
                          Width="200"
                          HorizontalAlignment="Left"/>

                <TextBlock Text="{local:Localize Settings.Language.Description}"
                           FontSize="11"
                           Foreground="{DynamicResource TextSecondaryBrush}"
                           TextWrapping="Wrap"/>
            </StackPanel>
        </Border>

        <!-- Reszta ustawień... -->
    </StackPanel>
</UserControl>
```

---

## 7. Helper Classes (opcjonalnie)

### 7.1 LocalizationKeys (Strongly-typed keys)

**Lokalizacja**: `SUSModder/Services/Localization/LocalizationKeys.cs`

```csharp
namespace SUSModder.Services.Localization;

/// <summary>
/// Strongly-typed keys dla tłumaczeń.
/// Zapobiega błędom literówek i daje autocomplete w IDE.
/// </summary>
public static class LocalizationKeys
{
    public static class UI
    {
        public static class Buttons
        {
            public const string Install = "UI.Buttons.Install";
            public const string Launch = "UI.Buttons.Launch";
            public const string Update = "UI.Buttons.Update";
            public const string Delete = "UI.Buttons.Delete";
            public const string Cancel = "UI.Buttons.Cancel";
        }

        public static class Labels
        {
            public const string InstalledMods = "UI.Labels.InstalledMods";
            public const string Version = "UI.Labels.Version";
        }
    }

    public static class Dialogs
    {
        public static class Error
        {
            public const string Title = "Dialogs.Error.Title";
            public const string InstallFailed = "Dialogs.Error.InstallFailed";
        }

        public static class Confirm
        {
            public const string Title = "Dialogs.Confirm.Title";
            public const string UninstallMessage = "Dialogs.Confirm.UninstallMessage";
        }
    }

    // ... itd.
}
```

**Użycie:**
```csharp
// Zamiast magic string:
await ShowErrorAsync(_localization.Get("Dialogs.Error.Title"), ...)

// Użyj const:
await ShowErrorAsync(_localization.Get(LocalizationKeys.Dialogs.Error.Title), ...)
```

### 7.2 Extension methods (wygoda)

```csharp
public static class LocalizationExtensions
{
    /// <summary>
    /// Skrócona metoda Get() dla łatwiejszego użycia.
    /// </summary>
    public static string L(this ILocalizationService loc, string key)
        => loc.Get(key);

    /// <summary>
    /// Skrócona metoda GetFormatted() dla łatwiejszego użycia.
    /// </summary>
    public static string LF(this ILocalizationService loc, string key, params object[] args)
        => loc.GetFormatted(key, args);
}
```

**Użycie:**
```csharp
// Zamiast:
_localization.Get("Dialogs.Error.Title")

// Możesz napisać:
_localization.L("Dialogs.Error.Title")

// Z formatowaniem:
_localization.LF("Dialogs.Confirm.UninstallMessage", modName)
```

---

## Podsumowanie implementacji

### Pliki do stworzenia:

1. **SUSModder.Core/Services/Localization/**
   - `ILocalizationService.cs` (interface)

2. **SUSModder/Services/Localization/**
   - `LocalizationService.cs` (implementacja)
   - `LocalizeExtension.cs` (MarkupExtension)
   - `LocalizationKeys.cs` (opcjonalnie)

3. **SUSModder/Localization/**
   - `pl.json` (polski - domyślny)
   - `en.json` (angielski - tłumaczenie)

4. **Modyfikacje istniejących plików:**
   - `SUSModder.Core/Configuration/ConfigManager.cs` (+ 2 metody)
   - `SUSModder/App.axaml.cs` (rejestracja DI)
   - `SUSModder/ViewModels/AppSettingsViewModel.cs` (UI wyboru języka)
   - `SUSModder/Views/AppSettingsView.axaml` (ComboBox języka)
   - `appsettings.json` (dodanie Configuration.Language)

### Kolejność implementacji:

1. ✅ Interface (ILocalizationService)
2. ✅ Implementacja (LocalizationService)
3. ✅ MarkupExtension (LocalizeExtension)
4. ✅ ConfigManager integration
5. ✅ DI setup (App.axaml.cs)
6. ✅ JSON files (pl.json, en.json) z przykładowymi stringami
7. ✅ UI wyboru języka (AppSettingsView)
8. ✅ Testy na kilku przykładowych stringach

---

**Następny krok**: Migration Guide - jak przenieść istniejące stringi do systemu lokalizacji.
