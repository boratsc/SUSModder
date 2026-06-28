# 21 – Aero Liquid Glass Theme (Vista Aero × macOS Liquid Glass)

**Priorytet:** 🟡 P1 dla SUSModder 3.0 / visual identity  
**Effort:** ~5-9 dni (theme tokens + window transparency + contrast QA + fallbacki)  
**Status:** 📄 **POC / kierunek wizualny** — półprzezroczysty, personalizowany styl z rozmyciem tła użytkownika  
**Powiązane:** [`20-consistent-browser-inspector-ui.md`](20-consistent-browser-inspector-ui.md), [`../POC/2026-06-01-ui-refresh-v3-poc.md`](../POC/2026-06-01-ui-refresh-v3-poc.md), [`06-microinteractions-polish.md`](06-microinteractions-polish.md), [`DOC/2025-10-21 - propozycje usprawnień/04 - avalonia third-party packages.md`](../2025-10-21%20-%20propozycje%20usprawnie%C5%84/04%20-%20avalonia%20third-party%20packages.md)

---

## Cel

Stworzyć nowy motyw wizualny SUSModder 3.0 oparty o półprzezroczyste materiały, subtelne rozmycie tła i integrację z personalizowanym stylem Windows użytkownika.

Kierunek estetyczny:

> **Windows Vista Aero Glass + nowoczesny Windows 11 Mica/Acrylic + macOS Liquid Glass**

Motyw ma wyglądać premium i świeżo, ale nadal ma być praktyczny: czytelne karty, jasna hierarchia akcji, brak neonowego chaosu, zachowana ergonomia z #20.

---

## Inspiracja i granice

### Inspiracje

- **Windows Vista Aero** — przezroczystość, blur, „szklana” głębia, miękkie highlighty.
- **Windows 11 Mica/Acrylic** — personalizacja przez tapetę i system theme, performance-aware backdrop.
- **macOS Liquid Glass** — mleczne powierzchnie, warstwowość, delikatne odbicia, zaokrąglenia.

### Granice

Nie robimy dosłownego klona żadnego systemu. SUSModder ma mieć własny styl:

- bardziej czytelny niż Vista;
- lżejszy niż agresywne acrylic UI;
- mniej „cukierkowy” niż przesadny glassmorphism;
- kompatybilny z obecnymi zakładkami: Katalog / Moje zestawy / DLL.

---

## Non-goals

- ❌ Nie robimy tła przez przechwytywanie tapety lub screenshotów pulpitu aplikacją.
- ❌ Nie wysyłamy informacji o tapecie, kolorach systemu ani motywie użytkownika do telemetrii.
- ❌ Nie wymuszamy przezroczystości, jeśli system/Windows/transparency settings ją wyłączają.
- ❌ Nie wprowadzamy WebView ani ciężkich rendererów.
- ❌ Nie poświęcamy kontrastu tekstu dla efektu glass.
- ❌ Nie robimy glass jako jedynego motywu — ma być tryb opcjonalny lub wariant 3.0 z fallbackiem.
- ❌ Nie stosujemy wielu sąsiadujących paneli acrylic bez separacji, bo tworzy to szwy i chaos.

---

## Założenia techniczne

### Avalonia

Avalonia wspiera okna z transparentnym tłem i platform-dependent transparency hints:

```xml
<Window TransparencyLevelHint="Mica"
        Background="Transparent">
```

oraz acrylic blur:

```xml
<Window TransparencyLevelHint="AcrylicBlur"
        Background="Transparent">
    <Panel>
        <ExperimentalAcrylicBorder Material="{DynamicResource AcrylicMaterial}"/>
        <!-- UI content -->
    </Panel>
</Window>
```

Windows wspiera `Transparent`, `AcrylicBlur` i `Mica`, ale efekt jest zależny od OS i ustawień użytkownika. Motyw musi mieć fallback do zwykłych solid/opaque brushy.

### Windows materials

Microsoft zaleca:

- **Mica** jako bazowy materiał dla długowiecznych okien — wydajniejszy, personalizowany przez tapetę i theme.
- **Acrylic** raczej dla powierzchni tymczasowych lub wybranych paneli, bo mocna przezroczystość na dużych powierzchniach może pogarszać czytelność.
- Acrylic musi respektować High Contrast, Battery Saver, wyłączone transparency effects i nieaktywną aplikację.

