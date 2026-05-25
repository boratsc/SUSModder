# 05 – Performance i RAM

**Priorytet:** 🔴 P0 (po review – to bug, nie polish)  
**Effort:** ~2-3h (audyt + podstawowe poprawki + lazy WebView2)  

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

### 3. WebView2 – lazy load tylko dla Epic

Paczka `WebView2.Avalonia` jest w `SUSModder.csproj`. **Jest używana** – do logowania Epic Games, gdy legendary wymaga autoryzacji przez przeglądarkę.

**Problem:** WebView2 ładuje się przy starcie aplikacji zawsze, niezależnie od platformy:
- Użytkownicy **Steam** – WebView2 nigdy nie jest potrzebny, ale zżera ~50-100 MB RAM
- Użytkownicy **Epic** – potrzebny tylko przy re-autoryzacji (rzadko)

**Rozwiązanie: lazy load WebView2:**

```csharp
// Zamiast ładować WebView2 w App.axaml.cs przy starcie:
// WebView2 jest ładowany tylko gdy:
// 1. Platforma == Epic
// 2. legendary wymaga logowania (auth required)

private WebView2? _webView2;

private async Task<WebView2> GetWebView2Async()
{
    if (_webView2 == null)
    {
        _webView2 = new WebView2();
        await _webView2.EnsureCoreWebView2Async();
    }
    return _webView2;
}

// Po zakończeniu auth – można Dispose, jeśli to jednorazowa akcja:
public void DisposeWebView2()
{
    _webView2?.Dispose();
    _webView2 = null;
}
```

**Oszczędność:** ~50-100 MB RAM dla użytkowników Steam (większość) i Epic bez re-auth.  
**Gdzie sprawdzić:** `EpicVersionManager.cs`, `EpicAuthDialog.axaml.cs` – gdzie WebView2 jest tworzony.

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
| WebView2 init | `EpicVersionManager.cs`, `EpicAuthDialog.axaml.cs` |
| Compatibility cache | `CompatibilityService.cs:55` |
| WhenActivated | `MainWindow.axaml.cs:56` |

## Decyzje

- [ ] Dodać `IDisposable` do `MainWindowViewModel`?
- [ ] Dodać Dispose bitmap w `DiscordPromo`?
- [ ] Dodać lazy-load WebView2 (tylko gdy Epic + auth)?
- [ ] Dodać cache dla ConfigService API calls?

### ⚠️ Do weryfikacji

Obecnie WebView2 jest inicjalizowany w `EpicAuthDialog.axaml.cs:InitializeWebView()`, czyli tylko przy otwarciu dialogu Epic auth. **Teoretycznie już jest lazy-loaded.** Ale:

1. Czy natywne DLL WebView2 są ładowane do pamięci przy starcie aplikacji (przez .NET host)?
2. Czy sam `CoreWebView2Environment.CreateAsync()` alokuje pamięć zanim user zobaczy dialog?
3. Dla użytkowników Steam – czy WebView2 w ogóle trafia do outputu? Jeśli tak, to nawet nieużywane DLL mogą być mapowane do pamięci.

**Do sprawdzenia:** Task Manager → RAM SUSModder.exe na Steam vs Epic. Jeśli Steam ma podobne zużycie co Epic, WebView2 DLL są ładowane eager.
