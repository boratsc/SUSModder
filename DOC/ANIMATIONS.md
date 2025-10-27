# Animacje w SUSModder

## Przejścia między modami

Dodano płynne animacje przejść podczas przełączania się między różnymi modami w panelu głównym.

### Dostępne style animacji

Aplikacja oferuje 3 różne style animacji dla panelu modów (w pliku `MainWindow.axaml`, linia ~503):

#### 1. **mod-content-transition** - Fade + Scale
```xml
Classes="mod-content-transition"
```
- Efekt: Delikatne powiększenie (scale) + fade in/out
- Czas trwania: 350ms
- Najsubtelniejsza animacja

#### 2. **mod-content-slide** (obecnie używana)
```xml
Classes="mod-content-slide"
```
- Efekt: Wysuwanie od dołu (20px) + fade in/out
- Czas trwania: 400ms
- Balans między subtelnością a efektem wizualnym

#### 3. **mod-content-fancy** - Kombinacja wszystkiego
```xml
Classes="mod-content-fancy"
```
- Efekt: Slide (15px) + Scale (98% → 100%) + fade in/out
- Czas trwania: 450ms
- Najbardziej "bajerancka" animacja

### Jak zmienić styl animacji?

W pliku `SUSModder/Views/MainWindow.axaml` znajdź linię (~503):

```xml
<StackPanel DataContext="{Binding SelectedMod}"
            Classes="mod-content-slide"
            Classes.visible="{Binding $parent[local:MainWindow].DataContext.IsModContentVisible}">
```

Zmień `Classes="mod-content-slide"` na jedną z innych dostępnych opcji:
- `mod-content-transition`
- `mod-content-fancy`

### Jak działa system animacji?

1. **ViewModel** (`MainWindowViewModel.cs`):
   - Property `IsModContentVisible` kontroluje widoczność zawartości
   - Przy zmianie moda: najpierw ustawia `false` (fade out), po 150ms → `true` (fade in)
   - Przy pierwszym wyborze: krótsze opóźnienie (50ms)

2. **Style** (`AnimationStyles.axaml`):
   - Definiują początkowy stan (Opacity=0, Transform)
   - Definiują stan docelowy (z klasą `.visible`)
   - Transitions automatycznie animują przejście

3. **View** (`MainWindow.axaml`):
   - StackPanel z klasą animacji
   - Binding `Classes.visible` do property `IsModContentVisible`

### Dostosowywanie animacji

Aby zmienić parametry animacji, edytuj `SUSModder/Styles/AnimationStyles.axaml`:

- **Czas trwania**: `Duration="0:0:0.4"` (format: godziny:minuty:sekundy.milisekundy)
- **Easing**: `Easing="CubicEaseOut"` (dostępne: Linear, CubicEaseIn, CubicEaseOut, CubicEaseInOut, QuadraticEaseIn, itd.)
- **Wartości transform**: Zmień wartości `TranslateTransform Y`, `ScaleTransform ScaleX/ScaleY`
- **Opóźnienie w ViewModelu**: Zmień wartość w `Task.Delay(150)` (linia ~220 w MainWindowViewModel.cs)

### Przykład: Stworzenie własnej animacji

```xml
<!-- Super bajerancka z rotation -->
<Style Selector="StackPanel.mod-content-rotate">
    <Setter Property="Opacity" Value="0"/>
    <Setter Property="RenderTransform">
        <Setter.Value>
            <TransformGroup>
                <ScaleTransform ScaleX="0.9" ScaleY="0.9"/>
                <RotateTransform Angle="-5"/>
                <TranslateTransform Y="30"/>
            </TransformGroup>
        </Setter.Value>
    </Setter>
    <Setter Property="Transitions">
        <Transitions>
            <DoubleTransition Property="Opacity" Duration="0:0:0.5" Easing="CubicEaseOut"/>
            <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.5" Easing="CubicEaseOut"/>
        </Transitions>
    </Setter>
</Style>

<Style Selector="StackPanel.mod-content-rotate.visible">
    <Setter Property="Opacity" Value="1"/>
    <Setter Property="RenderTransform">
        <Setter.Value>
            <TransformGroup>
                <ScaleTransform ScaleX="1" ScaleY="1"/>
                <RotateTransform Angle="0"/>
                <TranslateTransform Y="0"/>
            </TransformGroup>
        </Setter.Value>
    </Setter>
</Style>
```

## Inne animacje w aplikacji

### Panele boczne
- `fade-in-panel` - proste fade in dla InfoPanel, AdditionalActionsPanel, DllModificationsPanel
- `slide-in-right` - wysuwanie od prawej strony
- `slide-in-left` - wysuwanie od lewej strony

Wszystkie style znajdują się w: `SUSModder/Styles/AnimationStyles.axaml`
