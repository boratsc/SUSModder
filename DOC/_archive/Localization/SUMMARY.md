# Podsumowanie Dokumentacji Systemu Lokalizacji

## 📚 Spis Dokumentów

1. **[README.md](README.md)** - Punkt wejścia, przegląd całego systemu
2. **[01_Architecture.md](01_Architecture.md)** - Szczegółowa architektura, komponenty, przepływ danych
3. **[02_Implementation.md](02_Implementation.md)** - Kod implementacji wszystkich klas i serwisów
4. **[03_Migration_Guide.md](03_Migration_Guide.md)** - Jak migrować istniejące stringi (AXAML + C#)
5. **[04_Translation_Guide.md](04_Translation_Guide.md)** - Jak tłumaczyć i dodawać nowe języki
6. **[TEMPLATE_pl.json](TEMPLATE_pl.json)** - Szablon polskiego JSON (~300 kluczy)
7. **[TEMPLATE_en.json](TEMPLATE_en.json)** - Szablon angielskiego JSON (pełne tłumaczenie)

---

## 🎯 Quick Start dla Implementacji

### Krok 1: Przeczytaj fundamenty
```
README.md → 01_Architecture.md
```
Zrozum jak działa system (30 min czytania)

### Krok 2: Implementuj infrastrukturę
```
02_Implementation.md → Sekcje 1-5
```
Stwórz klasy LocalizationService, Extension, DI setup (2-3h pracy)

### Krok 3: Migruj stringi
```
03_Migration_Guide.md + TEMPLATE_pl.json
```
Przenieś hardcoded stringi do JSON (4-6h pracy)

### Krok 4: Przetłumacz
```
04_Translation_Guide.md + TEMPLATE_en.json
```
Wypełnij en.json tłumaczeniami (3-4h pracy)

---

## 💡 Kluczowe Koncepty

### System Live Switching (bez restartu)
- Użycie ReactiveUI + INotifyPropertyChanged
- LocalizeExtension dla AXAML bindingu
- Dependency Injection LocalizationService
- Zmiana języka: `ChangeCulture("en")` → instant UI refresh

### Struktura JSON
```json
{
  "Category": {         // UI, Dialogs, Settings
    "Subcategory": {    // Buttons, Error, Paths
      "Key": "Value"    // Install, Title, Label
    }
  }
}
```

### Użycie w kodzie

**AXAML:**
```xml
<Button Content="{local:Localize UI.Buttons.Install}"/>
```

**C# (ViewModel):**
```csharp
_localization.Get("Dialogs.Error.Title")
_localization.GetFormatted("Messages.ModInstalled", modName)
```

---

## 📊 Statystyki Projektu

| Metryka | Wartość |
|---------|---------|
| Szacowana liczba stringów | 500-700 |
| Główne pliki AXAML | 32 widoki + 30 dialogów |
| ViewModels do migracji | ~10 głównych |
| Kategorie w JSON | 11 (UI, Dialogs, Settings, Messages, Status, Tooltips, Errors, ModTypes, About, Updates, DllManager) |
| Języki na start | 2 (PL, EN) |
| Czas implementacji | 12-17h (infrastruktura + migracja) |
| Czas tłumaczenia | 3-4h (PL → EN) |
| **ŁĄCZNIE** | **15-21h** |

---

## 🏗️ Pliki do Utworzenia

### Core Library (SUSModder.Core)
```
/Services/Localization/
  └── ILocalizationService.cs        (~50 linii)
```

### Main App (SUSModder)
```
/Services/Localization/
  ├── LocalizationService.cs         (~250 linii)
  ├── LocalizeExtension.cs           (~50 linii)
  └── LocalizationKeys.cs            (~200 linii, opcjonalnie)

/Localization/
  ├── pl.json                        (~300-500 kluczy)
  └── en.json                        (~300-500 kluczy)
```

### Modyfikacje Istniejących Plików
```
SUSModder.Core/Configuration/ConfigManager.cs     (+2 metody, ~20 linii)
SUSModder/App.axaml.cs                            (+rejestracja DI, ~10 linii)
SUSModder/ViewModels/AppSettingsViewModel.cs      (+UI języka, ~30 linii)
SUSModder/Views/AppSettingsView.axaml             (+ComboBox, ~15 linii)
appsettings.json                                  (+Language field)
```

---

## 🔄 Workflow Implementacji (Zalecany)

### Faza 1: Infrastruktura (Dzień 1, ~3h)
- [ ] Stwórz ILocalizationService interface
- [ ] Zaimplementuj LocalizationService
- [ ] Stwórz LocalizeExtension
- [ ] Dodaj metody do ConfigManager
- [ ] Zarejestruj w DI (App.axaml.cs)
- [ ] Stwórz puste pl.json i en.json
- [ ] Test na 2-3 stringach (button Install, Launch)

### Faza 2: Ekstrakcja PL (Dzień 2, ~4h)
- [ ] Stwórz Excel/Sheets do trackingu
- [ ] Wyciągnij wszystkie stringi z AXAML
- [ ] Wyciągnij wszystkie stringi z ViewModels
- [ ] Nadaj klucze (UI.Buttons.Install, etc.)
- [ ] Wypełnij TEMPLATE_pl.json (~500 kluczy)
- [ ] Walidacja struktury JSON

### Faza 3: Migracja AXAML (Dzień 3, ~5h)
- [ ] Dodaj xmlns:local do wszystkich widoków
- [ ] Migruj MainWindow.axaml (~80 stringów)
- [ ] Migruj AppSettingsView.axaml (~30 stringów)
- [ ] Migruj InfoPanel.axaml (~20 stringów)
- [ ] Migruj wszystkie dialogi (30+ plików, ~200 stringów)
- [ ] Test po każdych 20 stringach

### Faza 4: Migracja ViewModels (Dzień 4, ~3h)
- [ ] Inject ILocalizationService do ViewModels
- [ ] Migruj MainWindowViewModel (~100 stringów)
- [ ] Migruj AppSettingsViewModel (~20 stringów)
- [ ] Migruj pozostałe ViewModele (~80 stringów)
- [ ] Test wszystkich ścieżek (happy path + error paths)

### Faza 5: UI Języka (Dzień 4, ~1h)
- [ ] Dodaj ComboBox wyboru języka w Settings
- [ ] Binding do SelectedLanguage property
- [ ] Test live switching (PL ↔ EN)

### Faza 6: Tłumaczenie EN (Dzień 5, ~4h)
- [ ] Skopiuj pl.json → en.json
- [ ] Przetłumacz wszystkie wartości
- [ ] Weryfikacja placeholders {0}, {1}
- [ ] Walidacja struktury (Python script)
- [ ] Test wszystkich ekranów w EN

### Faza 7: Testy i Refinement (Dzień 5, ~2h)
- [ ] Test wszystkich funkcji w PL
- [ ] Test wszystkich funkcji w EN
- [ ] Szukaj [KEY_NOT_FOUND] w UI
- [ ] Sprawdź długie teksty (czy się mieszczą)
- [ ] Final review i poprawki

---

## 🛠️ Narzędzia Pomocnicze

### Walidacja JSON
```bash
# Online
https://jsonlint.com/

# CLI
jq . pl.json  # Sprawdza poprawność
```

### Porównanie struktur
```python
# Python script (w 04_Translation_Guide.md)
validate_translation('pl.json', 'en.json')
```

### VS Code Extensions
- **JSON Tools** - formatowanie
- **i18n Ally** - zarządzanie tłumaczeniami
- **Error Lens** - błędy inline

### Git Commits
```bash
feat(i18n): Add localization infrastructure
feat(i18n): Add Polish strings (pl.json)
feat(i18n): Migrate AXAML strings to localization
feat(i18n): Migrate ViewModels to localization
feat(i18n): Add English translation (en.json)
feat(i18n): Add language selection UI
```

---

## 📖 FAQ

**Q: Czy muszę restartować aplikację po zmianie języka?**
A: NIE! System obsługuje live switching - zmiana języka natychmiast odświeża UI.

**Q: Jak dodać trzeci język (np. niemiecki)?**
A: Skopiuj pl.json → de.json, przetłumacz wartości. System automatycznie wykryje nowy język.

**Q: Co jeśli zapomnę przetłumaczyć klucz w en.json?**
A: System użyje fallback do pl.json. Brakujący klucz będzie po polsku.

**Q: Czy mogę używać emoji w tłumaczeniach?**
A: Tak, ale nie jest zalecane (chyba że użytkownik o to poprosi).

**Q: Jak formatować stringi z parametrami?**
A: Użyj placeholders: `"Message": "Mod {0} installed"` → `_loc.GetFormatted("Message", modName)`

**Q: Czy live switching działa w dialogach?**
A: Tak! Wszystkie bindingi (AXAML i C#) odświeżają się automatycznie.

**Q: Co jeśli mam 1000+ stringów?**
A: System skaluje się doskonale. Można rozważyć lazy loading lub podział na moduły.

---

## ✅ Checklist Końcowy

### Przed rozpoczęciem implementacji:
- [ ] Przeczytałem README.md i Architecture.md
- [ ] Rozumiem jak działa ReactiveUI + INotifyPropertyChanged
- [ ] Rozumiem różnicę między AXAML binding a C# usage
- [ ] Mam plan migracji (Excel/Sheets gotowy)

### Po implementacji infrastruktury:
- [ ] LocalizationService działa poprawnie
- [ ] Test na 2-3 stringach przeszedł pomyślnie
- [ ] Live switching działa (zmiana CurrentCulture odświeża UI)
- [ ] ConfigManager zapisuje/odczytuje język

### Po migracji stringów:
- [ ] Wszystkie widoki używają {local:Localize ...}
- [ ] Wszystkie ViewModele używają _loc.Get(...)
- [ ] Brak hardcoded stringów w kodzie
- [ ] Wszystkie klucze istnieją w pl.json

### Po tłumaczeniu:
- [ ] en.json ma identyczną strukturę jak pl.json
- [ ] Wszystkie placeholders {0}, {1} są zachowane
- [ ] Test walidacji struktury przeszedł (Python script)
- [ ] Aplikacja działa w PL i EN bez błędów

### Przed mergem do main:
- [ ] Code review zakończony
- [ ] Testy manualne w PL i EN
- [ ] Brak [KEY_NOT_FOUND] w UI
- [ ] Performance OK (żadnych lagów przy zmianie języka)
- [ ] Dokumentacja zaktualizowana (CHANGELOG)

---

## 🎓 Dodatkowe Zasoby

### W tym repozytorium:
- [CLAUDE.md](../../CLAUDE.md) - Ogólna dokumentacja projektu
- [DOC/Frontend/](../Frontend/) - Dokumentacja frontendu
- [DOC/Core/](../Core/) - Dokumentacja core library

### Zewnętrzne:
- [Avalonia UI Docs](https://docs.avaloniaui.net/)
- [ReactiveUI Documentation](https://www.reactiveui.net/)
- [Microsoft Globalization Guide](https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization)

---

## 📞 Kontakt i Wsparcie

Jeśli masz pytania podczas implementacji:
1. Sprawdź FAQ powyżej
2. Przeczytaj odpowiedni rozdział dokumentacji
3. Stwórz issue na GitHubie

---

**Powodzenia z implementacją! 🚀**

Ostatnia aktualizacja: 2025-10-24
Wersja dokumentacji: 1.0
