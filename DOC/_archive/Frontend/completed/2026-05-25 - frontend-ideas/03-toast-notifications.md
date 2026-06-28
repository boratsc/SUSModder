# 03 – Toast notification system

**Status:** ✅ **Zaimplementowane (2026-05-25)**  
**Priorytet:** 🟡 P1  
**Effort:** ~3-4h (faktyczny: ~4h z code review i fixami)  
**Commit:** `12f3097` na `susmodder-3.0`

## Stan przed

Brak systemu powiadomień. Feedback tylko przez:
- Progress bar w prawym panelu (per mod)
- Dialogi modalne (przerywają flow)
- Status bar (statyczny)

## Stan po

Działający system toast notifications:
- Lekki, bez zewnętrznych bibliotek
- 4 typy: Success/Info/Warning/Error z kolorowym akcentem
- Slide-in animacja (Avalonia Animation API, bez styli)
- Auto-close przez DispatcherTimer (UI thread-safe)
- Max 3 widoczne, FIFO kolejka
- Nad modalami (ostatni w z-order Grid)
- PL/EN lokalizacja (12 kluczy `Toast.*`)
- Zintegrowany z Install/Uninstall/Update/DLL/Updates

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

## Decyzje (podjęte przed implementacją)

- [x] Max liczba widocznych toastów: **3**
- [x] Auto-close: domyślnie **4s** (Success/Info), **6s** (Warning), **8s** (Error) – konfigurowalny per-toast
- [x] Animacja: slide-in z prawej + fade (oparte na istniejących `AnimationStyles.axaml`)
- [x] Kolejność: FIFO, najnowszy toast na górze stosu
- [x] Ręczne zamykanie: przycisk ✕ w prawym górnym rogu
- [x] Klikalny toast: opcjonalny `OnClick` callback
- [x] Lokalizacja klucze w `Toast.` namespace

## Plan implementacji

### Faza 1: Model i serwis

| Plik | Lokalizacja | Opis |
|------|-------------|------|
| `ToastNotification.cs` | `SUSModder/ViewModels/` | Model z ToastType, Title, Subtitle, AutoCloseMs, OnClick, Timestamp |
| `ToastService.cs` | `SUSModder/ViewModels/` | Singleton serwis: Enqueue, ActiveToasts, auto-close timery, max 3 widoczne |

### Faza 2: Widoki

| Plik | Lokalizacja | Opis |
|------|-------------|------|
| `ToastNotificationView.axaml` | `SUSModder/Views/` | Pojedynczy toast: ikona, tytuł, subtitle, ✕ button |
| `ToastNotificationView.axaml.cs` | `SUSModder/Views/` | Code-behind z obsługą zamknięcia + animacja slide-in (Avalonia Animation API) |
| `ToastHost.axaml` | `SUSModder/Views/` | ItemsControl + StackPanel jako kontener (czysty XAML, bez styli) |
| `ToastHost.axaml.cs` | `SUSModder/Views/` | Code-behind – pobiera ToastService z DI i ustawia jako DataContext |

### Faza 3: Integracja w MainWindow

- MainWindow.axaml: `<local:ToastHost Grid.Row="0" Grid.RowSpan="2"/>` – overlay nad status barem, pod modalami
- App.axaml.cs: `services.AddSingleton<ToastService>()`
- ToastHost.axaml.cs: `DataContext = App.GetService<ToastService>()`
- MainWindowViewModel.cs: `ToastService = App.GetService<ToastService>()`

### Faza 4: Lokalizacja

- pl.json: sekcja `Toast.{ModInstalled, ModUpdated, ModDeleted, UpdateAvailable, DownloadError, GameLaunchError, DllInstalled, DllRemoved, Info}`
- en.json: odpowiedniki EN

### Faza 5: Integracja z operacjami

- `ModOperations.cs` – po Install/Uninstall/Update
- `DllManagement.cs` – po InstallDll/RemoveDll
- `Updates.cs` – gdy znaleziono aktualizacje
- `GameLaunch.cs` – błędy uruchamiania
- `Initialization.cs` – CheckForModUpdatesAsync

### Architektura przepływu

```
┌──────────────────────────────────────────────────┐
│ ToastService (singleton, ObservableCollection)    │
│  - Enqueue(ToastNotification)                     │
│  - Dismiss(id)                                    │
│  - Auto-close: DispatcherTimer (UI thread-safe)   │
│  - Max 3 widoczne, FIFO kolejka                   │
│  - ShowSuccess/ShowWarning/ShowError/ShowInfo     │
└──────────────────────┬───────────────────────────┘
                       │ binding (ItemsSource)
┌──────────────────────▼───────────────────────────┐
│ ToastHost (UserControl, ItemsControl)             │
│  - StackPanel orientation=Vertical                │
│  - Bottom-right overlay, Margin="0,0,20,96"      │
│  - Binding do ToastService.ActiveToasts           │
│  - DataContext = App.GetService<ToastService>()   │
└──────────────────────┬───────────────────────────┘
                       │ DataTemplate
┌──────────────────────▼───────────────────────────┐
│ ToastNotificationView (UserControl)               │
│  - Ikona (emoji) + kolorowy pasek akcentu         │
│  - Title + Subtitle (Subtitle opcjonalny)         │
│  - ✕ przycisk do ręcznego zamknięcia              │
│  - Kliknięcie → OnClick (jeśli ustawiony)         │
│  - Slide-in animacja (OnAttachedToVisualTree)     │
│  - Fade-out przez Opacity binding → DispatcherTimer│
└──────────────────────────────────────────────────┘
```

### Uwagi z code review (zaimplementowane)

#### Poprawki krytyczne

1. **Opacity animation** (sus-senior-quality-reviewer): Usunięto style transitions z ToastHost, które kolidowały z lokalnym bindingiem `Opacity={Binding Opacity}` (style setter ma niższy priorytet niż local value w Avalonii). Zastąpiono czystą animacją w code-behind przez `Avalonia.Animation.Animation` API (wzór jak `LayoutAnimationBehavior`).

2. **Thread safety** (sus-senior-quality-reviewer): Zastąpiono `Task.Run` + `Task.Delay` + `Dispatcher.UIThread.InvokeAsync` przez `Avalonia.Threading.DispatcherTimer`. Dzięki temu cały auto-close callback wykonuje się na UI thread, co eliminuje race conditions przy modyfikacji `ObservableCollection<_activeToasts>`. Usunięto `ReaderWriterLockSlim` jako niepotrzebny – wszystkie operacje są teraz wykonywane na UI thread.

3. **Slide-in animation** (code-behind): Użyto bezpośrednio `Avalonia.Animation.Animation` z `TranslateTransform` i `CubicEaseOut`, wzorowane na istniejącym `LayoutAnimationBehavior.cs`.
