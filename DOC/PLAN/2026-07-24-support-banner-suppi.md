# Support Banner (suppi.pl) — Implementation Plan

> **For agentic workers:** Implement task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Delikatnie poinformować użytkowników o dobrowolnym wsparciu projektu przez suppi.pl (wirtualna kawa), bez nachalnych CTA.

**URL wsparcia:** `https://suppi.pl/susmodder`

**Architecture:** Trzy touchpointy w tym samym tonie: (1) zamykalna belka nad `BrowserToolbar` z cooldownem 7 dni w `user_settings`, (2) karta w `InfoPanel`, (3) dyskretny link w stopce `AppSettingsView`. Link otwiera URL w przeglądarce (`Process.Start` + `UseShellExecute`).

**Tech Stack:** Avalonia XAML, ReactiveUI / MainWindowViewModel, UserSettings + SQLite, lokalizacja pl.json/en.json.

**Design (zatwierdzony 2026-07-24):**
- Belka styl B: ikona ☕ + tekst open-source + link `suppi.pl →` + X
- Po X: ukryta 7 dni, potem wraca
- Info: karta „Wesprzyj projekt” nad copyrightem
- Settings: link w lewej części stopki
- Bez status bara / post-install / changelogu

## Global Constraints

- Ton: open source, całkowicie dobrowolne, wirtualna kawa — nie paywall
- URL: `https://suppi.pl/susmodder` (stała `ProjectSupport.SuppiUrl`)
- Motywy: Dark / Pink / Light / Szklany — brushy dynamiczne
- Commit messages po polsku gdy commitowane

---

## Task 1: Persystencja dismiss belki

**Files:**
- `SUSModder.Core/Configuration/UserSettings.cs`
- `SUSModder.Core/Data/UserSettingsRepository.cs`
- `SUSModder.Core/Data/DatabaseService.cs` (migracja v13)
- `SUSModder.Core/Utilities/SupportBannerPolicy.cs`

- [x] Dodać `SupportBannerDismissedAt` (ISO string w SQLite)
- [x] Migracja schema + mapowanie CRUD
- [x] Helper: `SupportBannerPolicy.ShouldShow` → true jeśli null lub `now - dismissed >= 7 dni`

## Task 2: Belka w MainWindow

**Files:**
- `SUSModder/Views/MainWindow.axaml`
- `SUSModder/ViewModels/MainWindowViewModel.SupportBanner.cs`
- `SUSModder/Services/ProjectSupport.cs`
- `SUSModder/Localization/pl.json`, `en.json`

- [x] Wstawić belkę nad `BrowserToolbar`
- [x] Binding `IsSupportBannerVisible`, komendy Open + Dismiss
- [x] Dismiss zapisuje timestamp i chowa belkę

## Task 3: InfoPanel + AppSettings footer

**Files:**
- `SUSModder/Views/InfoPanel.axaml` (+ `.cs`)
- `SUSModder/Views/AppSettingsView.axaml` (+ `.cs`)
- lokalizacja `Support.*`

- [x] Karta wsparcia w Informacjach
- [x] Link w stopce Ustawień (lewo)

## Task 4: Weryfikacja

- [x] `dotnet build SUSModder.sln`
- [x] Testy `SupportBannerPolicy` (4/4)
- [ ] Ręcznie: belka widoczna → X → znika; Info + Settings link otwierają URL
