# Plan: Reorganizacja frontendu SUSModder

**Data:** 2026-02-23  
**Status:** Plan do zatwierdzenia

## Problem
Przy rosnącej liczbie modów UI stało się mniej wygodne:
- Siatka kart 140x140 jest niepraktyczna przy wielu modach
- Jeden prawy panel pełni 6 różnych ról (szczegóły moda, DLL, ustawienia, narzędzia, SUStats, info)
- Promowanie Discordów jest słabo widoczne (rotacyjny baner przy FAB)
- Brak filtrowania/sortowania modów
- Nowy endpoint `/api/public/discord-server-counts` nie jest zintegrowany

## Decyzje podjęte

| Kwestia | Decyzja |
|---------|---------|
| Widok modów | Przełączany siatka ↔ lista (użytkownik wybiera) |
| Prawy panel | Tylko dla szczegółów moda + DLL. Reszta → osobne okna |
| Discord promo | Kompaktowy pasek/karuzela w status barze z liczbą członków |
| FAB menu | Bez zmian (ale opcje otwierają osobne okna) |
| Filtrowanie | Tak - zainstalowane/dostępne, A-Z, najnowsze + wyszukiwanie |
| Lokalizacja Discordów | W status barze (dodatkowa sekcja) |

---

## FAZA 1: Przełączany widok modów (siatka ↔ lista) + filtrowanie

### 1.1 Toolbar z filtrowaniem/sortowaniem/przełączaniem widoku

**Opis:** Dodać pasek nad listą modów (pod górną krawędzią okna).

**Zawartość toolbara:**
- TextBox szukania (SearchBox z ikoną lupy)
- ComboBox/ToggleButtons filtrowania: Wszystkie | Zainstalowane | Dostępne
- ComboBox sortowania: Domyślne | A-Z | Z-A | Najnowsze
- ToggleButton widoku: Siatka 🔲 | Lista 📋 (z ikonami)

**Pliki do zmiany:**
- `MainWindow.axaml` - dodanie toolbara nad ListBox
- `MainWindowViewModel.cs` - nowe property: `SearchText`, `FilterMode`, `SortMode`, `ViewMode`, `FilteredMods`

### 1.2 Widok listy modów

**Opis:** Nowy DataTemplate dla trybu listy.

**Wygląd wiersza listy:**
```
[Ikona 40x40] | Nazwa moda | Wersja moda | Wersja Among Us | Status (Zainstalowany ✅ / Dostępny ⬇️) |
```

**Pliki do zmiany/dodania:**
- `MainWindow.axaml` - nowy DataTemplate + przełączanie ItemsPanel
- Nowy: `Styles/ModListStyle.axaml` - style dla trybu listy

### 1.3 Logika filtrowania i sortowania

**Opis:** Reaktywne filtrowanie kolekcji modów.

**Implementacja:**
- Nowy partial class `MainWindowViewModel.ModFiltering.cs`
- Property `FilteredMods` (ObservableCollection) - filtrowane/sortowane mody
- DynamicData (już w projekcie) do reaktywnego filtrowania na `Mods` kolekcji
- Enums: `ModFilterMode { All, Installed, Available }`, `ModSortMode { Default, NameAsc, NameDesc, Newest }`, `ModViewMode { Grid, List }`
- ListBox.ItemsSource bindowany do `FilteredMods` zamiast `Mods`

**Pliki do zmiany/dodania:**
- Nowy: `ViewModels/MainWindowViewModel.ModFiltering.cs`
- `MainWindowViewModel.cs` - dodanie pól i inicjalizacji
- `MainWindow.axaml` - zmiana bindingu ItemsSource

---

## FAZA 2: Wydzielenie narzędzi i ustawień do osobnych okien

### 2.1 Narzędzia jako osobne okno (ToolsWindow)

**Opis:** Zebranie rozproszonych narzędzi w jedno okno z zakładkami.

**Struktura ToolsWindow:**
```
┌─────────────────────────────────────┐
│ 🔧 Narzędzia                       │
├─────────────────────────────────────┤
│ [ToU Tools] [SUStats] [Naprawa]    │  ← TabControl
│                                     │
│ (zawartość aktywnej zakładki)       │
│                                     │
└─────────────────────────────────────┘
```

