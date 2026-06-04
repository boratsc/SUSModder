# 20 – Spójny Browser + Inspector dla Katalogu, Zestawów i DLL

**Priorytet:** 🔴 P0/P1 dla SUSModder 3.0  
**Effort:** ~6-10 dni (UI + VM + i18n + testy manualne) — **~70% UI w kodzie** (2026-06-04)  
**Status:** 🟡 **WDROŻONE W KODZIE (MVP+)** — Browser + Inspector na trzech zakładkach; brak screenshotów README i formalnego smoke DPI  
**Ostatnia aktualizacja postępu:** 2026-06-04  
**Powiązane:** [`19-local-modpack-instances.md`](19-local-modpack-instances.md), [`18-beanmodmanager-ideas.md`](18-beanmodmanager-ideas.md), [`06-microinteractions-polish.md`](06-microinteractions-polish.md), [`../POC/2026-06-01-ui-refresh-v3-poc.md`](../POC/2026-06-01-ui-refresh-v3-poc.md)

---

## Stan implementacji (2026-06-04)

### Zrobione ✅

| Obszar | Co wdrożono |
|--------|-------------|
| **Layout okna** | Domyślnie 1400×820, min. 1180×720; inspektor 440 px (max 460) |
| **Zakładki** | `BrowserToolbar` — Katalog (default) / Moje zestawy / Dodatki DLL |
| **Wyszukiwarka** | `BrowserSearchText` + filtr na trzech zakładkach + `UI.Browser.NoResults` |
| **Karty (BrowserCard)** | Poziome 340×160: `ModCard`, `PackInstanceCard`, `DllAddonCard`; `BrowserCardStyle.axaml` |
| **Katalog — Inspector** | Sekcje: opis, kompatybilne DLL (API), szybkie akcje, narzędzia, danger zone; sticky CTA Instaluj/Uruchom |
| **Zestawy — Inspector** | `PackInstanceDetailDrawer` — sekcje: status/aktualizacja, zawartość, konfiguracja, udostępnianie, danger zone; sticky Uruchom |
| **DLL — Browser** | Siatka w głównym obszarze (nie modal jako pierwszy krok) |
| **DLL — Inspector** | `DllAddonInspector` — kompatybilność (macierz API), zainstalowany w, checkboxy targetów, Zastosuj zmiany |
| **Bulk** | Chip + bar na wszystkich zakładkach; akcje DLL: „Dodaj do…” → inspektor pierwszego zaznaczonego |
| **Nawigacja** | Klik wiersza kompat. DLL w katalogu → zakładka DLL + inspektor; Esc / klik tła zamyka inspektor |
| **i18n PL/EN** | `UI.Tabs.*`, `UI.Browser.*`, `UI.Inspector.*`, `UI.Actions.*`, `UI.Bulk.SelectedDlls`, rozszerzone `UI.DllManager.*` |
| **Fallback modal** | `DllManagementPanel` + `DllModSelectionView` w overlay FAB (stary flow) |
| **Build** | `SUSModder.sln` — kompilacja OK |

### Częściowo / do dopracowania 🟡

| Obszar | Stan |
|--------|------|
| **`Controls/BrowserCard.axaml`** | Wzorzec wizualny w stylach + 3 karty; brak jednego wspólnego user control `BrowserCard` |
| **`StatusBadge` / `CardActionRow`** | Statusy na kartach (ellipse, badge typu); brak osobnych kontrolek z doc |
| **Kompatybilność DLL w inspektorze** | Pełna lista full modów z katalogu; bez grupowania „tylko zainstalowane zestawy” |
| **Focus / klawiatura** | Esc + podstawowe kliknięcia; brak audytu Tab order i focus ring |
| **DPI 125/150%** | Niezweryfikowane formalnie |
| **Screenshoty** | Zaktualizowany [`../readme/SCREENSHOTS.md`](../readme/SCREENSHOTS.md); pliki PNG **nie dodane** |

### Nie zrobione (zgodnie z non-goals lub później) ❌

