# 01 – DLL modal + komunikacja kompatybilności — PLAN WDROŻENIA

**Priorytet:** 🔴 P0 ✅  
**Effort:** ~2.5h (zrealizowane)  
**Status:** ✅ Zaimplementowane 2026-05-25

> Implementacja: `sus-builder` (DeepSeek V4 Flash Free)  
> Review: `sus-quality-reviewer` (Qwen3.6 Plus Free)  
> Dodatkowo: automatyczne zaznaczanie polecanych (Favorite) DLL

---

## 1. Co już działa (nie trzeba robić)

Po analizie kodu (`DllModSelectionView.axaml:64-81`, `DllModSelectionViewModel.cs:140-180`):

- ✅ **`CompatibilityWarning`** – już pokazywany jako tekst (`#FF9800`, italic) pod opisem DLL
- ✅ **`CompatibilityDescription`** – już pokazywany jako tekst w kolumnie 3 (prawa strona karty)
- ✅ **Filtrowanie `NotWork`** – niekompatybilne DLL są pomijane w `LoadAndSortDllModsAsync():152`
- ✅ **Sortowanie wg priorytetu** – Favorite → Works → NotTested (linia 183-197)
- ✅ **i18n** – `DllManager.*` ma 30+ kluczy PL/EN, infrastruktura gotowa

**Wniosek:** nie trzeba dodawać opisu kompatybilności ani warningu – już są. Skupiamy się na komunikacji z userem.

---

## 2. Co trzeba zrobić (5 zadań)

### Zadanie 1: Kontekstowy nagłówek z nazwą moda

**Problem:** `ToolModalTitle` zwraca sztywne `"Modyfikacje DLL"` – user nie wie, że to dotyczy Town of Us.

**Gdzie:** `MainWindowViewModel.FrontendLayout.cs:47-58`

**Obecnie:**
```csharp
// linia 54
return _localizationService.Get("DllManager.ViewTitle");  // "Modyfikacje DLL"
```

**Zmiana:**
```csharp
// W property ToolModalTitle, case dla DLL:
if (IsDllModalVisible)
{
    if (DllSelectionModalViewModel != null)
    {
        var modName = DllSelectionModalViewModel.TargetModName;  // NOWE
        return _localizationService.GetFormatted("DllManager.ViewTitleForMod", modName);
    }
    return _localizationService.Get("DllManager.ViewTitle");
}
```

**Co dodać:**
- `DllModSelectionViewModel.TargetModName` – property zwracająca `_targetMod.ModName`
- Klucze i18n: `"DllManager.ViewTitleForMod": "Modyfikacje DLL dla {0}"` / `"DLL Modifications for {0}"`

**Pliki:** `MainWindowViewModel.FrontendLayout.cs`, `DllModSelectionViewModel.cs`, `pl.json`, `en.json`

---

### Zadanie 2: Legenda kompatybilności + podtytuł

**Problem:** Emoji 🟢🔵⚪ są nieczytelne bez legendy. Brak wyjaśnienia czym są DLL.

**Gdzie:** `DllModSelectionView.axaml` – dodać przed `ScrollViewer` (linia 16)

**Nowy XAML (przed `<ScrollViewer>`, wewnątrz `Grid.Row="0"`):**
```xml
<!-- PODTYTUŁ -->
<StackPanel Margin="0,0,0,12" Spacing="6">
    <TextBlock Text="{localize:Localize DllManager.WhatAreDlls}"
               FontSize="12"
               Foreground="{DynamicResource TextSecondaryBrush}"
               TextWrapping="Wrap"/>
    
    <!-- LEGENDA -->
    <Border Background="{DynamicResource SecondaryBackgroundBrush}"
            CornerRadius="6" Padding="10,8">
        <StackPanel Orientation="Horizontal" Spacing="20">
            <StackPanel Orientation="Horizontal" Spacing="6">
                <TextBlock Text="🟢" FontSize="14"/>
                <TextBlock Text="{localize:Localize DllManager.LegendFavorite}"
                           FontSize="12" VerticalAlignment="Center"
                           Foreground="{DynamicResource TextPrimaryBrush}"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="6">
                <TextBlock Text="🔵" FontSize="14"/>
                <TextBlock Text="{localize:Localize DllManager.LegendWorks}"
                           FontSize="12" VerticalAlignment="Center"
                           Foreground="{DynamicResource TextPrimaryBrush}"/>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="6">
                <TextBlock Text="⚪" FontSize="14"/>
                <TextBlock Text="{localize:Localize DllManager.LegendNotTested}"
                           FontSize="12" VerticalAlignment="Center"
                           Foreground="{DynamicResource TextPrimaryBrush}"/>
            </StackPanel>
        </StackPanel>
    </Border>
</StackPanel>
```

