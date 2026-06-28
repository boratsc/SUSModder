# Avalonia Advanced Features – Pomysły na dalszy rozwój UI

## Wprowadzenie
W projekcie SUSModder dodaliśmy dwa potężne pakiety:
- **Avalonia.Labs.Panels 11.3.1** – eksperymentalne panele layoutu z zaawansowanymi możliwościami
- **Xaml.Behaviors.Avalonia 11.3.6.5** – oficjalny system Behaviors pozwalający na attachowanie logiki do kontrolek bez dziedziczenia (wraz z dodatkowymi modułami: DragAndDrop, Draggable, Events, Responsive, Custom)

Ten dokument przedstawia pomysły na dalsze wykorzystanie tych narzędzi do ulepszenia UX aplikacji.

---

## 1. Avalonia.Labs.Panels – Zaawansowane Layouty

### 1.1 FlexPanel – Wykorzystane możliwości
**Status:** ✅ Zaimplementowane
- FlexPanel z `Wrap="Wrap"` do wyświetlania modów
- LayoutAnimationBehavior dla shuffle animacji

### 1.2 FlexPanel – Dodatkowe możliwości

#### Sortowanie z animacją
```xml
<!-- Można dynamicznie zmieniać kolejność dzieci FlexPanel i automatycznie dostaniemy animację -->
<labs:FlexPanel x:Name="ModsPanel" Wrap="Wrap">
    <i:Interaction.Behaviors>
        <behaviors:LayoutAnimationBehavior Duration="0:0:0.4" Easing="CubicEaseInOut" />
    </i:Interaction.Behaviors>
</labs:FlexPanel>
```

**Zastosowanie w SUSModder:**
- Sortowanie modów (alfabetycznie, po dacie instalacji, po rozmiarze) z płynną animacją przestawiania
- Filtrowanie modów (pokazywanie tylko full/dll/outdated) z animowanym usuwaniem/pojawianiem się

**Implementacja:** 
- W `MainWindowViewModel` dodać `ObservableCollection` z CollectionView
- Przy zmianie kryteriów sortowania – użyć `CollectionView.SortDescriptions`
- FlexPanel automatycznie animuje nowe pozycje

#### Drag & Drop z reorderowaniem
```csharp
// Behavior pozwalający przeciągać elementy i zmieniać ich kolejność
public class DragReorderBehavior : Behavior<FlexPanel>
{
    // Logika obsługi PointerPressed, PointerMoved, PointerReleased
    // Zmiana pozycji w ObservableCollection
    // FlexPanel + LayoutAnimationBehavior automatycznie animują
}
```

**Zastosowanie w SUSModder:**
- Możliwość ręcznego ustawiania kolejności ulubionych modów
- Zapisywanie preferencji użytkownika w `appsettings.json`

#### Responsywny layout z breakpoints
```csharp
// Behavior dynamicznie zmieniający Direction zależnie od szerokości okna
public class ResponsiveFlexBehavior : Behavior<FlexPanel>
{
    protected override void OnAttached()
    {
        AssociatedObject.GetObservable(Layoutable.BoundsProperty)
            .Subscribe(bounds =>
            {
                // < 800px: kolumny, >= 800px: wiersze
                AssociatedObject.Direction = bounds.Width < 800 
                    ? FlexDirection.Column 
                    : FlexDirection.Row;
            });
    }
}
```

**Zastosowanie w SUSModder:**
- Lepsze skalowanie na małych ekranach
- Automatyczne przełączanie layoutu panel boczny vs. górny

---

## 2. Avalonia.Xaml.Interactions – Behaviors & Triggers

### 2.1 Wykorzystane możliwości
**Status:** ✅ Zaimplementowane
- `LayoutAnimationBehavior` – animacja shuffle przy zmianie layoutu

### 2.2 Built-in Behaviors z pakietu

#### EventTriggerBehavior + InvokeCommandAction
```xml
<Button Content="Zainstaluj">
    <i:Interaction.Behaviors>
        <ia:EventTriggerBehavior EventName="PointerEnter">
            <ia:InvokeCommandAction Command="{Binding PreloadModDetailsCommand}" />
        </ia:EventTriggerBehavior>
    </i:Interaction.Behaviors>
</Button>
```

**Zastosowanie w SUSModder:**
- Preloadowanie szczegółów moda przy najechaniu myszką (szybsze otwieranie detali)
- Lazy loading opisów i screenów
- Prefetch kolejnych ikonek przy scrollowaniu

