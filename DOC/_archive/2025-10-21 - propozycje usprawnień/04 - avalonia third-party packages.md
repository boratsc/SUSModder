# Avalonia Third-Party Packages – Przegląd Bibliotek UI

## Wprowadzenie
Lista dodatkowych pakietów NuGet, które mogą znacząco poprawić wygląd i funkcjonalność aplikacji SUSModder. Podzielone na kategorie wg zastosowania.

---

## 1. Kompletne Systemy Designu

### 1.1 FluentAvalonia ⭐⭐⭐⭐⭐
**Pakiet:** `FluentAvalonia` (amwx/FluentAvalonia)  
**Wersja:** 2.x  
**Opis:** Implementacja Windows 11 Fluent Design System dla Avalonia

**Kontrolki:**
- `NavigationView` – nowoczesna nawigacja w stylu Windows 11 (hamburger menu, breadcrumbs)
- `InfoBar` – powiadomienia inline (success, warning, error, info) z ikoną i przyciskiem zamknięcia
- `ContentDialog` – modalne dialogi w stylu Fluent (zamiast standardowego Window)
- `SettingsExpander` – rozwijane sekcje ustawień
- `FABorder` – border z zaokrąglonymi rogami i acrylic blur
- `FAButton` – przyciski Fluent (Primary, Accent, Standard, Subtle)
- `PersonPicture` – zaokrąglone avatary użytkowników
- `ProgressRing` – ładujący spinner w stylu Windows 11
- `TeachingTip` – tooltips z dodatkowymi informacjami (jak w Office)

**Zastosowanie w SUSModder:**
- Zamiana głównego layoutu na `NavigationView` z sekcjami (Mody, Ustawienia, O Aplikacji, Discord Links)
- `InfoBar` zamiast message boxów dla notyfikacji (np. "Mod zainstalowany", "Dostępna aktualizacja")
- `ContentDialog` dla potwierdzenia usunięcia (zamiast naszego custom dialog)
- `SettingsExpander` w panelu ustawień
- `ProgressRing` podczas sprawdzania aktualizacji

**Dlaczego warto:**
- Profesjonalny, nowoczesny wygląd jak natywne aplikacje Windows 11
- Doskonała integracja z Avalonia (używane przez wiele komercyjnych projektów)
- Aktywnie rozwijane, dobra dokumentacja
- Light/Dark theme out of the box

**Rating:** ⭐⭐⭐⭐⭐ (Must-have dla nowoczesnych aplikacji Windows)

---

### 1.2 Material.Avalonia
**Pakiet:** `Material.Avalonia` (AvaloniaCommunity/Material.Avalonia)  
**Opis:** Material Design implementation dla Avalonia

**Kontrolki:**
- `Card` – material design cards z elevation shadows
- `Snackbar` – floating notifications na dole ekranu
- `FloatingButton` – FAB (Floating Action Button)
- `MaterialButton` – przyciski z ripple effect
- `Chip` – małe, klikalne tagi
- `NavigationDrawer` – wysuwalny panel boczny
- `AppBar` – górna belka z tytułem i akcjami

**Zastosowanie w SUSModder:**
- `Card` zamiast obecnych Border dla kafelków modów
- `Snackbar` dla szybkich powiadomień (toast-like)
- `Chip` do tagów modów (Full, DLL, Outdated)
- `FloatingButton` dla akcji "Sprawdź wszystkie aktualizacje"

**Dlaczego warto:**
- Świetne animacje i efekty (ripple, elevation)
- Spójny system kolorów i typografii
- Dobrze znany design language (użytkownicy Android będą czuć się komfortowo)

**Rating:** ⭐⭐⭐⭐ (Świetna alternatywa dla Fluent, bardziej cross-platform feel)

---

### 1.3 Semi.Avalonia
**Pakiet:** `Semi.Avalonia`  
**Opis:** Semi Design system (popularny w Chinach, używany przez Alibaba, ByteDance)

