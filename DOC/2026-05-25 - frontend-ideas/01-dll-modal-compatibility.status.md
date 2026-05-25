# 01 – DLL modal compatibility — STATUS IMPLEMENTACJI

**Data:** 2026-05-25  
**Branch:** `susmodder-3.0` (nowy)  
**Stan:** ✅ W pełni zaimplementowane

## Co zostało zrobione

### Zadanie 1 — Kontekstowy nagłówek z nazwą moda
- **Plik:** `MainWindowViewModel.FrontendLayout.cs`
- `ToolModalTitle` dla `IsDllSelectionModalVisible` sprawdza `DllSelectionModalViewModel.TargetModName`
- Wyświetla `"Modyfikacje DLL dla {nazwa moda}"` lub fallback `"Modyfikacje DLL"`

### Zadanie 2 — Legenda kompatybilności + podtytuł
- **Plik:** `DllModSelectionView.axaml`
- Podtytuł "Czym są DLL" nad listą
- Legenda z kolorami: ⭐ Polecane / ✅ Działa / ❓ Nieprzetestowane

### Zadanie 3 — Przycisk "Pomiń wszystko"
- **Plik:** `DllModSelectionView.axaml`
- Stopka: `*,Auto,Auto` — przycisk "Pomiń wszystko" (CloseCommand) obok "Zastosuj"

### Zadanie 4 — Licznik ukrytych niekompatybilnych
- **Plik:** `DllModSelectionViewModel.cs`, `DllModSelectionView.axaml`
- `_hiddenIncompatibleCount` resetowany i inkrementowany w `LoadAndSortDllModsAsync()`
- Wyświetlany jako `⚠️ N modyfikacji ukryto (niekompatybilne)` pod listą

### Zadanie 5 — Kolory → DynamicResource
- **Pliki:** `DllModSelectionView.axaml`, `DarkTheme.axaml`, `PinkTheme.axaml`
- `#4CAF50` → `{DynamicResource SuccessBrush}`
- `#FF9800` → `{DynamicResource WarningBrush}`
- `WarningBrush` dodany do DarkTheme i PinkTheme

### Auto-select dla polecanych (Favorite) DLL
- **Plik:** `DllModSelectionViewModel.cs`
- `mod.IsSelected = mod.IsInstalled || compat?.Status == CompatibilityStatus.Favorite`
- Polecane DLL automatycznie zaznaczone nawet jeśli nie były wcześniej zainstalowane

### i18n
- **Pliki:** `pl.json`, `en.json`
- 7 nowych kluczy: `ViewTitleForMod`, `WhatAreDlls`, `LegendFavorite`, `LegendWorks`, `LegendNotTested`, `SkipAll`, `HiddenIncompatible`

### Fix layoutu — legenda była niewidoczna
- Wewnętrzny `<Grid>` nie miał `RowDefinitions` → wszystkie elementy nachodziły na siebie
- Dodano `RowDefinitions="Auto,*,Auto"` i przypisano wiersze (legenda→0, lista→1, licznik→2)

## Zmodyfikowane pliki (12)
| Plik | Zmiana |
|------|--------|
| `DllModSelectionView.axaml` | Layout (RowDefinitions), legenda, przycisk Pomiń, licznik, DynamicResource |
| `DllModSelectionViewModel.cs` | TargetModName, HiddenIncompatibleCount, auto-select Favorite, zliczanie |
| `MainWindowViewModel.FrontendLayout.cs` | ToolModalTitle z nazwą moda |
| `DarkTheme.axaml` | Dodany WarningBrush |
| `PinkTheme.axaml` | Dodany WarningBrush |
| `pl.json` | 7 nowych kluczy PL |
| `en.json` | 7 nowych kluczy EN |
