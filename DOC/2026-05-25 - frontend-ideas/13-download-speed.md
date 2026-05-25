# 13 – Prędkość pobierania w progress barze

**Priorytet:** 🟢 P2
**Effort:** ~1-2h

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

## Gdzie w kodzie

| Co | Plik |
|----|------|
| Nowe pole | `SUSModder/ViewModels/ModItem.cs` – `DownloadSpeed` |
| UI progress bar | `SUSModder/Views/MainWindow.axaml:233` |
| Epic speed parsing | `SUSModder.Core/GameIntegration/LegendaryProgressParser.cs:60` |
| Steam/mod download | `SUSModder.Core/GameIntegration/ModManager.cs` – kod pobierania |

## Efekt w UI

```
┌─────────────────────────────────┐
│ Pobieranie Town of Us...        │ ← InstallStatusMessage
│ ████████████░░░░░░░ 67%         │ ← ProgressBar + %
│ 12.5 MB/s                       │ ← NOWE: DownloadSpeed
└─────────────────────────────────┘
```

**Effort:** ~1-2h – pole, UI, obliczanie w 2 miejscach (Steam/mod + przepięcie Epic).
