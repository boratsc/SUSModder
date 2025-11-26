# Changelog - Wersja 2.3.14-beta

**Data wydania:** 2025-11-18  
**Typ:** Beta Release  
**Główny fix:** Nieskończona pętla reinstalacji Epic

---

## 🐛 Naprawione błędy (Fixed)

### Epic Launch - Infinite Reinstallation Loop
**Problem:** Użytkownicy raportowali że przy uruchamianiu modów Epic aplikacja wpadała w nieskończoną pętlę reinstalacji (nawet 10+ prób). Gra nigdy się nie uruchamiała.

**Rozwiązanie:**
- ✅ **Dodano limit prób reinstalacji** - maksymalnie 2 próby zamiast nieskończenie
- ✅ **Weryfikacja plików przed importem** - sprawdza czy `Among Us.exe` istnieje przed próbą importu
- ✅ **Weryfikacja po instalacji** - potwierdza czy Legendary faktycznie zainstalował pliki
- ✅ **Ulepszone logowanie** - pokazuje numer próby (`1/2`, `2/2`) i dokładne ścieżki

**Dziennik zmian technicznych:**
```
- SUSModder.Core/GameIntegration/EpicVersionManager.cs:
  - Dodano _retryCount i MaxRetryAttempts (=2)
  - Weryfikacja Among Us.exe przed importem (linia 433-443)
  - Weryfikacja po instalacji (linia 670-680)
  - Recursive retry w PerformReinstallationSequence (linia 647-652)
  - Lepsze komunikaty błędów z numerem próby
```

**Testowano:**
- ✅ Import fail → Auto-reinstalacja → Launch OK
- ✅ Obie próby fail → Dialog błędu (NIE infinite loop)
- ✅ Brak plików w InstallPath → Pełna reinstalacja

---

## 📝 Pełny changelog entry (do głównego CHANGELOG.md)

```markdown
## [2.3.14-beta] - 2025-11-18

### Fixed
- **Epic launch**: Naprawiono nieskończoną pętlę reinstalacji przy błędach uruchamiania
  - Dodano limit prób reinstalacji (max 2 próby)
  - Dodano weryfikację plików przed importem i po instalacji
  - Ulepszone logowanie dla łatwiejszego debugowania
  - Po 2 nieudanych próbach pokazywany jest dialog błędu zamiast dalszych prób
```

---

## 🔐 Informacje o podpisywaniu

**Certyfikat:**
- Wydawca: Certum Code Signing 2021 CA
- Właściciel: Open Source Developer, Bartosz Gradzik
- Thumbprint: `97171de086564a84fa22a72c4260f72ba13096c6`

**Podpisane pliki:**
- ✅ `SUSModder.exe` (główna aplikacja)
- ✅ `tools/7z.exe` (narzędzie do archiwów)
- ✅ `createdump.exe` (.NET diagnostic tool)
- ✅ `SUSModder-beta-Setup.exe` (instalator)

---

## 📦 Pliki wydania

Lokalizacja: `C:\Users\borat\Desktop\SUSModder\releases-beta\`

| Plik | Rozmiar | Opis |
|------|---------|------|
| `RELEASES` | ~1 KB | Manifest Velopack (KRYTYCZNY!) |
| `releases.beta.json` | ~1 KB | Manifest API |
| `SUSModder-2.3.14-beta-beta-full.nupkg` | 51.63 MB | Pakiet aplikacji |
| `SUSModder-beta-Setup.exe` | 54.10 MB | Instalator (podpisany) |
| `SUSModder-beta-Portable.zip` | 51.63 MB | Wersja portable |

**SHA256 checksums:**
- .nupkg: `0EEFD169C3573EE4FAD12F2C7B735E3880F3D9EED056AA9313156272DD5CB721`

---

## 🚀 Deployment Checklist

### 1. Upload plików na serwer
```bash
# Lokacja na serwerze: /srv/synapsekit-boracik/nginx/html/susmodder/releases/beta/
# Plik RELEASES MUSI być bez sufixu -beta (już skopiowany)

scp releases-beta/RELEASES debian@vps-b99a39c3.vps.ovh.net:/srv/.../beta/
scp releases-beta/releases.beta.json debian@vps-b99a39c3.vps.ovh.net:/srv/.../beta/
scp releases-beta/SUSModder-2.3.14-beta-beta-full.nupkg debian@vps-b99a39c3.vps.ovh.net:/srv/.../beta/
scp releases-beta/SUSModder-beta-Setup.exe debian@vps-b99a39c3.vps.ovh.net:/srv/.../beta/
scp releases-beta/SUSModder-beta-Portable.zip debian@vps-b99a39c3.vps.ovh.net:/srv/.../beta/
```

### 2. CRITICAL: Upload JSON do API location
```bash
# To jest KLUCZOWE - API czyta z tego folderu, nie z /releases/beta/!
scp releases-beta/releases.beta.json debian@vps-b99a39c3.vps.ovh.net:/srv/.../susmodder-velopack/
```

### 3. Weryfikacja
```bash
# Test API endpoint
curl https://susmodder.app/api/releases?channel=beta | jq

# Sprawdź czy zwraca version: "2.3.14-beta"
# Sprawdź czy downloadBaseUrl jest poprawny
```

### 4. Test w aplikacji
- Zainstaluj/uruchom aplikację z kanałem beta
- Kliknij "Sprawdź aktualizacje"
- Powinien wykryć wersję 2.3.14-beta
- Test pobierania i instalacji

### 5. Test fix dla Epic launch
- Przetestuj uruchomienie moda Epic z różnymi scenariuszami
- Sprawdź czy logi pokazują numer próby reinstalacji
- Upewnij się że nie ma infinite loop (max 2 próby)

---

## 📚 Dokumentacja

- **Technical fix details**: `DOC/EPIC_LAUNCH_FIX_2025-11-18.md`
- **Code signing guide**: `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`
- **Deployment guide**: `DOC/Updater-Refactoring/SIGNED_DEPLOYMENT_GUIDE.md`

---

## 👥 Testing Notes

**Dla testerów:**
Po aktualizacji do 2.3.14-beta:
1. Uruchom mod Epic (np. Town of Us)
2. Jeśli instalacja fail → sprawdź logi
3. Powinno być max 2 próby reinstalacji (nie 10+)
4. Po 2 próbach → dialog błędu z logami
5. Zgłoś feedback: czy fix działa?

**Logi do sprawdzenia:**
- `legendary.log.txt` w katalogu aplikacji
- Okno diagnostyczne w aplikacji (Konsola)
- Szukaj tekstu: "próba 1/2", "próba 2/2", "Przekroczono limit prób"

---

## ℹ️ Known Issues

Brak znanych problemów w tej wersji. Jeśli znajdziesz bug, zgłoś na Discord.

---

**Build by:** Claude AI + Bartosz Gradzik  
**Build date:** 2025-11-18 23:20 CET  
**Build time:** ~3 minuty (publish + signing + packaging)
