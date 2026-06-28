# 08 – System tray / minimalizacja do zasobnika

**Priorytet:** 🟡 P1  
**Effort:** ~2-3h  
**Status:** ✅ **Zaimplementowano** (2026-05-26)  

## Stan obecny

Brak. Zamknięcie okna = zamknięcie aplikacji.

## Propozycja

Minimalizacja do zasobnika systemowego (system tray) z kontrolowanym UX.

### Wymagania UX

1. **Checkbox w ustawieniach:** `☐ Minimalizuj do zasobnika zamiast zamykania`
2. **Przy pierwszej minimalizacji:** toast/dymek informujący co się stało i jak wyłączyć
3. **Menu PPM (prawy przycisk myszy) na ikonce tray:**

```
┌─────────────────────────────┐
│ 🚀 Town of Us               │  ← ostatnio uruchamiane
│ 🚀 The Other Roles          │      (max 3, tylko zainstalowane)
│ 🚀 Vanilla                  │
├─────────────────────────────┤
│ 📂 Przywróć SUSModder       │
│ ❌ Zamknij                  │
└─────────────────────────────┘
```

### Flow pierwszego razu

```
1. User klika ✕ (zamyka okno)
2. Okno znika, apka leci do tray
3. Pojawia się dymek systemowy:
   ┌──────────────────────────────────────┐
   │ SUSModder                            │
   │ Aplikacja została zminimalizowana    │
   │ do zasobnika systemowego.            │
   │                                      │
   │ Kliknij ikonkę aby przywrócić.       │
   │ [Nie pokazuj więcej]  [OK]           │
   └──────────────────────────────────────┘
4. Ikonka tray jest widoczna w zasobniku
```

### Ustawienia

W `AppSettingsView` – nowa sekcja "Zachowanie okna":
```
┌────────────────────────────────────────┐
│ Zachowanie okna                        │
│                                        │
│ ☐ Minimalizuj do zasobnika zamiast     │
│   zamykania aplikacji                  │
│                                        │
│ ☐ Pokaż szybkie uruchamianie w menu    │
│   zasobnika (3 ostatnie mody)          │
└────────────────────────────────────────┘
```

**Domyślnie:** wyłączone. User sam włącza.

### Technicznie

Avalonia 12.x nie ma wbudowanego system tray – trzeba użyć natywnego API:

- **Windows:** `System.Windows.Forms.NotifyIcon` (wymaga referencji do `System.Windows.Forms`)  
  LUB `H.NotifyIcon` (NuGet, cross-platform)
- **Linux (przyszłość):** `libappindicator` / `StatusNotifier`

**Decyzja implementacyjna:** Używamy `System.Windows.Forms.NotifyIcon` z `<UseWindowsForms>true</UseWindowsForms>` – 
  - zero nowych zewnętrznych zależności NuGet
  - sprawdzone, stabilne API
  - aplikacja już jest Windows-only (`net10.0-windows`)
  - NotifyIcon w .NET Core+ tworzy własne okno message-only, nie potrzebuje WinForms pętli komunikatów

### Architektura

```
┌─────────────────────────────────────────────────────────┐
│                    SystemTrayService                      │
│  (SUSModder/Services/, singleton w DI)                   │
│                                                          │
│  - Inicjalizuje NotifyIcon (icon z Assets/icon.ico)      │
│  - Zarządza menu kontekstowym (ToolStripMenuItem)        │
│  - Obsługuje BalloonTip (first-minimize toast)           │
│  - Eventy: RestoreRequested, ExitRequested              │
│  - Metody: Show(), Hide(), UpdateModsList()             │
└──────────────────────┬──────────────────────────────────┘
                       │
         ┌─────────────┼─────────────┐
         ▼             ▼             ▼
   MainWindow     MainVM       AppSettingsVM
   .OnClosing     .GetMods()   .MinimizeToTray
   .Show()/.Hide()             .ShowQuickLaunch
```

---

## ✅ Status implementacji (2026-05-26)

**Wszystkie elementy zaimplementowane i działające:**

- ✅ Minimalizacja do zasobnika zamiast zamykania (opcja w ustawieniach, domyślnie wyłączona)
- ✅ Ikona SUSModdera w tray (wyciągana z .exe przez `Icon.ExtractAssociatedIcon`)
- ✅ Menu PPM: szybkie uruchamianie modów (max 3, tylko full mody), Przywróć, Zamknij
- ✅ Dymek systemowy przy pierwszym minimalizowaniu (z opcją "Nie pokazuj więcej")
- ✅ Kliknięcie ikonki → przywrócenie okna
- ✅ i18n PL + EN dla wszystkich stringów
- ✅ Brak nowych zależności NuGet (użyto wbudowanego `System.Windows.Forms.NotifyIcon`)
- ✅ Zero breaking changes — opcja domyślnie wyłączona

**Użycie:** Ustawienia → Zachowanie okna → przełącz "Minimalizuj do zasobnika"

### Plany implementacyjne (kroki)

1. ✅ **Dodanie `<UseWindowsForms>true</UseWindowsForms>`** w `SUSModder.csproj`
2. ✅ **Dodanie pól do `UserSettings.cs`**:
   - `MinimizeToTray` (bool, default false)
   - `ShowQuickLaunchInTray` (bool, default false)
   - `TrayFirstMinimizeShown` (bool, default false)
3. ✅ **Dodanie kluczy i18n** w `pl.json` i `en.json`:
   - `Settings.SystemTray.*` (Title, MinimizeToTray.Label/Description, QuickLaunch.Label/Description)
   - `SystemTray.*` (Restore, Exit, FirstMinimize)