**Zakładki:**
1. **Narzędzia ToU** - przeniesione z `AdditionalActionsPanel` (lobby set, configs)
2. **SUStats** - przeniesione z `SUStatsConfigView`
3. **Naprawa gry** - Fix Black Screen, certyfikaty, regiony, firewall, Epic auth

**Pliki do dodania:**
- Nowy: `Views/ToolsWindow.axaml(.cs)`
- Nowy: `ViewModels/ToolsWindowViewModel.cs`

### 2.2 Ustawienia jako osobne okno

**Opis:** `AppSettingsView` opakowane w osobne okno.

**Uwaga:** `AppSettingsWindow.axaml` już istnieje w projekcie - sprawdzić czy wystarczy go użyć lub rozbudować.

**Pliki do zmiany:**
- `AppSettingsWindow.axaml(.cs)` - sprawdzić/rozbudować
- `MainWindowViewModel.cs` - zmiana `ShowAppSettings()` na otwarcie okna

### 2.3 Uproszczenie prawego panelu

**Opis:** Prawy panel obsługuje TYLKO szczegóły wybranego moda + DLL management.

**Co usunąć z prawego panelu:**
- `InfoPanel` (IsInfoPanelVisible) → osobne małe okno/dialog
- `AdditionalActionsPanel` (IsAdditionalActionsVisible) → ToolsWindow
- `SUStatsConfigView` (IsSUStatsConfigVisible) → ToolsWindow
- `AppSettingsView` (IsAppSettingsVisible) → AppSettingsWindow

**Co pozostaje w prawym panelu:**
- Szczegóły wybranego moda (header, opis, przyciski install/launch/update/uninstall)
- DLL management (IsDllModificationsVisible, IsDllInstallDialogVisible)

**Pliki do zmiany:**
- `MainWindow.axaml` - usunięcie sekcji z prawego panelu
- `MainWindowViewModel.cs` - czyszczenie property i visibility logic
- Border.IsVisible MultiBinding - uproszczenie

### 2.4 Aktualizacja FAB menu

**Opis:** Opcje FAB otwierają osobne okna zamiast paneli w prawym panelu.

**Mapowanie zmian:**
| FAB opcja | Przed | Po |
|-----------|-------|-----|
| Narzędzia ToU | Panel w prawym panelu | ToolsWindow |
| DLL Mods | Panel w prawym panelu | Bez zmian (zostaje w prawym panelu) |
| SUStats | Panel w prawym panelu | ToolsWindow (zakładka) |
| Naprawa gry | Dialog | ToolsWindow (zakładka) |
| Sprawdź aktualizacje | Dialog | Bez zmian |
| Ustawienia | Panel w prawym panelu | AppSettingsWindow |
| Polecane Discordy | Osobne okno | Bez zmian |
| Info | Panel w prawym panelu | Osobne małe okno |
| Zmień motyw | Inline | Bez zmian |

**Pliki do zmiany:**
- `MainWindowViewModel.ExternalActions.cs` - nowe metody otwierania okien
- `MainWindowViewModel.cs` - zmiany w komendach

---

## FAZA 3: Integracja Discord w status barze

### 3.1 Serwis do pobierania liczby członków Discord

**Opis:** Nowy serwis w SUSModder.Core do endpointu `/api/public/discord-server-counts`.

**API Response:**
```json
{
  "counts": {
    "123456789": 420,
    "987654321": 150
  }
}
```

**Implementacja:**
- Serwis `DiscordServerCountService` z metodą `GetServerCountsAsync()`
- Model `DiscordServerCountsResponse` 
- Periodyczne odświeżanie co 60 sekund
- Cache wyników

**Pliki do dodania:**
- Nowy: `SUSModder.Core/Services/DiscordServerCountService.cs`
- Nowy: `SUSModder.Core/Models/DiscordServerCountsResponse.cs`

**Pytanie implementacyjne:** Endpoint zwraca `serverId` jako klucz. Czy `DiscordServer` model ma pole ServerId? Jeśli nie, trzeba dodać mapowanie (np. po nazwie serwera lub dodać ServerId do DiscordServerData z API).

### 3.2 Rozszerzenie DiscordServerViewModel o liczbę członków

