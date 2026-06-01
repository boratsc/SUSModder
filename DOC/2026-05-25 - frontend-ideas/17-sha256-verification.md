# 17 – SHA256 Verification (Weryfikacja integralności plików)

**Priorytet:** 🟢 P3 (infrastruktura bezpieczeństwa)  
**Effort:** ~3-5 dni (w całym projekcie)  
**Status:** 📄 **Pomysł** — specyfikacja gotowa do omówienia  
**Zależy od:** Decyzji architektonicznej — SHA256 powinno być sprawdzane wszędzie gdzie pobieramy pliki

---

## Cel

Dodać weryfikację SHA256 przy każdym pobieraniu plików przez SUSModder — nie tylko dla zewnętrznych DLL w mod packach, ale dla **wszystkich** plików ściąganych z sieci. Obecnie SHA256 jest liczone dla uploadu (do backendu), ale **nigdy nie weryfikowane przy pobieraniu**.

---

## Zakres — gdzie brakuje SHA256

| Miejsce | Pobiera | Ryzyko | Obecnie |
|---------|---------|--------|---------|
| `ModPackInstaller.InstallExternalDllAsync()` | Zewnętrzne DLL paczek | WYSOKIE — malware | ❌ Brak weryfikacji |
| `ModManager.ModifyAsync()` | Vanilla 7z, mody full zip | ŚREDNIE — podmiana archiwum | ❌ Brak |
| `DllModificationService` | DLL z katalogu API | NISKIE — dane z API | ❌ Brak |
| `EpicVersionManager` | legendary.exe | NISKIE — znane źródło | ❌ Brak |
| `ModUpdateChecker` | Aktualizacje modów | ŚREDNIE — podmiana paczki | ❌ Brak |
| Velopack updater | .nupkg | NISKIE — Velopack ma własny system | Velopack ma swój checksum |

---

## Problem

1. ModPackSharing: `ModPackInstaller.InstallExternalDllAsync()` pobiera DLL z URL i zapisuje do `BepInEx/plugins/` bez sprawdzenia, czy hash pobranego pliku zgadza się z `ext.Sha256` (który jest w modelu i na backendzie).
2. Inne miejsca podobnie: brak weryfikacji integralności pobranych plików.
3. Backend już liczy SHA256 i zwraca go w API — klient tylko nie sprawdza.

---

## Rozwiązanie

### Faza 1: Uniwersalna metoda `VerifySha256OrThrow`

Dodać do `SUSModder.Core/Utilities/Sha256Verifier.cs`:

```csharp
public static class Sha256Verifier
{
    /// <summary>
    /// Sprawdza czy SHA256 pliku zgadza się z oczekiwanym hashem.
    /// Rzuca Sha256MismatchException jeśli nie.
    /// </summary>
    public static async Task VerifyAsync(
        string filePath, string expectedHash,
        CancellationToken ct = default)

    /// <summary>
    /// Sprawdza SHA256 byte array vs expected hash.
    /// </summary>
    public static bool Matches(byte[] data, string expectedHash)

    /// <summary>
    /// Oblicza SHA256 z byte array.
    /// </summary>
    public static string ComputeHash(byte[] data)

    /// <summary>
    /// Oblicza SHA256 z pliku.
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(
        string filePath, CancellationToken ct = default)
}
```

### Faza 2: Weryfikacja w ModPackInstaller

W `InstallExternalDllAsync()`, po pobraniu bajtów DLL:

```csharp
var bytes = await response.Content.ReadAsByteArrayAsync();

// ✨ NOWE — weryfikacja SHA256
if (!string.IsNullOrEmpty(ext.Sha256) &&
    !Sha256Verifier.Matches(bytes, ext.Sha256))
{
    throw new InvalidDataException(
        $"SHA256 mismatch for external DLL {ext.FileName}. " +
        $"Expected {ext.Sha256}, got {Sha256Verifier.ComputeHash(bytes)}.");
}

var dest = Path.Combine(pluginsDir, ext.FileName);
await File.WriteAllBytesAsync(dest, bytes);
```

### Faza 3: Rozszerzenie na resztę projektu

Po wdrożeniu w ModPackSharing, dodać weryfikację SHA256 w:

1. `ModManager.ModifyAsync()` — dla vanilla 7z i modów zip (gdzie backend zwraca hash)
2. `DllModificationService` — dla DLL z katalogu (gdzie backend może zwracać hash)
3. `EpicVersionManager` — dla legendary.exe
4. `ModUpdateChecker` — dla aktualizacji modów

---

## Hash skąd?

| Źródło pliku | Skąd hash | Status |
|-------------|-----------|--------|
| External DLL z API | `ModPackExternalDll.Sha256` (zwracany przez API) | ✅ Już w modelu |
| Full mody | `ModConfiguration` — dodać pole `sha256` | ⬜ Nowe pole |
| Vanilla 7z | Backend API — dodać hash do response | ⬜ Nowe |
| DLL katalogowe | Backend API — dodać hash do katalogu | ⬜ Nowe |
| legendary.exe | GitHub releases — signed release | ⬜ Alternatywnie |

---

## Co NIE jest objęte

- **Velopack** — ma własny system weryfikacji (SHA256 w RELEASES, podpisany nuget package)
- **Config JSON / SQLite** — dane lokalne, nie pobierane z sieci
- **appsettings.json** — read-only, dostarczany z aplikacją
- **Lokalne kopie** — pliki już na dysku nie są weryfikowane (tylko przy pobieraniu)

---

## Ryzyka i uwagi

| Ryzyko | Mitygacja |
|--------|-----------|
| Performance — liczenie SHA256 dużych plików | Liczyć strumieniowo, async, cancelowalne |
| Brak hasha w API dla starszych modów | Fail-open: jeśli brak hasha → brak weryfikacji (log warning) |
| Podwójne buforowanie | Strumień → SHA256 → zapis (bez trzymania całości w RAM) |
| False positive (rzadkie) | Rzucić wyjątkiem z komunikatem, user może zgłosić |

---

## i18n

Jeden nowy klucz (angielski + polski):
- `Sha256Mismatch`: "Plik {fileName} jest uszkodzony lub został zmodyfikowany (SHA256 niezgodny). Pobierz ponownie."
- `Sha256Mismatch`: "File {fileName} is corrupted or has been modified (SHA256 mismatch). Download again."

---

## Zależności

- `System.Security.Cryptography` — wbudowane w .NET 8, brak nowych NuGetów
- Brak zmian w SQLite / user_settings / appsettings.json

---

## Implementation Order

```
Dzień 1: Sha256Verifier utility + testy jednostkowe
Dzień 2: ModPackInstaller — weryfikacja external DLL
Dzień 3-4: Rozszerzenie na ModManager, DllModificationService
Dzień 5: Dodanie pól SHA256 do API modeli + backend
```
