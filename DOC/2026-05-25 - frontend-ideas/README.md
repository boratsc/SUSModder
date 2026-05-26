# 2026-05-25 – Frontend Ideas

Sesja burzy mózgów z `sus-ui` + review `sus-senior-quality-reviewer` (GLM 5.1).

## Dokumenty

| Plik | Temat | Priorytet | Effort |
|------|-------|-----------|--------|
| [`00-decyzje-negatywne.md`](00-decyzje-negatywne.md) | Co NIE jest potrzebne i dlaczego (10 rzeczy) | – | – |
| [`05-performance-ram.md`](05-performance-ram.md) | Performance i RAM – Dispose, bitmapy, lazy WebView2 | 🔴 P0 | ~2-3h |
| [`01-dll-modal-compatibility.md`](01-dll-modal-compatibility.md) | DLL modal + komunikacja kompatybilności | 🔴 P0 ✅ | ~4-5h → ~2.5h |
| [`03-toast-notifications.md`](03-toast-notifications.md) | Toast notification system (infrastruktura) | 🟡 P1 | ~3-4h |
| [`04-changelog-whatsnew.md`](04-changelog-whatsnew.md) | Changelog / "Co nowego" po aktualizacji | 🟡 P1 | ~2-3h |
| [`09-offline-error-state.md`](09-offline-error-state.md) | Obsługa stanu offline / braku internetu ✨ | 🟡 P1 | ~1-2h |
| [`08-system-tray.md`](08-system-tray.md) | System tray – minimalizacja do zasobnika | 🟡 P1 | ~2-3h |
| [`02-fab-communication.md`](02-fab-communication.md) | FAB – badge, kontekstowa ikona, Discord promo | 🟡 P1 (e/w) | ~2-3h |
| [`06-microinteractions-polish.md`](06-microinteractions-polish.md) | Mikrointerakcje: hover, ripple, skeleton, tooltipy | 🟢 P2 | ~1 dzień |
| [`10-lobby-code-sharing.md`](10-lobby-code-sharing.md) | Udostępnianie kodów lobby (P2P mini-chat) ✨ | 🟢 P2 | ~3-5 dni |
| [`11-lobby-searcher.md`](11-lobby-searcher.md) | Wyszukiwarka lobby (skanowanie serwerów AU) ✨ | 🟢 P2 | ~3-5 dni |
| [`12-voice-chat-integration.md`](12-voice-chat-integration.md) | Integracja voice chat: Discord / BetterCrewLink ✨ | 🟢 P2 | ~1-5 dni |
| [`13-download-speed.md`](13-download-speed.md) | Prędkość pobierania w progress barze ✨ | 🟢 P2 | ~1-2h |
| [`14-splash-video-webview2.md`](14-splash-video-webview2.md) | Splash screen video przez WebView2 | 🟢 P2 | ~3-4h |
| [`15-sharpcompress.md`](15-sharpcompress.md) | Zastąpienie 7z.exe przez SharpCompress (7z + zip z progresem) | 🟢 P2 | ~2-3h |
| [`07-future-features.md`](07-future-features.md) | Dwuetapowy flow, auto-update | ⚪ P3-P4 | 1-5 dni |

## Kolejność implementacji (po review GLM 5.1)

```
P0 ── 1. Performance i RAM ──────────────────── 2-3h
        2. DLL modal + kompatybilność ────────── 4-5h
P1 ── 3. Toast notifications ────────────────── 3-4h  ← infra
        4. Changelog / "Co nowego" ───────────── 2-3h  ← używa toastów
        5. Offline / error state ✨ ──────────── 1-2h
        6. System tray ───────────────────────── 2-3h
        7. FAB badge + ikona (ewentualnie) ───── 2-3h
        ────────────────────────────────────────
        łącznie P0+P1: ~17-24h
P2 ── 8. Mikrointerakcje + voice chat ✨ ────────────────────── 1 dzień
        9. Lobby code sharing ✨ ─────────────── 3-5 dni  ← nowe
       10. Lobby searcher ✨ ─────────────────── 3-5 dni  ← nowe (bazuje na DOC/Lobby-searcher/PoC.md)
P3 ── 11. Dwuetapowy flow + auto-update ─────── kilka dni
```

## Co zmienił review GLM 5.1

| Zmiana | Dlaczego |
|--------|----------|
| **Performance P0** | Memory leak to bug, nie polish |
| **Toasty PRZED Changelog** | Changelog używa toastów – infra przed feature |
| **Offline state ✨** | Przeoczony problem – pusta strona przy braku neta |
| **SystemTray ↓** | Launcher, nie daemon |
| **WebView2 lazy load** | Ładować tylko gdy Epic + auth (oszczędność ~50-100 MB RAM) |
| ~~WebView2 do usunięcia~~ | Używany do Epic login – zostaje |

## Nowe tematy (dodane po review)

| Plik | Temat |
|------|-------|
| [`09-offline-error-state.md`](09-offline-error-state.md) | Empty state + tryb offline przy braku neta |
| [`10-lobby-code-sharing.md`](10-lobby-code-sharing.md) | P2P mini-chat do dzielenia się kodami lobby per mod |
| [`11-lobby-searcher.md`](11-lobby-searcher.md) | Aktywne skanowanie serwerów Among Us w poszukiwaniu lobby |

## Kontekst techniczny

- .NET 10, Avalonia 12.0.3, ReactiveUI, MVVM
- ~10 modów full, kilkanaście DLL
- System kompatybilności: 4 poziomy (F/W/NT/NW)
- Aktualizacje: Velopack
- PL + EN (491+ kluczy i18n)
- WebView2: używany do Epic Games login (lazy-load)

