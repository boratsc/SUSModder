# Updater – Dokumentacja

## Spis treści
1. [O aplikacji](#o-aplikacji)
2. [Architektura](#architektura)
3. [Proces aktualizacji](#proces-aktualizacji)
4. [Szczegóły implementacji](#szczegóły-implementacji)
5. [Konfiguracja projektu](#konfiguracja-projektu)
6. [Wywołanie](#wywołanie)
7. [Obsługa błędów](#obsługa-błędów)

---

## O aplikacji

**Updater** to mała aplikacja konsolowa (.NET 8) odpowiedzialna za **automatyczną aktualizację SUSModder**.

### Kluczowe cechy:
- **Minimalistyczna** – jeden plik `Program.cs` (~200 linii)
- **Self-contained** – publikowana jako single-file executable (win-x64)
- **Bezpieczna** – czeka na zamknięcie aplikacji przed aktualizacją
- **Inteligentna** – usuwa stare pliki, które nie istnieją w nowej wersji
- **Transparentna** – loguje wszystkie operacje do konsoli

### Odpowiedzialności:
1. Czekanie na zamknięcie SUSModder.exe
2. Rozpakowanie archiwum ZIP z nową wersją
3. Sprzątanie starych plików
4. Kopiowanie nowych plików
5. Uruchomienie zaktualizowanej aplikacji

---

## Architektura

### Struktura projektu

```
Updater/
├── Program.cs              # Cała logika aktualizacji (~200 linii)
├── Updater.csproj          # Konfiguracja projektu (.NET 8)
└── bin/Release/            # Output publikacji
    └── Updater.exe         # Single-file executable
```

### Technologia
- **.NET 8.0** – platforma runtime
- **System.IO.Compression** – rozpakowanie archiwum ZIP
- **System.Diagnostics.Process** – monitorowanie procesów i uruchomienie aplikacji

### Deployment
- **PublishDir:** `../publish/updater/`
- **PublishSingleFile:** `true` (jeden plik .exe)
- **SelfContained:** `true` (nie wymaga .NET Runtime)
- **PublishTrimmed:** `true` (zminimalizowany rozmiar)

---

## Proces aktualizacji

### Przepływ (szczegółowy)

```
┌─────────────────────────────────────────────────────────┐
│ 1. SUSModder wykrywa nową wersję (AppUpdateService)    │
│    - Porównanie CurrentVersion z API                   │
│    - Pokazanie AppUpdateDialog użytkownikowi           │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 2. SUSModder pobiera archiwum ZIP do %TEMP%            │
│    - URL: {UpdateServerUrl}/latest/{file}              │
│    - Progress reporting (DownloadProgress 0-100)       │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 3. SUSModder zapisuje kopię ustawień użytkownika       │
│    - Mode (steam/epic), Theme, lastLaunchId            │
│    - ModsInstallPath                                    │
│    - Backup do %TEMP%/susmodder_settings_backup.json   │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 4. SUSModder uruchamia Updater.exe                     │
│    - Argumenty: <target-dir> <zip-file-path>           │
│    - Przykład: Updater.exe "C:\...\publish"            │
│                "C:\Users\...\Temp\update.zip"           │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 5. SUSModder zamyka się (Application.Shutdown())       │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 6. Updater czeka na zamknięcie SUSModder.exe           │
│    - Process.GetProcessesByName("SUSModder")           │
│    - proc.WaitForExit()                                 │
│    - Sleep(1000ms) dla bezpieczeństwa                   │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 7. Updater rozpakowuje ZIP do %TEMP%                   │
│    - Katalog: %TEMP%/LatestVersionExtract              │
│    - Pomija: config.json, updater/                     │
│    - Ekstrahuje tylko zawartość "SUSModder/" z ZIP     │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 8. Updater SPRZATA stare pliki                         │
│    - Usuwa pliki, których nie ma w nowej wersji        │
│    - Pomija: config.json, updater/                     │
│    - Usuwa puste katalogi                               │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 9. Updater kopiuje nowe pliki                          │
│    - Kopiuje wszystko z %TEMP% do target-dir           │
│    - Nadpisuje istniejące pliki                         │
│    - Tworzy brakujące katalogi                          │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 10. Updater uruchamia SUSModder.exe                    │
│     - Process.Start(SUSModder.exe)                      │
│     - WorkingDirectory = target-dir                     │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 11. Updater usuwa pliki tymczasowe                     │
│     - Delete(%TEMP%/LatestVersionExtract)               │
│     - Delete(%TEMP%/update.zip)                         │
└──────────────────────┬──────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 12. Updater kończy pracę                               │
└─────────────────────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────┐
│ 13. SUSModder startuje (nowa wersja!)                  │
│     - Program.Main() przywraca ustawienia użytkownika  │
│     - AppUpdateService.RestoreUserSettingsIfNeeded()   │
└─────────────────────────────────────────────────────────┘
```

---

## Szczegóły implementacji

### 1. Argumenty wiersza poleceń

```csharp
static void Main(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: Updater <target-dir> <zip-file-path>");
        return;
    }

    string targetDir = args[0];      // Katalog instalacji SUSModder (np. C:\...\publish)
    string tempFilePath = args[1];   // Ścieżka do pobranego archiwum ZIP
    // ...
}
```

**Przykład wywołania:**
```bash
Updater.exe "D:\Apps\SUSModder" "C:\Users\Jan\AppData\Local\Temp\susmodder_update_1.2.3.zip"
```

---

### 2. Czekanie na zamknięcie SUSModder.exe

```csharp
string exeName = "SUSModder.exe";
bool found = false;

foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)))
{
    try
    {
        if (proc.MainModule?.FileName?.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase) == true)
        {
            found = true;
            Console.WriteLine("Czekam na zamknięcie SUSModder.exe...");
            proc.WaitForExit(); // BLOKUJE do zamknięcia procesu
        }
    }
    catch { /* ignoruj procesy systemowe bez dostępu */ }
}

if (found)
{
    System.Threading.Thread.Sleep(1000); // Daj systemowi czas na zwolnienie plików
}
```

**Dlaczego `StartsWith(targetDir)`?**
- Może być wiele procesów "SUSModder.exe" (różne kopie aplikacji)
- Czekamy tylko na ten z właściwego katalogu

---

### 3. Rozpakowanie archiwum ZIP

```csharp
string tempExtractPath = Path.Combine(Path.GetTempPath(), "LatestVersionExtract");

// Wyczyść stary katalog tymczasowy (jeśli istnieje)
if (Directory.Exists(tempExtractPath))
{
    Directory.Delete(tempExtractPath, true);
}
Directory.CreateDirectory(tempExtractPath);

using (ZipArchive archive = ZipFile.OpenRead(tempFilePath))
{
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        // Pomijanie config.json i updatera
        if (entry.FullName.EndsWith("config.json") || 
            entry.FullName.StartsWith("SUSModder/updater/"))
        {
            continue; // NIE nadpisuj config.json i Updater.exe
        }

        if (entry.FullName.StartsWith("SUSModder/"))
        {
            // Usuń prefix "SUSModder/" ze ścieżki
            string relativePath = entry.FullName.Substring("SUSModder/".Length);
            string destinationPath = Path.Combine(tempExtractPath, relativePath);

            if (entry.Name == "")
            {
                // To jest katalog
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                // To jest plik
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
    }
}
```

**Kluczowe decyzje:**
- **`config.json` NIE jest nadpisywany** – zachowuje lokalne konfiguracje użytkownika
- **`updater/` NIE jest nadpisywany** – Updater nie może zastąpić samego siebie podczas działania
- **Filtrowanie `SUSModder/`** – archiwum ZIP ma strukturę `SUSModder/SUSModder.exe`, ale instalujemy bezpośrednio do `targetDir`

---

### 4. Sprzątanie starych plików ⚠️

```csharp
// Zbierz listę plików w nowej wersji
var newFiles = new HashSet<string>(
    Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(tempExtractPath, f).Replace('\\', '/'))
);

// Usuń pliki, które nie istnieją w nowej wersji
foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
{
    string relPath = Path.GetRelativePath(targetDir, file).Replace('\\', '/');

    // Wyjątki: NIE usuwaj
    if (relPath.Equals("config.json", StringComparison.OrdinalIgnoreCase))
        continue;
    if (relPath.StartsWith("updater/", StringComparison.OrdinalIgnoreCase))
        continue;
    if (newFiles.Contains(relPath))
        continue; // Plik istnieje w nowej wersji

    try
    {
        Console.WriteLine($"Usuwam niepotrzebny plik: {file}");
        File.Delete(file);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Nie udało się usunąć pliku {file}: {ex.Message}");
    }
}

// Usuń puste katalogi
foreach (var dir in Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length)) // Od najgłębszych
{
    string relPath = Path.GetRelativePath(targetDir, dir).Replace('\\', '/');
    if (relPath.StartsWith("updater/", StringComparison.OrdinalIgnoreCase))
        continue;

    if (!Directory.EnumerateFileSystemEntries(dir).Any())
    {
        try
        {
            Console.WriteLine($"Usuwam pusty katalog: {dir}");
            Directory.Delete(dir);
        }
        catch { }
    }
}
```

**Dlaczego to jest ważne?**
- Bez tego mogłyby pozostać **stare pliki** z poprzedniej wersji (np. przestarzałe DLL, usunięte features)
- Utrzymanie **czystego stanu aplikacji**

**Wyjątki:**
- `config.json` – ustawienia użytkownika
- `updater/` – nie usuwaj folderu Updatera

---

### 5. Kopiowanie nowych plików

```csharp
foreach (string file in Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories))
{
    string relativePath = Path.GetRelativePath(tempExtractPath, file);
    string destFile = Path.Combine(targetDir, relativePath);
    
    Console.WriteLine($"Kopiowanie {file} do {destFile}");

    // Utwórz katalog docelowy, jeśli nie istnieje
    string destDir = Path.GetDirectoryName(destFile) ?? string.Empty;
    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
    {
        Directory.CreateDirectory(destDir);
    }
    
    File.Copy(file, destFile, overwrite: true); // NADPISZ istniejące pliki
}
```

**Efekt:**
- Wszystkie pliki z nowej wersji są kopiowane do `targetDir`
- Istniejące pliki są nadpisywane

---

### 6. Uruchomienie zaktualizowanej aplikacji

```csharp
string appExePath = Path.Combine(targetDir, "SUSModder.exe");

if (File.Exists(appExePath))
{
    Console.WriteLine($"Próba uruchomienia aplikacji: {appExePath}");
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = appExePath,
            UseShellExecute = true,        // Uruchom jako osobny proces
            WorkingDirectory = targetDir   // Ustaw katalog roboczy
        });
        Console.WriteLine("Aplikacja została uruchomiona pomyślnie.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Błąd podczas uruchamiania aplikacji: {ex.Message}");
    }
}
else
{
    Console.WriteLine("Plik wykonywalny nie istnieje: " + appExePath);
}
```

**`UseShellExecute = true`:**
- Uruchamia aplikację jako osobny proces (nie blokuje Updater)
- Pozwala na uruchomienie .exe bez czekania na zakończenie

---

### 7. Czyszczenie plików tymczasowych

```csharp
Console.WriteLine("Usuwanie tymczasowych plików...");
Directory.Delete(tempExtractPath, recursive: true); // Usuń katalog z rozpakowanymi plikami
File.Delete(tempFilePath);                          // Usuń pobrany archiwum ZIP
```

**Efekt:**
- Brak śmieci w %TEMP%
- Oszczędność miejsca na dysku

---

## Konfiguracja projektu

### Updater.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net8.0</TargetFramework>
        <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
        <PublishDir>..\publish\updater\</PublishDir>
    </PropertyGroup>

    <!-- Konfiguracja dla Release/Publish -->
    <PropertyGroup Condition="'$(Configuration)' == 'Release'">
        <SelfContained>true</SelfContained>              <!-- Zawiera runtime .NET -->
        <PublishSingleFile>true</PublishSingleFile>      <!-- Jeden plik .exe -->
        <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
        <PublishTrimmed>true</PublishTrimmed>            <!-- Usuń nieużywany kod -->
        <DebugType>none</DebugType>                      <!-- Brak PDB -->
        <DebugSymbols>false</DebugSymbols>
    </PropertyGroup>
</Project>
```

### Publikacja

```bash
cd d:\repos\SUSModder\Updater
dotnet publish -c Release
```

**Output:**
- Plik: `..\publish\updater\Updater.exe` (~8 MB self-contained)
- Zależności: wbudowane (brak potrzeby .NET Runtime)

---

## Wywołanie

### Z poziomu SUSModder (AppUpdateService)

```csharp
// W SUSModder.Core/Services/AppUpdateService.cs
public static void LaunchUpdater(string updateZipPath, string appDirectory)
{
    string updaterPath = Path.Combine(appDirectory, "updater", "Updater.exe");

    if (!File.Exists(updaterPath))
    {
        throw new FileNotFoundException("Updater.exe nie został znaleziony", updaterPath);
    }

    var processStartInfo = new ProcessStartInfo
    {
        FileName = updaterPath,
        Arguments = $"\"{appDirectory}\" \"{updateZipPath}\"",
        UseShellExecute = true,
        CreateNoWindow = false, // Pokaż okno konsoli
        WorkingDirectory = Path.GetDirectoryName(updaterPath)
    };

    Process.Start(processStartInfo);
}
```

**Przykład argumentów:**
```
"D:\Apps\SUSModder" "C:\Users\Jan\AppData\Local\Temp\susmodder_update_1.2.3.zip"
```

---

## Obsługa błędów

### Błędy obsługiwane przez Updater:

| Błąd | Obsługa |
|------|---------|
| Brak argumentów | Wyświetlenie komunikatu `Usage: ...` i wyjście |
| Proces SUSModder nie może być znaleziony | Ignorowanie (catch w pętli `foreach`) |
| Błąd rozpakowywania ZIP | Przechwycenie `Exception`, wyświetlenie komunikatu, czekanie na Enter |
| Błąd usuwania pliku/katalogu | Logowanie błędu, kontynuacja (nie przerywa procesu) |
| Błąd kopiowania pliku | Wyjątek propaguje się do głównego `try-catch` |
| Błąd uruchomienia SUSModder.exe | Logowanie błędu, ale Updater kończy pracę (użytkownik może uruchomić manualnie) |

### Główny try-catch

```csharp
try
{
    // Cała logika aktualizacji
}
catch (Exception ex)
{
    Console.WriteLine($"Uaktualnienie nie powiodło się: {ex.Message}");
    Console.WriteLine("Naciśnij Enter, aby zakończyć.");
    Console.ReadLine(); // Czekaj na potwierdzenie użytkownika
}
```

**Efekt:**
- W razie błędu Updater **nie zamyka się automatycznie** – użytkownik może zobaczyć komunikat błędu
- Backup ustawień użytkownika (w SUSModder) pozwala na przywrócenie stanu przy ponownym uruchomieniu

---

## Ograniczenia i znane problemy

### 1. Updater nie może zaktualizować samego siebie

**Problem:**
- Updater.exe nie może zastąpić samego siebie podczas działania (Windows blokuje plik wykonywalny)

**Rozwiązanie:**
- Aktualizacje Updatera muszą być pomijane (`entry.FullName.StartsWith("SUSModder/updater/")`)
- Updater jest aktualizowany w **następnej** aktualizacji (dwuetapowy proces)

**Alternatywne rozwiązanie (nie zaimplementowane):**
- Updater mógłby skopiować samego siebie do `Updater_new.exe`, uruchomić go, a nowy Updater nadpisałby stary

---

### 2. Brak rollbacku w razie błędu

**Problem:**
- Jeśli aktualizacja się nie powiedzie w połowie (np. błąd kopiowania), aplikacja może być w niespójnym stanie

**Możliwe rozwiązanie:**
- Backup całego folderu przed aktualizacją
- Rollback w razie błędu

**Obecne podejście:**
- Błędy są logowane, ale proces nie jest cofany
- Użytkownik może ponownie pobrać aplikację z serwera

---

### 3. Wymaga uprawnień zapisu do targetDir

**Problem:**
- Jeśli targetDir jest w chronionym katalogu (np. `C:\Program Files`), Updater wymaga uprawnień administratora

**Rozwiązanie:**
- SUSModder jest instalowany w folderze użytkownika (np. `%APPDATA%` lub pobrane przez użytkownika)
- Brak potrzeby uprawnień admin

---

## Bezpieczeństwo

### 1. Weryfikacja źródła archiwum ZIP

**Obecny stan:**
- Brak weryfikacji podpisu/hashu archiwum
- SUSModder pobiera z `UpdateServerUrl` z `appsettings.json`

**Rekomendacja:**
- Dodanie weryfikacji SHA256 hash archiwum przed rozpakowanie
- HTTPS dla UpdateServerUrl (już zaimplementowane: `https://susmodder.boracik.pl`)

---

### 2. Uprawnienia plików

**Obecny stan:**
- Updater dziedziczy uprawnienia z procesu SUSModder
- Brak eskalacji uprawnień

**Bezpieczeństwo:**
- Dobra praktyka – brak potrzeby UAC prompt

---

## Statystyki

| Metryka | Wartość |
|---------|---------|
| **Plików źródłowych** | 1 (`Program.cs`) |
| **Linii kodu** | ~200 |
| **Rozmiar .exe (self-contained)** | ~8 MB |
| **Zależności** | System.IO.Compression, System.Diagnostics.Process |
| **Testowane platformy** | Windows 10/11 (win-x64) |

---

## Best practices

### ✅ DO:
- Czekaj na zamknięcie aplikacji przed aktualizacją (`Process.WaitForExit()`)
- Sprzątaj stare pliki, które nie istnieją w nowej wersji
- Loguj wszystkie operacje do konsoli (transparentność)
- Obsługuj błędy gracefully (nie kończ pracy bez komunikatu)
- Używaj `UseShellExecute = true` dla uruchomienia aplikacji

### ❌ NIE:
- Nie nadpisuj `config.json` (ustawienia użytkownika!)
- Nie próbuj aktualizować samego siebie (Windows zablokuje plik)
- Nie zakładaj, że użytkownik ma uprawnienia administratora
- Nie usuwaj plików bez sprawdzenia wyjątków (`updater/`, `config.json`)

---

## Przykładowy log działania

```
Czekam na zamknięcie SUSModder.exe...
Rozpakowywanie archiwum ZIP...
Processing entry: SUSModder/SUSModder.exe
Rozpakowywanie pliku: C:\Users\...\Temp\LatestVersionExtract\SUSModder.exe
Processing entry: SUSModder/appsettings.json
Rozpakowywanie pliku: C:\Users\...\Temp\LatestVersionExtract\appsettings.json
Processing entry: SUSModder/tools/7z.exe
Rozpakowywanie pliku: C:\Users\...\Temp\LatestVersionExtract\tools\7z.exe
...
Usuwam niepotrzebny plik: D:\Apps\SUSModder\OldFile.dll
Usuwam pusty katalog: D:\Apps\SUSModder\OldFolder
Instalowanie nowej wersji...
Kopiowanie C:\Users\...\Temp\LatestVersionExtract\SUSModder.exe do D:\Apps\SUSModder\SUSModder.exe
Kopiowanie C:\Users\...\Temp\LatestVersionExtract\appsettings.json do D:\Apps\SUSModder\appsettings.json
...
Próba uruchomienia aplikacji: D:\Apps\SUSModder\SUSModder.exe
Aplikacja została uruchomiona pomyślnie.
Usuwanie tymczasowych plików...
```

---

## Przyszłe ulepszenia (TODO)

- [ ] Weryfikacja SHA256 hash archiwum ZIP przed rozpakowanie
- [ ] Rollback w razie błędu aktualizacji (backup folderu)
- [ ] Możliwość aktualizacji samego siebie (dwuetapowy proces)
- [ ] Progress bar dla kopiowania plików
- [ ] GUI zamiast okna konsoli (opcjonalnie)
- [ ] Unit testy dla logiki sprzątania plików
- [ ] Logowanie do pliku zamiast tylko konsoli

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Status:** Kompletna dokumentacja Updater