#### DataTriggerBehavior – warunkowe akcje
```xml
<Border x:Name="UpdateBadge">
    <i:Interaction.Behaviors>
        <ia:DataTriggerBehavior 
            Binding="{Binding HasUpdate}" 
            ComparisonCondition="Equal" 
            Value="True">
            <!-- Animuj pojawienie się badge'a -->
            <ia:ChangePropertyAction 
                PropertyName="Opacity" 
                Value="1" 
                Duration="0:0:0.3" />
        </ia:DataTriggerBehavior>
    </i:Interaction.Behaviors>
</Border>
```

**Zastosowanie w SUSModder:**
- Automatyczne pokazywanie "UPDATE AVAILABLE" badge bez kodu w codebehind
- Pulsujący efekt na przyciskach akcji gdy dostępna aktualizacja
- Zmiana kolorystyki przy błędach (czerwone podświetlenie)

### 2.3 Custom Behaviors – Pomysły na nowe

#### ParallaxScrollBehavior
```csharp
// Tło przesuwa się wolniej niż foreground przy scrollowaniu
public class ParallaxScrollBehavior : Behavior<ScrollViewer>
{
    public Control? BackgroundElement { get; set; }
    public double ParallaxFactor { get; set; } = 0.5;
    
    // Obsługa ScrollViewer.ScrollChanged
    // TranslateTransform na BackgroundElement
}
```

**Zastosowanie w SUSModder:**
- Efekt głębi w głównym widoku modów
- Background pattern przesuwa się wolniej niż mod cards
- Możliwość użycia w "About" lub widoku szczegółów moda

#### ShakeOnErrorBehavior
```csharp
// Kontrolka trzęsie się przy błędzie walidacji
public class ShakeOnErrorBehavior : Behavior<Control>
{
    // Animation: TranslateTransform.X od -10 do 10 kilka razy
    // Triggerowane przez DataValidation error lub custom event
}
```

**Zastosowanie w SUSModder:**
- Pole ścieżki instalacji przy błędnym folderze
- Przycisk instalacji gdy brak miejsca na dysku
- Wizualna informacja zwrotna bez modal dialogów

#### AutoScrollToSelectedBehavior
```csharp
// Automatyczny scroll do wybranego elementu w ListBox
public class AutoScrollToSelectedBehavior : Behavior<ListBox>
{
    // Obsługa SelectionChanged
    // ListBox.ScrollIntoView(selectedItem) z smooth scroll
}
```

**Zastosowanie w SUSModder:**
- Po instalacji moda – automatyczny scroll do niego na liście
- Po wyszukiwaniu – scroll do pierwszego wyniku
- Powrót do ostatnio instalowanego moda po restarcie

#### RippleEffectBehavior
```csharp
// Material Design ripple effect na klikalnych elementach
public class RippleEffectBehavior : Behavior<Control>
{
    // Canvas z Ellipse animowany od punktu kliknięcia
    // ScaleTransform + OpacityTransform
}
```

**Zastosowanie w SUSModder:**
- Mod cards przy kliknięciu
- Przyciski w panelu bocznym
- Nowoczesny, responsywny feeling UI

#### LoadingSpinnerBehavior
```csharp
// Automatyczne pokazywanie spinnera gdy IsBusy=true
public class LoadingSpinnerBehavior : Behavior<ContentControl>
{
    public bool IsBusy { get; set; }
    // Zamiana Content na spinner, przywrócenie po zakończeniu
}
```

**Zastosowanie w SUSModder:**
- Automatyczne loading states bez duplikacji XAML
- Ikony modów podczas ładowania
- Panel statusu podczas sprawdzania aktualizacji

#### DoubleClickBehavior ✅ ZAIMPLEMENTOWANE
```csharp
// Rozróżnia single vs double click z własnymi komendami
public class DoubleClickBehavior : Behavior<Control>
{
    public ICommand? SingleClickCommand { get; set; }
    public ICommand? DoubleClickCommand { get; set; }
    public object? CommandParameter { get; set; }
    public int DoubleClickInterval { get; set; } = 300; // ms
}
```

**Implementacja w SUSModder:**
Dodany plik: `SUSModder/Behaviors/DoubleClickBehavior.cs`
- Używa `DispatcherTimer` do rozróżnienia kliknięć
- Jeśli w ciągu 300ms nastąpi drugie kliknięcie → wykonuje `DoubleClickCommand`
- Jeśli nie → wykonuje `SingleClickCommand` po timeout

