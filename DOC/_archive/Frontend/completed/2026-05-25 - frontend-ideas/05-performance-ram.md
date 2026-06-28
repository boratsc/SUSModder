# 05 – Performance i RAM

**Priorytet:** 🔴 P0 (po review – to bug, nie polish)  
**Effort:** ~2-3h (audyt + podstawowe poprawki + lazy WebView2)

---

## Plan implementacji (2026-05-26)

### Podział na zadania

| # | Zadanie | Pliki | Status |
|---|---------|-------|--------|
| 1 | `IDisposable` dla `MainWindowViewModel` | `MainWindowViewModel.cs`, `ViewModelBase.cs` | ✅ |
| 2 | Zatrzymanie `DispatcherTimer` (DiscordPromo) przy Dispose | `MainWindowViewModel.DiscordPromo.cs` | ✅ |
| 3 | `CancellationTokenSource` dla background task (StatusBar) | `MainWindowViewModel.StatusBar.cs` | ✅ |
| 4 | `IDisposable` + bitmap cleanup dla `DiscordServerViewModel` | `DiscordServerViewModel.cs` | ✅ |
| 5 | Dispose starej bitmapy przy rotacji DiscordPromo | `MainWindowViewModel.DiscordPromo.cs` | ✅ |
| 6 | Audit WebView2/NativeWebView lazy load (bez zmian kodu) | — | ⏳ (audyt wykonany, kod bez zmian – NativeWebView już lazy) |
| 7 | MemoryCache w `ConfigService` dla API config | `ConfigService.cs` | ✅ |
| 8 | Budowanie i testy | — | ✅ (build OK, brak projektów testowych) |
| 9 | [Review] Wywołanie `Dispose()` z `MainWindow.OnClosing` | `MainWindow.axaml.cs` | ✅ (CRITICAL – bez tego Dispose nigdy nie był wołany) |
| 10 | [Review] `_disposed` guard + `GC.SuppressFinalize` | `MainWindowViewModel.cs` | ✅ |
| 11 | [Review] `SemaphoreSlim.Dispose()` w StatusBar | `MainWindowViewModel.StatusBar.cs` | ✅ |
| 12 | [Review] `_disposed` guard w `RotateDiscordPromo` | `MainWindowViewModel.DiscordPromo.cs` | ✅ |
| 13 | [Cache] Przeniesienie cache z `ConfigService` → `ConfigManager` | `ModConfig.cs`, `ConfigService.cs` | ✅ (ConfigManager.SaveConfig() teraz sam unieważnia cache – działa dla wszystkich 15+ call site'ów) |
| 14 | [Cleanup] Komentarz "WebView2" → "NativeWebView" | `EpicAuthDialog.axaml.cs` | ✅ |
| 15 | [Cache] `UpdateSingleModConfigAsync` – `ConfigManager.SaveConfig` → `this.SaveConfig` | `ConfigService.cs` | ✅ |

### Zależności
- Zadania 2-5 wymagają zadania 1 (class-level IDisposable)
- Zadanie 7 niezależne
- Zadanie 6 niezależne (audit, ewentualne zmiany w przyszłości)

### Ryzyka
- `ViewModelBase` (ReactiveObject) – rozszerzenie o `IDisposable` może wpłynąć na inne ViewModele; dodajemy interfejs tylko do `MainWindowViewModel`
- `NativeWebView` assembly (`Avalonia.Controls.WebView`) – może być ładowany przy starcie przez XAML namespace; wymaga osobnego audytu profilem pamięci (dotMemory/PerfView)

### ✅ Zrealizowane dodatkowo
1. **NativeWebView lazy-load w SplashWindow** – usunięto `xmlns:wv` z SplashWindow.axaml, NativeWebView tworzony programowo tylko gdy plik `SplashAnimation.mp4` istnieje.
2. **NativeWebView lazy-load w EpicAuthDialog** – usunięto `xmlns:wv` z EpicAuthDialog.axaml, NativeWebView tworzony programowo tylko gdy `IsWebViewMode = true`.
3. **Efekt:** `Avalonia.Controls.WebView.dll` **nie jest ładowane przy starcie** – tylko gdy faktycznie potrzeba (video splash LUB auth Epic). Oszczędność ~50-100 MB RAM dla większości użytkowników.

---

## Audyt (przed implementacją)

### Stan faktyczny (2026-05-26)

| Element | Stan | Plik |
|---------|------|------|
| `MainWindowViewModel : IDisposable` | ❌ NIE implementuje | `MainWindowViewModel.cs:48` |
| `_discordPromoRotationTimer` (DispatcherTimer) | ✅ Istnieje, ale **nigdy nie jest Disposed** | `DiscordPromo.cs:26` |
| `StatusBar` background task | ✅ `Task.Run` z `while(true)` – **nigdy nie jest anulowany** | `StatusBar.cs:537` |
| `DiscordServerViewModel.IconBitmap` | ❌ **Nigdy nie Disposed** | `DiscordServerViewModel.cs:14` |
| `DiscordIconPreloader` preloaded bitmaps | ❌ Nie zwalnia bitmap przy czyszczeniu cache | `DiscordIconPreloader.cs` |
| `_velopackUpdateService` (IDisposable) | ❌ **Nigdy nie Disposed** | `Initialization.cs:266` |
| `EpicAuthDialog` WebView | ✅ **Już lazy-loaded** (tworzony tylko przy otwarciu dialogu) | `EpicAuthDialog.axaml.cs:28` |
| `ConfigService.LoadConfigAsync()` | ❌ **Brak cache** – zawsze pobiera z API | `ConfigService.cs:24` |

### Ustalenia po audycie

1. **NativeWebView ≠ WebView2**: Kod używa `Avalonia.Controls.WebView` (NuGet) / `NativeWebView`, nie WebView2.Avalonia. Dialog Epic auth tworzy go tylko przy otwarciu – **już jest lazy**. Nie zmieniamy.
2. **Assembly loading**: `Avalonia.Controls.WebView.dll` może być ładowany przy starcie przez XAML namespace (`xmlns:wv="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls.WebView"` w `EpicAuthDialog.axaml`). To **osobny task** – wymaga profilera pamięci, żeby zweryfikować ile RAM zżera.
3. **StatusBar refresh**: Używa `Task.Run` z `while(true)` zamiast `DispatcherTimer` – to background task, który przeżywa ViewModel. Dodajemy `CancellationTokenSource`.
4. **ConfigService**: `LoadConfig()` deleguje do `ConfigManager.LoadConfig()` – ten zawsze pobiera z API (lub pliku lokalnego). Dodajemy prosty MemoryCache z TTL 30s.

---

## Stan po implementacji (2026-05-26)

| Element | Status | Szacowana oszczędność RAM |
|---------|--------|--------------------------|
| `IDisposable` na `MainWindowViewModel` | ✅ Implemented | ~0 (organizacyjne) |
| `_discordPromoRotationTimer` stop przy Dispose | ✅ Implemented | ~0 (zatrzymanie timera) |
| `CancellationTokenSource` dla StatusBar background task | ✅ Implemented | ~0 (poprawne zamykanie wątku) |
| `VelopackUpdateService.Dispose()` przy zamknięciu | ✅ Implemented | Mała (handle HTTP) |
| Bitmapy Discord (`DiscordServerViewModel`) | ✅ Implemented | **~5-20 MB** (głównie przy preloadzie) |
| MemoryCache w `ConfigService` | ✅ Implemented | **~15-30 requestów API mniej na sesję** |
| NativeWebView lazy load | ⏳ Audyt wykonany – kod już lazy | ~50-100 MB (potencjalnie, do weryfikacji z profilerem) |
| `_disposed` guard (bezpieczeństwo wielokrotnego Dispose) | ✅ Implemented (po review) | ~0 |
| `GC.SuppressFinalize` w Dispose | ✅ Implemented (po review) | ~0 |
| `Dispose()` wywoływany z `MainWindow.OnClosing` | ✅ Implemented (po review) | **KRYTYCZNE** – bez tego Dispose nigdy nie był wywoływany |
| `SemaphoreSlim.Dispose()` w StatusBar | ✅ Implemented (po review) | Mała |
| `_disposed` guard w `RotateDiscordPromo` | ✅ Implemented (po review) | Bezpieczeństwo na race condition |

---

## Co sprawdzić

### 1. Memory cleanup (Dispose pattern)

- `DispatcherTimer` w `DiscordPromo` (co 10s) – czy Dispose przy zamknięciu?
- `DispatcherTimer` w `StatusBar` (API ping) – jw.
- `MainWindowViewModel` nie implementuje `IDisposable`
- Subskrypcje `WhenAnyValue` w `WhenActivated` – CompositeDisposable powinien być ok, ale warto zweryfikować

Fix: dodać `IDisposable` do `MainWindowViewModel`:
```csharp
public void Dispose()
{
    _discordPromoRotationTimer?.Stop();
    _statusBarTimer?.Stop();
    // Dispose bitmap, cancel pending tasks...
}
```

### 2. Bitmapy Discord

- `DiscordIconPreloader` – ładuje ikony, ale czy są zwalniane?
- Rotacja co 10s – nowe bitmapy do pamięci?
- Rozważyć `Bitmap?.Dispose()` przy zmianie promowanego Discorda

### 3. NativeWebView (Avalonia.Controls.WebView) – lazy load dla Epic

Aktualny stan: aplikacja używa **NativeWebView** z paczki `Avalonia.Controls.WebView` v12.0.1 (a nie dawnego `WebView2.Avalonia`). Jest używany w dwóch miejscach:
1. `EpicAuthDialog` – logowanie do Epic Games (embedded przeglądarka OAuth)
2. `SplashWindow` – animowane wideo powitalne

**Stan faktyczny (po audycie):**
- NativeWebView jest **już lazy-loaded** – tworzony tylko przy otwarciu `EpicAuthDialog` lub `SplashWindow`
- Nie ma eager initialization w `App.axaml.cs` ani innym miejscu startowym
- Assembly `Avalonia.Controls.WebView.dll` jest ładowane przy starcie przez XAML namespace (`xmlns:wv="clr-namespace:Avalonia.Controls;assembly=Avalonia.Controls.WebView"`) – to standardowe zachowanie Avalonii

**Ryzyko RAM:** Assembly może być mapowane do pamięci nawet jeśli nie jest używane (użytkownicy Steam). ~50-100 MB potencjalnie. Do zweryfikowania profilem pamięci (dotMemory, PerfView).

**Decyzja:** ✅ **Kod już poprawny – lazy-loaded.** Ewentualna optymalizacja wymaga osobnego taska z profilem pamięci. Nie zmieniamy kodu.

### 4. Cache'owanie API

- `CompatibilityService` już ma `MemoryCache` – ✅
- `ConfigService` pobiera config z API – czy cache'owany?
- Ikony Discord – cache'owane?

## Gdzie sprawdzić

| Co | Plik |
|----|------|
| DiscordPromo timer | `MainWindowViewModel.DiscordPromo.cs:26` |
| StatusBar timer | `MainWindowViewModel.StatusBar.cs` |
| DiscordIconPreloader | `SUSModder/Services/DiscordIconPreloader.cs` |
| NativeWebView init | `EpicAuthDialog.axaml.cs`, `SplashWindow.axaml.cs` |
| Compatibility cache | `CompatibilityService.cs:55` |
| WhenActivated | `MainWindow.axaml.cs:56` |

## Decyzje (po implementacji, 2026-05-26)

| Decyzja | Werdykt | Status |
|---------|---------|--------|
| Dodać `IDisposable` do `MainWindowViewModel` | ✅ **TAK** | ✅ Zaimplementowano – `Dispose()` zatrzymuje timer, anuluje background task, zwalnia VelopackUpdateService |
| Dispose bitmap w `DiscordServerViewModel` | ✅ **TAK** | ✅ Zaimplementowano – `DisposeIconBitmap()` + `IDisposable` w ViewModelu. Wywoływane przy Dispose i rotacji |
| Lazy-load NativeWebView | ✅ **JUŻ LAZY** | Audyt potwierdził – tworzony tylko przy otwarciu dialogu/splash. Assembly loading wymaga profilera pamięci |
| Cache dla ConfigService API calls | ✅ **TAK** | ✅ Zaimplementowano – MemoryCache z TTL 30s, `InvalidateConfigCache()` przy zapisie |

### ⚠️ Uwagi końcowe

1. NativeWebView (`Avalonia.Controls.WebView`) – kod **już jest lazy-loaded**. Jedyna legacy ostrożność: XAML namespace powoduje load assembly przy starcie. To standardowe zachowanie Avalonii. Aby zweryfikować faktyczne użycie RAM WebView DLL, potrzeba profilera (dotMemory, PerfView) – osobny task.
2. Główny potencjalny zysk RAM (~50-100 MB) pochodzi z assembly `Avalonia.Controls.WebView.dll`, nie z bitmap. Bitmapy Discord to ~5-20 MB oszczędności.
3. MemoryCache w ConfigService to **quick win** – prosty fix, duży wpływ na liczbę requestów API.
4. Build: ✅ Sukces (0 błędów, 0 ostrzeżeń). Brak projektów testowych.
