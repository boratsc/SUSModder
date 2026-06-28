# SUSModder - Refactoring Systemu Aktualizacji

**Data:** 2025-10-28 (Updated: 2025-11-04)
**Autor:** Claude Code
**Wersja obecna:** 2.0.1
**Wersja docelowa:** 2.1.0
**Status:** 🟡 Implementacja w toku - backend gotowy, wymaga testowego pakietu

---

## Executive Summary

Obecny system aktualizacji SUSModder jest flagowany przez antywirusy pomimo posiadania podpisu cyfrowego (Standard OV). Problem wynika z:
1. **Behavioral pattern** - pobieranie ZIP, rozpakowywanie, podmiana exe, auto-restart to klasyczny malware behavior
2. **Brak SmartScreen reputation** - Standard OV wymaga budowania reputacji przez tygodnie/miesiące instalacji
3. **External updater.exe** - dodatkowy proces wykonujący file operations zwiększa podejrzenia

**Proponowane rozwiązanie:** Migracja na **Velopack** - nowoczesny framework do instalacji i auto-update (następca Squirrel.Windows), napisany w Rust dla maksymalnej wydajności.

**Oczekiwane rezultaty:**
- ✅ Znacząco mniejsza liczba false positives (znany, zaufany pattern)
- ✅ Delta updates (tylko różnice zamiast pełnego ZIP, 80-90% oszczędności bandwidth)
- ✅ Lepszy UX (stała ścieżka exe, atomic swaps, brak problemów z firewall)
- ✅ Darmowe i open-source rozwiązanie
- ✅ Szybsze niż poprzednik (native Rust performance)
- ✅ Cross-platform ready (Windows, macOS, Linux)

---

## Struktura Dokumentacji

1. **[PROBLEM_ANALYSIS.md](./PROBLEM_ANALYSIS.md)** - Szczegółowa analiza dlaczego AV blokują obecny system
2. **[SOLUTION_COMPARISON.md](./SOLUTION_COMPARISON.md)** - Porównanie wszystkich rozważanych opcji
3. **[VELOPACK_IMPLEMENTATION.md](./VELOPACK_IMPLEMENTATION.md)** - **Plan implementacji Velopack** (główny dokument)
4. **[MIGRATION_PLAN.md](./MIGRATION_PLAN.md)** - Step-by-step migracja z obecnego systemu
5. **[BACKEND_SETUP.md](./BACKEND_SETUP.md)** - Wymagania po stronie serwera
6. **[CODE_EXAMPLES.md](./CODE_EXAMPLES.md)** - Gotowe snippety kodu do użycia

---

## Obecny Status (2025-11-04)

### ✅ Co zostało zaimplementowane:
- Backend API działa na `https://susmodder.app/api/releases`
- Kod aplikacji z pełną obsługą Velopack:
  - `VelopackUpdateService.cs` - główna logika
  - `VelopackApiSource.cs` - custom source dla API
  - `VelopackUpdateDialog` - UI dialog
  - Auto-detekcja Velopack z fallback do legacy updater
- NuGet package Velopack dodany do projektu

### ⚠️ Co wymaga ukończenia:
- **Wygenerowanie testowego pakietu .nupkg**
- Aktualizacja backend API do zwracania prawdziwego checksum (obecnie: "dummychecksum")
- Pełne testy instalacji i aktualizacji

### 🚀 Następne Kroki:

Dla **testowania**:
1. Uruchom: `.\build-velopack-test.ps1` (lub `.\generate-dummy-release.ps1` dla szybkiego testu)
2. Upload plików z `velopack-releases/` na serwer
3. Popraw backend aby zwracał prawdziwy SHA256 z pliku `RELEASES`
4. Zobacz [../../VELOPACK_TESTING_GUIDE.md](../../VELOPACK_TESTING_GUIDE.md) dla szczegółów

Dla **implementacji od zera**:
1. Przeczytaj [PROBLEM_ANALYSIS.md](./PROBLEM_ANALYSIS.md) aby zrozumieć problem
2. Przejrzyj [SOLUTION_COMPARISON.md](./SOLUTION_COMPARISON.md) aby potwierdzić wybór
3. Następuj krokom w [VELOPACK_IMPLEMENTATION.md](./VELOPACK_IMPLEMENTATION.md)
4. Użyj [CODE_EXAMPLES.md](./CODE_EXAMPLES.md) jako reference podczas implementacji

---

## Kontekst Techniczny

### Obecny System

```
App (SUSModder.exe)
  ↓
AppUpdateService
  ↓ pobiera ZIP
Updater.exe
  ↓ rozpakowanie + podmiana
Restart App
```

**Komponenty:**
- `SUSModder.Core/Services/AppUpdateService.cs` - zarządza sprawdzaniem/pobieraniem
- `Updater/Program.cs` - standalone exe wykonujący actual update
- Backend: `/api/download-latest` zwraca ZIP

### Proponowany System (Velopack)

```
App (SUSModder.exe)
  ↓
VelopackUpdateService
  ↓ UpdateManager.CheckForUpdatesAsync()
Update.exe (Velopack native updater, Rust)
  ↓ atomic swap, stała ścieżka
Restart App
```

