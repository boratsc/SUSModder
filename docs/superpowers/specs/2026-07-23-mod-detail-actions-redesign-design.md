# Redesign akcji w panelu moda (ModDetailDrawer)

Data: 2026-07-23  
Status: approved (brainstorm)

## Problem

W [`ModDetailDrawer.axaml`](SUSModder/Views/ModDetailDrawer.axaml) główne CTA (Instaluj/Uruchom + Lobby Board) są sticky, a ważne akcje (m.in. wybór wersji) są schowane pod zwijanym „Dodatkowe akcje”. Lista kompatybilnych DLL zajmuje dużo miejsca mimo że instalacja DLL jest też dostępna osobno.

## Cele

- Wybór wersji na pierwszym planie (bez szukania w expanderze).
- Usunięcie expandera „Dodatkowe akcje” — akcje zawsze widoczne.
- Kompatybilne DLL domyślnie zwinięte.
- Różny układ dla stanu niezainstalowanego i zainstalowanego.

## Decyzja UI (zatwierdzona)

### Niezainstalowany (`!InstallPath`, `!IsVanilla`)

1. **Sticky CTA:** Avalonia `SplitButton`
   - Główny klik → `InstallCommand` (najnowsza / domyślna wersja)
   - Strzałka → flyout z listą wersji → `InstallWithVersionSelectionCommand` / istniejący flow wyboru wersji (`VersionSelectionPanel` lub bezpośrednie pozycje jeśli już dostępne w VM)
2. **Siatka akcji:** `UniformGrid` **3 kolumny** (kafelki ikona + krótka etykieta, FluentIcons)
   - Lobby Board
   - Utwórz zestaw (`IsFullMod`)
   - Udostępnij zestaw (`IsFullMod`)
3. Brak osobnego przycisku „Wybierz wersję” w siatce (jest w SplitButton).

### Zainstalowany (`InstallPath`, `!IsVanilla`)

1. **Sticky CTA:** pełnoszerokościowy przycisk **Uruchom** (`LaunchCommand`)
2. **Toolbar:** `UniformGrid` **4 kolumny**, kompaktowe kafelki ikona + etykieta
   - Dodaj DLL
   - Role (enabled gdy `HasRoles`)
   - Lobby
   - Folder
   - Skrót
   - Zestaw (utwórz lokalny — zostaje widoczny także po instalacji)
   - Usuń (styl danger)

### Vanilla

Bez zmian względem obecnej logiki: tylko Uruchom; brak siatki / toolbara / kompatybilnych DLL / lobby.

### Kompatybilne DLL

- Sekcja zostaje, ale **domyślnie zwinięta** (tylko nagłówek + liczba, np. „▶ 7”).
- Rozwinięcie pokazuje obecną listę (preview / „pokaż więcej” bez zmian zachowania).
- Domyślny stan zwinięcia przy zmianie moda (jak dziś dla „Dodatkowe akcje”).

### Usunięte

- Expander / sekcja **„Dodatkowe akcje”** (`IsInspectorMoreActionsExpanded`, `ToggleInspectorMoreActionsCommand` — do usunięcia z UI; martwy kod VM można posprzątać w tym samym PR).
- Osobny pełnoszerokościowy sticky przycisk Lobby Board (Lobby jest w siatce / toolbarze).
- Osobne pełnoszerokościowe przyciski Folder / Skrót poniżej (wchodzą do toolbara zainstalowanego).

### Zachowane poniżej (scroll)

- Chat lobby (expander) — bez zmian koncepcyjnych.
- Auto-update toggle — bez zmian warunków widoczności.
- Progress instalacji — nadal w sticky CTA area.

## Implementacja (granice)

| Obszar | Pliki |
|--------|--------|
| Layout | [`SUSModder/Views/ModDetailDrawer.axaml`](SUSModder/Views/ModDetailDrawer.axaml) (+ code-behind jeśli flyout SplitButton wymaga) |
| Style kafelków / toolbara | [`SUSModder/Styles/ModDetailPanelStyle.axaml`](SUSModder/Styles/ModDetailPanelStyle.axaml) lub inline + wspólne klasy `mod-detail-action-tile` |
| Ikony | `FluentIcons.Avalonia` (`SymbolIcon`) — już w projekcie |
| VM widoczności / expand DLL | `MainWindowViewModel` partials: InspectorLayout / CatalogInspector / InspectorCompat — domyślne zwinięcie kompatybilnych DLL; usunięcie stanu MoreActions |
| Lokalizacja | klucze PL/EN jeśli skrócone etykiety toolbara (Lobby, DLL, Zestaw…) wymagają nowych stringów |

## Zachowanie interakcji

- SplitButton flyout: otwiera istniejący wybór wersji (preferencja: reuse `InstallWithVersionSelectionCommand` / `VersionSelectionPanel`, bez nowego API backendu).
- Kafelki wywołują te same komendy co obecne przyciski.
- `IsInstalling` wyłącza sticky CTA i kafelki jak dziś.
- Przy zmianie `SelectedMod`: reset expandera kompatybilnych DLL do zwiniętego.

## Poza zakresem

- Zmiany w `DllAddonInspector` / zakładce DLL.
- Redesign karty listy (`ModCard`).
- Zmiana logiki instalacji / packów / lobby.
- Nowy modal tylko dla listy DLL (wystarczy zwinięcie).

## Kryteria akceptacji

1. Niezainstalowany full-mod: SplitButton Instaluj+wersje widoczny bez expandera; 3 kafelki w jednym rzędzie gdy wszystkie widoczne.
2. Zainstalowany: Uruchom + toolbar 4-col z DLL/Role/Lobby/Folder/Skrót/Zestaw/Usuń.
3. „Dodatkowe akcje” nie występuje w UI.
4. Kompatybilne DLL startują zwinięte; po rozwinięciu działają jak wcześniej.
5. Vanilla: tylko Uruchom.
6. Motywy (dark/pink/glass) nie łamią czytelności kafelków.