**Nowe klucze i18n:**
```
"DllManager.WhatAreDlls": "Dodatkowe pluginy rozszerzające funkcje moda. Możesz pominąć."
"DllManager.LegendFavorite": "⭐ Polecane"
"DllManager.LegendWorks": "✅ Działa"
"DllManager.LegendNotTested": "❓ Nieprzetestowane"
```

**Pliki:** `DllModSelectionView.axaml`, `pl.json`, `en.json`

---

### Zadanie 3: Przycisk "Pomiń wszystko"

**Problem:** Jedyna opcja wyjścia to ✕ w rogu. User nie wie, że może bezpiecznie pominąć.

**Gdzie:** `DllModSelectionView.axaml` – stopka (linia 118-136)

**Obecnie (linia 123):**
```xml
<Grid ColumnDefinitions="*,Auto">
    <TextBlock ... Text="{localize:Localize DllManager.ApplyChangesHint}"/>
    <Button ... Content="{Binding ActionButtonText}" Command="{Binding ApplyChangesCommand}"/>
</Grid>
```

**Zmiana:**
```xml
<Grid ColumnDefinitions="*,Auto,Auto">
    <TextBlock Grid.Column="0" ... />
    <Button Grid.Column="1" 
            Content="{localize:Localize DllManager.SkipAll}"
            Command="{Binding CloseCommand}"
            Background="Transparent"
            Foreground="{DynamicResource TextSecondaryBrush}"
            BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1"
            Padding="15,10" CornerRadius="6"
            Margin="0,0,10,0"/>
    <Button Grid.Column="2" 
            Content="{Binding ActionButtonText}" 
            Command="{Binding ApplyChangesCommand}"
            Background="{DynamicResource AccentBrush}"
            Foreground="White"
            Padding="15,10" CornerRadius="6"/>
</Grid>
```

**Nowy klucz i18n:** `"DllManager.SkipAll": "Pomiń wszystko"` / `"Skip all"`

**Pliki:** `DllModSelectionView.axaml`, `pl.json`, `en.json`

---

### Zadanie 4: Licznik ukrytych niekompatybilnych

**Problem:** User nie wie, że niektóre DLL zostały ukryte (bo są `NotWork`).

**Gdzie:** `DllModSelectionViewModel.cs` – dodać property, `DllModSelectionView.axaml` – dodać TextBlock

**Nowa property w ViewModelu:**
```csharp
private int _hiddenIncompatibleCount;
public int HiddenIncompatibleCount
{
    get => _hiddenIncompatibleCount;
    set => this.RaiseAndSetIfChanged(ref _hiddenIncompatibleCount, value);
}

public bool HasHiddenIncompatible => HiddenIncompatibleCount > 0;
```

**W `LoadAndSortDllModsAsync()`, po linii 152 (gdzie filtrowane są NW):**
```csharp
// W pętli foreach, gdy compat?.Status == CompatibilityStatus.NotWork:
hiddenCount++;  // zliczaj pominięte
```

**W XAML, przed stopką (linia 115, po `</ScrollViewer>`):**
```xml
<TextBlock IsVisible="{Binding HasHiddenIncompatible}"
           FontSize="11"
           Foreground="{DynamicResource TextSecondaryBrush}"
           Margin="0,8,0,0">
    <Run Text="⚠️ "/>
    <Run Text="{Binding HiddenIncompatibleCount}"/>
    <Run Text="{localize:Localize DllManager.HiddenIncompatible}"/>
</TextBlock>
```