**Kontrolki:**
- `Banner` – duże powiadomienia na górze ekranu
- `SideSheet` – wysuwany panel z boku
- `Timeline` – oś czasu (timeline component)
- `Tree` – hierarchiczne drzewo z checkboxami
- `Transfer` – dual-listbox dla przenoszenia elementów
- `Skeleton` – skeleton loading screens

**Zastosowanie w SUSModder:**
- `Timeline` dla historii instalacji modów
- `Skeleton` podczas ładowania listy modów (lepsze UX niż spinner)
- `SideSheet` dla szczegółów moda (zamiast nowego okna)

**Dlaczego warto:**
- Unikalne kontrolki nie dostępne w innych bibliotekach
- Nowoczesny design
- Dobra performance

**Rating:** ⭐⭐⭐⭐ (Niszowy ale bardzo solidny)

---

## 2. Kontrolki Specjalistyczne

### 2.1 AvaloniaEdit ⭐⭐⭐⭐⭐
**Pakiet:** `AvaloniaEdit` (AvaloniaUI/AvaloniaEdit)  
**Opis:** Zaawansowany text editor z syntax highlighting

**Funkcje:**
- Code highlighting (C#, JSON, XML, itd.)
- Code folding
- Line numbers
- Search & replace
- IntelliSense support
- Custom syntax definitions

**Zastosowanie w SUSModder:**
- Podgląd/edycja plików konfiguracyjnych modów (JSON, XML)
- Podgląd logów instalacji z highlighting
- Advanced view dla zaawansowanych użytkowników

**Rating:** ⭐⭐⭐⭐⭐ (Najlepszy text editor dla Avalonia)

---

### 2.2 Avalonia.Controls.TreeDataGrid
**Pakiet:** `Avalonia.Controls.TreeDataGrid` (AvaloniaUI/Avalonia.Controls.TreeDataGrid)  
**Opis:** High-performance hierarchical grid/tree

**Funkcje:**
- Sortowanie kolumn
- Filtrowanie
- Virtualizacja (tysiące wierszy bez lagów)
- Hierarchia (tree + grid)
- Custom cell templates

**Zastosowanie w SUSModder:**
- "Advanced View" – tabela modów z sortowaniem po nazwie, rozmiarze, dacie
- Podgląd struktury plików moda (tree)
- Lista DLL dependencies z checkboxami

**Rating:** ⭐⭐⭐⭐⭐ (Must-have dla danych tabelarycznych)

---

### 2.3 AvaloniaProgressRing
**Pakiet:** `AvaloniaProgressRing` (Deadpikle/AvaloniaProgressRing)  
**Opis:** Customizable indeterminate progress indicator

**Funkcje:**
- Różne style (dots, arc, ring, bars)
- Konfigurowalne kolory i rozmiary
- Smooth animations
- Małe i wydajne

**Zastosowanie w SUSModder:**
- Ikona ładowania przy sprawdzaniu aktualizacji
- Indykator podczas pobierania modów
- Minimalny loading state w panelu statusu

**Rating:** ⭐⭐⭐⭐ (Ładniejsze niż standardowy ProgressBar)

---

### 2.4 AvaloniaWebView (ExCSS, Chromely, CefNet)
**Pakiety:** 
- `AvaloniaWebView.Chromely` (Kation/AvaloniaWebView)
- `CefNet.Avalonia` (CefNet/CefNet)

**Opis:** Embedded WebView control (Chromium)

**Zastosowanie w SUSModder:**
- Wyświetlanie opisów modów z formatowaniem HTML/Markdown
- Embedded changelogs z GitHuba
- Preview screenów modów z galerii online
- Potencjalnie: webowy panel ustawień dla advanced userów

**Rating:** ⭐⭐⭐ (Heavy dependency, ale powerful)

---

### 2.5 Markdown.Avalonia
**Pakiet:** `Markdown.Avalonia` (whistyun/Markdown.Avalonia)  
**Opis:** Native Markdown renderer bez WebView

**Funkcje:**
- Parse i render Markdown do native Avalonia controls
- GitHub Flavored Markdown support
- Code blocks z syntax highlighting (integracja z AvaloniaEdit)
- Tables, task lists, emoji
- Custom styles

**Zastosowanie w SUSModder:**
- Wyświetlanie opisów modów (jeśli są w Markdown)
- Changelogs z formatowaniem
- "About" page z README.md projektu
- Poradniki/Help w aplikacji

**Rating:** ⭐⭐⭐⭐⭐ (Lżejsze niż WebView, native feel)

---

### 2.6 LiveCharts2 (LiveChartsCore.SkiaSharpView.Avalonia)
**Pakiet:** `LiveChartsCore.SkiaSharpView.Avalonia` (beto-rodriguez/LiveCharts2)  
**Opis:** Zaawansowane wykresy i visualizacje

**Typy wykresów:**
- Line, Bar, Column, Pie, Scatter, Heatmaps
- Gauge meters
- Polar charts
- GeoMaps

**Zastosowanie w SUSModder:**
- Wykres użycia dysku przez mody (pie chart)
- Timeline instalacji modów (line chart)
- Statystyki: popularność modów, częstość aktualizacji
- Gauge dla miejsca na dysku

**Rating:** ⭐⭐⭐⭐ (Overkill dla SUSModder, ale świetny dla dashboardów)

---

### 2.7 Svg.Skia & SkiaSharp
**Pakiety:** 
- `Svg.Skia` (wieslawsoltes/Svg.Skia)
- `SkiaSharp` (mono/SkiaSharp)

**Opis:** SVG rendering i zaawansowana grafika 2D

**Zastosowanie w SUSModder:**
- Ikony SVG zamiast PNG (skalowalne, ostre na każdym DPI)
- Custom animations i efekty
- Generowanie thumbnails modów
- Visualizacje (np. gradient backgrounds)

**Rating:** ⭐⭐⭐⭐ (Świetne dla custom graphics)

---

### 2.8 Avalonia.HtmlRenderer
**Pakiet:** `Avalonia.HtmlRenderer` (AvaloniaUI/Avalonia.HtmlRenderer)  
**Opis:** Lightweight HTML renderer (bez pełnego browsera)

**Funkcje:**
- Basic HTML/CSS rendering
- Znacznie lżejszy niż WebView
- Brak JavaScript (security)

**Zastosowanie w SUSModder:**
- Proste formatted opisy modów
- Rich tooltips z HTML
- Styled notifications

**Rating:** ⭐⭐⭐ (Dobre dla prostego HTML, ale ograniczone CSS)

---

## 3. Utility Libraries

### 3.1 Avalonia.Themes.Mica ⭐⭐⭐⭐⭐
**Pakiet:** `Avalonia.Themes.Mica` (kikipoulet/Avalonia.Themes.Mica)  
**Opis:** Windows 11 Mica material effect

**Funkcje:**
- Mica backdrop (semi-transparent background z blur Desktop wallpaper)
- Acrylic blur
- Automatyczna integracja z OS theme

**Zastosowanie w SUSModder:**
- Tło głównego okna z efektem Mica (jak Settings w Windows 11)
- Zwiększone poczucie integracji z OS

**Rating:** ⭐⭐⭐⭐⭐ (Efekt wow za małą cenę)

---

### 3.2 Avalonia.ThemeManager
**Pakiet:** `Avalonia.ThemeManager` (wieslawsoltes/Avalonia.ThemeManager)  
**Opis:** Runtime theme switching framework

**Funkcje:**
- Dynamiczna zmiana motywów bez restartu
- Własne motywy jako pluginy
- Persist user theme choice
- Live theme preview

**Zastosowanie w SUSModder:**
- Rozszerzenie obecnego ThemeManager o więcej motywów
- User-created themes (community themes)
- Live preview w ustawieniach

**Rating:** ⭐⭐⭐⭐ (Nice-to-have dla theme enthusiasts)

---

### 3.3 Avalonia.PropertyGrid
**Pakiet:** `Avalonia.PropertyGrid` (bodong1987/Avalonia.PropertyGrid)  
**Opis:** Property editor jak w Visual Studio (PropertyGrid)

**Funkcje:**
- Automatyczne generowanie UI dla properties obiektu
- Kategorie, expandery
- Custom editors dla typów
- Data binding

**Zastosowanie w SUSModder:**
- Advanced settings editor
- Debugowanie modów (inspect ModConfiguration)
- Developer tools dla power users

**Rating:** ⭐⭐⭐ (Niszowe, ale przydatne dla advanced scenarios)

---

### 3.4 Avalonia.Markup.Declarative
**Pakiet:** `Avalonia.Markup.Declarative` (AvaloniaUI/Avalonia.Markup.Declarative)  
**Opis:** C# DSL dla UI (jak SwiftUI/Jetpack Compose)

**Przykład:**
```csharp
new StackPanel()
    .Children(
        new TextBlock().Text("Hello"),
        new Button().Content("Click")
    )
```

**Zastosowanie w SUSModder:**
- Dynamiczne generowanie UI dla custom mod settings
- Scripting support dla power users
- Rapid prototyping nowych features

**Rating:** ⭐⭐⭐ (Inny paradygmat, wymaga refactoru)

---

### 3.5 Avalonia.Gif
**Pakiet:** `Avalonia.Gif` (jmacato/Avalonia.GIF)  
**Opis:** Animated GIF support

**Zastosowanie w SUSModder:**
- Animowane ikony dla modów (np. loading spinner jako GIF)
- Animated previews modów
- Fun easter eggs

**Rating:** ⭐⭐⭐ (Niche, ale przydatne dla visual flair)

---

### 3.6 Avalonia.Controls.ColorPicker
**Pakiet:** `Avalonia.Controls.ColorPicker` (AvaloniaUI/Avalonia.Controls.ColorPicker)  
**Opis:** Color picker control

**Zastosowanie w SUSModder:**
- Custom theme colors (user-defined accent colors)
- Mod color tags/categories
- Customizacja UI

**Rating:** ⭐⭐⭐ (Nice-to-have dla deep customization)

---

## 4. Animation & Effects Libraries

### 4.1 Avalonia.FuncUI
**Pakiet:** `Avalonia.FuncUI` (fsprojects/Avalonia.FuncUI)  
**Opis:** Functional reactive UI framework (F# style w C#)

**Funkcje:**
- Elmish architecture
- Immutable state
- Time-travel debugging

**Rating:** ⭐⭐ (Wymaga zmiany paradygmatu, overkill dla SUSModder)

---

### 4.2 NXUI
**Pakiet:** `NXUI` (wieslawsoltes/NXUI)  
**Opis:** C# DSL extensions dla Avalonia

**Rating:** ⭐⭐⭐ (Podobne do Markup.Declarative)

---

## 5. Rekomendacje dla SUSModder

### Tier 1: Must-Have (immediate impact) 🔥
1. **FluentAvalonia** – nowoczesny Windows 11 look, `NavigationView`, `InfoBar`, `ContentDialog`
2. **Avalonia.Themes.Mica** – Mica effect dla premium feel
3. **Markdown.Avalonia** – native rendering opisów modów i changelogów
4. **AvaloniaEdit** – podgląd/edycja konfigów

### Tier 2: High Value (polish) ✨
5. **Material.Avalonia** – alternatywa dla Fluent, `Card` controls, `Snackbar`
6. **Avalonia.Controls.TreeDataGrid** – advanced view z sortowaniem
7. **AvaloniaProgressRing** – lepsze loading indicators
8. **Svg.Skia** – SVG icons dla sharp rendering

### Tier 3: Nice-to-Have (future) 🎨
9. **LiveCharts2** – statystyki i visualizacje
10. **Semi.Avalonia** – `Skeleton` loading, `Timeline`
11. **Avalonia.ThemeManager** – community themes
12. **Avalonia.Gif** – animated icons

### Tier 4: Niche/Experimental 🔬
- Avalonia.PropertyGrid (developer tools)
- Avalonia.Markup.Declarative (wymaga refactoru)
- AvaloniaWebView (heavy dependency)
- Avalonia.FuncUI (zmiana paradygmatu)

---

## 6. Plan Implementacji

### Faza 1: FluentAvalonia Migration (8-12h)
**Cel:** Modernizacja całego UI na Windows 11 style

**Kroki:**
1. Dodaj `FluentAvalonia` NuGet package
2. Zamień główny layout na `NavigationView`:
   ```xml
   <ui:NavigationView PaneDisplayMode="Left">
       <ui:NavigationViewItem Icon="Home" Content="Mody" Tag="mods" />
       <ui:NavigationViewItem Icon="Setting" Content="Ustawienia" Tag="settings" />
       <ui:NavigationViewItem Icon="People" Content="Discord" Tag="discord" />
   </ui:NavigationView>
   ```
3. Zamień message boxy na `ContentDialog` i `InfoBar`
4. Dodaj `ProgressRing` podczas loading states
5. Użyj `FABorder` dla mod cards (acrylic blur)
6. Przepisz status bar używając `InfoBar` dla notifications

**Zyski:**
- Natywny Windows 11 feel
- Lepsza nawigacja (hamburger menu)
- Non-intrusive notifications (InfoBar vs MessageBox)
- Professional polish

---

### Faza 2: Markdown & Icons (4-6h)
**Cel:** Rich content rendering

**Kroki:**
1. Dodaj `Markdown.Avalonia`
2. Fetch opisów modów w Markdown z GitHub README
3. Render w panelu szczegółów moda
4. Dodaj `Svg.Skia` dla SVG icons
5. Zamień PNG icons na SVG (Discord, settings, itp.)

**Zyski:**
- Formatted descriptions z code blocks, links, images
- Sharp icons na każdym DPI
- Mniejszy rozmiar aplikacji (SVG < PNG)

---

### Faza 3: Advanced Features (6-10h)
**Cel:** Power user features

**Kroki:**
1. Dodaj `AvaloniaEdit` dla podglądu config files
2. Dodaj `Avalonia.Controls.TreeDataGrid` dla advanced mod view
3. Implementuj "Developer Mode" toggle w settings
4. Tree view struktury plików zainstalowanego moda
5. Live log viewer z syntax highlighting

**Zyski:**
- Debug tools dla użytkowników zgłaszających błędy
- Transparency (users see what's installed)
- Power user appeal

---

### Faza 4: Visual Polish (4-6h)
**Cel:** Efekty wow

**Kroki:**
1. Dodaj `Avalonia.Themes.Mica` dla Mica backdrop
2. Dodaj `Material.Avalonia` jako alternatywny theme
3. Implementuj `Snackbar` dla quick notifications
4. Dodaj `AvaloniaProgressRing` w wielu miejscach
5. Animated icons z `Avalonia.Gif`

**Zyski:**
- Premium feel aplikacji
- Użytkownicy docenią attention to detail
- Wyróżnienie na tle innych mod managerów

---

## 7. Porównanie: FluentAvalonia vs Material.Avalonia

| Aspekt | FluentAvalonia | Material.Avalonia |
|--------|----------------|-------------------|
| **Look** | Windows 11 native | Android/Web modern |
| **Platform Feel** | Windows-first | Cross-platform |
| **Complexity** | Średnia | Średnia |
| **Kontrolki** | NavigationView, InfoBar, ContentDialog | Card, Snackbar, FAB |
| **Animacje** | Subtle, płynne | Wyraziste (ripple, elevation) |
| **Dla SUSModder** | ⭐⭐⭐⭐⭐ Perfect fit | ⭐⭐⭐⭐ Dobra alternatywa |
| **Dokumentacja** | Doskonała | Dobra |
| **Community** | Duża, aktywna | Średnia |

**Rekomendacja:** Start with **FluentAvalonia** as primary theme, offer **Material.Avalonia** as optional theme dla użytkowników preferujących Material Design.

---

## 8. Dependency Matrix

| Package | Size (MB) | Dependencies | Complexity | Maintenance |
|---------|-----------|--------------|------------|-------------|
| FluentAvalonia | ~1.5 | Avalonia 11+ | Medium | ⭐⭐⭐⭐⭐ |
| Material.Avalonia | ~1.2 | Avalonia 11+ | Medium | ⭐⭐⭐⭐ |
| Markdown.Avalonia | ~0.5 | Avalonia, Markdig | Low | ⭐⭐⭐⭐ |
| AvaloniaEdit | ~2.0 | Avalonia, TextMateSharp | Medium | ⭐⭐⭐⭐⭐ |
| TreeDataGrid | ~1.0 | Avalonia 11+ | Low | ⭐⭐⭐⭐⭐ |
| Svg.Skia | ~3.0 | SkiaSharp | Low | ⭐⭐⭐⭐⭐ |
| Mica Theme | ~0.3 | Avalonia 11+ | Low | ⭐⭐⭐⭐ |
| LiveCharts2 | ~4.5 | SkiaSharp | High | ⭐⭐⭐⭐ |
| AvaloniaWebView | ~80+ | Chromium | Very High | ⭐⭐⭐ |

**Wnioski:**
- FluentAvalonia, Markdown, TreeDataGrid – low overhead, high value
- SkiaSharp-based (Svg, LiveCharts) – ~3-5MB overhead, worth it
- AvaloniaWebView – avoid unless absolutely necessary (80MB+ Chromium)

---

## 9. Alternative Approaches (Custom Implementation)

Jeśli nie chcemy dodawać dependencies, niektóre features można zaimplementować custom:

### InfoBar (custom)
```csharp
// Lightweight info banner bez FluentAvalonia
public class InfoBanner : UserControl
{
    // Border z colored left stripe + icon + message + dismiss button
    // Slide-down animation przy pokazaniu
}
```

### Markdown Lite (custom)
```csharp
// Basic markdown parsing bez full library
// Support dla: **bold**, *italic*, [links], # headers
// Render do native TextBlocks w StackPanel
```

### SVG Icons (manual)
```xml
<!-- Można użyć Path geometry zamiast pełnego Svg.Skia -->
<Path Data="M12,2L2,7L12,12L22,7Z" Fill="{DynamicResource AccentColor}" />
```

**Trade-off:** Mniejsze dependencies, ale więcej maintanance work i mniej features.

---

## 10. Podsumowanie – Action Items

### Do zrobienia teraz (Quick Wins):
1. ✅ Dodać `FluentAvalonia` package
2. ✅ Migracjć główny layout na `NavigationView`
3. ✅ Zamienić UninstallConfirmDialog na `ContentDialog` z FluentAvalonia
4. ✅ Dodać `InfoBar` dla notifications (toast-like)
5. ✅ Dodać `Avalonia.Themes.Mica` dla backdrop effect

### Do rozważenia w przyszłości:
- `Markdown.Avalonia` dla rich mod descriptions
- `AvaloniaEdit` dla advanced mod configuration editing
- `Avalonia.Controls.TreeDataGrid` dla alternative list view
- `Svg.Skia` dla sharp icons
- `Material.Avalonia` jako alternative theme option

### Skip (zbyt duży overhead):
- AvaloniaWebView (80MB+ Chromium)
- LiveCharts2 (unless dodamy dashboard/statistics feature)
- Avalonia.FuncUI (wymaga architectural rewrite)

---

**Aktualizacja:** 21.10.2025  
**Autor:** GitHub Copilot  
**Kontekst:** SUSModder v2.0.0 – feature-2.0.0 branch  
**Status:** Research & Planning document