**Pliki do zmiany:**
- `DiscordServerViewModel.cs` - dodanie property `MemberCount` (int?), `MemberCountText` (string)

### 3.3 Sekcja Discord w status barze

**Obecny status bar:**
```
┌──────────────┬──────────────┬────────┐
│ 📦 Mody      │ 💾 Dysk      │ 🟢 API │
│ (statystyki) │ (przestrzeń) │(status)│
└──────────────┴──────────────┴────────┘
```

**Nowy status bar:**
```
┌──────────────┬──────────────┬────────┬──────────────────┐
│ 📦 Mody      │ 💾 Dysk      │ 🟢 API │ 💬 Discord       │
│ (statystyki) │ (przestrzeń) │(status)│ [karuzela]       │
└──────────────┴──────────────┴────────┴──────────────────┘
```

**Zawartość sekcji Discord (karuzela):**
```
[Ikona 24x24] Nazwa serwera  👥 420  [Dołącz →]
```
- Rotacja co 8-10 sekund z animacją fade
- Klik na "Dołącz" otwiera link w przeglądarce

**Pliki do zmiany:**
- `MainWindow.axaml` - rozszerzenie status bar Grid (4 kolumny)
- `MainWindowViewModel.DiscordPromo.cs` - rozszerzenie logiki rotacji
- `MainWindowViewModel.StatusBar.cs` - integracja z Discord count service

### 3.4 Usunięcie starego Discord promo przy FAB

**Co usunąć:**
- `FabDiscordPromoPanel` z `MainWindow.axaml` (linie ~958-1017)
- Property `IsFloatingPromoSpaceAvailable` z `MainWindowViewModel.DiscordPromo.cs`
- Style `fab-discord-promo-panel`, `fab-discord-promo-join`, `fab-discord-promo-content` z `FabButtonStyle.axaml`

**Co zachować:**
- `RecommendedDiscordsWindow` (otwierane z FAB menu)
- Logika ładowania serwerów Discord (preloader, fetcher)
- `CurrentPromotedDiscord`, `HasPromotedDiscord` (używane przez status bar)

**Pliki do zmiany:**
- `MainWindow.axaml` - usunięcie FabDiscordPromoPanel
- `MainWindowViewModel.DiscordPromo.cs` - czyszczenie
- `Styles/FabButtonStyle.axaml` - usunięcie stylów promo

---

## FAZA 4: Polerowanie i spójność

### 4.1 Aktualizacja RecommendedDiscordsWindow

**Opis:** Dodać wyświetlanie liczby członków przy każdym serwerze.

**Nowy wygląd karty serwera:**
```
[Ikona 64x64] | Nazwa serwera                    | [Dołącz]
              | Opis serwera...                   |
              | 👥 420 członków | discord.gg/xxx   |
```

**Pliki do zmiany:**
- `Views/RecommendedDiscordsWindow.axaml` - dodanie TextBlock z liczbą członków
- `ViewModels/RecommendedDiscordsViewModel.cs` - integracja z DiscordServerCountService

### 4.2 Zapisywanie preferencji UI

**Nowe pola w user-settings.json:**
```json
{
  "viewMode": "grid",       // "grid" | "list"
  "sortMode": "default",    // "default" | "nameAsc" | "nameDesc" | "newest"
  "filterMode": "all"       // "all" | "installed" | "available"
}
```

**Pliki do zmiany:**
- Model user-settings (jeśli jest typowany) lub `UserSettingsService`
- `MainWindowViewModel.ModFiltering.cs` - wczytywanie i zapisywanie

### 4.3 Lokalizacja nowych kluczy

**Nowe klucze (przykłady):**

```json
// pl.json
{
  "UI.ModList.SearchPlaceholder": "Szukaj modów...",
  "UI.ModList.FilterAll": "Wszystkie",
  "UI.ModList.FilterInstalled": "Zainstalowane",
  "UI.ModList.FilterAvailable": "Dostępne",
  "UI.ModList.SortDefault": "Domyślne",
  "UI.ModList.SortNameAsc": "A-Z",
  "UI.ModList.SortNameDesc": "Z-A",
  "UI.ModList.SortNewest": "Najnowsze",
  "UI.ModList.ViewGrid": "Siatka",
  "UI.ModList.ViewList": "Lista",
  "UI.Discord.Members": "członków",
  "UI.Discord.Join": "Dołącz",
  "UI.Tools.WindowTitle": "Narzędzia",
  "UI.Tools.TabToU": "Town of Us",
  "UI.Tools.TabSUStats": "SUStats",
  "UI.Tools.TabRepair": "Naprawa gry"
}
```