- Osobny control `CatalogModInspector.axaml` / `PackInstanceInspector.axaml` (zamiast tego rozszerzone drawery)
- `BrowserItemViewModel.cs` — logika w partialach `MainWindowViewModel`
- Ustawienia `user_settings`: `browser_density`, `inspector_width`, `last_catalog_filter`
- Telemetria UX (`tab_opened`, `inspector_opened`, …)

### Pliki (główne)

```
SUSModder/Styles/BrowserCardStyle.axaml
SUSModder/Styles/BrowserTabStyle.axaml
SUSModder/Controls/DllAddonCard.axaml(.cs)
SUSModder/Controls/ModCard.axaml          — poziomy layout
SUSModder/Controls/PackInstanceCard.axaml
SUSModder/Views/BrowserToolbar.axaml(.cs)
SUSModder/Views/DllAddonInspector.axaml(.cs)
SUSModder/Views/PackInstanceDetailDrawer.axaml
SUSModder/Views/ModDetailDrawer.axaml     — sekcje + sticky CTA
SUSModder/Views/MainWindow.axaml
SUSModder/ViewModels/MainWindowViewModel.ModInstances.cs
SUSModder/ViewModels/MainWindowViewModel.DllManagement.cs
SUSModder/ViewModels/MainWindowViewModel.BrowserFilter.cs
SUSModder/ViewModels/MainWindowViewModel.CatalogInspector.cs
SUSModder/ViewModels/DllInstallTargetItem.cs
SUSModder/ViewModels/DllCompatibilityLineItem.cs
SUSModder/Localization/pl.json, en.json
DOC/readme/SCREENSHOTS.md
```

### Kryteria akceptacji — checklist

| # | Kryterium | Status |
|---|-----------|--------|
| 1 | Domyślna zakładka: Katalog modów | ✅ |
| 2 | Spójne karty + selected/bulk na trzech zakładkach | ✅ |
| 3 | DLL w Browserze, nie od modala | ✅ |
| 4 | Inspector DLL: opis, kompatybilność, targety | ✅ |
| 5 | Inspector zestawu/katalogu spójny wizualnie | ✅ (sekcje) |
| 6 | Bulk z akcjami per zakładka | ✅ |
| 7 | PL/EN kompletne (klucze MVP) | ✅ |
| 8 | Bez ciężkich zależności / AV | ✅ |
| 9 | Screenshoty README | ❌ (tylko instrukcja) |
| 10 | Smoke DPI | ❌ |

---

## Cel

Zaprojektować spójny, nowoczesny i ergonomiczny wygląd głównego obszaru SUSModder 3.0 dla trzech zakładek:

1. **Katalog modów** — domyślny ekran aplikacji.
2. **Moje zestawy** — lokalne instancje modpacków.
3. **Dodatki DLL** — grid kart DLL zamiast niespójnego flow otwierającego modal.

Najważniejsza decyzja: **Katalog modów pozostaje defaultem**, bo większość użytkowników nadal będzie korzystać z prostego katalogowego flow. Modpacki i zestawy są rozszerzeniem, nie wymuszoną zmianą mentalnego modelu aplikacji.

---

## Non-goals

- ❌ Nie robimy „Moje zestawy” jako domyślnego ekranu.
- ❌ Nie mieszamy full modów, zestawów i DLL w jednej chaotycznej siatce.
- ❌ Nie usuwamy modalów całkowicie — zostają do potwierdzeń, ryzyka i destrukcyjnych operacji.
- ❌ Nie wprowadzamy ciężkich zależności UI ani WebView.
- ❌ Nie zmieniamy w tym POC modelu Core modpacków — #19 opisuje warstwę lokalnych instancji.
- ❌ Nie robimy runtime downloadów motywów ani zdalnych assetów.

---

## Problem obecnego UI

Obecny kierunek jest funkcjonalny, ale niespójny:

| Obszar | Obecne zachowanie | Problem |
|--------|-------------------|---------|
| Katalog modów | Siatka kart + prawy panel | Akceptowalne, ale karta jest mało informacyjna |
| Moje zestawy | Karty wyrównane do `ModCard` | Spójne technicznie, ale zestaw ma więcej danych niż mieści obecny kafelek |
| DLL | Panel/lista + klik otwiera modal wyboru targetów | Użytkownik wypada z głównego układu, niespójne z resztą aplikacji |
| Szczegóły | Osobne panele z listą przycisków | Brak wspólnej hierarchii sekcji i CTA |
| Zaznaczanie | Selected vs bulk wymaga ostrożności | Trzeba utrzymać jeden system kolorów i trybów |