Wniosek dla SUSModder:

> Base window: Mica/Mica-like backdrop.  
> Cards/Inspector: controlled in-app glass z tintem i fallbackiem.  
> Flyouty/modalne potwierdzenia: mocniejszy acrylic/frosted glass.

---

## Wizualny kierunek

### Warstwy

```text
[Wallpaper / system backdrop]
        ↓
[Window backdrop: Mica/AcrylicBlur, transparent background]
        ↓
[Global tint layer: dark/light/pink-aware, 35-60% opacity]
        ↓
[Glass panels: Browser, Inspector, status bar]
        ↓
[Cards: frosted material + border highlight]
        ↓
[Text, icons, CTA, badges]
```

### Ogólny wygląd

- Tło okna prześwituje do tapety użytkownika, ale jest przygaszone tintem.
- Panele są mleczne, półprzezroczyste, z lekkim rozmyciem i cienką jasną krawędzią.
- Karty mają delikatny „liquid” highlight u góry i cień pod spodem.
- Inspector ma mocniejszy tint niż browser, żeby tekst i akcje były stabilnie czytelne.
- Statusy nadal mają semantyczne kolory: niebieski preview, fiolet bulk, żółty update, zielony ready, czerwony danger.

---

## Proponowane tokeny motywu

```text
GlassBackdropTintColor          #9910141F   // globalny tint na window backdrop
GlassPanelFillColor             #B01B2433   // główne panele
GlassPanelStrongFillColor       #D0202738   // Inspector, modale
GlassCardFillColor              #8CFFFFFF   // light glass overlay w dark tint
GlassCardFillDarkColor          #661E2433
GlassStrokeColor                #55FFFFFF
GlassStrokeSubtleColor          #22FFFFFF
GlassHighlightColor             #66FFFFFF
GlassShadowColor                #66000000
GlassNoiseOpacity               0.03-0.06
GlassBlurFallbackColor          #E61E2433
```

Dla dark mode karta nie powinna być po prostu jasna i półprzezroczysta. Lepiej użyć mieszanki:

```text
fill = ciemny tint + delikatny biały highlight + jasny stroke
```

Dla light mode:

```text
fill = mleczna biel + subtelny szary tint + ciemniejszy tekst
```

Dla pink:

```text
fill = jasny mleczny róż + hearts background jako fallback, ale bez przesadnego chaosu pod szkłem
```

---

## Główne powierzchnie

### Window backdrop

```text
TransparencyLevelHint fallback order:
1. Mica
2. AcrylicBlur
3. Transparent
4. Opaque fallback brush
```

W praktyce można rozważyć dwa warianty:

- `GlassMica` — bezpieczniejszy, wydajniejszy, mniej transparentny.
- `GlassAcrylic` — bardziej Aero/Vista, większe wow, ale z fallbackiem i testami kontrastu.

### Top bar / tabs

```text
┌──────────────────────────────────────────────────────────────┐
│ SUSModder      Steam/Epic     [Szukaj...]       [⚙]          │
│ [Katalog modów] [Moje zestawy] [Dodatki DLL]                 │
└──────────────────────────────────────────────────────────────┘
```

- Jedna szklana belka z mocniejszym tintem.
- Aktywna zakładka: liquid pill z jasnym highlightem.
- Nieaktywne zakładki: transparentne, ale czytelne.

### Browser panel

- Lekko transparentny panel bez ciężkiej ramki.
- Karty unoszą się na tle, ale nie konkurują z tapetą.
- Empty state jako glass card na środku.

### Inspector

- Mocniej przyciemniony/podbarwiony niż Browser.
- Sekcje oddzielone nie pełnymi liniami, tylko subtelnymi separators/strokes.
- Sticky primary CTA w dolnej części z mocnym kontrastem.

### Cards

Wspólne z #20 `BrowserCard`, ale w wariancie glass:

```text
┌────────────────────────────────────────────┐
│ [icon]  Title                       BADGE  │
│        Subtitle                            │
│        Metadata                            │
│                                            │
│        [Primary]                    [⋯]    │
└────────────────────────────────────────────┘
```

Efekty:

