# 06 – Mikrointerakcje i polish

**Priorytet:** 🟢 P2  
**Effort:** ~1 dzień (wszystkie razem)  

Rzeczy które nie zmieniają funkcjonalności, ale podnoszą jakość odczuwaną.

## 1. Hover efekty na kartach modów

- **Scale 1.03** na hover
- **BoxShadow** zwiększający się (elevation: 2dp → 4dp → 8dp)
- Już jest `ToolTip.ShowDelay="300"` – ✅

## 2. Ripple effect na przyciskach

- Material Design – efekt fali od punktu kliknięcia
- `Xaml.Behaviors.Avalonia` już w projekcie → można jako Behavior
- Szczególnie na: FAB, primary buttony, karty modów

## 3. Skeleton loading / shimmer

- Przy przeładowaniu listy modów: szare "szkielety" zamiast pustki
- Lepsze wrażenie szybkości
- Można zrobić jako `SkeletonCard` control + `IsLoading` property

## 4. Lepsze tooltipy na kartach modów

Zamiast gołego tekstu – sformatowany tooltip:
```
┌─────────────────────────┐
│ Town of Us              │
│ Wersja: 5.1.2           │
│ Among Us: 2024.10.29    │
│ Status: Zainstalowany ✅ │
└─────────────────────────┘
```

## 5. Button states

- **Pressed** – ciemniejszy / wklęśnięty
- **Disabled** – wygaszony + `Cursor="No"`
- **Loading** – spinner zamiast tekstu (np. podczas instalacji)

## Gdzie w kodzie

| Co | Plik |
|----|------|
| Style kart modów | `SUSModder/Styles/ModCardStyle.axaml` |
| Style przycisków | `SUSModder/Styles/MenuButtonStyle.axaml`, `FabButtonStyle.axaml` |
| Behavior ripple | Nowy: `Behaviors/RippleBehavior.cs` |
| Skeleton | Nowy: `Controls/SkeletonCard.axaml` + `Controls/SkeletonCard.axaml.cs` |
| Tooltipy | `MainWindow.axaml` → DataTemplate kart modów |

---

# Plan Implementacji

## Założenia

- **Zero nowych zależności NuGet** – wykorzystujemy istniejące pakiety (`Xaml.Behaviors.Avalonia`, `Avalonia.Labs.Panels`)
- **MVVM + stylowanie przez style** – cały wygląd idzie przez `Styles/*.axaml` i `Behaviors/*.cs`, ViewModele pozostają nietknięte (poza ewentualnym dodaniem `IsLoading` property)
- **i18n-ready** – nowe stringi w tooltipach używają locale-aware resource keys (PL/EN)
- **Backward compatible** – istniejące selektory i klasy nie są zmieniane, tylko rozszerzane

## Krok 1: Hover efekty na kartach modów

**Plik:** `SUSModder/Styles/ModCardStyle.axaml`

1. Dodać do `.mod-card`:
   - `RenderTransform="scale(1.0)"` z `Transitions` dla `RenderTransform` (0.2s, CubicEaseOut)
   - `BoxShadow="0 2 8 0 #30000000"` (spoczynek, ~2dp)
2. Dodać selektor `ListBoxItem:pointerover Border.mod-card`:
   - `RenderTransform="scale(1.03)"`
   - `BoxShadow="0 8 24 0 #40000000"` (~8dp)
   - Zachować istniejący `BorderBrush` / `BorderThickness`

## Krok 2: Ripple effect na przyciskach

**Nowy plik:** `SUSModder/Behaviors/RippleBehavior.cs`

Behavior typu `RippleBehavior : Behavior<Control>`:
- Tworzy `Canvas` overlay na kontrolce (pojawi się przy kliknięciu)
- `PointerPressed` → tworzy `Ellipse` w punkcie kliknięcia z `ScaleTransform` i `Opacity` animacją
- Fala rośnie (scale 0 → docelowy rozmiar) i znika (opacity 1 → 0) w ~600ms
- Po animacji Ellipse jest usuwana z drzewa
- Właściwości konfigurowalne: `RippleColor`, `RippleDuration`, `MaxRadius`

**Zastosowanie w XAML:**
```xml
<Button Classes="fab">
    <i:Interaction.Behaviors>
        <behaviors:RippleBehavior RippleColor="#FFFFFF" RippleOpacity="0.3"/>
    </i:Interaction.Behaviors>
</Button>
```

## Krok 3: Skeleton loading / shimmer

**Nowe pliki:** `SUSModder/Controls/SkeletonCard.axaml` + `.axaml.cs`

Kontrolka `SkeletonCard : ContentControl`:
- Wyświetla "szkielet" karty moda (prostokąt 140×140 z zaokrąglonymi rogami 8px)
- Animowany shimmer: gradient z przesuwającym się pasem światła (LinearGradientBrush z animacją offsetów)
- Właściwość `IsLoading` → gdy `true` pokazuje skeleton overlay, gdy `false` przepuszcza Content

