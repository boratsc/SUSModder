# Szacowanie Wysiłku: Port SUSModder na Linux

**Data:** 2025-10-30
**Założenia:** 1 developer full-time, 40h/tydzień

---

## Spis treści

1. [Podsumowanie wykonawcze](#podsumowanie-wykonawcze)
2. [Breakdown per komponent](#breakdown-per-komponent)
3. [Szczegółowe szacowanie](#szczegółowe-szacowanie)
4. [Porównanie: Full Port vs MVP](#porównanie-full-port-vs-mvp)
5. [Resource requirements](#resource-requirements)
6. [Cost analysis](#cost-analysis)

---

## Podsumowanie wykonawcze

### Full Port (Complete Linux Support)

| Metryka | Wartość |
|---------|---------|
| **Całkowity czas** | 8-12 tygodni |
| **Godziny developerskie** | 320-480 godzin |
| **Linie kodu (new/modified)** | ~5,000-7,000 LOC |
| **Files touched** | ~50-70 plików |
| **New projects** | 3 (Platform, Platform.Windows, Platform.Linux) |
| **Confidence** | Medium (60%) - due to BepInEx/Proton uncertainty |

### MVP (Steam-only, Ubuntu focus)

| Metryka | Wartość |
|---------|---------|
| **Całkowity czas** | 4-6 tygodni |
| **Godziny developerskie** | 160-240 godzin |
| **Linie kodu (new/modified)** | ~3,000-4,000 LOC |
| **Files touched** | ~35-45 plików |
| **Confidence** | High (80%) |

---

## Breakdown per komponent

### Tabela główna

| Komponent | Effort (godziny) | % czasu | Priorytet | Ryzyko |
|-----------|-----------------|---------|-----------|--------|
| **1. Platform Layer** | 60-80h | 19% | Krytyczny | Low |
| **2. Core Refactor** | 80-120h | 31% | Krytyczny | Medium |
| **3. External Tools Integration** | 40-60h | 16% | Wysoki | Medium |
| **4. UI Adjustments** | 20-30h | 8% | Średni | Low |
| **5. Epic Games Support** | 30-40h | 11% | Niski | High |
| **6. Testing** | 40-60h | 16% | Krytyczny | Medium |
| **7. Packaging** | 30-40h | 11% | Wysoki | Low |
| **8. Documentation** | 20-30h | 8% | Średni | Low |
| **TOTAL** | **320-480h** | **100%** | - | - |

### Wizualizacja

```
Platform Layer      [████████████████░░] 60-80h (19%)
Core Refactor       [████████████████████████░░░░░░░░] 80-120h (31%)
External Tools      [████████████░░] 40-60h (16%)
UI Adjustments      [██████░░] 20-30h (8%)
Epic Games          [████████░░] 30-40h (11%)
Testing             [████████████░░] 40-60h (16%)
Packaging           [████████░░] 30-40h (11%)
Documentation       [██████░░] 20-30h (8%)
```

---

## Szczegółowe szacowanie

### 1. Platform Layer (60-80 godzin)

#### 1.1 Interfejsy (8-12h)

| Zadanie | Godziny | Opis |
|---------|---------|------|
| Zdefiniowanie IPlatformServices | 1h | Root interface |
| IPathProvider | 2h | Interface + XML docs |
| IGameLocator | 2h | Interface + XML docs |
| IProcessManager | 1h | Interface + XML docs |
| IArchiveExtractor | 1h | Interface + XML docs |
| IPermissionManager | 1h | Interface + XML docs |
| IHardwareInfoProvider | 1h | Interface + XML docs |

**Subtotal:** 9h

---

#### 1.2 Windows Implementations (20-28h)

| Klasa | Godziny | Notatki |
|-------|---------|---------|
| WindowsPathProvider | 3h | Przeniesienie z PathSettings |
| WindowsGameLocator | 4h | Przeniesienie z GameLocator |
| WindowsProcessManager | 3h | Przeniesienie z ViewModels |
| WindowsArchiveExtractor | 4h | Wrapper dla 7z.exe |
| WindowsPermissionManager | 4h | PowerShell UAC logic |
| WindowsHardwareInfoProvider | 3h | WMI wrapper |
| WindowsPlatformServices | 2h | Factory class |

**Subtotal:** 23h

---

#### 1.3 Linux Implementations (25-35h)

| Klasa | Godziny | Notatki |
|-------|---------|---------|
| LinuxPathProvider | 5h | XDG paths, Proton detection |
| LinuxGameLocator | 8h | Steam library parsing, Flatpak/Snap paths |
| LinuxProcessManager | 4h | xdg-open, Steam URI |
| LinuxArchiveExtractor | 5h | System 7z detection + wrapper |
| LinuxPermissionManager | 5h | pkexec logic |
| LinuxHardwareInfoProvider | 4h | /proc, /sys parsing |
| LinuxPlatformServices | 2h | Factory class |

**Subtotal:** 33h

---

#### 1.4 DI Setup (7-10h)

| Zadanie | Godziny |
|---------|---------|
| Dodanie Microsoft.Extensions.DI | 1h |
| Konfiguracja w Program.cs | 3h |
| Registration logic (conditional per OS) | 2h |
| Testing DI | 2h |

**Subtotal:** 8h

**Total Platform Layer:** 73h (avg)

---

### 2. Core Refactor (80-120 godzin)

#### 2.1 PathSettings → IPathProvider (12-16h)

| Zadanie | Godziny |
|---------|---------|
| Adapter w PathSettings | 2h |
| Refactor ModConfigHandler | 3h |
| Refactor LobbyUtils | 2h |
| Refactor FixBlackScreen | 2h |
| Replace calls w ViewModels | 4h |
| Testing | 3h |

**Subtotal:** 16h

---

#### 2.2 GameLocator → IGameLocator (10-14h)

| Zadanie | Godziny |
|---------|---------|
| Adapter w GameLocator | 2h |
| Refactor GameService | 3h |
| Replace calls w MainWindowViewModel | 3h |
| Testing (Windows + Linux) | 4h |

**Subtotal:** 12h

---

#### 2.3 ConfigManager → ConfigService (8-12h)

| Zadanie | Godziny |
|---------|---------|
| Create ConfigService class | 4h |
| Migrate logic from ConfigManager | 3h |
| Replace all calls | 3h |
| Testing | 2h |

**Subtotal:** 12h

---

#### 2.4 ModManager → IArchiveExtractor (12-16h)

| Zadanie | Godziny |
|---------|---------|
| Add IArchiveExtractor to ModManager constructor | 1h |
| Replace Extract7zWithPassword logic | 4h |
| Testing Windows (7z.exe) | 2h |
| Testing Linux (system 7z) | 4h |
| Edge cases handling | 3h |

**Subtotal:** 14h

---

#### 2.5 FileSystemUtilities → IPermissionManager (10-14h)

| Zadanie | Godziny |
|---------|---------|
| Refactor FileSystemUtilities | 4h |
| Refactor FileSystemHelper | 3h |
| Testing Windows (PowerShell UAC) | 2h |
| Testing Linux (pkexec) | 3h |
| Edge cases | 2h |

**Subtotal:** 14h

---

#### 2.6 HardwareIdProvider → IHardwareInfoProvider (8-12h)

| Zadanie | Godziny |
|---------|---------|
| Refactor HardwareIdProvider | 3h |
| Conditional compilation WMI | 2h |
| Testing Windows | 1h |
| Testing Linux | 2h |
| TelemetryService updates | 2h |

**Subtotal:** 10h

---

#### 2.7 MainWindowViewModel Refactor (16-24h)

| Zadanie | Godziny |
|---------|---------|
| GameLaunch.cs - IProcessManager | 6h |
| ExternalActions.cs - IProcessManager | 4h |
| Replace Process.Start calls | 4h |
| Testing uruchamiania gry (Windows) | 2h |
| Testing uruchamiania gry (Linux/Proton) | 6h |

**Subtotal:** 22h

---

#### 2.8 Unit Tests (12-16h)

| Zadanie | Godziny |
|---------|---------|
| Setup SUSModder.Tests project | 2h |
| Tests dla PathProvider (Windows + Linux) | 3h |
| Tests dla GameLocator | 3h |
| Tests dla ProcessManager | 2h |
| Tests dla ArchiveExtractor | 2h |
| Other tests | 2h |

**Subtotal:** 14h

**Total Core Refactor:** 114h (avg)

---

### 3. External Tools Integration (40-60 godzin)

#### 3.1 7z Integration (12-16h)

| Zadanie | Godziny |
|---------|---------|
| WindowsArchiveExtractor (already in Platform Layer) | - |
| LinuxArchiveExtractor (already in Platform Layer) | - |
| Conditional packaging (7z.exe tylko Windows) | 2h |
| Testing extraction Windows | 3h |
| Testing extraction Linux (p7zip) | 4h |
| Error handling (7z nie zainstalowany) | 2h |
| Documentation dla users | 2h |

**Subtotal:** 13h

---

#### 3.2 Steam URI Integration (8-12h)

| Zadanie | Godziny |
|---------|---------|
| Windows - existing logic | 1h (minor adjustments) |
| Linux - Steam URI through xdg-open | 3h |
| Testing różnych Steam installations (native, Flatpak, Snap) | 5h |
| Fallback logic | 2h |

**Subtotal:** 11h

---

#### 3.3 Updater Port (10-14h)

| Zadanie | Godziny |
|---------|---------|
| Dodaj linux-x64 do Updater.csproj | 1h |
| Cross-platform path logic w Updater | 3h |
| Testing updater Windows | 2h |
| Testing updater Linux | 3h |
| Permissions handling (chmod +x) | 2h |

**Subtotal:** 11h

---

#### 3.4 Proton/BepInEx Integration (10-18h)

| Zadanie | Godziny |
|---------|---------|
| Research BepInEx na Proton | 3h |
| Testing różnych Proton versions | 5h |
| Dokumentacja workarounds | 2h |
| Edge cases handling | 4h |

**Subtotal:** 14h

**Total External Tools:** 49h (avg)

---

### 4. UI Adjustments (20-30 godzin)

| Zadanie | Godziny |
|---------|---------|
| Otwieranie folderów (xdg-open) | 3h |
| Otwieranie URL (already done - UrlToCommandConverter) | 1h |
| Desktop shortcuts (.desktop files) | 6h |
| Icon handling (.ico vs .png) | 3h |
| ApplicationManifest conditional | 1h |
| Testing na różnych DE (GNOME, KDE, XFCE) | 8h |

**Total UI Adjustments:** 22h

---

### 5. Epic Games Support (30-40 godzin) - OPTIONAL

| Zadanie | Godziny |
|---------|---------|
| LinuxEpicGameManager implementation | 8h |
| Heroic Launcher detection | 4h |
| Heroic config parsing | 6h |
| Uruchamianie gry przez Heroic CLI | 4h |
| Testing z Heroic | 6h |
| Error handling (Heroic nie zainstalowany) | 2h |
| Documentation | 3h |

**Total Epic Games:** 33h

**UWAGA:** Jeśli MVP, skip Epic (save 33h)

---

### 6. Testing (40-60 godzin)

#### 6.1 Integration Testing (16-24h)

| Zadanie | Godziny |
|---------|---------|
| End-to-end test Ubuntu (instalacja moda) | 6h |
| End-to-end test Fedora | 5h |
| End-to-end test Arch | 5h |
| Multi-mod testing | 4h |
| Regression testing Windows | 4h |

**Subtotal:** 24h

---

#### 6.2 Bug Fixing (16-24h)

| Zadanie | Godziny |
|---------|---------|
| Triage bugs | 2h |
| Fix critical bugs | 10h |
| Fix high priority bugs | 6h |
| Retesting | 4h |

**Subtotal:** 22h

---

#### 6.3 Performance Testing (4-6h)

| Zadanie | Godziny |
|---------|---------|
| Profiling aplikacji | 2h |
| Memory leak detection | 2h |
| Performance optimizations | 1h |

**Subtotal:** 5h

**Total Testing:** 51h

---

### 7. Packaging (30-40 godzin)

| Package | Godziny |
|---------|---------|
| .deb (Debian/Ubuntu) | 8h |
| .rpm (Fedora/RHEL) | 6h |
| AUR (Arch Linux) | 4h |
| AppImage (Universal) | 8h |
| Flatpak (Optional) | 6h |
| Testing instalacji packages | 6h |

**Total Packaging:** 38h (32h if skip Flatpak)

---

### 8. Documentation (20-30 godzin)

| Zadanie | Godziny |
|---------|---------|
| README update | 2h |
| Linux installation guide | 4h |
| Troubleshooting guide | 4h |
| FAQ | 2h |
| Release notes | 2h |
| Code documentation (XML comments) | 4h |
| Video tutorial (optional) | 6h |

**Total Documentation:** 24h

---

## Porównanie: Full Port vs MVP

### Full Port

| Faza | Godziny |
|------|---------|
| Platform Layer | 73h |
| Core Refactor | 114h |
| External Tools | 49h |
| UI Adjustments | 22h |
| Epic Games | 33h |
| Testing | 51h |
| Packaging (all) | 38h |
| Documentation | 24h |
| **TOTAL** | **404h** |

**Timeline:** 10 tygodni @ 40h/tydzień
**Range:** 8-12 tygodni (320-480h)

---

### MVP (Steam-only, Ubuntu focus)

| Faza | Godziny | Savings |
|------|---------|---------|
| Platform Layer | 73h | - |
| Core Refactor (tylko Steam) | 90h | -24h |
| External Tools (bez Epic) | 35h | -14h |
| UI Adjustments (basic) | 15h | -7h |
| Epic Games | **0h** | **-33h** |
| Testing (tylko Ubuntu + Windows) | 30h | -21h |
| Packaging (.deb + AppImage tylko) | 20h | -18h |
| Documentation (basic) | 15h | -9h |
| **TOTAL** | **278h** | **-126h** |

**Timeline:** 7 tygodni @ 40h/tydzień
**Range:** 6-8 tygodni (240-320h)

**Savings:** 126 godzin (~3 tygodnie)

---

## Resource Requirements

### Development Environment

**Hardware:**
- Development workstation (Windows lub Linux)
- 16 GB RAM minimum (dla VMs)
- 100 GB SSD dla VMs
- Multi-core CPU (dla parallel builds)

**Software:**
- Visual Studio 2022 / Rider
- .NET SDK 8.0
- Docker (dla containerized testing)
- VirtualBox / VMware dla VMs

**VMs needed:**
```
1. Ubuntu 24.04 (8 GB RAM, 40 GB disk)
2. Fedora 40 (8 GB RAM, 40 GB disk)
3. Arch Linux (8 GB RAM, 40 GB disk)
```

**Total VM requirements:** 24 GB RAM allocated, 120 GB disk

---

### Team Composition

#### Option A: Solo Developer

**Wymagania:**
- Doświadczenie: Senior .NET developer
- Znajomość: C#, Avalonia, Linux basics
- Timeline: 10-12 tygodni full-time

**Pros:**
- Niższy koszt
- Spójność kodu

**Cons:**
- Slower
- Single point of failure
- Może brakować Linux expertise

---

#### Option B: Pair (Recommended)

**Team:**
1. Senior .NET Developer (Windows expert)
2. Linux Developer / DevOps (Linux expert)

**Podział pracy:**
- Developer 1: Core refactor, Windows implementations
- Developer 2: Linux implementations, packaging, testing

**Timeline:** 6-8 tygodni

**Pros:**
- Faster
- Better quality (peer review)
- Complementary skills

**Cons:**
- Wyższy koszt (2x)

---

#### Option C: Team + Community

**Team:**
- 1 Senior Developer (lead)
- Community beta testers

**Approach:**
- Developer: Core development (6 tygodni)
- Community: Testing, feedback, documentation (2-4 tygodnie)

**Timeline:** 8 tygodni total

**Pros:**
- Balance cost vs speed
- Community engagement
- Real-world testing

**Cons:**
- Requires community management

---

## Cost Analysis

### Assumptions

- Developer hourly rate: $50-100/h (Poland market)
- Senior rate: $75/h (average)

### Full Port Cost

| Scenario | Godziny | Rate | Total Cost |
|----------|---------|------|------------|
| **Minimum** | 320h | $50/h | $16,000 |
| **Average** | 404h | $75/h | **$30,300** |
| **Maximum** | 480h | $100/h | $48,000 |

---

### MVP Cost

| Scenario | Godziny | Rate | Total Cost |
|----------|---------|------|------------|
| **Minimum** | 240h | $50/h | $12,000 |
| **Average** | 278h | $75/h | **$20,850** |
| **Maximum** | 320h | $100/h | $32,000 |

**Savings:** $9,450 (average)

---

### ROI Considerations

**Jeśli internal project:**
- Cost = developer salary for 8-12 tygodni
- ROI = strategic value + learning + community goodwill

**Jeśli commercial:**
- Revenue needed = Cost / # of expected Linux users
- Break-even analysis needed

**Example:**
- Cost: $30,300
- Linux users needed (@ $10 revenue/user): 3,030 users
- If Among Us has 100k SUSModder users, need 3% adoption on Linux

---

## Confidence Intervals

| Estimate | Confidence | Range |
|----------|-----------|-------|
| **Platform Layer** | 80% | 60-80h |
| **Core Refactor** | 70% | 80-120h |
| **External Tools** | 60% | 40-60h (BepInEx uncertainty) |
| **UI Adjustments** | 90% | 20-30h |
| **Epic Games** | 50% | 30-40h (Heroic complexity unknown) |
| **Testing** | 60% | 40-60h (distro fragmentation) |
| **Packaging** | 80% | 30-40h |
| **Documentation** | 90% | 20-30h |
| **Overall** | **70%** | **320-480h (8-12 tygodni)** |

---

## Recommendations

### Dla małego zespołu (1-2 devs):

✅ **GO FOR MVP**
- 6-8 tygodni
- $20,850 cost (avg)
- Niższe ryzyko
- Quick feedback
- Iterate based on user needs

### Dla większego zespołu (3+ devs):

✅ **GO FOR FULL PORT**
- 8-12 tygodni
- $30,300 cost (avg)
- Complete feature set
- Better long-term value

### Dla community project:

✅ **GO FOR MVP + COMMUNITY**
- 8 tygodni total
- Lower cost (developer + volunteers)
- Community engagement
- Sustainable development

---

## Podsumowanie końcowe

**Rekomendowany approach:**

1. **Week 0:** Setup (1 tydzień prep)
2. **Week 1-6:** MVP Development
3. **Week 7-8:** Community Beta + Bug Fixes
4. **Week 9:** Release v2.1.0-linux-mvp

**Post-MVP (v2.2):**
- Epic Games support
- Additional distro support
- Advanced features

**Total investment:** 9 tygodni, $25,000-30,000

**Expected outcome:** Functional Linux version z możliwością iteracji based on user feedback
