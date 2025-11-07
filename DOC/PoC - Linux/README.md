# Proof of Concept: SUSModder na Linux

**Data utworzenia:** 2025-10-30
**Wersja SUSModder:** 2.0.1
**Autor analizy:** Claude Code
**Status:** Proof of Concept - Badanie wykonalności

---

## Executive Summary

Ten dokument przedstawia kompleksową analizę możliwości przeniesienia aplikacji **SUSModder** (menedżera modów dla Among Us) na platformę Linux, obejmującą dystrybucje Arch Linux, Debian/Ubuntu oraz systemy oparte na RPM (Fedora, RHEL).

### Kluczowe wnioski

#### ✅ Wykonalne z modyfikacjami

Port SUSModder na Linux **jest możliwy**, ale wymaga **znaczących modyfikacji** w architekturze aplikacji. Główne wyzwania to:

1. **Zależności od Windows API** - System.Management (WMI), Microsoft.Win32.Registry
2. **Ścieżki systemowe** - Hardcodowane ścieżki Windows (%APPDATA%, %PROGRAMFILES%)
3. **Zewnętrzne narzędzia** - 7z.exe, legendary.exe, PowerShell
4. **Integracja z platformami** - Steam i Epic Games Store na Linux działają inaczej
5. **Among Us + BepInEx** - Wymaga Proton/Wine, co komplikuje modowanie

#### 📊 Ocena komponentów

| Komponent | Gotowość do Linux | Wymagany wysiłek |
|-----------|-------------------|------------------|
| **Avalonia UI** | ✅ Gotowy (11.3.7) | Minimalny |
| **SUSModder.Core** | ⚠️ Wymaga refaktoru | Wysoki |
| **GameLocator** | ❌ Windows-only | Średni |
| **ModManager** | ⚠️ Częściowo | Wysoki |
| **EpicVersionManager** | ❌ Windows-only | Wysoki |
| **Updater** | ❌ Windows-only | Średni |
| **External Tools** | ❌ Windows executables | Średni |

#### ⏱️ Szacowany wysiłek

- **Całkowity czas:** 8-12 tygodni pracy developerskiej
- **Ryzyko:** Średnie do wysokiego
- **Złożoność:** Wysoka
- **ROI:** Zależy od wielkości rynku użytkowników Linux

---

## Struktura dokumentacji

Niniejszy Proof of Concept składa się z następujących dokumentów:

### 📄 Dokumenty analityczne

1. **[01-FEASIBILITY-ANALYSIS.md](./01-FEASIBILITY-ANALYSIS.md)**
   Szczegółowa analiza wykonalności projektu, zależności platformowych i kompatybilności

2. **[02-ARCHITECTURE.md](./02-ARCHITECTURE.md)**
   Porównanie obecnej architektury z proponowaną architekturą cross-platform

3. **[03-MIGRATION-STRATEGY.md](./03-MIGRATION-STRATEGY.md)**
   Strategia migracji kodu z Windows-specific na cross-platform

4. **[04-IMPLEMENTATION-PLAN.md](./04-IMPLEMENTATION-PLAN.md)**
   Fazowy plan implementacji z priorytetami i zależnościami

5. **[05-DEPENDENCIES.md](./05-DEPENDENCIES.md)**
   Zewnętrzne zależności i wymagania systemowe dla różnych dystrybucji

6. **[06-RISKS-AND-CHALLENGES.md](./06-RISKS-AND-CHALLENGES.md)**
   Analiza ryzyk, wyzwań technicznych i potencjalnych problemów

7. **[07-EFFORT-ESTIMATION.md](./07-EFFORT-ESTIMATION.md)**
   Szczegółowe szacowanie wysiłku, czasu i zasobów

### 💻 Przykłady kodu

W katalogu `code-samples/` znajdują się przykładowe implementacje:

- `CrossPlatformPathProvider.cs` - Zarządzanie ścieżkami cross-platform
- `CrossPlatformProcessManager.cs` - Uruchamianie procesów cross-platform
- `LinuxGameLocator.cs` - Lokalizacja Among Us na Linux