4. ✅ **Utworzenie `Services/SystemTrayService.cs`**:
   - `Initialize(Window mainWindow)` - tworzy NotifyIcon + context menu
   - `Show()`/`Hide()` - pokazuje/ukrywa ikonkę
   - `ShowBalloonTip(title, text)` - wyświetla dymek systemowy
   - `UpdateRecentMods(List<TrayModInfo> mods)` - aktualizuje szybkie uruchamianie
   - `ShowFirstMinimizeNotificationIfNeeded()` - dymek przy pierwszym minimize
   - `Dispose()` - cleanup
5. ✅ **Dodanie properties do `AppSettingsViewModel`**:
   - `MinimizeToTray`, `ShowQuickLaunchInTray`
   - Podpięcie do `LoadCurrentSettings()`, `CheckForChanges()`, `SaveSettings()`
6. ✅ **Dodanie sekcji "Zachowanie okna"** w `AppSettingsView.axaml`
7. ✅ **Modyfikacja `MainWindow.axaml.cs`**:
   - W `OnClosing()`: jeśli MinimizeToTray → e.Cancel=true, Hide()
   - Przywracanie: `SystemTrayService.RestoreRequested` → Show()
   - `ForceClose()` - wymusza zamknięcie z pominięciem minimize (fix buga)
   - Podpięcie `SystemTrayService.UpdateRecentMods()` po inicjalizacji
8. ✅ **Inicjalizacja w `App.axaml.cs`** po MainWindow.Show()

---

## Plan implementacji — szczegóły

### Krok 1: SUSModder.csproj
```xml
<UseWindowsForms>true</UseWindowsForms>
```

### Krok 2: UserSettings.cs
```csharp
[JsonPropertyName("minimizeToTray")]
public bool MinimizeToTray { get; set; } = false;

[JsonPropertyName("showQuickLaunchInTray")]
public bool ShowQuickLaunchInTray { get; set; } = false;

[JsonPropertyName("trayFirstMinimizeShown")]
public bool TrayFirstMinimizeShown { get; set; } = false;
```

### Krok 3: i18n

**pl.json:**
```json
"SystemTray": {
  "Title": "Zachowanie okna",
  "MinimizeToTray": {
    "Label": "Minimalizuj do zasobnika zamiast zamykania aplikacji",
    "Description": "Po zamknięciu okna aplikacja zostanie zminimalizowana do zasobnika systemowego"
  },
  "QuickLaunch": {
    "Label": "Pokaż szybkie uruchamianie w menu zasobnika",
    "Description": "Wyświetla 3 ostatnio używane mody w menu kontekstowym ikonki w zasobniku"
  },
  "Restore": "Przywróć SUSModder",
  "Exit": "Zamknij",
  "FirstMinimize": {
    "Title": "SUSModder",
    "Message": "Aplikacja została zminimalizowana do zasobnika systemowego.\n\nKliknij ikonkę w zasobniku, aby przywrócić okno.\nMożesz zmienić to zachowanie w Ustawieniach → Zachowanie okna.",
    "DontShowAgain": "Nie pokazuj więcej"
  }
}
```

### Krok 4: SystemTrayService.cs

`SystemTrayService` łączy `System.Windows.Forms.NotifyIcon` z Avalonią:
- `Initialize(nativeWindowHandle, restoreAction, exitAction)`
- `Show()` / `Hide()` - pokazuje/ukrywa ikonkę tray
- `UpdateRecentMods(mods)` - rebuild menu
- `Dispose()` - cleanup tray icon

Działanie:
1. Tworzy NotifyIcon z ikoną, tooltipem
2. Konfiguruje ContextMenuStrip z:
   - Dynamiczną listą modów (max 3)
   - Separator
   - "Przywróć SUSModder" 
   - "Zamknij"
3. Double-click na ikonce → restore
4. Balloon tip na pierwsze minimize

### Krok 5: AppSettingsViewModel.cs
Dodanie properties z wzorcem jak TelemetryEnabled / DeveloperMode:
```csharp
private bool _minimizeToTray;
public bool MinimizeToTray { get; set; } // with RaiseAndSetIfChanged + CheckForChanges
```

### Krok 6: AppSettingsView.axaml
Nowa sekcja przed "Resetowanie aplikacji":
```xml
<Border> <!-- Zachowanie okna -->
  <StackPanel>
    <TextBlock Text="{local:Localize Settings.SystemTray.Title}" />
    <ToggleSwitch IsChecked="{Binding MinimizeToTray}" />
    <ToggleSwitch IsChecked="{Binding ShowQuickLaunchInTray}" />
  </StackPanel>
</Border>
```

### Krok 7: MainWindow.axaml.cs
```csharp
protected override void OnClosing(WindowClosingEventArgs e)
{
    if (_systemTrayService?.IsEnabled == true)
    {
        e.Cancel = true;
        _systemTrayService.Show();
        this.Hide();
        return;
    }
    base.OnClosing(e);
}
```

### Krok 8: App.axaml.cs
Rejestracja SystemTrayService w DI jako singleton.

---

## Testowanie

1. Włącz opcję "Minimalizuj do zasobnika" w ustawieniach
2. Zamknij okno (✕) → okno znika, ikonka w tray
3. Kliknij ikonkę → okno wraca
4. PPM na ikonce → menu z modami (jeśli włączone)
5. "Zamknij" w menu → aplikacja się zamyka
6. Wyłącz opcję → zamknięcie okna zamyka aplikację jak przed zmianą
7. Sprawdź tooltip przy pierwszym minimize