**Komponenty:**
- `SUSModder.Core/Services/VelopackUpdateService.cs` - wrapper na Velopack API
- Velopack NuGet package (https://www.nuget.org/packages/Velopack)
- Backend: `/releases/` z release manifest + pakiety

---

## Kluczowe Decyzje

| Aspekt | Obecny system | Velopack |
|--------|---------------|----------|
| **Format pakietu** | ZIP | Velopack packages (.nupkg) |
| **Updater** | External updater.exe | Wbudowany Update.exe (Rust) |
| **Delta updates** | Nie | Tak (tylko różnice, ~80-90% oszczędności) |
| **AV reputation** | Niska (custom pattern) | Wysoka (znany pattern, następca Squirrel) |
| **Backend complexity** | Prosty (1 endpoint) | Średni (releases manifest) |
| **Maintenance** | Custom code | Aktywnie maintained (2025) |
| **Performance** | C# updater | Native Rust (szybsze) |
| **File path stability** | Zmienne | Stała ścieżka (fewer AV/firewall issues) |

---

## Alternatywne Rozwiązania (Nie Rekomendowane)

### 1. Redirect do przeglądarki (propozycja użytkownika)
- ❌ **Drastycznie gorszy UX** (5-6 kroków zamiast 1)
- ❌ **Nie rozwiązuje problemu** (plik nadal może być flagowany)
- ❌ **Więcej supportu** (użytkownicy gubią plik, nie wiedzą co zrobić)

### 2. Direct EXE Download (quick fix)
- ⚠️ **Prostszy pattern** ale nadal nie idealny
- ⚠️ **Nie aktualizuje innych plików** (tylko exe)
- ✅ **Szybka implementacja** (2-4h)
- **Ocena:** Tymczasowe rozwiązanie, nie długoterminowe

### 3. AutoUpdater.NET
- ✅ Najprostsze rozwiązanie (XML-based)
- ✅ Built-in GUI dialogs
- ❌ Brak delta updates
- ❌ Tylko Windows

### 4. ClickOnce
- ✅ Microsoft native
- ❌ Więcej refactoringu niż Velopack
- ❌ Mniej elastyczny

### 5. EV Code Signing Certificate
- ✅ **Instant SmartScreen reputation**
- 💰 **Koszt: $400-600/rok**
- ✅ Zero zmian w kodzie
- **Ocena:** Najlepszy długoterminowy fix, ale kosztowny

### 6. Microsoft Store
- ✅ Zero problemów z AV
- ❌ Review process (1-3 dni)
- ❌ Revenue split / submission fee

---

## Timeline Implementacji

### Tydzień 1: Setup & Core Implementation
- **Dzień 1-2:** Instalacja Velopack, testowanie lokalnie, zrozumienie API
- **Dzień 3-4:** Implementacja `VelopackUpdateService`, integracja z `MainWindowViewModel`
- **Dzień 5:** Dostosowanie `Program.cs` (Velopack hooks), testowanie instalacji/update flow

### Tydzień 2: Backend & Deployment
- **Dzień 6:** Backend changes - releases endpoint, hosting pakietów
- **Dzień 7:** Build process automation (`vpk pack`)
- **Dzień 8-9:** Testing end-to-end (świeża instalacja → update → rollback)
- **Dzień 10:** Dokumentacja, deployment na produkcję

---

## Ryzyka i Mitigacje

### Ryzyko 1: Breaking Changes dla Istniejących Użytkowników
**Mitigacja:**
- Stwórz "bridge update" (2.0.2) który instaluje Velopack wrapper
- Kolejne updatey (2.1.0+) już przez Velopack
- Zachowaj stary endpoint `/api/download-latest` przez 1-2 miesiące

### Ryzyko 2: Backend Deployment Downtime
**Mitigacja:**
- Releases endpoint może być static hosting (GitHub Pages, S3)
- Package files mogą być CDN
- Zero downtime podczas przełączenia

### Ryzyko 3: Velopack Learning Curve
**Mitigacja:**
- Doskonała dokumentacja: https://docs.velopack.io/
- Automatyczna migracja z innych frameworków
- Przykłady w CODE_EXAMPLES.md
- Aktywny development i community support

---

## Metryki Sukcesu

Po implementacji Velopack, śledź:

1. **False Positive Rate**
   - Baseline (teraz): ~10-30% instalacji flagowanych
   - Target (po 2 tygodniach): <5%
   - Target (po 2 miesiącach): <1%

2. **Update Success Rate**
   - Baseline: ~85-90%
   - Target: >95%

3. **Support Tickets (update-related)**
   - Baseline: X tickets/tydzień
   - Target: Redukcja o 50%

4. **Update Download Size**
   - Baseline: ~50-100MB (full ZIP)
   - Target: 5-20MB (delta updates)

---

## Następne Kroki

1. ✅ Zapoznaj się z dokumentacją w tym folderze
2. 🔄 Skomentuj/zadaj pytania jeśli coś jest niejasne
3. 🔄 Potwierdź wybór Velopack jako rozwiązania
4. 🔄 Zacznij implementację wg [VELOPACK_IMPLEMENTATION.md](./VELOPACK_IMPLEMENTATION.md)

---

## Kontakt i Pytania

Jeśli masz pytania lub wątpliwości podczas implementacji:
- Sprawdź [CODE_EXAMPLES.md](./CODE_EXAMPLES.md) dla konkretnych snippetów
- Zobacz Velopack docs: https://docs.velopack.io/
- GitHub repo: https://github.com/velopack/velopack
- Migration guide: https://docs.velopack.io/migrating/squirrel

---

## Historia Zmian

- **2025-10-28 (v2):** Updated - przejście na Velopack (Squirrel.Windows deprecated)
- **2025-10-28 (v1):** Initial draft - analiza problemu, oryginalna propozycja Squirrel.Windows
