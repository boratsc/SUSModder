# Ryzyka i Wyzwania: Port SUSModder na Linux

**Data:** 2025-10-30

---

## Spis treści

1. [Podsumowanie ryzyk](#podsumowanie-ryzyk)
2. [Ryzyka techniczne](#ryzyka-techniczne)
3. [Ryzyka biznesowe](#ryzyka-biznesowe)
4. [Ryzyka operacyjne](#ryzyka-operacyjne)
5. [Mitigation strategies](#mitigation-strategies)

---

## Podsumowanie ryzyk

### Risk Matrix

| Ryzyko | Prawdopodobieństwo | Wpływ | Severity | Mitigation |
|--------|-------------------|-------|----------|------------|
| BepInEx nie działa na Proton | Średnie | Krytyczny | 🔴 HIGH | Testowanie wcześnie, dokumentacja workarounds |
| Fragmentacja Linux distro | Wysokie | Wysoki | 🟡 MEDIUM | Focus na top 3 distro (Ubuntu, Fedora, Arch) |
| Dependencies conflicts | Średnie | Średni | 🟡 MEDIUM | Bundled dependencies w AppImage |
| Performance issues | Niskie | Średni | 🟢 LOW | Performance testing, profiling |
| Timeline delays | Średnie | Wysoki | 🟡 MEDIUM | MVP approach, scope reduction |
| Lack of Linux testers | Średnie | Wysoki | 🟡 MEDIUM | Community beta testing |
| Epic Games complexity | Wysokie | Średni | 🟡 MEDIUM | Make Epic optional/post-MVP |
| Steam paths detection | Średnie | Krytyczny | 🔴 HIGH | Comprehensive path detection, manual override |

---

## Ryzyka techniczne

### 1. BepInEx Compatibility na Proton 🔴 CRITICAL

**Opis:**
BepInEx (mod loader dla Unity) może nie działać poprawnie z Among Us uruchomionym przez Proton/Wine.

**Prawdopodobieństwo:** Średnie (40%)

**Wpływ:** Krytyczny - bez działającego BepInEx mody nie będą działać

**Scenariusze:**
- BepInEx w ogóle się nie ładuje
- BepInEx ładuje się ale mody crashują grę
- Niektóre mody działają, inne nie
- Performance issues z BepInEx na Proton

**Mitigation:**
1. **Early testing** - Przetestować BepInEx na Proton w tygodniu 1
2. **Documentation** - Udokumentować known issues i workarounds
3. **Fallback** - Jeśli nie działa, dokumentuj manual installation dla advanced users
4. **Community** - Konsultuj z BepInEx community o best practices dla Proton

**Probability po mitigation:** 20%

---

### 2. Fragmentacja dystrybucji Linux 🟡 MEDIUM

**Opis:**
Różne dystrybucje Linux mają różne:
- Package managers (apt, dnf, pacman)
- System paths
- Desktop environments
- Default applications
- File system layouts

**Prawdopodobieństwo:** Wysokie (80%)

**Wpływ:** Wysoki - aplikacja może nie działać na niektórych distro

**Przykłady problemów:**
- Steam w różnych lokalizacjach (native, Flatpak, Snap)
- Różne wersje dependencies
- Conflicting libraries
- DE-specific issues (GNOME vs KDE vs XFCE)

**Mitigation:**
1. **Focus na top 3** - Ubuntu/Debian, Fedora, Arch Linux (80% użytkowników Linux desktop)
2. **Runtime detection** - Detect distro i adjust paths dynamically
3. **Packaging diversity** - Provide .deb, .rpm, AppImage, Flatpak
4. **User override** - Allow manual path configuration
5. **Testing matrix** - Test na VM każdej głównej distro

**Probability po mitigation:** 60% (ale with graceful fallbacks)

---

### 3. Steam Library Detection 🔴 HIGH

**Opis:**
Steam może być zainstalowany w różnych lokalizacjach:
- Native: `~/.steam/steam`, `~/.local/share/Steam`
- Flatpak: `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`
- Snap: `~/snap/steam/common/.local/share/Steam`
- Custom paths (użytkownik zmienił)
- Multiple library folders (libraryfolders.vdf)

**Prawdopodobieństwo:** Średnie (50%)

**Wpływ:** Krytyczny - bez wykrycia gry, aplikacja jest bezużyteczna

**Mitigation:**
1. **Comprehensive detection** - Check wszystkie możliwe paths
2. **VDF parsing** - Parse `libraryfolders.vdf` dla custom libraries
3. **Manual override** - UI option dla użytkowników do podania custom path
4. **Error messaging** - Clear instructions jeśli gra nie znaleziona
5. **Steam API** - Consider using Steam API dla game discovery (jeśli dostępne)

**Probability po mitigation:** 20%

---

### 4. Epic Games Store via Heroic 🟡 MEDIUM

**Opis:**
Epic Games Store nie działa natywnie na Linux. Użytkownicy używają:
- Heroic Games Launcher (najpopularniejsze)
- Legendary CLI
- Lutris

Każde z tych ma inną strukturę katalogów i konfigurację.

**Prawdopodobieństwo:** Wysokie (70%)

**Wpływ:** Średni - Epic jest mniej popularny na Linux niż Steam

**Mitigation:**
1. **Make Epic optional** - Epic support nie jest w MVP
2. **Focus on Heroic** - Heroic jest najpopularniejszy, support tylko jego w v1
3. **Clear documentation** - Dokumentuj wymagania (musi mieć Heroic zainstalowany)
4. **Post-MVP** - Epic support w v2.2 po stabilizacji Steam support

**Probability po mitigation:** N/A (Epic is post-MVP)

---

### 5. Elevated Permissions (pkexec/sudo) 🟡 MEDIUM

**Opis:**
Usuwanie modów czasem wymaga elevated permissions. Na Linux:
- `pkexec` (PolicyKit) - GUI authorization prompt
- `sudo` - terminal-based, wymaga hasła
- Różne distro mają różne default auth mechanisms

**Prawdopodobieństwo:** Średnie (50%)

**Wpływ:** Średni - użytkownik nie może usunąć niektórych modów

**Scenariusze:**
- pkexec nie zainstalowany
- pkexec authorization failed
- sudo wymaga terminal (GUI app nie może)
- Użytkownik nie ma sudo permissions

**Mitigation:**
1. **Try normal delete first** - Elevated tylko jeśli normalny fails
2. **pkexec preferred** - Użyj pkexec (GUI) jako primary
3. **Graceful degradation** - Jeśli pkexec fails, show error z instrukcjami
4. **Manual instructions** - Dokumentuj manual deletion przez file manager
5. **Avoid elevated if possible** - Install mods w user-writable directories

**Probability po mitigation:** 30%

---

### 6. Desktop Environment Differences 🟢 LOW

**Opis:**
Różne DE mają różne file managers, dialogi, ikonki, etc.

**Prawdopodobieństwo:** Wysokie (90%)

**Wpływ:** Niski - mostly cosmetic issues

**Przykłady:**
- GNOME: Nautilus
- KDE: Dolphin
- XFCE: Thunar
- LXDE: PCManFM

**Mitigation:**
1. **Use xdg-open** - Automatic detection of default file manager
2. **Standard .desktop files** - Compatible across all DE
3. **Don't rely on DE-specific features**
4. **Test on major DE** - GNOME, KDE, XFCE

**Probability po mitigation:** Minimal impact

---

### 7. Performance na Proton 🟢 LOW

**Opis:**
Among Us przez Proton może mieć gorszą performance niż native Windows.

**Prawdopodobieństwo:** Niskie (30%)

**Wpływ:** Średni - jeśli performance jest zła, użytkownicy nie będą grać

**Mitigation:**
1. **Use Proton Experimental** - Najnowsza wersja
2. **Recommend Proton GE** - Community-improved Proton
3. **Gamescope** - Optional overlay dla lepszego performance
4. **Documentation** - Tweaking guide dla performance

**Probability po mitigation:** 10%

---

## Ryzyka biznesowe

### 8. Wielkość rynku Linux 📊 MEDIUM

**Opis:**
Linux desktop market share to ~3-4%. Wśród graczy może być niższy.

**Prawdopodobieństwo:** N/A (fact)

**Wpływ:** Wysoki - ROI może być niski

**Analiza:**
- Steam Hardware Survey (2024): ~2% Linux
- Among Us players: unknown % na Linux
- Potencjalnie mały user base

**Mitigation:**
1. **Community-driven** - Treat jako community contribution
2. **Steam Deck** - Steam Deck uses Linux (SteamOS) - większy market
3. **Future-proofing** - Cross-platform architecture benefits future ports (macOS)
4. **Learning experience** - Technical skills valuable dla team

**Decision:** Jeśli ROI jest głównym concern, consider MVP only lub community-led effort

---

### 9. Support Burden 🟡 MEDIUM

**Opis:**
Linux users często mają unique issues z powodu różnych konfiguracji.

**Prawdopodobieństwo:** Wysokie (80%)

**Wpływ:** Średni - increased support workload

**Scenariusze:**
- Custom kernel configurations
- Obscure distro (Gentoo, NixOS, etc.)
- Incompatible drivers
- Missing dependencies
- Permissions issues

**Mitigation:**
1. **Clear system requirements** - "Officially supported: Ubuntu 24.04, Fedora 40, Arch"
2. **Community support** - Linux community often self-supports (forums, Discord)
3. **Diagnostic tools** - Built-in diagnostic output dla troubleshooting
4. **Documentation** - Comprehensive troubleshooting guide
5. **GitHub Issues** - Template for bug reports z system info

**Probability po mitigation:** Moderate but manageable

---

## Ryzyka operacyjne

### 10. Timeline Delays ⏱️ MEDIUM

**Opis:**
8-12 tygodni to ambitious timeline dla tak dużego refactoru.

**Prawdopodobieństwo:** Średnie (60%)

**Wpływ:** Wysoki - delayed release, opportunity cost

**Przyczyny delays:**
- Unexpected technical challenges
- BepInEx compatibility issues
- Learning curve (team nie zna Linux deeply)
- Scope creep
- Testing takes longer than expected

**Mitigation:**
1. **MVP approach** - Release basic version first (Steam only)
2. **Time buffers** - 20% buffer w timeline
3. **Weekly checkpoints** - Assess progress co tydzień
4. **Scope reduction** - Cut Epic support jeśli delayed
5. **Agile sprints** - 2-week sprints z clear deliverables

**Plan B:**
- Week 8: If behind schedule, cut to MVP:
  - Ubuntu-only support
  - Steam-only (no Epic)
  - .deb + AppImage only (no .rpm)
  - Post-MVP Epic + other distro

---

### 11. Lack of Linux Expertise 🎓 MEDIUM

**Opis:**
Zespół może nie mieć deep Linux experience.

**Prawdopodobieństwo:** Zależy od zespołu

**Wpływ:** Wysoki - slower development, more bugs

**Mitigation:**
1. **Hire/consult Linux expert** - Part-time consultant dla code reviews
2. **Learning resources** - Linux development training
3. **Community** - Ask Linux community dla best practices
4. **Open source** - Study other cross-platform .NET apps (Avalonia examples)

---

### 12. Testing Resources 🧪 HIGH

**Opis:**
Trzeba testować na wielu:
- Distro (Ubuntu, Fedora, Arch, Debian)
- Desktop Environments (GNOME, KDE, XFCE)
- Steam versions (native, Flatpak, Snap)
- Proton versions (Experimental, GE, różne wersje)

**Prawdopodobieństwo:** Wysokie (90%)

**Wpływ:** Wysoki - bugs slip through

**Mitigation:**
1. **Virtual Machines** - Setup VMs dla każdej distro
2. **Docker containers** - Dla quick testing
3. **Community beta** - Public beta dla community testing
4. **CI/CD** - Automated tests na różnych environments
5. **Steam Deck** - Jeśli możliwe, test na real hardware

**Infrastructure needed:**
```
VMs:
- Ubuntu 24.04 (GNOME)
- Ubuntu 24.04 (KDE)
- Fedora 40 (GNOME)
- Arch Linux (KDE)
- Debian 12 (XFCE)
```

---

## Mitigation Strategies

### Strategy 1: MVP-First Approach

**Reduce scope to core features:**
- ✅ Steam Among Us detection
- ✅ Mod installation (basic)
- ✅ Mod launching
- ✅ Ubuntu/Debian primary support
- ❌ Epic Games (post-MVP)
- ❌ Advanced features (shortcuts, etc. - post-MVP)

**Benefits:**
- Faster time to market (6 weeks vs 12)
- Lower risk
- Early user feedback
- Can iterate based on actual user needs

---

### Strategy 2: Community Beta Program

**Recruit Linux users dla testing:**
1. Announce beta on Reddit r/linux_gaming, r/AmongUs
2. Discord channel for beta testers
3. GitHub Discussions for feedback
4. Regular beta releases (weekly)

**Benefits:**
- Free testing
- Real-world scenarios
- Community engagement
- Early bug detection

---

### Strategy 3: Comprehensive Documentation

**Create detailed docs:**
- Installation guide per distro
- Troubleshooting guide
- FAQ
- Video tutorials (optional)
- GitHub Wiki

**Benefits:**
- Reduces support burden
- Empowers users to self-solve
- Community can contribute to docs

---

### Strategy 4: Graceful Degradation

**Design for failures:**
- If BepInEx fails → Show error z link do manual guide
- If pkexec fails → Show manual deletion instructions
- If Steam not detected → Allow manual path input
- If dependency missing → Clear error message z install command

**Benefits:**
- Better user experience
- Reduces frustration
- App still usable even with partial failures

---

## Risk Scoring

### Before Mitigation

| Category | Risk Score (1-10) |
|----------|------------------|
| Technical | 8/10 |
| Business | 6/10 |
| Operational | 7/10 |
| **Overall** | **7/10 - HIGH** |

### After Mitigation (with MVP approach)

| Category | Risk Score (1-10) |
|----------|------------------|
| Technical | 5/10 |
| Business | 4/10 |
| Operational | 4/10 |
| **Overall** | **4.3/10 - MEDIUM** |

---

## Rekomendacje końcowe

### Approach: Phased Rollout

**Phase 1: MVP (6 tygodni)**
- Steam support tylko
- Ubuntu/Debian focus
- Basic features
- AppImage + .deb

**Phase 2: Expansion (4 tygodnie)**
- Fedora/Arch support
- Epic Games (Heroic)
- Advanced features
- .rpm packaging

**Phase 3: Polish (2 tygodnie)**
- Bug fixes
- Performance optimization
- Documentation
- Community feedback implementation

**Total:** 12 tygodni (same as original, but with safety checkpoints)

---

**Następny dokument:** [07-EFFORT-ESTIMATION.md](./07-EFFORT-ESTIMATION.md) - Szczegółowe szacowanie wysiłku
