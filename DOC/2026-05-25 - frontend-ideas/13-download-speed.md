# 13 – Prędkość pobierania w progress barze

**Priorytet:** 🟢 P2
**Effort:** ~1-2h

> **Status:** Zaimplementowano 2026-05-26. Szczegóły implementacji poniżej.
> 
> **Plany implementacyjne (sekcja niżej) – aktualne na dzień implementacji.**

## Problem

Obecny progress bar pokazuje tylko procent (`InstallProgress`) i status (`InstallStatusMessage`). Brak informacji o aktualnej prędkości pobierania.

- **Epic (legendary):** prędkość już jest parsowana z outputu (regex w `LegendaryProgressParser.cs:60`) – `DownloadSpeed = "{value} {unit}/s"`
- **Steam / mody:** brak – tylko `InstallProgress` 0-100

## Rozwiązanie

Dodać `DownloadSpeed` do `ModItem` i pokazać w UI pod progress barem.

### Krok 1: Nowe pole w `ModItem.cs`

```csharp
private string? _downloadSpeed;
public string? DownloadSpeed
{
    get => _downloadSpeed;
    set => this.RaiseAndSetIfChanged(ref _downloadSpeed, value);
}
```

### Krok 2: UI – dodać wiersz z prędkością

W `MainWindow.axaml`, pod istniejącym `{0}%` (linia 233):

```xml
<TextBlock Text="{Binding DownloadSpeed}"
           FontSize="11"
           Foreground="{DynamicResource TextSecondaryBrush}"
           TextAlignment="Center"
           IsVisible="{Binding DownloadSpeed, Converter={StaticResource StringNotNullOrEmptyToBoolConverter}}"/>
```

### Krok 3: Obliczanie prędkości w kodzie pobierania

W `ModManager` (lub gdziekolwiek jest `HttpClient.DownloadAsync`):

```csharp
private string CalculateSpeed(long bytesReceived, Stopwatch stopwatch)
{
    if (stopwatch.Elapsed.TotalSeconds < 0.5) return null;
    double speed = bytesReceived / stopwatch.Elapsed.TotalSeconds;
    if (speed > 1_000_000)
        return $"{speed / 1_000_000:F1} MB/s";
    if (speed > 1_000)
        return $"{speed / 1_000:F0} KB/s";
    return $"{speed:F0} B/s";
}
```

### Krok 4: Epic – już działa

W `EpicVersionManager` / legendary flow, `DownloadSpeed` już jest parsowane – wystarczy przepiąć na `ModItem.DownloadSpeed` w callbacku progressu.

## Plan implementacyjny (z 2026-05-26)

### Architektura przepływu prędkości

```
Steam/mod:  ModManager.DownloadFileWithMemoryManagementAsync()
              ↓ (onSpeedUpdate callback)
            InstallSteamAsync() → ModifyAsync()
              ↓
            ViewModel (UIProgressReporter lambda + onSpeedUpdate)
              ↓
            ModItem.DownloadSpeed = "12.5 MB/s"

Epic:       EpicVersionManager.ParseLegendaryProgress()
              ↓ (SpeedChanged event)
            ViewModel event handler
              ↓
            ModItem.DownloadSpeed = "12.5 MB/s"
```

### Zmiany w plikach

| # | Plik | Zmiana |
|---|------|--------|
| 1 | `SUSModder/ViewModels/ModItem.cs` | Dodanie właściwości `DownloadSpeed` (reactive) |
| 2 | `SUSModder/Views/MainWindow.axaml:233` | Dodanie TextBlock `{Binding DownloadSpeed}` pod % |
| 3 | `SUSModder.Core/GameIntegration/ModManager.cs` | Dodanie `CalculateDownloadSpeed()`, optional `onSpeedUpdate` callback przez łańcuch wywołań |
| 4 | `SUSModder.Core/GameIntegration/EpicVersionManager.cs` | Dodanie eventu `SpeedChanged`, odpalanie przy parsowaniu `_lastDownloadSpeed` |
| 5 | `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs` | Steam: przekazanie speed callback; Epic: subskrypcja `SpeedChanged` |
| 6 | `SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs` | Subskrypcja `SpeedChanged` przy uruchamianiu Epic |
| 7 | `SUSModder/ViewModels/MainWindowViewModel.Updates.cs` | Subskrypcja speed w ścieżce aktualizacji (Epic + Steam) |

### Decyzje projektowe

1. **`IProgressReporter` nie zmieniamy** – dodajemy osobny `onSpeedUpdate` callback, aby nie łamać istniejącego kontraktu.
2. **`ModifyDllAsync` pomijamy** – pobieranie DLL nie ma progressu strumieniowego (używa `CopyToAsync`), co wymagałoby refaktoringu wykraczającego poza scope POC.
3. **Epic: `SpeedChanged` osobny event** – zamiast parsowania prędkości z message stringa w ViewModelu (fragile).
4. **Speed callback to `Action<string>?`** – optional, `null` domyślnie = brak change.

## Gdzie w kodzie (zaktualizowane)

| Co | Plik |
|----|------|
| Nowe pole `DownloadSpeed` | `SUSModder/ViewModels/ModItem.cs` |
| UI dla prędkości | `SUSModder/Views/MainWindow.axaml` (pod linią 233) |
| Obliczanie prędkości (Steam/mod) | `SUSModder.Core/GameIntegration/ModManager.cs` – `CalculateDownloadSpeed()` + `DownloadFileWithMemoryManagementAsync` |
| Event prędkości (Epic) | `SUSModder.Core/GameIntegration/EpicVersionManager.cs` – `SpeedChanged` event |
| Podłączenie ViewModel (Steam) | `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs` – `InstallSteamModAsync` |
| Podłączenie ViewModel (Epic install) | `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs` – `InstallEpicModAsync` |
| Podłączenie ViewModel (Epic launch) | `SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs` – `LaunchEpicGameAsync` |
| Podłączenie ViewModel (update) | `SUSModder/ViewModels/MainWindowViewModel.Updates.cs` – `UpdateSingleModWithDialogAsync` |

## Efekt w UI

```
┌─────────────────────────────────┐
│ Pobieranie Town of Us...        │ ← InstallStatusMessage
│ ████████████░░░░░░░ 67%         │ ← ProgressBar + %
│ 12.5 MB/s                       │ ← NOWE: DownloadSpeed
└─────────────────────────────────┘
```

**Effort:** ~1-2h – pole, UI, obliczanie w 2 miejscach (Steam/mod + przepięcie Epic).