Wniosek: potrzebny jest wspólny wzorzec **Browser + Inspector**, ale z zachowaniem odrębnych zakładek i katalogu jako defaultu.

---

## Docelowy układ

```text
┌──────────────────────────────────────────────────────────────────────┐
│ SUSModder       Steam/Epic       [Szukaj...]       [Zaznacz] [⚙]     │
├──────────────────────────────────────────────────────────────────────┤
│ [Katalog modów] [Moje zestawy] [Dodatki DLL]                         │
├────────────────────────────────────────┬─────────────────────────────┤
│ BROWSER                                │ INSPECTOR                   │
│ spójne karty dla aktywnej zakładki     │ szczegóły wybranego itemu   │
│ filtry / sortowanie / empty state      │ akcje / status / sekcje     │
├────────────────────────────────────────┴─────────────────────────────┤
│ Bulk bar — tylko w trybie zaznaczania                                 │
├──────────────────────────────────────────────────────────────────────┤
│ Status bar                                                            │
└──────────────────────────────────────────────────────────────────────┘
```

### Rozmiar okna

- Domyślnie: około **1400 × 820**.
- Minimum: około **1180 × 720**.
- Inspector: **430-460 px**.
- Na Full HD okno nie zajmuje całego ekranu, ale daje miejsce na poziome karty.

---

## Zakładki

### 1. Katalog modów — default

To jest ekran startowy.

Cel użytkownika: „chcę szybko zainstalować moda albo stworzyć zestaw”.

Primary action na karcie/panelu:

- `Instaluj` dla klasycznego katalogowego flow.

Secondary actions:

- `Utwórz zestaw`;
- `Wybierz wersję`;
- `Role` / `Lobby` dla modów, które to obsługują.

### 2. Moje zestawy

Cel użytkownika: „chcę uruchomić, zaktualizować, sklonować albo udostępnić konkretny lokalny zestaw”.

Primary action:

- `Uruchom`.

Secondary actions:

- `Aktualizuj`;
- `Zmień nazwę`;
- `Klonuj`;
- `Wczytaj/Zapisz config ToU`;
- `Udostępnij`;
- `Usuń zestaw`.

### 3. Dodatki DLL

Cel użytkownika: „chcę zobaczyć dodatki DLL i dodać je do konkretnego moda lub zestawu”.

Primary action:

- `Dodaj do...` albo `Zastosuj zmiany` w Inspectorze.

Flow docelowy:

1. Użytkownik wchodzi w `Dodatki DLL`.
2. Widzi grid kart DLL.
3. Klik DLL pokazuje Inspector.
4. Inspector pokazuje kompatybilność i targety.
5. Użytkownik zaznacza docelowe klasyczne instalacje lub lokalne zestawy.
6. Klik `Zastosuj zmiany`.

Modal `DllModSelectionView` nie powinien być głównym doświadczeniem. Może zostać jako fallback lub etap przejściowy, ale docelowo target selection ma być inline.

---

## Spójna karta: `BrowserCard`

Wizualnie każda zakładka używa tego samego języka:

```text
┌────────────────────────────────────────────┐
│ [ikona]  Tytuł                     [badge] │
│         Podtytuł                           │
│         Meta / status / opis skrócony      │
│                                            │
│         [primary action]            [⋯]    │
└────────────────────────────────────────────┘
```

### Parametry

- Rozmiar: **320-360 × 150-170 px**.
- Układ: poziomy, nie kwadratowy.
- Ikona: 48-56 px.
- Corner radius: 12-14 px.
- Status badge w prawym górnym rogu.
- Progress strip 3 px na dole przy instalacji/aktualizacji.
- Hover: delikatny lift + jaśniejszy border, bez agresywnego scale.
- Selected/preview: niebieski border/outline.
- Bulk checked: fioletowy border/checkbox.

### Karta katalogowego moda