**Pliki do zmiany:**
- `Localization/pl.json`
- `Localization/en.json`

### 4.4 Testy i weryfikacja

**Checklist:**
- [ ] Motyw Dark - wszystko widoczne, kolory spójne
- [ ] Motyw Light - kontrast OK
- [ ] Motyw Pink - serduszka + nowe elementy
- [ ] Resize okna (minimalne 890x820) - toolbar nie ucina
- [ ] Pusta lista modów - filtr/widok nie crashuje
- [ ] Duża lista modów (20+) - widok listy czytelny, filtrowanie responsywne
- [ ] Status bar z Discord - karuzela działa, nie ucina tekstu
- [ ] ToolsWindow - zakładki działają, ToU configs OK
- [ ] AppSettingsWindow - otwiera się z FAB, zapisuje
- [ ] DLL management w prawym panelu - nadal działa
- [ ] FAB menu - nowe mapowanie opcji do okien

---

## Podsumowanie plików

### Nowe pliki (~8-10):
| Plik | Opis |
|------|------|
| `Styles/ModListStyle.axaml` | Style dla trybu listy |
| `Views/ToolsWindow.axaml(.cs)` | Okno narzędzi z zakładkami |
| `ViewModels/ToolsWindowViewModel.cs` | ViewModel narzędzi |
| `ViewModels/MainWindowViewModel.ModFiltering.cs` | Filtrowanie/sortowanie modów |
| `SUSModder.Core/Services/DiscordServerCountService.cs` | Serwis liczby członków |
| `SUSModder.Core/Models/DiscordServerCountsResponse.cs` | Model odpowiedzi API |

### Modyfikowane pliki (~12-15):
| Plik | Zakres zmian |
|------|-------------|
| `Views/MainWindow.axaml` | Toolbar, widok listy, status bar Discord, usunięcie paneli |
| `ViewModels/MainWindowViewModel.cs` | Nowe properties, czyszczenie starych |
| `ViewModels/MainWindowViewModel.DiscordPromo.cs` | Integracja z counts, usunięcie FAB promo |
| `ViewModels/MainWindowViewModel.ExternalActions.cs` | Otwieranie osobnych okien |
| `ViewModels/MainWindowViewModel.StatusBar.cs` | Sekcja Discord w status barze |
| `ViewModels/DiscordServerViewModel.cs` | MemberCount property |
| `Views/RecommendedDiscordsWindow.axaml` | Liczba członków |
| `ViewModels/RecommendedDiscordsViewModel.cs` | Integracja z count service |
| `Styles/FabButtonStyle.axaml` | Usunięcie stylów promo |
| `Localization/pl.json` | Nowe klucze PL |
| `Localization/en.json` | Nowe klucze EN |
| `UserSettingsService` / model | Preferencje widoku |

---

## Ryzyka i uwagi

1. **MainWindow.axaml jest duży (42KB)** - zmiany muszą być chirurgiczne, po kawałku
2. **DynamicData** - jest w projekcie, ale trzeba sprawdzić czy jest aktywnie używany do filtrowania
3. **3 motywy** - każda nowa sekcja musi działać z Dark, Light i Pink
4. **Status bar ma ograniczoną przestrzeń** - karuzela Discord musi być bardzo kompaktowa
5. **DiscordServerCountService** - endpoint zwraca serverId jako klucz, trzeba zmapować na istniejące serwery (brak ServerId w modelu DiscordServer - może trzeba dodać)
6. **Kompatybilność wsteczna** - user-settings.json musi gracefully handlować brak nowych pól

## Kolejność implementacji (rekomendowana)

1. **Faza 1** (filtrowanie + widoki) - największy impact na UX
2. **Faza 3** (Discord w status barze) - nowa funkcjonalność
3. **Faza 2** (wydzielenie okien) - refactoring
4. **Faza 4** (polerowanie) - na koniec