**Nowy klucz i18n:** `"DllManager.HiddenIncompatible": "modyfikacji ukryto (niekompatybilne)"` / `"modifications hidden (incompatible)"`

**Pliki:** `DllModSelectionViewModel.cs`, `DllModSelectionView.axaml`, `pl.json`, `en.json`

---

### Zadanie 5: Hardcoded kolory → DynamicResource

**Problem:** `#4CAF50` i `#FF9800` są na sztywno – nie działają z pink theme.

**Gdzie:** `DllModSelectionView.axaml:49` (StatusInstalled) i `:66` (CompatibilityWarning)

**Zmiana:**
```xml
<!-- Z: -->
<TextBlock Foreground="#4CAF50" ... />
<TextBlock Foreground="#FF9800" ... />

<!-- Na: -->
<TextBlock Foreground="{DynamicResource SuccessBrush}" ... />
<TextBlock Foreground="{DynamicResource WarningBrush}" ... />
```

Upewnić się, że `SuccessBrush` i `WarningBrush` są zdefiniowane w `DarkTheme.axaml` i `PinkTheme.axaml`.

**Pliki:** `DllModSelectionView.axaml`, `Themes/DarkTheme.axaml`, `Themes/PinkTheme.axaml`

---

## 3. Nie robimy (już jest)

| ❌ Zadanie z pierwotnego planu | Dlaczego nie |
|------|-------------|
| Tekstowy status pod nazwą DLL | Już jest – `CompatibilityDescription` w Grid.Column=3 |
| Warning o kompatybilności | Już jest – `CompatibilityWarning` italic pod opisem |
| "Favorite" → opis w UI | Tylko w legendzie (Zadanie 2) – enum zostaje `Favorite` |

---

## 4. Podsumowanie – tabela implementacji

| # | Zadanie | Pliki | Effort |
|---|---------|-------|--------|
| 1 | Kontekstowy nagłówek "Modyfikacje DLL dla {mod}" | `FrontendLayout.cs`, `DllModSelectionViewModel.cs`, `pl.json`, `en.json` | 30 min |
| 2 | Legenda + podtytuł "Czym są DLL" | `DllModSelectionView.axaml`, `pl.json`, `en.json` | 45 min |
| 3 | Przycisk "Pomiń wszystko" | `DllModSelectionView.axaml`, `pl.json`, `en.json` | 20 min |
| 4 | Licznik ukrytych niekompatybilnych | `DllModSelectionViewModel.cs`, `DllModSelectionView.axaml`, `pl.json`, `en.json` | 30 min |
| 5 | Hardcoded kolory → DynamicResource | `DllModSelectionView.axaml`, `DarkTheme.axaml`, `PinkTheme.axaml` | 15 min |
| **Razem** | | | **~2.5h** |

---

## 5. Kolejność implementacji

```
1. Najpierw i18n (5 nowych kluczy PL + EN)          ← blokuje UI
2. DllModSelectionViewModel – TargetModName + HiddenIncompatibleCount
3. DllModSelectionView.axaml – legenda, podtytuł, przycisk Pomiń, licznik
4. FrontendLayout.cs – ToolModalTitle z nazwą moda
5. Theme – DynamicResource zamiast #4CAF50/#FF9800
```

---

## 6. Nowe klucze i18n

| Klucz | PL | EN |
|-------|-----|-----|
| `DllManager.ViewTitleForMod` | `Modyfikacje DLL dla {0}` | `DLL Modifications for {0}` |
| `DllManager.WhatAreDlls` | `Dodatkowe pluginy rozszerzające funkcje moda. Możesz pominąć.` | `Additional plugins that extend mod features. You can skip this.` |
| `DllManager.LegendFavorite` | `⭐ Polecane` | `⭐ Recommended` |
| `DllManager.LegendWorks` | `✅ Działa` | `✅ Works` |
| `DllManager.LegendNotTested` | `❓ Nieprzetestowane` | `❓ Not tested` |
| `DllManager.SkipAll` | `Pomiń wszystko` | `Skip all` |
| `DllManager.HiddenIncompatible` | `modyfikacji ukryto (niekompatybilne)` | `modifications hidden (incompatible)` |