```text
┌────────────────────────────────────────────┐
│ [ikona]  Town of Us                 FULL   │
│         v5.4.0 · Among Us 2024.6           │
│         Popularny mod full                 │
│                                            │
│         [Instaluj]        [Utwórz zestaw]  │
└────────────────────────────────────────────┘
```

### Karta lokalnego zestawu

```text
┌────────────────────────────────────────────┐
│ [ikona]  ToU - Psychopaci piątek   UPDATE  │
│         Town of Us · v5.4.0 · Steam        │
│         3 DLL · config ToU                 │
│                                            │
│         [Uruchom]                  [⋯]     │
└────────────────────────────────────────────┘
```

### Karta DLL

```text
┌────────────────────────────────────────────┐
│ [ikona]  AleLuduMod                  DLL   │
│         v2.0 · dodatek                      │
│         Zainstalowany w 2 zestawach         │
│                                            │
│         [Dodaj do...]               [⋯]    │
└────────────────────────────────────────────┘
```

---

## Inspector — jeden wzorzec sekcji

Każdy Inspector ma tę samą strukturę:

```text
Header
Primary action
Status / warnings
Details
Related items / contents
Secondary actions
Danger zone
```

### Inspector katalogowego moda

```text
Town of Us
Full mod · v5.4.0 · Among Us 2024.6

[Instaluj]

Szybkie akcje:
[Utwórz zestaw] [Wybierz wersję]

Opis:
...

Kompatybilne DLL:
- AleLuduMod
- ExtraRoles

Narzędzia:
[Role] [Lobby]
```

### Inspector lokalnego zestawu

```text
ToU - Psychopaci piątek
Town of Us · v5.4.0 · Steam

[Uruchom]

Status:
Aktualizacja dostępna
[Aktualizuj]

Zawartość:
- AleLuduMod
- ExtraRoles
- config ToU

Konfiguracja:
[Wczytaj config] [Zapisz config]
[Zmień nazwę] [Klonuj]

Udostępnianie:
[Utwórz kod modpacka]

Niebezpieczne:
[Usuń zestaw]
```

### Inspector DLL

```text
AleLuduMod
DLL · v2.0

[Dodaj do...]

Kompatybilność:
✅ Town of Us
❓ The Other Roles
⚠️ Nie testowano z X

Zainstalowany w:
- ToU - Psychopaci piątek
- ToU - test beta

Dodaj / usuń z:
[ ] Town of Us klasycznie
[ ] ToU - znajomi
[ ] ToU - beta

[Zastosuj zmiany]
```

---

## Zaznaczanie i bulk

Spójnie na wszystkich zakładkach:

| Stan | Kolor / zachowanie |
|------|--------------------|
| Preview/selected | niebieski outline |
| Bulk checked | fioletowy checkbox + fioletowy outline |
| Update | żółty badge |
| Installed/ready | zielony badge |
| Busy | niebieski progress strip |
| Error/danger | czerwony badge/action |

### Tryb normalny

- Klik karty = pokazuje item w Inspectorze.
- Dwuklik:
  - katalog: start instalacji lub wybór flow instalacji;
  - zestaw: uruchom;
  - DLL: pokaż Inspector z targetami.
- `Esc` zamyka Inspector, jeśli nie ma otwartego modala.
- Klik tła nie powinien przypadkowo resetować kontekstu.

### Tryb zaznaczania

- Aktywowany przyciskiem `Zaznacz`.
- Checkboxy pojawiają się na kartach.
- Klik karty toggle zaznaczenia, nie skacze Inspectorem.
- Bulk bar pokazuje tylko akcje sensowne dla aktywnej zakładki.

```text
Katalog:      Zaznaczono 3 mody       [Zainstaluj] [Utwórz zestawy] [Anuluj]
Moje zestawy: Zaznaczono 3 zestawy    [Aktualizuj] [Usuń] [Anuluj]
DLL:          Zaznaczono 3 DLL        [Dodaj do zestawu] [Anuluj]
```

---

## Core business logic responsibilities

Core nie odpowiada za wygląd, ale musi dostarczyć stabilne dane dla kart i Inspectorów:

- katalog modów: `ModConfiguration` / `IModRepository`;
- lokalne zestawy: `ModInstance`, `ModInstanceDll`, `ModInstanceConfig` / `IModInstanceRepository`;
- DLL: lista DLL, informacja gdzie są zainstalowane, kompatybilność, update status;
- operacje: install/update/delete/launch dla konkretnego kontekstu;
- błędy jako stabilne `errorCode` + techniczny fallback.

Nie należy rozszerzać Core o user-facing copy.

---

## UI / Avalonia responsibilities

UI odpowiada za:

- wspólny styl `BrowserCard`;
- trzy warianty zawartości kart: catalog / pack / dll;
- wspólny layout Inspectorów;
- zachowanie zakładek i domyślną aktywną zakładkę `Katalog modów`;
- inline target selection dla DLL;
- Browse vs Select mode;
- dostępność klawiatury i focus states;
- animacje 150-250 ms, bez rozpraszających efektów.

Docelowo warto wydzielić:

```text
Controls/BrowserCard.axaml
Controls/StatusBadge.axaml
Views/CatalogModInspector.axaml
Views/PackInstanceInspector.axaml
Views/DllAddonInspector.axaml
ViewModels/BrowserItemViewModel.cs
```

`ModCard` i `PackInstanceCard` mogą być etapem przejściowym, ale długoterminowo powinny korzystać ze wspólnego stylu bazowego.

---

## Config i migracje

Ten POC nie wymaga nowych tabel sam z siebie.

Opcjonalne ustawienia w przyszłości:

- `default_main_tab = catalog` — domyślnie i tak katalog, więc nie trzeba w MVP;
- `browser_density = comfortable | compact`;
- `last_catalog_filter`;
- `inspector_width`.

Zasady:

- nie pisać runtime do `appsettings.json`;
- ustawienia użytkownika tylko przez `user_settings` / `UserSettingsService`;
- lokalne nazwy i targety DLL nie trafiają do telemetrii.

---

## Platform, packaging, updater, telemetry, privacy, AV

### Platforma

- Steam i Epic muszą mieć te same zakładki i wzorzec kart.
- Dla Epic komunikaty o instalacji/legendary nie mogą rozwalać layoutu Inspectorów.
- Ścieżki folderów pokazywać tylko w szczegółach lub po akcji `Otwórz folder`.

### Packaging / updater

- Brak nowych ciężkich zależności.
- Velopack bez zmian.
- Nie dodawać WebView, zewnętrznych rendererów ani runtime skin downloadów.

### Telemetry / privacy

- Nie wysyłać lokalnych nazw zestawów, folderów ani customowych nazw plików.
- Jeśli mierzyć UX, to tylko neutralne eventy: `tab_opened`, `inspector_opened`, `bulk_mode_used`, bez nazw modów użytkownika.
- Locale wysyłać tylko jako `pl` / `en`.

### AV

- Inline DLL target selection nie może ukrywać ostrzeżeń dla external DLL.
- Potwierdzenia ryzykownych i destrukcyjnych akcji nadal modalne.
- Usuwanie zestawu pokazuje dokładnie, co zostanie usunięte.

---

## Language / i18n impact

MVP locale: PL i EN. Fallback: PL.

Nowe lub wymagane klucze:

```text
UI.Tabs.Catalog
UI.Tabs.MyPacks
UI.Tabs.DllAddons
UI.Browser.SearchPlaceholder
UI.Browser.SelectMode
UI.Browser.ExitSelectMode
UI.Browser.NoResults
UI.Inspector.Details
UI.Inspector.Contents
UI.Inspector.Compatibility
UI.Inspector.InstalledIn
UI.Inspector.AddToTargets
UI.Inspector.QuickActions
UI.Inspector.Configuration
UI.Inspector.Sharing
UI.Inspector.DangerZone
UI.Actions.CreatePack
UI.Actions.InstallClassic
UI.Actions.AddTo
UI.Actions.ApplyChanges
UI.Bulk.SelectedMods
UI.Bulk.SelectedPacks
UI.Bulk.SelectedDlls
```

Wymagania:

- brak hardcoded user-facing text w XAML/ViewModel/Core;
- placeholdery zgodne w PL/EN;
- liczniki przez ICU MessageFormat;
- nazwy własne (`SUSModder`, `Among Us`, `Steam`, `Epic`, `BepInEx`, `legendary`) bez tłumaczenia;
- przyszły język dodawany przez locale file/metadata.

---

## Verification plan

### Manual UX

1. Aplikacja startuje na `Katalog modów`.
2. `Katalog modów`, `Moje zestawy`, `Dodatki DLL` mają ten sam styl kart.
3. Klik karty na każdej zakładce otwiera prawy Inspector, nie modal.
4. Zakładka DLL pokazuje grid kart DLL.
5. DLL Inspector pozwala wybrać targety inline.
6. Modal pojawia się tylko dla potwierdzenia ryzyka/destrukcji albo jako transitional fallback.
7. Selected i bulk są wizualnie różne.
8. Bulk bar pokazuje akcje właściwe dla aktywnej zakładki.
9. PL i EN mają wszystkie nowe klucze.
10. Smoke test 100%, 125%, 150% DPI.

### Techniczne

1. Build `SUSModder.sln`.
2. LSP/diagnostics dla nowych ViewModeli i XAML.
3. Test kliknięć: catalog -> inspector, pack -> inspector, DLL -> inspector.
4. Test instalacji DLL do klasycznej instalacji i lokalnej instancji.
5. Test `Esc`, focus, keyboard navigation.

---

## Suggested implementation order

### Faza 1 — wspólny system wizualny (1-2 dni) ✅

- [x] Semantyczne statusy na kartach (busy / update / installed).
- [x] `BrowserCardStyle.axaml` + poziome karty 340×160.
- [ ] Osobne kontrolki `StatusBadge`, `CardActionRow` (zastąpione inline w XAML).

### Faza 2 — katalog jako baseline (1-2 dni) ✅

- [x] Katalog na nowych kartach.
- [x] Inspector: sekcje, kompatybilne DLL, sticky CTA.
- [x] Katalog jako default tab.
- [x] Wyszukiwarka browser.

### Faza 3 — Moje zestawy na tym samym wzorcu (1-2 dni) ✅

- [x] `PackInstanceCard`.
- [x] `PackInstanceDetailDrawer` w sekcjach.
- [x] Bulk dla zestawów.

### Faza 4 — DLL bez głównego modala (2-3 dni) ✅

- [x] Grid `DllAddonCard` w zakładce.
- [x] `DllAddonInspector` + inline target selection (`DllInstallTargetItem`).
- [x] Fallback: `DllManagementPanel` / `DllModSelectionView` w FAB overlay.

### Faza 5 — polish i review (1 dzień) 🟡

- [x] PL/EN (klucze MVP).
- [ ] DPI, kontrast, focus states (audyt).
- [ ] Screenshoty PNG w `DOC/readme/`.
- [ ] Review i18n + security dla DLL (checklist przed release).

### Równoległość

- Style kart i i18n mogą iść równolegle.
- Inspector katalogu i zestawów mogą iść równolegle po ustaleniu sekcji.
- DLL grid można zacząć od read-only kart, a target selection dołożyć później.

---

## Kryteria akceptacji

1. Domyślna zakładka po starcie to **Katalog modów**.
2. Wszystkie trzy zakładki używają spójnej karty i selected/bulk/status visual language.
3. Zakładka DLL pokazuje kafelki/listę w głównym Browserze, nie startuje od modala.
4. Klik DLL otwiera Inspector z opisem, kompatybilnością i targetami.
5. Modpack/zestaw ma szczegóły spójne wizualnie z katalogowym modem i DLL.
6. Bulk mode działa według tych samych zasad na zakładkach, z akcjami właściwymi dla zakładki.
7. PL/EN copy jest kompletne.
8. Nie wprowadzono ciężkich zależności ani nowych AV-ryzyk.

---

## Decyzja rekomendowana

SUSModder 3.0 powinien mieć **Katalog modów jako default** oraz jeden spójny wzorzec **Browser + Inspector** dla katalogu, lokalnych zestawów i DLL. DLL powinny stać się pełnoprawną zakładką z kartami i inline target selection, a modalowy flow powinien zostać tylko jako fallback lub potwierdzenie ryzyka.
