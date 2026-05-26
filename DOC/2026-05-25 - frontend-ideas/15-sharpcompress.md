# 15 – Zastąpienie 7z.exe przez SharpCompress (zarządzane 7z + zip)

**Priorytet:** 🟢 P2
**Effort:** ~2-3h

## Problem

Obecnie do rozpakowywania archiwów używamy dwóch różnych mechanizmów:

| Archiwum | Mechanizm | Progres? | Hasła? | Uwagi |
|----------|-----------|----------|--------|-------|
| Vanilla 7z (z hasłem) | `tools/7z.exe` jako proces | ❌ Brak | ✅ Tak | ~~575KB~~ + ~~1.9MB~~ DLL w paczce |
| Mod ZIP | `ZipFile.ExtractToDirectory` | ❌ Brak | ❌ Niepotrzebne | Czysty .NET, ale bez progresu |

Oba podejścia mają wady:
- **7z.exe**: zewnętrzny proces, brak progresu, brak ETA, ~2.5MB w deployed paczce, `Task.Run` + `WaitForExit`, brak async natywnie
- **ZipFile.ExtractToDirectory**: brak progresu, brak ETA, sync API

## Rozwiązanie

Zastąpić oba mechanizmy biblioteką **SharpCompress** (v0.48.1+) — czysto zarządzaną, async, z `IProgress<T>`.

### SharpCompress – dlaczego?

| Cecha | SharpCompress | 7z.exe | ZipFile |
|-------|--------------|--------|---------|
| Czysty .NET (net10.0) | ✅ | ❌ Proces | ✅ |
| Async/await | ✅ | ❌ Task.Run | ❌ Sync |
| Progres (`IProgress<T>`) | ✅ | ❌ | ❌ |
| Hasła (7z) | ✅ | ✅ | N/A |
| 7z + ZIP + RAR + ... | ✅ | ✅ (7z tylko) | ❌ Tylko ZIP |
| Bez zewn. plików | ✅ | ❌ 7z.exe + 7z.dll | ✅ |
| Cross‑platform | ✅ | ❌ Windows only | ✅ |
| Rozmiar w paczce | ~400KB DLL | ~~2.5MB~~ | wbudowany |

### Czyszczenie starych zależności

Po wdrożeniu usuwamy z projektu:
- `tools/7z.exe` (575KB)
- `tools/7z.dll` (1.9MB)
- Wpisy `<Content Include="tools\7z.exe">` i `tools\7z.dll` z `SUSModder.csproj`
- Cały kod `Extract7zWithPassword()` z `ModManager.cs`

## Plan implementacji

### Krok 1: Dodanie SharpCompress do projektu

```
dotnet add SUSModder.Core\SUSModder.Core.csproj package SharpCompress --version 0.48.1
```

### Krok 2: Nowa abstrakcja `IArchiveExtractor` w Core

```csharp
// SUSModder.Core/Utilities/IArchiveExtractor.cs
namespace SUSModder.Core.Utilities;

public interface IArchiveExtractor
{
    /// <summary>
    /// Ekstrakcja dowolnego archiwum (7z, zip, rar, itp.) z opcjonalnym hasłem i progresem.
    /// </summary>
    Task ExtractAsync(
        string archivePath,
        string extractPath,
        string? password = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct = default);
}

public record ExtractionProgress(
    long BytesExtracted,
    long TotalBytes,
    string? CurrentFile = null);
```

### Krok 3: Implementacja `SharpCompressExtractor`

```csharp
// SUSModder.Core/Utilities/SharpCompressExtractor.cs
using SharpCompress.Readers;
using SharpCompress.Common;

public class SharpCompressExtractor : IArchiveExtractor
{
    public async Task ExtractAsync(
        string archivePath,
        string extractPath,
        string? password = null,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveFileTime = true
        };

        var readerOptions = new ReaderOptions
        {
            Password = password,
            LeaveStreamOpen = false,
            LookForHeader = true
        };

        await using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.Open(stream, readerOptions);

        long totalBytes = stream.Length;
        long extractedBytes = 0;

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (!reader.Entry.IsDirectory)
            {
                var entry = reader.Entry;
                progress?.Report(new ExtractionProgress(
                    extractedBytes,
                    totalBytes,
                    entry.Key));

                reader.WriteEntryToDirectory(extractPath, options);

                extractedBytes += entry.Size;
                progress?.Report(new ExtractionProgress(
                    extractedBytes,
                    totalBytes,
                    entry.Key));
            }
            else
            {
                reader.WriteEntryToDirectory(extractPath, options);
            }
        }
    }
}
```

Uwaga: SharpCompress pozwala też na `ExtractToDirectoryAsync` z `IProgress<(long, long)>`, ale podejście z `ReaderFactory` daje nam więcej kontroli (nazwy plików, lepszy progres).

### Krok 4: Refaktor `ModManager.Extract7zWithPassword`

Zastąpić:
```csharp
// Stary kod
await Task.Run(() => Extract7zWithPassword(vanilla7zPath, modFolderPath, zipPassword));
```