**Zastosowanie w aplikacji:**
```xml
<Border Classes="mod-card">
    <i:Interaction.Behaviors>
        <behaviors:DoubleClickBehavior 
            DoubleClickCommand="{Binding $parent[Window].DataContext.ModDoubleClickCommand}"
            CommandParameter="{Binding}" 
            DoubleClickInterval="300" />
    </i:Interaction.Behaviors>
    <!-- zawartość kafelka -->
</Border>
```

**Funkcjonalność:**
- **Dwuklik na zainstalowanym modzie** → uruchamia grę z tym modem (`LaunchAsync`)
- **Dwuklik na niezainstalowanym modzie** → instaluje mod (`Install`)
- Sprawdza `mod.IsInstalling` aby uniknąć konfliktów podczas instalacji
- Komenda: `ModDoubleClickCommand` w `MainWindowViewModel.cs`

---

## Zaimplementowane Custom Dialogi

### UninstallConfirmDialog ✅ ZAIMPLEMENTOWANY
**Cel:** Ładny, nowoczesny dialog potwierdzenia usunięcia moda z animacjami i dodatkowymi informacjami.

**Pliki:**
- `SUSModder/Views/UninstallConfirmDialog.axaml`
- `SUSModder/Views/UninstallConfirmDialog.axaml.cs`
- Używany w: `MainWindowViewModel.ModOperations.cs` (metoda `Uninstall`)

**Funkcje:**
1. **Animacje:**
   - Fade-in całego okna (300ms)
   - Slide-down ikony kosza z opóźnieniem (500ms)
   - Slide-up contentu z większym opóźnieniem (600ms)
   - Shake effect na przycisku "Usuń mod" (po 600ms, 2x powtórzenia)

