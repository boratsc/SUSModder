# Plan: FAB – lepsza komunikacja i discoverability

**Data:** 2026-05-25
**Źródło:** DOC/2026-05-25 - frontend-ideas/02-fab-communication.md
**Priorytet:** 🟡 P1
**Effort:** ~1.5-2h

## Decyzje

| Decyzja | Wybór |
|---|---|
| FAB badge | **Licznik** (nie kropka) |
| Kontekstowa ikona | **Pełny zestaw** (Navigation / ArrowDownload / ArrowSync) |
| Long-press = ostatnia akcja | ❌ Nie warto – nikt nie użyje na desktopie |
| Discord promo | ✅ Zostawić w obecnej formie (status bar) |

## Codebase verification

| Co sprawdzono | Wynik |
|---|---|
| AvailableUpdatesCount (ReactiveProperty) | ✅ Istnieje w MainWindowViewModel.StatusBar.cs:176 |
| IsPaneOpen (ReactiveProperty) | ✅ Istnieje w MainWindowViewModel.cs:304 |
| TogglePaneCommand | ✅ Istnieje w MainWindowViewModel.cs:417 |
| FAB button (XAML) | ✅ MainWindow.axaml:430, klasa ab, ikona Symbol.Navigation |
| FAB menu (XAML) | ✅ MainWindow.axaml:856-931, 7 pozycji + separator + theme |
| "Sprawdź aktualizacje" w menu FAB | ❌ **Brakuje** |
| Badge/licznik na FAB | ❌ **Brakuje** |
| Kontekstowa ikona FAB | ❌ **Brakuje** – zawsze Symbol.Navigation |
| Klucze i18n UI.Menu.CheckAppUpdates | ✅ Istnieją, nieużywane |
| FluentIcons.Avalonia | ✅ v2.1.328 |

## Goal

FAB ma lepiej komunikować stan aplikacji – przede wszystkim dostępność aktualizacji modów.

## Non-goals

- Nie zmieniamy ogólnego układu status bara poza FAB
- Nie implementujemy long-press/tap
- Nie przenosimy Discord promo spod FAB
- Nie dotykamy logiki sprawdzania aktualizacji – tylko podłączamy istniejące dane

## i18n – nowe klucze

| Klucz | PL | EN |
|---|---|---|
| UI.Menu.CheckModUpdates | Sprawdź aktualizacje modów | Check mod updates |
| UI.Fab.UpdatesBadgeTooltip | {0} dostępnych aktualizacji | {0} available updates |

## Architektura

Wszystkie zmiany w warstwie UI (SUSModder). Core bez zmian.

`
SUSModder/
├── Views/MainWindow.axaml            ← badge + binding ikony + menu item
├── Styles/FabButtonStyle.axaml       ← styl badge + styl menu item badge
├── ViewModels/MainWindowViewModel.cs ← FabIconSymbol, IsAnyModInstalling, CheckForModUpdatesFromMenuCommand
├── ViewModels/MainWindowViewModel.StatusBar.cs  ← sync RaisePropertyChanged
├── Localization/pl.json              ← nowe klucze
└── Localization/en.json              ← nowe klucze
`

## Zadania

### Z1: FAB badge z licznikiem
- Nowe property: FabBadgeCount, FabHasBadge, FabBadgeTooltip
- Styl Border.fab-badge w FabButtonStyle.axaml (czerwony, position absolute right-top)
- Binding w MainWindow.axaml

### Z2: Kontekstowa ikona FAB
- FabIconSymbol property (getter zwraca Navigation/ArrowDownload/ArrowSync)
- IsAnyModInstalling property ustawiane przy starcie/końcu instalacji
- Bez animacji rotacji w MVP

### Z3: "Sprawdź aktualizacje modów" w menu FAB
- Nowy przycisk w menu FAB z badge
- CheckForModUpdatesFromMenuCommand → zamyka menu → CheckForModUpdatesAsync

### Z4: Synchronizacja
- W setterze AvailableUpdatesCount: RaisePropertyChanged dla FabHasBadge, FabBadgeCount, FabBadgeTooltip, FabIconSymbol

### Z5: Discord promo – bez zmian w kodzie

## Kolejność implementacji

`
Z4 (sync) → Z1 (badge) → Z2 (ikona) → Z3 (menu item) → Z5 (decyzja)
`

## Weryfikacja końcowa

| Test | Oczekiwane |
|---|---|
| 0 update'ów | FAB: ➕, bez badge |
| 3 update'y | FAB: ⬇️, badge "3" |
| Menu FAB | "Sprawdź aktualizacje modów" ma badge "3" |
| Instalacja w toku | FAB: ⏳ |
| Koniec instalacji, 0 update'ów | FAB: ➕, badge znika |
| Tooltip badge | "3 dostępne aktualizacje" / "3 available updates" |
| Zmiana języka na EN | Wszystkie stringi po angielsku |