- stroke: `GlassStrokeColor`;
- top highlight gradient 1 px / 2 px;
- shadow miękki, nie czarny ciężki;
- hover: większy highlight + minimalny lift;
- selected: niebieski outline bez pogrubiania layoutu;
- bulk: fioletowy outline + checkbox.

---

## Ergonomia i czytelność

Glass nie może utrudniać korzystania.

### Zasady

1. Tekst zawsze na warstwie z wystarczającym tintem.
2. Opisy i metadane nie mogą leżeć bezpośrednio na transparentnym tle.
3. Primary CTA ma być solidny lub prawie solidny.
4. Danger zone nie może być tylko czerwonym transparentnym szkłem — musi być jednoznaczna.
5. Update/status badges są zawsze solidne lub z bardzo wysokim opacity.
6. Jeśli transparency disabled/high contrast/battery saver — fallback do opaque theme.

### Kontrast

Minimalne wymagania:

- normalny tekst: WCAG 4.5:1;
- większy tekst/badges: 3:1;
- focus ring widoczny na jasnym i ciemnym tle;
- test na jasnej, ciemnej i kolorowej tapecie.

---

## Integracja z #20 Browser + Inspector

Ten POC nie zmienia ergonomii #20. Zmienia tylko warstwę wizualną.

Kolejność pozostaje:

1. Katalog modów — default.
2. Moje zestawy.
3. Dodatki DLL.
4. Wspólny Browser + Inspector.
5. Spójne karty.
6. DLL bez głównego flow modalowego.

Glass theme ma być skórą nad tym wzorcem, nie osobnym layoutem.

---

## Core business logic responsibilities

Core nie powinien wiedzieć o glass theme.

Core dostarcza tylko:

- statusy instalacji/aktualizacji;
- dane katalogu/zestawów/DLL;
- stabilne error codes;
- informacje o platformie i trybie.

Nie dodawać user-facing copy w Core. Nie dodawać zależności UI do `SUSModder.Core`.

---

## UI / Avalonia responsibilities

UI odpowiada za:

- `GlassTheme.axaml` lub wariant tokenów w istniejących theme dictionaries;
- ustawienie `Window.TransparencyLevelHint` i `Background="Transparent"`;
- fallback do opaque brushes;
- glass variants dla `BrowserCard`, Inspector, status bar, tabs, flyouts;
- detekcję/ustawienia użytkownika: transparency enabled/disabled;
- test DPI i High Contrast.

Docelowe pliki:

```text
SUSModder/Themes/GlassTheme.axaml
SUSModder/Styles/GlassSurfaceStyles.axaml
SUSModder/Styles/BrowserCardStyle.axaml
SUSModder/Services/ThemeManager.cs          // dodać ThemeType.Glass
SUSModder/Views/MainWindow.axaml            // transparency hints / glass layers
SUSModder/Localization/pl.json, en.json     // copy ustawień
```

---

## Config i migracje

### Ustawienia

Dodać nowy theme value:

```json
"theme": "glass"
```

Do `user_settings.theme` — bez nowej tabeli.

Nie pisać do `appsettings.json`.

### Opcjonalne ustawienia w przyszłości

```text
glass_intensity = low | medium | high
glass_use_system_backdrop = true | false
glass_reduce_transparency = true | false
```

MVP: tylko `theme = glass`, reszta automatyczna/fallback.

---

## Platform, packaging, updater, telemetry, privacy, AV

### Platforma

- Windows 11: preferowany Mica/Acrylic.
- Windows 10: możliwy AcrylicBlur/fallback, zależnie od Avalonia/platformy.
- Linux/macOS: fallback do opaque lub platform-supported transparency bez obietnicy pełnego Aero efektu.

### Packaging / updater

- Bez WebView i bez dodatkowych exe.
- Bez runtime pobierania zasobów.
- Velopack bez zmian.
- Jeśli dodamy `Avalonia.Themes.Mica`, sprawdzić NuGet, rozmiar i kompatybilność przed wdrożeniem. MVP może próbować bez nowej paczki, korzystając z Avalonia transparency hints.

### Privacy

- Nie przechwytujemy tapety.
- Nie zapisujemy informacji o tapecie.
- Nie wysyłamy kolorów systemu/tła.
- Backdrop pochodzi z OS/DWM/Avalonia, nie z custom capture.

### AV

