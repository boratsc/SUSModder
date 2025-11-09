# Steam Integration - Quick Overview

**Status**: PoC Complete
**Data**: 2025-11-07

## Problem

Obecny system pobierania gier ze Steam (własne repo) łamie EULA. Potrzebujemy legalnej metody.

## Rozwiązanie: Hybrydowe Podejście

### Opcja 1: SteamCMD (Domyślna)
- ✅ **Oficjalny tool Valve** - zero ryzyka prawnego
- ❌ Email-based Steam Guard (gorsze UX)
- ❌ Brak real-time progress reporting

### Opcja 2: DepotDownloader (Opt-in)
- ✅ **QR code authentication** - świetne UX
- ✅ Real-time progress reporting
- ⚠️ Community tool (gray area prawnie, ale szeroko akceptowane)

## Dlaczego Hybrydowe?

1. **Bezpieczeństwo**: SteamCMD jako safe default
2. **User choice**: Każdy może wybrać co preferuje
3. **Gradual rollout**: Obserwuj feedback przed pełnym commitem do jednej metody
4. **Fallback**: Oficjalna opcja zawsze dostępna

## Porównanie

| Feature | SteamCMD | DepotDownloader | Legendary (Epic) |
|---------|----------|-----------------|------------------|
| **Legal Status** | ✅ Official | ⚠️ Community | ✅ Official |
| **Auth UX** | ⭐⭐ Email codes | ⭐⭐⭐⭐ QR code | ⭐⭐⭐⭐⭐ Web OAuth |
| **Progress** | ⭐⭐ Unreliable | ⭐⭐⭐⭐ Event-based | ⭐⭐⭐⭐⭐ Regex parsing |
| **Speed** | ~100 Mbit/s | ~15 Mbit/s | ~100 Mbit/s |
| **Integration** | ⭐⭐⭐ NuGet | ⭐⭐⭐⭐ NuGet | ⭐⭐⭐⭐⭐ Process |

## Quick Links

📄 **[Pełna analiza techniczna](STEAM_INTEGRATION_POC.md)** - szczegóły implementacji, code examples, trade-offs

## Implementation Plan

- **Phase 1**: PoC (scaffold, manual testing) - 1-2 dni
- **Phase 2**: MVP z SteamCMD - 3-5 dni
- **Phase 3**: DepotDownloader integration - 2-3 dni
- **Phase 4**: Polish & docs - 1-2 dni

**Total**: 7-12 dni roboczych

## Następne Kroki

1. ✅ Review tego dokumentu
2. ⏳ Decyzja: start implementacji?
3. ⏳ Phase 1: Scaffold `SteamVersionManager.cs`
4. ⏳ Manual testing z Among Us

---

**TL;DR**: Rekomendacja = **SteamCMD (default) + DepotDownloader (opt-in)**. Legal + flexible + user choice.
