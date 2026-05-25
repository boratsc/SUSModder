# 03 – Toast notification system

**Priorytet:** 🟡 P1  
**Effort:** ~3-4h  

## Stan obecny

Brak systemu powiadomień. Feedback tylko przez:
- Progress bar w prawym panelu (per mod)
- Dialogi modalne (przerywają flow)
- Status bar (statyczny)

## Propozycja

Lekki system toastów bez dodatkowych bibliotek:

```
┌────────────────────────────────────┐
│  ✅ Town of Us zainstalowany!      │
│     Wersja 5.1.2 | 12:34          │
└────────────────────────────────────┘
        ↓ slide-in, auto-close po 4s
```

### Typy toastów

| Typ | Ikona | Kolor | Przykład |
|-----|-------|-------|----------|
| Success | ✅ | Zielony | `{mod} zainstalowany pomyślnie` |
| Warning | ⚠️ | Żółty | `Dostępna aktualizacja: {mod}` |
| Error | ❌ | Czerwony | `Błąd pobierania – sprawdź połączenie` |
| Info | ℹ️ | Niebieski | `Trwa sprawdzanie aktualizacji...` |

### Architektura

```
ToastService (singleton)
├── Enqueue(toast) → kolejka FIFO, max 3 widoczne
├── Auto-close po X sekundach (konfigurowalne)
└── Slide-in/out animacja (istniejące AnimationStyles)

ToastHost (UserControl w MainWindow.axaml)
└── StackPanel w prawym dolnym rogu, nad FAB-em

ToastNotification (UserControl)
├── Ikona + tytuł + opcjonalny subtitle
├── Klikalny (opcjonalny callback, np. "Zobacz szczegóły")
└── Przycisk ✕ do ręcznego zamknięcia
```

### Integracja z istniejącym kodem

```csharp
// W ViewModelach (przez DI):
_toastService.Enqueue(new ToastNotification
{
    Type = ToastType.Success,
    Title = _localization.GetFormatted("Toast.ModInstalled", modName),
    Subtitle = $"Wersja {version}",
    AutoCloseMs = 4000
});

// Toast z akcją:
_toastService.Enqueue(new ToastNotification
{
    Type = ToastType.Warning,
    Title = "Dostępna aktualizacja",
    Subtitle = "Town of Us v5.2.0",
    OnClick = () => { /* otwórz update dialog */ }
});
```

### Gdzie użyć (przykłady)

- `ModOperations.cs` – po Install/Uninstall/Update
- `DllManagement.cs` – po InstallDll/RemoveDll
- `Updates.cs` – gdy znaleziono update''y
- `GameLaunch.cs` – błędy uruchamiania
- `Initialization.cs` – CheckForModUpdatesAsync znalazł update''y
- `Changelog` – "Zaktualizowano do vX.Y.Z"

## Pliki do dodania

| Plik | Co |
|------|-----|
| `Services/ToastService.cs` | Kolejka, enqueue, timery |
| `Models/ToastNotification.cs` | Model: typ, tytuł, subtitle, callback |
| `Views/ToastNotificationView.axaml` | Pojedynczy toast z animacją |
| `Views/ToastHost.axaml` | Kontener toastów w MainWindow |

## Decyzje

- [ ] Max liczba widocznych toastów: 3?
- [ ] Domyślny auto-close: 4s? 5s?
- [ ] Toast z akcją ma być dłużej widoczny (klikalny)?
- [ ] Czy tosty mają dźwięk? (raczej nie – desktop)