- Brak screen capture i brak hooków pulpitu — minimalizuje ryzyko AV.
- Nie używać własnego procesu do blur/wallpaper capture.
- Nie dodawać native DLL tylko dla efektu szkła bez mocnego uzasadnienia.

---

## Language / i18n impact

Nowe copy PL/EN:

```text
UI.Theme.Glass
UI.Theme.GlassDescription
UI.Settings.Appearance
UI.Settings.TransparencyWarning
UI.Settings.ReduceTransparency
UI.Settings.GlassUnsupportedTitle
UI.Settings.GlassUnsupportedMessage
```

Zasady:

- nazwa marketingowa może zostać `Glass` / `Aero Glass` albo `Liquid Glass`, ale opis musi być lokalizowany;
- fallback messages PL/EN;
- brak hardcoded komunikatów o niedostępności efektu.

---

## Verification plan

### Visual QA

1. Jasna tapeta, ciemna tapeta, bardzo kolorowa tapeta.
2. Windows dark mode i light mode.
3. Transparency effects ON/OFF w Windows Settings.
4. High Contrast mode.
5. Battery Saver / inactive window fallback.
6. DPI 100%, 125%, 150%.
7. Inspector z długim opisem moda.
8. Karty w trzech zakładkach: Katalog, Moje zestawy, DLL.
9. Modal/flyout external DLL warning.
10. Pink theme fallback — czy nie gryzie się z glass.

### Functional QA

1. Build `SUSModder.sln`.
2. Start z `theme=glass`.
3. Przełączanie Dark → Light → Pink → Glass.
4. Restart aplikacji zachowuje motyw.
5. Gdy effect unsupported, aplikacja nie crashuje i używa fallback brush.
6. Brak regresji w install/update/delete/launch.

### Accessibility QA

1. Kontrast tekstu na kartach.
2. Kontrast badges.
3. Focus ring na glass card.
4. Czytelność primary/danger buttons.
5. Czy reduced transparency fallback działa.

---

## Suggested implementation order

### Faza 0 — POC techniczny okna (0.5-1 dzień)

- Test `TransparencyLevelHint="Mica"` i `AcrylicBlur` w `MainWindow`.
- Sprawdzić fallback Windows 10/11.
- Sprawdzić czy obecne gradienty nie przykrywają backdropu.

### Faza 1 — tokeny GlassTheme (1-2 dni)

- `GlassTheme.axaml` z aliasami do obecnych brushy.
- Zachować stare klucze jako aliasy.
- Dodać `ThemeType.Glass` i zapis `theme=glass`.

### Faza 2 — powierzchnie (1-2 dni)

- Window backdrop.
- Top bar/tabs.
- Browser panel.
- Inspector panel.
- Status bar.

### Faza 3 — karty i Inspectory (1-2 dni)

- Glass variant dla `BrowserCard`.
- Selected/bulk/update states.
- Flyouts i context menus.

### Faza 4 — fallbacki i dostępność (1-2 dni)

- Opaque fallback.
- High Contrast.
- Transparency disabled.
- Kontrast tekstu.

### Faza 5 — polish (1 dzień)

- Animacje 150-250 ms.
- Delikatny noise/highlight overlay.
- Screenshoty README.
- Review i18n + quality.

---

## Kryteria akceptacji

1. Motyw Glass działa jako osobny wybór motywu.
2. Okno korzysta z systemowego backdropu, bez custom screenshot/wallpaper capture.
3. Tapeta użytkownika subtelnie wpływa na wygląd, ale tekst pozostaje czytelny.
4. Katalog, Moje zestawy i DLL zachowują spójny Browser + Inspector z #20.
5. Przy wyłączonej przezroczystości lub braku wsparcia aplikacja używa czytelnego opaque fallbacku.
6. High Contrast nie jest popsuty.
7. Brak nowych ciężkich zależności lub ryzyk AV.
8. PL/EN mają komplet copy dla ustawień i fallbacków.

---

## Decyzja rekomendowana

Dodać **Glass** jako opcjonalny motyw SUSModder 3.0, oparty o systemowy Mica/Acrylic backdrop i własne semantyczne tokeny glass. Nie robić pełnego custom wallpaper blur ani screen capture. Glass powinien być warstwą wizualną na spójnym Browser + Inspector (#20), nie osobnym layoutem.
