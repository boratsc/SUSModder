# System Lokalizacji SUSModder

## Przegląd

System wielojęzyczności dla SUSModder umożliwiający dynamiczną zmianę języka interfejsu bez restartu aplikacji.

### Główne założenia

- **Języki**: Polski (domyślny) + Angielski (z możliwością łatwego dodania kolejnych)
- **Format**: JSON (pl.json, en.json) - prosty, czytelny, łatwy do edycji
- **Live Switching**: Zmiana języka natychmiast odświeża interfejs bez restartu aplikacji
- **Własna implementacja**: Lekkie rozwiązanie bez zewnętrznych zależności
- **ReactiveUI**: Wykorzystanie INotifyPropertyChanged dla automatycznego odświeżania UI
- **Kategoryzacja**: Logiczna struktura kluczy (UI.Buttons, Dialogs.Error, Settings, etc.)

### Zakres tłumaczeń

**Co jest tłumaczone:**
- Wszystkie elementy interfejsu użytkownika (przyciski, etykiety, nagłówki)
- Dialogi i komunikaty (błędy, potwierdzenia, informacje)
- Teksty w ustawieniach aplikacji
- Komunikaty statusu i postępu

**Co NIE jest tłumaczone (na razie):**
- Dane z API (nazwy modów, opisy, wersje)
- Logi diagnostyczne
- Nazwy plików i ścieżek

### Szacowany zakres

- **~500-700 stringów** do przetłumaczenia
- **Główne komponenty:**
  - MainWindow: ~80-100 stringów
  - Dialogi (30+ okien): ~200-300 stringów
  - Ustawienia: ~40-50 stringów
  - ViewModel messages: ~100-150 stringów
  - Pozostałe: ~80-100 stringów

## Struktura dokumentacji

1. **[Architecture.md](01_Architecture.md)** - Architektura systemu, komponenty, przepływ danych
2. **[Implementation.md](02_Implementation.md)** - Szczegóły implementacji klas i serwisów
3. **[Migration_Guide.md](03_Migration_Guide.md)** - Przewodnik migracji istniejących stringów
4. **[Translation_Guide.md](04_Translation_Guide.md)** - Jak dodawać nowe języki i tłumaczyć

## Quick Start

### Użycie w AXAML

```xml
<!-- Przed -->
<Button Content="Instaluj"/>

<!-- Po -->
<Button Content="{local:Localize UI.Buttons.Install}"/>
```

### Użycie w ViewModels

```csharp
// Dependency injection
public class MyViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;

    public MyViewModel(ILocalizationService localization)
    {
        _localization = localization;
    }

    private async Task ShowError()
    {
        await ShowErrorAsync(
            _localization.Get("Dialogs.Error.Title"),
            _localization.Get("Dialogs.Error.InstallFailed")
        );
    }
}
```

### Użycie z formatowaniem

```csharp
// pl.json: "Dialogs.Confirm.Uninstall": "Czy na pewno chcesz odinstalować {0}?"
var message = _localization.GetFormatted("Dialogs.Confirm.Uninstall", modName);
```

## Struktura plików

```
SUSModder/
├── Localization/
│   ├── pl.json          # Polski (domyślny)
│   ├── en.json          # Angielski
│   └── ...              # Przyszłe języki (de.json, fr.json)
├── Services/
│   ├── Localization/
│   │   ├── ILocalizationService.cs
│   │   ├── LocalizationService.cs
│   │   └── LocalizeExtension.cs
│   └── ...
└── ...

SUSModder.Core/
├── Services/
│   └── Localization/
│       └── ILocalizationService.cs (interface)
└── ...

appsettings.json (dodane pole):
{
  "Configuration": {
    ...
    "Language": "pl"
  }
}
```

## Kategorie stringów w JSON

