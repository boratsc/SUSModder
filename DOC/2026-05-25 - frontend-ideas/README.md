# 2026-05-25 – Frontend Ideas

Sesja burzy mózgów z `sus-ui` + review `sus-senior-quality-reviewer` (GLM 5.1).

## Status implementacji (aktualizacja 2026-05-27)

**12 z 17 pomysłów w pełni zaimplementowanych**, 1 zaniechany, 4 pozostałych do realizacji (P1-P3).

Dodatkowo zrealizowano:
- **[POC] Migracja JSON → SQLite** – cała warstwa danych przepisana z plików JSON na `Microsoft.Data.Sqlite` 10.0.8 (✅ zaimplementowane 2026-05-27)
- **Auto-update modów** – cicha aktualizacja modów w tle z progresem UI
- **Post-install dialog** – ekran sukcesu po instalacji moda z wyborem Uruchom / Dodaj DLL

## Dokumenty

| Plik | Temat | Priorytet | Effort | Status |
|------|-------|-----------|--------|--------|
| [`00-decyzje-negatywne.md`](00-decyzje-negatywne.md) | Co NIE jest potrzebne i dlaczego (10 rzeczy) | – | – | ✅ |
| [`05-performance-ram.md`](05-performance-ram.md) | Performance i RAM – Dispose, bitmapy, lazy WebView2 | 🔴 P0 | ~2-3h | ✅ **Zrobione** |
| [`01-dll-modal-compatibility.md`](01-dll-modal-compatibility.md) | DLL modal + komunikacja kompatybilności | 🔴 P0 ✅ | ~2.5h | ✅ **Zrobione** — [status](01-dll-modal-compatibility.status.md) |
| [`03-toast-notifications.md`](03-toast-notifications.md) | Toast notification system (infrastruktura) | 🟡 P1 | ~3-4h | ✅ **Zrobione** |
| [`04-changelog-whatsnew.md`](04-changelog-whatsnew.md) | Changelog / "Co nowego" po aktualizacji | 🟡 P1 | ~2-3h | ✅ **Zrobione** (GitHub API) |
| [`09-offline-error-state.md`](09-offline-error-state.md) | Obsługa stanu offline / braku internetu ✨ | 🟡 P1 | ~1-2h | ⏳ Do zrobienia |
| [`08-system-tray.md`](08-system-tray.md) | System tray – minimalizacja do zasobnika | 🟡 P1 | ~2-3h | ✅ **Zrobione** |
| [`02-fab-communication.md`](02-fab-communication.md) | FAB – badge, kontekstowa ikona, Discord promo | 🟡 P1 (e/w) | ~2-3h | ✅ **Zrobione** |
| [`06-microinteractions-polish.md`](06-microinteractions-polish.md) | Mikrointerakcje: hover, ripple, skeleton, tooltipy | 🟢 P2 | ~1 dzień | ✅ **Zrobione** |
| [`13-download-speed.md`](13-download-speed.md) | Prędkość pobierania w progress barze ✨ | 🟢 P2 | ~1-2h | ✅ **Zrobione** |
| [`15-sharpcompress.md`](15-sharpcompress.md) | Zastąpienie 7z.exe przez SharpCompress (7z + zip z progresem) | 🟢 P2 | ~2-3h | ✅ **Zrobione** |
| [`14-splash-video-webview2.md`](14-splash-video-webview2.md) | Splash screen video przez WebView2 | 🟢 P2 | ~6-8h | 🛑 **Zaniechane** — problemy techniczne WebView2 |
| [`10-lobby-code-sharing.md`](10-lobby-code-sharing.md) | Udostępnianie kodów lobby (P2P mini-chat) ✨ | 🟢 P2 | ~3-5 dni | ⏳ Do zrobienia |
| [`11-lobby-searcher.md`](11-lobby-searcher.md) | Wyszukiwarka lobby (skanowanie serwerów AU) ✨ | 🟢 P2 | ~3-5 dni | ⏳ Do zrobienia (zależne od PoC) |
| [`12-voice-chat-integration.md`](12-voice-chat-integration.md) | Integracja voice chat: Discord / BetterCrewLink ✨ | 🟢 P2 | ~1-5 dni | ⏳ Do zrobienia |
| [`07-future-features.md`](07-future-features.md) | Dwuetapowy flow, auto-update | ⚪ P3-P4 | 1-5 dni | ✅ **Zrobione** (częściowo: PostInstall + auto-update) |

## Kolejność implementacji (po review GLM 5.1)

