# Narzędzia Analizy Lokalizacji SUSModder

Ten katalog zawiera narzędzia do zarządzania i analizy kluczy lokalizacyjnych.

## 🔧 Dostępne narzędzia

### 1. `analyze_keys.py`
**Analiza użycia kluczy lokalizacyjnych**

Funkcje:
- Porównuje klucze w `pl.json` i `en.json`
- Wykrywa nadmiarowe klucze (zdefiniowane ale nie używane)
- Wykrywa brakujące klucze (używane ale nie zdefiniowane)
- Generuje szczegółowy raport statystyczny

Użycie:
```bash
python analyze_keys.py
```

Output:
- Raport konsolowy z podziałem na kategorie
- Statystyki użycia (% wykorzystania, liczba kluczy)
- Lista potencjalnie nadmiarowych kluczy

### 2. `clean_keys.py`
**Czyszczenie i formatowanie plików JSON**

Funkcje:
- Usuwa duplikaty kluczy
- Formatuje JSON z odpowiednim wcięciem
- Zachowuje kolejność kluczy

Użycie:
```bash
python clean_keys.py
```

Output:
- Czyści `pl.json` i `en.json`
- Raportuje usunięte duplikaty
- Zapisuje z formatowaniem UTF-8

## 📋 Workflow

### Regularna weryfikacja (co miesiąc):
```bash
# 1. Analiza
python analyze_keys.py > monthly_report.txt

# 2. Review raportu
# Sprawdź czy są nowe nadmiarowe/brakujące klucze

# 3. Opcjonalne czyszczenie
python clean_keys.py
```

### Przed wydaniem nowej wersji:
```bash
# 1. Pełna analiza
python analyze_keys.py

# 2. Usuń nieużywane klucze (opcjonalnie)
# Edytuj ręcznie pl.json i en.json

# 3. Wyczyść duplikaty
python clean_keys.py

# 4. Weryfikacja
python analyze_keys.py

# 5. Build i testy
dotnet build
```

## 📊 Interpretacja wyników

### Nadmiarowe klucze (wysokie %)
- **40-60%**: Normalne dla aplikacji w rozwoju
- **60-80%**: Rozważ przegląd i usunięcie nieużywanych
- **>80%**: Zalecane czyszczenie

### Brakujące klucze
- **Zawsze napraw natychmiast** - powodują błędy [KEY_NOT_FOUND]

### Niespójności pl.json vs en.json
- **Zawsze zsynchronizuj** - zapewnia kompletne tłumaczenia

## 🎯 Best Practices

1. **Uruchom `analyze_keys.py` przed każdym commitem** z większymi zmianami w UI
2. **Nie usuwaj kluczy z kategorii `Errors`, `Dialogs`, `Status`** - są używane dynamicznie
3. **Zachowuj spójność** - zawsze dodawaj klucze do obu plików (pl i en)
4. **Dokumentuj** - dodawaj komentarze w KEYS_ANALYSIS_REPORT.md o powodach zachowania nadmiarowych kluczy

## 📁 Pliki wygenerowane

- `analysis_output.txt` - Ostatni raport analizy (ignorowany przez Git)
- `DOC/Localization/KEYS_ANALYSIS_REPORT.md` - Pełny raport z rekomendacjami

## 🔍 Znane ograniczenia

- Narzędzia nie wykrywają **dynamicznego użycia kluczy** (np. w interpolacji stringów)
- Nie analizują kodu w `SUSModder.Core` (tylko ViewModels i Views)
- Regex może nie wykryć wszystkich edge case'ów

## 📝 Przyszłe usprawnienia

- [ ] Wykrywanie dynamicznego użycia kluczy
- [ ] Generowanie sugestii usuwania w formacie skryptu
- [ ] Integracja z CI/CD (automatyczne testy przed merge)
- [ ] Raportowanie zmian w czasie (trend użycia kluczy)

---

**Autor**: Narzędzia wygenerowane automatycznie w ramach optymalizacji systemu lokalizacji
**Data utworzenia**: 2025-10-24
