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
| Skeleton | Nowy: `Controls/SkeletonCard.axaml` |
| Tooltipy | `MainWindow.axaml` → DataTemplate kart modów |