```
P0 ── 1. Performance i RAM ──────────────────── ✅ 2-3h (zrobione)
        2. DLL modal + kompatybilność ────────── ✅ ~2.5h (zrobione)
P1 ── 3. Toast notifications ────────────────── ✅ 3-4h (zrobione)
        4. Changelog / "Co nowego" ───────────── ✅ 2-3h (zrobione)
        5. Offline / error state ✨ ──────────── ⏳ 1-2h (do zrobienia)
        6. System tray ───────────────────────── ✅ 2-3h (zrobione)
        7. FAB badge + ikona (ewentualnie) ───── ✅ 2-3h (zrobione)
        ────────────────────────────────────────
        z P0+P1 zostało: ~3-5h
P2 ── 8. Mikrointerakcje ────────────────────── ✅ 1 dzień (zrobione)
        9. Lobby code sharing ✨ ─────────────── ⏳ 3-5 dni
       10. Lobby searcher ✨ ─────────────────── ⏳ 3-5 dni (bazuje na DOC/Lobby-searcher/PoC.md)
P3 ── 11. Dwuetapowy flow + auto-update ─────── ✅ kilka dni (zrobione)
        Splash video ─────────────────────────── 🛑 ZANIECHANE
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

| Plik | Temat | Status |
|------|-------|--------|
| [`09-offline-error-state.md`](09-offline-error-state.md) | Empty state + tryb offline przy braku neta | ⏳ Do zrobienia |
| [`10-lobby-code-sharing.md`](10-lobby-code-sharing.md) | P2P mini-chat do dzielenia się kodami lobby per mod | ⏳ Do zrobienia |
| [`11-lobby-searcher.md`](11-lobby-searcher.md) | Aktywne skanowanie serwerów Among Us w poszukiwaniu lobby | ⏳ Do zrobienia |

## Kontekst techniczny

- .NET 10, Avalonia 12.0.3, ReactiveUI, MVVM
- ~10 modów full, kilkanaście DLL
- System kompatybilności: 4 poziomy (F/W/NT/NW)
- Aktualizacje: Velopack
- PL + EN (491+ kluczy i18n)
- WebView2: używany do Epic Games login (lazy-load)

---

## Podsumowanie implementacji (stan na 2026-05-27)

### W pełni zaimplementowane ✅

| # | Temat | Commit | Data |
|---|-------|--------|------|
| 1 | **Performance i RAM** – IDisposable, cache ConfigManager, lazy NativeWebView | `b8931be` | 2026-05-26 |
| 2 | **DLL modal compatibility** – legenda, kontekstowy tytuł, Pomiń wszystko, licznik ukrytych | `(susmodder-3.0)` | 2026-05-25 |
| 3 | **System toast notifications** – 4 typy, FIFO, slide-in, PL/EN | `12f3097` | 2026-05-25 |
| 4 | **Changelog / "Co nowego"** – GitHub API + toast po aktualizacji | `e925818` | 2026-05-26 |
| 5 | **System tray** – minimalizacja, menu PPM, ikonka, first-minimize toast | `06b6a68` | 2026-05-26 |
| 6 | **Mikrointerakcje** – ripple, skeleton loading, tooltipy, button states | `7b43b96` | 2026-05-26 |
| 7 | **Download speed** – prędkość pobierania w progress barze (Steam + Epic) | `f2a7131` | 2026-05-26 |
| 8 | **SharpCompress** – zastąpienie ZipFile, 7z + zip z progresem per-plik | `a60a5b2` | 2026-05-26 |
| 9 | **Post-install dialog** – ekran sukcesu po instalacji moda | `3d90937` | 2026-05-27 |
| 10 | **Auto-update modów** – cicha aktualizacja w tle z progresem UI | `86a5854` | 2026-05-27 |
| 11 | **SQLite migration** – JSON → SQLite, 3 tabele, 7 repozytoriów, migracja one-shot | `3737cb7` | 2026-05-27 |
| 12 | **FAB badge + kontekstowa ikona** – licznik aktualizacji, badge tooltip | `(z AvailableUpdatesCount)` | 2026-05-27 |

### Zaniechane 🛑

| # | Temat | Powód |
|---|-------|-------|
| 14 | **Splash video przez WebView2** | Nierozwiązywalne problemy techniczne z WebView2 (layout skacze, autoplay bug, query string w file:// nie działa). Zostaje statyczny splash screen. |

### Do zrobienia ⏳

| # | Temat | Priorytet | Uwagi |
|---|-------|-----------|-------|
| 5 | **Offline / error state** | 🟡 P1 | Detekcja offline + empty state + tryb offline |
| 10 | **Lobby code sharing** | 🟢 P2 | P2P mini-chat przez Nostr |
| 11 | **Lobby searcher** | 🟢 P2 | Zależne od PoC w Pythonie |
| 12 | **Voice chat integration** | 🟢 P2 | Clair + Discord + BetterCrewLink |

### Szacowany pozostały czas: ~5-10 dni
- Offline state: 1-2h
- Lobby code sharing: 3-5 dni (Nostr)
- Lobby searcher: 3-5 dni (zależne od PoC)
- Voice chat: 1-5 dni (Clair already exists)
- **Razem: ~5-10 dni programistycznych**
