# Dokumentacja Publikacji SUSModder (Velopack)

Ten katalog zawiera kompleksową dokumentację procesu publikacji wersji SUSModder z wykorzystaniem systemu Velopack.

---

## 📚 Dokumenty

### [`RELEASE_GUIDE.md`](RELEASE_GUIDE.md) - Główny Przewodnik ⭐
Kompletny, szczegółowy przewodnik krok po kroku procesu publikacji wersji.

**Zawiera:**
- ✅ Pełny proces publikacji release i beta
- ✅ Wymagania i setup środowiska
- ✅ Komendy PowerShell gotowe do użycia
- ✅ Checklistę publikacji
- ✅ Troubleshooting najczęstszych problemów
- ✅ Diagramy procesu

**Dla kogo:** Każdy kto publikuje nową wersję (zarówno release jak i beta)

---

### [`QUICKSTART.md`](QUICKSTART.md) - Szybki Start 🚀
Skrócona wersja dla doświadczonych użytkowników.

**Zawiera:**
- Minimalistyczny checklist
- Gotowe komendy do skopiowania
- Brak wyjaśnień (tylko akcje)

**Dla kogo:** Osoby znające już proces, potrzebujące szybkiej ściągawki

---

## 🎯 Jak Zacząć

### Pierwszy Raz?
1. Przeczytaj [`RELEASE_GUIDE.md`](RELEASE_GUIDE.md) od początku do końca
2. Sprawdź [sekcję Wymagania](RELEASE_GUIDE.md#wymagania)
3. Przygotuj dane: thumbprint certyfikatu, SSH credentials
4. Wykonaj publikację krok po kroku zgodnie z przewodnikiem

### Masz Doświadczenie?
1. Otwórz [`QUICKSTART.md`](QUICKSTART.md)
2. Skopiuj komendy i dostosuj wersje
3. Wykonaj publikację

### Coś Nie Działa?
1. Sprawdź [sekcję Troubleshooting](RELEASE_GUIDE.md#troubleshooting)
2. Sprawdź [Checklist Publikacji](RELEASE_GUIDE.md#checklist-publikacji)
3. Otwórz GitHub Issue jeśli problem nie jest opisany

---

## 🔑 Kluczowe Informacje

### Struktura Katalogów
```
D:\Development\SUSModder\
├── SUSModder\
│   └── version.json                    ← Aktualizuj to ZAWSZE!
├── publish-velopack-release/           ← Build release (tymczasowy)
├── publish-velopack-beta/              ← Build beta (tymczasowy)
├── releases-release/                   ← Pliki do uploadu (release)
│   ├── RELEASES                        ← MUSI istnieć (bez sufixu)
│   ├── releases.release.json           ← Manifest
│   ├── SUSModder-X.Y.Z-release-full.nupkg
│   ├── SUSModder-X.Y.Z-release-delta.nupkg
│   ├── SUSModder-release-Setup.exe
│   └── SUSModder-release-Portable.zip
└── releases-beta/                      ← Pliki do uploadu (beta)
    ├── RELEASES                        ← MUSI istnieć (bez sufixu)
    ├── releases.beta.json              ← Manifest
    ├── SUSModder-X.Y.Z-beta-beta-full.nupkg
    ├── SUSModder-beta-Setup.exe
    └── SUSModder-beta-Portable.zip
```

### Struktura Serwera SSH
```
/srv/your-path/
├── susmodder/
│   └── releases/
│       ├── release/                    ← Pliki release channel
│       │   ├── RELEASES
│       │   ├── releases.release.json
│       │   ├── *.nupkg (full + delta)
│       │   ├── Setup.exe
│       │   └── Portable.zip
│       └── beta/                       ← Pliki beta channel
│           ├── RELEASES
│           ├── releases.beta.json
│           ├── *.nupkg (full)
│           ├── Setup.exe
│           └── Portable.zip
└── susmodder-velopack/                 ← CRITICAL! API czyta stąd!
    ├── releases.release.json
    └── releases.beta.json
```

### URLs API
- **Release:** `https://susmodder.app/api/releases?channel=release`
- **Beta:** `https://susmodder.app/api/releases?channel=beta`
- **Pliki:** `https://susmodder.app/releases/{channel}/{filename}`

---

## ⚠️ CRITICAL: Najważniejsze Punkty

### 1. Plik RELEASES (bez sufixu)
```powershell
# PO każdym vpk pack MUSISZ skopiować:
Copy-Item 'RELEASES-release' -Destination 'RELEASES' -Force
Copy-Item 'RELEASES-beta' -Destination 'RELEASES' -Force
```

Bez tego pliku Velopack nie będzie działać!

### 2. Manifesty JSON w /susmodder-velopack/
```powershell
# API czyta manifesty z tego katalogu, NIE z /releases/!
pscp releases.release.json user@host:/srv/path/susmodder-velopack/
pscp releases.beta.json user@host:/srv/path/susmodder-velopack/
```

Bez tego API zwróci starą wersję!

### 3. Delta Packages
Velopack automatycznie tworzy delty jeśli poprzednia wersja jest w katalogu:
- `SUSModder-2.2.4-release-delta.nupkg` (2-5 MB vs 52 MB full)
- Oszczędza 90% bandwidth dla użytkowników!

### 4. Code Signing
`--signTemplate` w `vpk pack` podpisuje:
- ✅ Wszystkie 42 pliki .exe w pakiecie
- ✅ Update.exe (Velopack updater)
- ✅ Setup.exe (instalator)

---

## 📊 Metryki Procesu

| Krok | Czas | Rozmiar Plików |
|------|------|----------------|
| dotnet publish | ~30s | N/A |
| vpk pack (release) | ~50s | Full: 52 MB, Delta: 2.5 MB |
| vpk pack (beta) | ~50s | Full: 52 MB |
| Upload release | ~3 min | ~110 MB (full + delta + setup + portable) |
| Upload beta | ~2 min | ~107 MB (full + setup + portable) |
| **TOTAL** | **~15-20 min** | **~217 MB** |

---

## 🛠️ Narzędzia

| Narzędzie | Wersja | Instalacja |
|-----------|--------|------------|
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| Velopack CLI | 0.0.1298+ | `dotnet tool install -g vpk` |
| signtool | Windows SDK | `winget install Microsoft.VisualStudio.2022.BuildTools` |
| PuTTY | Latest | `winget install PuTTY.PuTTY` |

---

## 📝 Changelog

### v2.2.4 + v2.3.12-beta (2025-11-13)
- ✅ Pierwsza pełna publikacja z nowym systemem Velopack
- ✅ Automatyczna delta między 2.2.3 → 2.2.4
- ✅ Pełne podpisywanie wszystkich plików .exe (42 + Update.exe + Setup.exe)
- ✅ Dwa kanały jednocześnie: release + beta
- ✅ Upload manifestów do `/susmodder-velopack/` (CRITICAL)
- ✅ Weryfikacja API i dostępności plików

---

## 🤝 Contributing

Jeśli znalazłeś błąd w dokumentacji lub masz sugestie:
1. Otwórz GitHub Issue
2. Zaproponuj zmiany w PR
3. Opisz problem szczegółowo

---

**Ostatnia aktualizacja:** 2025-11-13
**Wersja dokumentacji:** 1.0
**Autor:** Bartosz Gradzik / AI Assistant