```json
{
  "UI": {
    "Buttons": { "Install": "Instaluj", "Launch": "Uruchom", ... },
    "Labels": { "InstalledMods": "Zainstalowanych modów", ... },
    "Menu": { "Settings": "Ustawienia", ... },
    "Status": { "Ready": "Gotowe", ... }
  },
  "Dialogs": {
    "Error": { "Title": "Błąd", "Message": "...", ... },
    "Confirm": { "Title": "Potwierdzenie", ... },
    "Info": { "Title": "Informacja", ... }
  },
  "Settings": {
    "Title": "Ustawienia aplikacji",
    "Sections": { "General": "Ogólne", ... },
    "Language": { "Label": "Język", "Polish": "Polski", "English": "English" }
  },
  "Messages": {
    "RestartRequired": "Restart wymagany",
    "UpdateAvailable": "Dostępna aktualizacja",
    ...
  },
  "Tooltips": { ... },
  "Errors": { ... }
}
```

## Główne komponenty

### 1. LocalizationService
- Ładuje pliki JSON z folderu `/Localization/`
- Zarządza aktualną kulturą (pl, en)
- Observable properties dla live switching
- Fallback do pl.json w razie braku tłumaczenia

### 2. LocalizeExtension
- MarkupExtension dla AXAML
- Automatyczny binding do LocalizationService
- Live update przy zmianie języka

### 3. ConfigManager Integration
- Odczyt/zapis wybranego języka w appsettings.json
- Inicjalizacja języka przy starcie aplikacji

### 4. UI Settings
- ComboBox wyboru języka w AppSettingsView
- Natychmiastowa zmiana bez restartu

## Zalety rozwiązania

✅ **Elastyczność** - Łatwe dodanie kolejnych języków (wystarczy nowy plik JSON)
✅ **Edytowalność** - JSON można edytować w dowolnym edytorze tekstu
✅ **Live switching** - Zmiana języka natychmiast odświeża UI bez restartu
✅ **Brak zależności** - Własne rozwiązanie, bez dodatkowych bibliotek
✅ **Kategoryzacja** - Logiczne grupowanie stringów dla łatwości zarządzania
✅ **Format strings** - Wsparcie dla parametrów {0}, {1}, etc.
✅ **Fallback** - Zawsze działa (pl.json jako domyślny)
✅ **Typesafe** - Opcjonalne klucze jako const stringi

## Roadmap

### Faza 1: Infrastruktura ✅
- [x] Plan architektury
- [ ] LocalizationService implementation
- [ ] LocalizeExtension dla AXAML
- [ ] DI setup w App.axaml.cs
- [ ] Aktualizacja appsettings.json

### Faza 2: Migracja UI (~400 stringów)
- [ ] MainWindow.axaml
- [ ] AppSettingsView.axaml
- [ ] InfoPanel.axaml
- [ ] Dialogi (30+ plików)

### Faza 3: Migracja ViewModels (~200 stringów)
- [ ] MainWindowViewModel
- [ ] AppSettingsViewModel
- [ ] Error/Info/Confirm dialogs

### Faza 4: Tłumaczenia
- [ ] Wypełnienie pl.json (ekstrakcja z kodu)
- [ ] Tłumaczenie en.json
- [ ] UI wyboru języka

### Faza 5: Testy i refinement
- [ ] Testy jednostkowe LocalizationService
- [ ] Testy live switching
- [ ] Weryfikacja wszystkich stringów

## Szacowany nakład pracy

- **Infrastruktura (LocalizationService + DI)**: ~2-3h
- **Migracja AXAML (400 stringów)**: ~4-6h
- **Migracja ViewModels (200 stringów)**: ~2-3h
- **UI wyboru języka**: ~1h
- **Tłumaczenie en.json**: ~3-4h
- **Testy i refinement**: ~2h
- **ŁĄCZNIE**: ~14-19h

## Status projektu

🚧 **W PLANOWANIU** - Dokumentacja gotowa, oczekiwanie na implementację

---

**Ostatnia aktualizacja**: 2025-10-23
**Wersja dokumentacji**: 1.0
**Autor**: Claude Code + Developer