Nowy kod:
```csharp
var extractor = new SharpCompressExtractor();
var progressReporter = new Progress<ExtractionProgress>(p =>
{
    if (p.TotalBytes > 0)
    {
        int pct = (int)(p.BytesExtracted * 100 / p.TotalBytes);
        int mappedProgress = 50 + (pct * 15 / 100); // 50-65% overall
        progress.Report(mappedProgress, 
            $"Rozpakowywanie: {pct}% ({FormatSize(p.BytesExtracted)}/{FormatSize(p.TotalBytes)})");
    }
});

await extractor.ExtractAsync(vanilla7zPath, modFolderPath, zipPassword, progressReporter);
```

#### ETA

Dodajemy do `InstallSteamAsync` (lub do wrappera) `Stopwatch` i kalkulację ETA:

```csharp
private string FormatEta(Stopwatch sw, double progressFraction)
{
    if (progressFraction < 0.01) return "...";
    var elapsed = sw.Elapsed;
    var total = TimeSpan.FromTicks((long)(elapsed.Ticks / progressFraction));
    var remaining = total - elapsed;
    return remaining.TotalHours >= 1
        ? $"{remaining.Hours}h {remaining.Minutes}m"
        : $"{remaining.Minutes}m {remaining.Seconds}s";
}
```

### Krok 5: Refaktor `ZipFile.ExtractToDirectory` → SharpCompress

Dwa miejsca:
- `ModManager.cs:266` — `ZipFile.ExtractToDirectory(modFile, ...)`
- `EpicVersionManager.cs:378` — `ZipFile.ExtractToDirectory(modFile, ...)`

Oba zastępujemy `extractor.ExtractAsync(...)` z `Progress<ExtractionProgress>`.

**Uwaga:** ZIP nie ma hasła (mod pliki), ale SharpCompress i tak działa — obsługuje ZIP natywnie.

### Krok 6: Usunięcie `tools/7z.exe` i `tools/7z.dll`

Po wdrożeniu i testach:
- Usunąć pliki z `SUSModder/tools/`
- Usunąć wpisy z `SUSModder.csproj`:
  ```xml
  <!-- DO USUNIĘCIA -->
  <Content Include="tools\7z.exe">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
  </Content>
  <Content Include="tools\7z.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
  </Content>
  ```
- Zaktualizować skrypty build (jeśli gdzieś kopiują tools/)

### Krok 7: Usunięcie starego kodu

```csharp
// Do usunięcia z ModManager.cs
private void Extract7zWithPassword(string archivePath, string extractPath, string password)
```

### Krok 8: Testy

- Instalacja moda Steam (7z vanilla + zip moda) — progres i ETA w UI
- Instalacja moda Epic (zip) — progres i ETA w UI
- Błędne hasło 7z — obsługa błędu
- Uszkodzone archiwum — obsługa błędu
- Build i brak ostrzeżeń

## Mapa zmian

| Plik | Zmiana |
|------|--------|
| `SUSModder.Core/SUSModder.Core.csproj` | +SharpCompress NuGet |
| `SUSModder.Core/Utilities/IArchiveExtractor.cs` | **NOWY** — interfejs |
| `SUSModder.Core/Utilities/SharpCompressExtractor.cs` | **NOWY** — implementacja |
| `SUSModder.Core/GameIntegration/ModManager.cs` | Refaktor `Extract7zWithPassword` → `IArchiveExtractor`, refaktor `ZipFile.ExtractToDirectory` |
| `SUSModder.Core/GameIntegration/EpicVersionManager.cs` | Refaktor `ZipFile.ExtractToDirectory` |
| `SUSModder/SUSModder.csproj` | Usunięcie `<Content Include="tools\7z.exe">` i `7z.dll` |
| `SUSModder/tools/7z.exe` | **Usunięty** |
| `SUSModder/tools/7z.dll` | **Usunięty** |

## Efekt w UI

Zamiast:
```
Instalowanie: Rozpakowywanie gry vanilla...
████████░░░░ 60%
```

Dostajemy:
```
Instalowanie: Rozpakowywanie: 45% (210MB/467MB) ETA: 30s
████████░░░░ 57%
```

Po każdym pliku aktualizuje się też `CurrentFile` — możemy pokazać nazwę pliku.

## Ryzyka

| Ryzyko | Prawdopodobieństwo | Mitigacja |
|--------|-------------------|-----------|
| SharpCompress nie wspiera hasła 7z LZMA2 | Bardzo niskie | Testy integracyjne z rzeczywistym plikiem vanilla |
| Wydajność gorsza niż 7z.exe (C++ native) | Średnie | SharpCompress używa managed LZMA, benchmarki pokazują ~80-90% prędkości native 7z. Dla ~500MB vanilla różnica to ~2-3s |
| Regresja przy uszkodzonych archiwach | Niskie | SharpCompress rzuca wyjątki, łapiemy je tak jak obecnie |
| `tools/` używane przez skrypty build | Średnie | Sprawdzić wszystkie skrypty w `SKRYPTY/` |

## Effort: ~2-3h

- SharpCompress integracja + `IArchiveExtractor`: ~30min
- `SharpCompressExtractor` implementacja: ~30min
- Refaktor `ModManager`: ~30min
- Refaktor `EpicVersionManager`: ~15min
- Usunięcie `tools/7z.exe` + csproj clean: ~15min
- Testy: ~30min