**Użycie na liście modów:**
W `MainWindow.axaml` ListBox ItemTemplate opakować w skeleton:
```xml
<controls:SkeletonCard IsLoading="{Binding ...}">
    <Border Classes="mod-card">...</Border>
</controls:SkeletonCard>
```

**Gdzie `IsLoading`?** Dodajemy do `ModItem` property `IsLoading` (np. `true` podczas początkowego ładowania konfiguracji), a w ViewModelu `MainWindowViewModel` ustawiamy `IsLoading = true` na wszystkich modach przed załadowaniem konfiguracji, i `false` po.

## Krok 4: Lepsze tooltipy na kartach modów

**Plik:** `SUSModder/Views/MainWindow.axaml` – sekcja DataTemplate karty moda

Zamienić prosty tooltip z `Name` + `Description` na sformatowaną kartę:
- Ramka (Border) z paddingiem, zaokrągleniami, gradientowym tłem
- Nagłówek: **Nazwa moda** (bold, fontSize 14)
- Separator (linia)
- Szczegóły:
  - "Wersja: {ModVersion}" lub "Wersja: —"
  - "Among Us: {AmongVersion}" lub "Among Us: —"
  - "Status: Zainstalowany ✅" / "Status: Niezainstalowany ❌"
- Wykorzystać `StringTruncateConverter` dla długich wersji
- i18n: lokalizowane stringi przez binding do statycznych zasobów lub convertera

## Krok 5: Button states

### 5a. MenuButtonStyle.axaml (pressed / disabled)
- Selektor `Button.menu-button:pressed` → ciemniejsze tło / scale(0.97)
- Selektor `Button.menu-button:disabled` → opacity 0.5, `Cursor="No"`

### 5b. FabButtonStyle.axaml (disabled)
- Selektor `Button.fab:disabled` → opacity 0.5, `Cursor="No"`, bez cienia
- Selektor `Button.fab-menu-item:disabled` → opacity 0.5, `Cursor="No"`

### 5c. LinkButtonStyle.axaml (disabled)
- Selektor `Button.link:disabled` → opacity 0.4, `Cursor="No"`

## Kolejność implementacji

1. Hover efekty na kartach modów (ModCardStyle.axaml) – najprostsze, czysty XAML
2. Button states (MenuButtonStyle.axaml, FabButtonStyle.axaml) – też XAML tylko
3. Lepsze tooltipy (MainWindow.axaml) – XAML + bindingi
4. Ripple behavior (RippleBehavior.cs) – nowy plik C#
5. Skeleton loading (SkeletonCard.axaml + .cs) – nowa kontrolka, najwięcej roboty

## Status implementacji (2026-05-26)

| Komponent | Status | Pliki |
|-----------|--------|-------|
| Hover efekty na kartach modów | ✅ Zaimplementowane | `SUSModder/Styles/ModCardStyle.axaml` |
| Button states (pressed/disabled) | ✅ Zaimplementowane | `MenuButtonStyle.axaml`, `FabButtonStyle.axaml`, `LinkButtonStyle.axaml` |
| Lepsze tooltipy | ✅ Zaimplementowane | `SUSModder/Views/MainWindow.axaml` |
| RippleBehavior | ✅ Zaimplementowane | `SUSModder/Behaviors/RippleBehavior.cs` |
| SkeletonCard (pulsujący placeholder) | ✅ Zaimplementowane | `SUSModder/Controls/SkeletonCard.axaml` + `.cs` |
| InverseBoolConverter | ✅ Dodany | `SUSModder/Converters/InverseBoolConverter.cs` |

**Build:** ✅ 0 błędów, 0 ostrzeżeń

## Uwagi końcowe

- **SkeletonCard**: Używa pulsującej animacji opacity zamiast pełnego shimmera (prostsze i niezawodne). Do docelowego użycia wymaga podpięcia `IsLoading` w ViewModelu i DataTemplate.
- **RippleBehavior**: Pełny efekt fali (Canvas + Ellipse) działa na `Panel` i `Border`. Dla `Button` i innych kontrolek zostaje pominięty (bezpieczny fallback). Do zastosowania w XAML wymaga dodania `<behaviors:RippleBehavior/>` w `Interaction.Behaviors`.
- **Tooltip**: Używa istniejących kluczy lokalizacji (`UI.Labels.ModVersion`, `UI.ModStatus.Installed`). Wymaga działania `LocalizationService`.

## Testowanie

1. `dotnet build` po każdym kroku
2. Weryfikacja wizualna przez uruchomienie aplikacji
3. Sprawdzenie tooltipów na modach (zainstalowanych i nie)
4. Sprawdzenie hover na kartach i przyciskach
5. Sprawdzenie ripple na FAB i przyciskach menu
6. Sprawdzenie skeleton podczas wolnego ładowania
