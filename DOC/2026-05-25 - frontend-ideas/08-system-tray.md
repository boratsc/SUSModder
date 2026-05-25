# 08 – System tray / minimalizacja do zasobnika

**Priorytet:** 🟡 P1  
**Effort:** ~2-3h  

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

Najprościej na start: `H.NotifyIcon` (lekki, czysty API, działa na Win + Linux).

### Przykład z `H.NotifyIcon`

```csharp
var trayIcon = new TaskbarIcon
{
    Icon = new System.Drawing.Icon("Assets/icon.ico"),
    ToolTipText = "SUSModder",
    ContextMenu = new ContextMenu
    {
        Items = {
            new TaskbarIconMenuItem("Town of Us", LaunchMod(1)),
            new TaskbarIconMenuItem("Vanilla", LaunchMod(0)),
            new TaskbarIconSeparator(),
            new TaskbarIconMenuItem("Przywróć", RestoreWindow),
            new TaskbarIconMenuItem("Zamknij", ShutdownApp)
        }
    }
};
```

### Gdzie w kodzie

| Co | Plik |
|----|------|
| Inicjalizacja tray | `App.axaml.cs` lub `Program.cs` |
| Logika menu PPM | Nowy: `Services/TrayService.cs` |
| Ustawienia | `AppSettingsView.axaml` – nowa sekcja |
| `user-settings.json` | Nowe pole: `minimizeToTray: bool` |
| Obsługa ✕ (zamknij) | `MainWindow.axaml.cs` – `OnClosing` → Hide zamiast Close |
| Ostatnio uruchamiane | `MainWindowViewModel.GameLaunch.cs` – już jest `lastLaunchId`, wystarczy lista 3 ostatnich |

### Decyzje

- [ ] `H.NotifyIcon` czy `System.Windows.Forms.NotifyIcon`?
- [ ] Domyślnie włączone czy wyłączone? (rekomendacja: wyłączone)
- [ ] Czy przy starcie apka ma startować w tray (opcja "Uruchom z systemem")?
- [ ] Szybkie uruchamianie: 3 ostatnie mody czy wszystkie zainstalowane?
- [ ] Czy ikona tray ma pokazywać badge gdy są update''y?