2. **Wizualne akcenty:**
   - Duża ikona kosza 🗑️ w okrągłym czerwonym obramowaniu
   - Czerwony przycisk usuwania (#DC2626) z hover effectem
   - Przycisk anulowania z hover effectem (akcent niebieski)
   - Transparency blur background

3. **Automatyczne obliczanie rozmiaru:**
   - Dialog asynchronicznie oblicza rozmiar katalogu moda
   - Wyświetla info: "📦 Rozmiar do usunięcia: X.XX MB"
   - Formatowanie: B, KB, MB, GB, TB

**Przykład użycia:**
```csharp
var dialog = new UninstallConfirmDialog(modName, installPath);
await dialog.ShowDialog(mainWindow);
bool confirmed = dialog.Result;
```

**UX Improvements:**
- Jasno komunikuje nieodwracalność operacji
- Pokazuje użytkownikowi ile miejsca zwolni
- Animacje przyciągają uwagę doważnej decyzji
- Shake effect na przycisku usuwania podkreśla destrukcyjny charakter akcji

---

## 3. Kombinacje – Zaawansowane Scenariusze

### 3.1 Animated Search & Filter
```xml
<FlexPanel x:Name="SearchResults">
    <i:Interaction.Behaviors>
        <!-- Layout animation -->
        <behaviors:LayoutAnimationBehavior Duration="0:0:0.3" />
        
        <!-- Stagger animation - dzieci pojawiają się jeden po drugim -->
        <behaviors:StaggeredAppearBehavior 
            DelayBetweenItems="0:0:0.05" 
            AnimationType="FadeSlideUp" />
    </i:Interaction.Behaviors>
</FlexPanel>
```

**Implementacja:**
- `StaggeredAppearBehavior` dodaje każdemu dziecku delay proporcjonalny do indeksu
- Efekt "kaskadowego" pojawiania się wyników wyszukiwania
- Profesjonalny feeling jak w nowoczesnych webappach

### 3.2 Context-Aware Tooltips
```csharp
// Tooltip pokazuje różne treści zależnie od stanu
public class SmartTooltipBehavior : Behavior<Control>
{
    // Dynamiczna zmiana ToolTip.Content
    // Dla moda: "Kliknij aby zobaczyć szczegóły" vs "Instalowanie... 45%"
}
```

### 3.3 Gesture Recognition
```csharp
// Rozpoznawanie gestów (swipe, pinch, etc.)
public class SwipeGestureBehavior : Behavior<Control>
{
    // PointerPressed -> PointerMoved -> PointerReleased
    // Wykrycie kierunku i prędkości
    public ICommand? SwipeLeftCommand { get; set; }
    public ICommand? SwipeRightCommand { get; set; }
}
```

**Zastosowanie w SUSModder:**
- Swipe right na modzie: szybka instalacja
- Swipe left: usunięcie
- Touch-friendly na tabletach z Windows

### 3.4 Skeleton Loading
```csharp
// Placeholder z animowanym gradientem podczas ładowania
public class SkeletonBehavior : Behavior<Control>
{
    // Gradient brush z AnimatedGradientBrush
    // Shimmer effect przesuwający się w lewo
}
```

**Zastosowanie w SUSModder:**
- Podczas ładowania listy modów – skeleton cards
- Lepsze UX niż pusty ekran lub spinner
- Użytkownik wie że coś się dzieje i ile miejsca zajmie content

---

## 4. Rekomendacje Implementacyjne

### Faza 1: Quick Wins (1-2h pracy)
1. **AutoScrollToSelectedBehavior** – natychmiastowa poprawa UX po instalacji
2. **DataTriggerBehavior** dla update badges – czysty XAML zamiast codebehind
3. ✅ **ShakeOnErrorBehavior** – lepsza informacja zwrotna przy błędach (ZAIMPLEMENTOWANE jako ShakeOnLoadBehavior)
   - Zastosowane w UninstallConfirmDialog na przycisku "Usuń mod"
   - Subtelny shake effect zwracający uwagę na destrukcyjną akcję
   - Konfigurowalne: Intensity, RepeatCount, Delay

### Faza 2: Polish (3-5h)
4. ✅ **DoubleClickBehavior** – ergonomia używania listy modów (ZAIMPLEMENTOWANE)
   - Dwuklik na zainstalowanym modzie → uruchamia grę
   - Dwuklik na niezainstalowanym modzie → instaluje mod
   - Behavior odróżnia single i double click z 300ms timeoutem
5. **RippleEffectBehavior** – nowoczesny material design feel
6. **SmartTooltipBehavior** – kontekstowa pomoc

### Faza 3: Advanced (5-10h)
7. **Drag & Drop reordering** – personalizacja kolejności modów
8. **StaggeredAppearBehavior** – animacje wyszukiwania
9. **SkeletonBehavior** – professional loading states
10. **ResponsiveFlexBehavior** – wsparcie dla małych ekranów

### Faza 4: Nice-to-Have
11. **ParallaxScrollBehavior** – efekt wow
12. **SwipeGestureBehavior** – touch support
13. **LoadingSpinnerBehavior** – unified loading pattern

---

## 5. Dodatkowe Pakiety do Rozważenia

### Xaml.Behaviors.Interactions.* (już zainstalowane!)
Wraz z głównym pakietem otrzymaliśmy kilka dodatkowych modułów:
- **Xaml.Behaviors.Interactions.DragAndDrop** – gotowe behaviors do drag & drop
- **Xaml.Behaviors.Interactions.Draggable** – elementy draggable
- **Xaml.Behaviors.Interactions.Events** – zaawansowane event triggers
- **Xaml.Behaviors.Interactions.Responsive** – responsive behaviors
- **Xaml.Behaviors.Interactions.Custom** – dodatkowe custom behaviors

Warto zbadać te moduły – mogą zawierać gotowe rozwiązania zamiast pisania własnych!

### Semi.Avalonia
Biblioteka Material Design komponentów – jeśli chcemy pełnego Material look.

### Avalonia.Controls.DataGrid
Jeśli dodamy "advanced view" do zarządzania modami w formie tabeli zamiast kart.

### FluentAvalonia
Fluent Design System (Windows 11 style) – alternatywa dla obecnego Fluent Theme z dodatkowymi kontrolkami (NavigationView, InfoBar, etc.).

---

## 6. Podsumowanie

Dodane pakiety otwierają drogę do:
- ✨ **Bogatszych animacji** bez pisania skomplikowanego kodu
- 🎯 **Lepszej separacji logiki** (behaviors zamiast codebehind)
- 🚀 **Szybszego prototypowania** nowych feature'ów UI
- 💎 **Bardziej profesjonalnego feelingu** aplikacji

**Najlepsza strategia:** Implementować stopniowo, zaczynając od quick wins (AutoScroll, DataTrigger, Shake), następnie dodawać polish (Ripple, DoubleClick), a na końcu advanced features (Drag&Drop, Stagger, Skeleton).

Każdy behavior jest reużywalny – raz napisany może być użyty w wielu miejscach aplikacji.

---

*Dokument stworzony: 21.10.2025*  
*Autor: GitHub Copilot*  
*Kontekst: SUSModder v2.0.0 – feature branch z animacjami i status barem*
