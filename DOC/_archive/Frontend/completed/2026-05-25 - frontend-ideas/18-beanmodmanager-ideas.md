# 18 – Mechanizmy z BeanModManager do adaptacji w SUSModder

**Priorytet:** 🟡 P1 (wybrane elementy P0)  
**Effort:** ~6-10 dni (suma 4 feature'ów)  
**Status:** 📋 **Burza mózgów** — przeanalizowane źródła konkurencyjnego mod managera, wybrane mechanizmy do adaptacji  
**Źródło analizy:** `D:\Development\Źródła\BeanModManager-master` — WinForms (.NET Framework), ~30 modów, GitHub-based distribution

---

## Cel

Przeanalizowano kod źródłowy **BeanModManager** — konkurencyjnego mod managera do Among Us — pod kątem mechanizmów, które można zaadaptować w SUSModder. Poniżej lista feature'ów **potwierdzonych jako wartościowe** po odrzuceniu tych, które SUSModder już ma lub które działają inaczej.

---

## Kontekst: co BeanModManager robi inaczej

| Obszar | BeanModManager | SUSModder | Werdykt |
|--------|---------------|-----------|---------|
| Instalacja modów full | Storage w `{AU}/Mods/{id}/`, copy-on-launch | Pełna kopia Among Us per mod | ✅ Obecny model SUSModder OK |
| Asset filters per platforma | JSON registry z regex patterns | Backend + API ogarnia warianty | ✅ Już rozwiązane |
| Dependency / incompatibility | `versionDependencies`, `incompatibilities` w registry | Compatibility matrix wdrożony | ✅ Już rozwiązane |
| Źródło modów | GitHub Releases API bezpośrednio | Własne API + CDN | ✅ Obecny model lepszy |
| Depot download | Steam console commands | 7z/DepotDownloader | ✅ Obecny model lepszy |

---

## ✅ Mechanizmy do adaptacji (potwierdzone)

### A. Bulk Operations + Kolejkowanie — 🔴 P0

**Co BeanModManager robi dobrze:**
- Multi-select modów (HashSet-based tracking)
- Przyciski "Install Selected", "Uninstall Selected", "Update Selected"
- `lock(_installLock)` dla thread safety przy operacjach masowych

**Czego brakuje w SUSModder:**
- Każda operacja (install/uninstall/update) jest pojedyncza
- Użytkownik klika N razy dla N modów

**Zakres implementacji:**
1. **UI:** Checkboxy per `ModItem` w widoku grid + "Zainstaluj zaznaczone" / "Odinstaluj zaznaczone" / "Aktualizuj zaznaczone"
2. **ViewModel:** `ObservableCollection<string> SelectedModIds`, komendy `InstallSelectedCommand`, `UninstallSelectedCommand`, `UpdateSelectedCommand`
3. **Core:** `ModInstallQueue` — sekwencyjna kolejka operacji z progress per-item, obsługa błędów per-item (nie przerywaj całej kolejki przy jednym failu)
4. **UI:** Progress dla całej kolejki (np. "3/5 modów zainstalowanych") + per-item w `ModItem`

**Zależności:** brak zewnętrznych

---

### B. Walidacja ZIP przed rozpakowaniem — 🟡 P1

**Co BeanModManager robi dobrze:**
```csharp
private bool ValidateZipFile(string zipPath) {
    using (var archive = ZipFile.OpenRead(zipPath)) {
        return archive.Entries.Count > 0;
    }
}
```
- Przed instalacją: otwiera ZIP, sprawdza czy ma pliki, czy nie jest uszkodzony
- Przy błędzie: czyści temp plik, pokazuje komunikat

**Czego brakuje w SUSModder:**
- Brak walidacji integralności pobranych plików (SHA256 w planach jako #17, ale ZIP validation to prostszy, szybszy krok)

**Zakres implementacji:**
1. `SUSModder.Core/Utilities/ZipValidator.cs`:
   - `ValidateAsync(string path)` — otwiera ZIP, sprawdza `Entries.Count > 0`
   - `ValidateOrThrow(string path)` — rzuca wyjątek z nazwą pliku przy failu
2. Wywołanie w `ModManager.ModifyAsync()` i `ModDownloader` przed rozpakowaniem
3. Automatyczne czyszczenie temp plików przy błędzie

**Zależności:** brak (używamy `System.IO.Compression.ZipFile` lub SharpCompress który już mamy)

**Uwaga:** Ten feature jest już na liście TODO przed v3.0 (wspomniane w #17-sha256-verification.md jako krok pośredni). SHA256 verification (#17) to osobny, szerszy feature.

---

### C. Import modów z lokalnych plików — 🟡 P1

**Co BeanModManager robi dobrze:**
- `ModImporter.cs` — import z lokalnego `.zip`, `.dll`, lub folderu
- Automatyczne wykrywanie struktury: szuka `BepInEx/`, obsługuje nested directories
- Walidacja ZIP przed importem

**Czego brakuje w SUSModder:**
- Nie można zainstalować moda spoza oficjalnego katalogu API
- Użytkownicy nie mogą testować niestandardowych buildów ani modów z zamkniętych beta-testów

**Zakres implementacji:**
1. **UI:** Przycisk "Importuj mod" w toolbarze / menu, dialog wyboru pliku (`.zip`, `.dll`)
2. **Core:** `ModImportService`:
   - `ImportFromZipAsync(string zipPath)` — rozpakowuje, wykrywa strukturę, tworzy wpis w `mods` table z `ModType = "full"` i `InstallPath` ustawionym na rozpakowany folder
   - `ImportDllAsync(string dllPath)` — kopiuje DLL, tworzy wpis jako `ModType = "dll"`
   - Generuje unikalne `Id`, nazwę z nazwy pliku
3. **UX:** Po imporcie — modal z podsumowaniem (nazwa, typ, ścieżka) + opcja "Zainstaluj teraz"

**Non-goals:**
- ❌ Nie tworzymy pełnego edytora metadanych dla zaimportowanych modów
- ❌ Nie dodajemy zaimportowanych modów do publicznego katalogu API
- ❌ Nie wspieramy `.7z` (tylko `.zip` i `.dll`)

**Zależności:** brak zewnętrznych

---

### D. Niestandardowa nazwa pliku wykonywalnego — 🟢 P3

**Co BeanModManager robi dobrze:**
- Pole `executableName` w rejestrze modów
- Przy launch: sprawdza custom executable, fallback do `Among Us.exe`

**Czego brakuje w SUSModder:**
- Na sztywno zakłada `Among Us.exe`

**Zakres implementacji:**
1. `ModConfiguration.ExecutableName` — nowe pole (nullable, default null = `Among Us.exe`)
2. `GameService.LaunchGameAsync()` — używa `ExecutableName ?? "Among Us.exe"`
3. Backend: pole w API `/susmodder-config` (opcjonalne)

**Edge case:** bardzo rzadki (może 1-2 mody), stąd niski priorytet.

---

## 🟢 Mechanizmy niższego priorytetu (do rozważenia później)

### E. GitHub ETag Cache dla DLL modów

**Co BeanModManager robi:**
- `GitHubCacheHelper` — file-persisted cache z ETag w `%APPDATA%/BeanModManager/cache/`
- Przy requestach: `If-None-Match` → 304 Not Modified → oszczędność rate limitu

**Kontekst SUSModder:**
- Tylko DLL mody są pobierane z GitHuba (full mody idą przez CDN backendu)
- DLL to dużo mniejsze pliki, ale rate limiting wciąż może wystąpić przy wielu użytkownikach

**Zakres (niski priorytet):**
- `SUSModder.Core/Services/GitHubCacheService.cs` — file cache z ETag, timeout 1h
- Użyć w `ModDownloader` przy pobieraniu DLL

### F. Koncepty cache'owania stanu kart modów

**Co BeanModManager robi:**
- `VirtualizedModPanel` z `Dictionary<string, ModCard> _cardCache`
- Stan karty (zaznaczenie, status instalacji) zachowany między scrollami

**Kontekst SUSModder:**
- Avalonia ma wbudowaną wirtualizację (`VirtualizingStackPanel`)
- Cache stanu może być użyteczny w `ModItem` przy dużej liczbie modów (obecnie ~10 full + kilkanaście DLL — nie jest problemem)

---

## Kolejność implementacji

```
P0 ── A. Bulk Operations + Kolejkowanie ──────── ~4-6 dni
P1 ── B. ZIP validation (szybki win) ─────────── ~0.5 dnia
        C. Import modów (link, ZIP, DLL) ──────── ~2-3 dni
P3 ── D. Custom executable name ──────────────── ~0.5 dnia
───
        E. ETag cache (opcjonalnie) ───────────── ~1 dzień
        F. Card state cache (opcjonalnie) ─────── ~0.5 dnia
```

---

## Co z analizy NIE wchodzi (decyzje negatywne)

| Mechanizm z BeanModManager | Dlaczego NIE |
|---|---|
| Współdzielona instalacja gry (storage + copy-on-launch) | Obecny model per-mod copy działa lepiej — izolacja, prostsze cofanie |
| Asset filters per platforma (regex w JSON) | Backend i API SUSModder ogarniają warianty — klient nie musi filtrować |
| System zależności + incompatibilities | Compatibility matrix + ModVersionService już wdrożone |
| GitHub Releases jako źródło modów | SUSModder ma własne API + CDN — lepsza kontrola, szybsze pobieranie |
| Steam Console Commands (depot download) | SUSModder używa 7z/DepotDownloader — dojrzalsze rozwiązania |
| WinForms UI patterns | SUSModder to Avalonia — nieaplikowalne |
| JSON file config | SUSModder ma SQLite od v2.9.0 — lepsza integralność |
| Monolityczny Form (brak separacji) | SUSModder ma MVVM + Core library — lepsza architektura |

---

## Kontekst techniczny

- **Źródło analizy:** BeanModManager v1.x (WinForms, .NET Framework 4.8, GitHub-sourced mods)
- **SUSModder:** .NET 8, Avalonia 12.0.3, ReactiveUI MVVM, SQLite, własne API
- **Pokrycie z istniejącymi planami:** ZIP validation (#17 pokrewne), Bulk ops (nowe)
- **i18n:** wszystkie nowe stringi UI wymagają kluczy PL + EN