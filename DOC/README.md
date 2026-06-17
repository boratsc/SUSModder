# Dokumentacja projektu SUSModder

Ten katalog zawiera dokumentację roboczą i techniczną dla aktualnego repozytorium.

## Aktualna struktura

- `Core/` — opis kluczowych obszarów `SUSModder.Core`.
- `Frontend/` — dokumentacja warstwy Avalonia/MVVM.
- `PLAN/` — miejsce na nowe aktywne plany; po cleanupie Beta 1 nie zawiera aktywnych planów funkcjonalnych.
- `POC/` — tylko POC-e nadal otwarte lub świadomie post-beta.
- `Updater/` — krótka dokumentacja legacy updatera; obecnym mechanizmem aktualizacji jest Velopack.
- `_archive/` — zamknięte plany, stare refaktoryzacje, raporty review i historyczna dokumentacja.

## Zasady porządku

1. Nowy aktywny plan dodawaj do `PLAN/` z jasnym statusem na początku pliku.
2. Po zamknięciu funkcji lub review przenieś dokument do `_archive/PLAN/...` albo odpowiedniego katalogu archiwum.
3. Wyniki lokalnych testów, zrzuty API i pliki tymczasowe nie powinny trafiać do `DOC/` ani `SKRYPTY/`.
4. Dokumenty historyczne z nieaktualnymi instrukcjami build/release trzymaj w `_archive/`, nie w aktywnych katalogach.

## Beta 1

Review Beta 1 został zamknięty jako `READY FOR BETA 1`. Materiały z tego review przeniesiono do:

- `_archive/PLAN/beta-1/`
- `_archive/PLAN/discord-oauth2/`
- `_archive/PLAN/stale-post-beta/`

Po dodatkowej weryfikacji lokalnego kodu przeniesiono też zakończone plany, POC-e i frontend ideas do:

- `_archive/PLAN/completed/`
- `_archive/POC/completed/`
- `_archive/Frontend/completed/2026-05-25 - frontend-ideas/`