---

## Najważniejsze rekomendacje

### 1. Warstwa abstrakcji platformy

Wprowadzenie warstwy abstrakcji dla operacji zależnych od platformy:

```csharp
public interface IPlatformServices
{
    IPathProvider PathProvider { get; }
    IProcessManager ProcessManager { get; }
    IGameLocator GameLocator { get; }
    IPermissionManager PermissionManager { get; }
}
```

### 2. Refaktor SUSModder.Core

Przeprojektowanie kluczowych serwisów z myślą o cross-platform:

- ✅ `PathSettings` → `CrossPlatformPathProvider`
- ✅ `GameLocator` → `SteamGameLocator` + `ProtonGameLocator`
- ✅ `ModManager` → Abstrakcja dla 7z (native vs .exe)
- ✅ `EpicVersionManager` → Integracja z Heroic Games Launcher

### 3. Conditional compilation i Runtime detection

Wykorzystanie warunkowej kompilacji i runtime detection:

```xml
<!-- .csproj -->
<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64</RuntimeIdentifiers>

<ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
    <Content Include="tools\7z.exe" />
</ItemGroup>
```

```csharp
// Runtime detection
if (OperatingSystem.IsWindows())
{
    // Windows-specific logic
}
else if (OperatingSystem.IsLinux())
{
    // Linux-specific logic
}
```

### 4. Heroic Games Launcher dla Epic

Zamiast legendary.exe, integracja z **Heroic Games Launcher** (dostępny natywnie na Linux):

```bash
# Uruchomienie gry przez Heroic
heroic launch "Among Us"
```

### 5. Packaging dla dystrybucji

Stworzenie pakietów dla różnych dystrybucji:

- **Debian/Ubuntu:** `.deb` package
- **Fedora/RHEL:** `.rpm` package
- **Arch Linux:** AUR (Arch User Repository)
- **Universal:** AppImage lub Flatpak

---

## Alternatywne podejścia

### Opcja A: Pełny port natywny (REKOMENDOWANE)

- **Czas:** 8-12 tygodni
- **Złożoność:** Wysoka
- **Zalety:** Pełna funkcjonalność, natywne wsparcie
- **Wady:** Duży wysiłek developerski

### Opcja B: Wine/Proton dla całej aplikacji

- **Czas:** 1-2 tygodnie (testowanie)
- **Złożoność:** Niska
- **Zalety:** Minimalny kod changes
- **Wady:** Gorsze UX, zależność od Wine, problemy z integracją systemową

### Opcja C: Minimalna wersja CLI

- **Czas:** 4-6 tygodni
- **Złożoność:** Średnia
- **Zalety:** Prostsza implementacja
- **Wady:** Brak GUI (Avalonia), ograniczona funkcjonalność

---

## Następne kroki

Jeśli projekt zostanie zatwierdzony do implementacji:

1. **Faza Discovery (2 tygodnie)**
   - Głębsza analiza kodu źródłowego
   - Prototyp CrossPlatformPathProvider
   - Testy Among Us + BepInEx na Linux

2. **Faza PoC (3-4 tygodnie)**
   - Implementacja podstawowej warstwy abstrakcji
   - Port GameLocator dla Steam na Linux
   - Testy instalacji jednego moda

3. **Faza MVP (4-6 tygodni)**
   - Pełny refactor SUSModder.Core
   - Integracja wszystkich zewnętrznych narzędzi
   - UI adjustments dla Linux

4. **Faza Beta (2-3 tygodnie)**
   - Testing na różnych dystrybucjach
   - Bug fixes
   - Dokumentacja użytkownika

5. **Release (1 tydzień)**
   - Packaging (.deb, .rpm, AUR)
   - Dokumentacja release notes
   - Publikacja

---

## Kontakt i feedback

Ten dokument jest **living document** i będzie aktualizowany w miarę postępu projektu.

Dla pytań, sugestii lub feedback:
- Utworzyć issue w repozytorium GitHub
- Skontaktować się z zespołem developerskim

---

**Data ostatniej aktualizacji:** 2025-10-30
