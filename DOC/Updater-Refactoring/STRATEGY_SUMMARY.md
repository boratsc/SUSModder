# Strategia Wydania v2.2.0 - Podsumowanie

**Data:** 2025-11-06
**Cel:** Bezpieczna migracja z legacy updater na Velopack + wprowadzenie systemu kanałów beta

---

## 🎯 Strategia w Skrócie

### Problem
- Użytkownicy na **v2.0.1** używają **legacy updater** (Updater.exe + ZIP download)
- Nowy system **Velopack** jest lepszy (delta updates, stabilne ścieżki, lepsza reputacja AV)
- Potrzeba **bezpiecznej migracji** bez zostawiania użytkowników w tyle

### Rozwiązanie: Dual Release
```
┌─────────────────────────────────────────────────────────┐
│ v2.0.1 (legacy updater)                                 │
└───────────────┬─────────────────────────────────────────┘
                │
    ┌───────────┴───────────┐
    │                       │
    │ Legacy Update         │ Nowe instalacje
    │ (ZIP download)        │ (Setup.exe)
    ↓                       ↓
┌─────────────────────────────────────────────────────────┐
│ v2.2.0 (release)                                        │
│ ├─ Legacy ZIP (zawiera Velopack framework)              │
│ ├─ Velopack release package                             │
│ └─ Velopack beta package (2.3.0-beta)                   │
└───────────────┬─────────────────────────────────────────┘
                │
                │ Kolejne aktualizacje
                ↓
┌─────────────────────────────────────────────────────────┐
│ Przyszłe wersje (tylko Velopack)                        │
│ ├─ v2.2.1, v2.2.2 (bugfix - delta updates)              │
│ ├─ v2.3.0-beta (nowe funkcje testowe)                   │
│ └─ v2.4.0 (kolejny stabilny release)                    │
└─────────────────────────────────────────────────────────┘
```

---

## 📋 Plan Działania

### Faza 1: Build i Packaging ✅

**Skrypt:** `.\SKRYPTY\Build\build-release-2.2.0.ps1`

**Co robi:**
1. Buduje **legacy ZIP** z Updater.exe (dla v2.0.1 → v2.2.0)
2. Buduje **Velopack release** (2.2.0 stable)
3. Buduje **Velopack beta** (2.3.0-beta - następny cykl)
4. **Podpisuje wszystkie pliki .exe** (opcjonalnie)

**Użycie:**
```powershell
# Metoda 1: Interaktywny helper (REKOMENDOWANE) ⭐
# Skrypt zapyta o thumbprint certyfikatu
.\SKRYPTY\Build\sign-and-build.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta"
# Domyślny Certum thumbprint: YOUR_CERTIFICATE_THUMBPRINT_HERE

# Metoda 2: Z thumbprint (Certum - obecna konfiguracja)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Metoda 3: Z plikiem PFX (alternatywna)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "HasłoDoCertyfikatu"

# Bez podpisywania (tylko dla testów)
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -SkipSigning
```

**Output:**
```
releases-legacy/
└── SUSModder-2.2.0-legacy.zip           (~50 MB)

releases-release/
├── SUSModder-2.2.0-release-full.nupkg   (~50 MB)
├── RELEASES
└── releases.release.json

releases-beta/
├── SUSModder-2.3.0-beta-beta-full.nupkg (~50 MB)
├── RELEASES
└── releases.beta.json
```

### Faza 2: Backend Configuration

**Wymagane endpointy:**

#### 1. Legacy version check (dla v2.0.2 i starszych)
```javascript
// GET /api/susmodder-current-version?current=2.0.2
{
    "currentVersion": "2.2.0",
    "downloadUrl": "/api/download-latest",
    "updateType": "legacy"
}
```

#### 2. Legacy download (ZIP)
```javascript
// GET /api/download-latest
// → Zwraca: /susmodder-versions/SUSModder-2.2.0.zip
// → URL: https://susmodder.boracik.pl/SUSModder-2.2.0.zip
```

#### 3. Velopack manifest (nowe instalacje i v2.2.0+)
```javascript
// GET /api/releases?channel=release
{
    "success": true,
    "channel": "release",
    "latestVersion": "2.2.0",
    "manifest": {
        "LatestVersion": "2.2.0",
        "Releases": [{
            "Version": "2.2.0",
            "File": "SUSModder-2.2.0-release-full.nupkg",
            "SHA256": "...",
            "Channel": "release"
        }]
    },
    "downloadBaseUrl": "https://susmodder.app/releases/release"
}

// GET /api/releases?channel=beta
{
    "success": true,
    "channel": "beta",
    "latestVersion": "2.3.0-beta",
    "manifest": { ... },
    "downloadBaseUrl": "https://susmodder.app/releases/beta"
}
```

**Struktura plików na serwerze:**
```
/srv/synapsekit-boracik/nginx/html/
├── susmodder/
│   └── releases/
│       ├── legacy/
│       │   └── SUSModder-2.2.0-legacy.zip (backup)
│       ├── release/
│       │   ├── SUSModder-2.2.0-release-full.nupkg
│       │   ├── RELEASES
│       │   └── releases.release.json
│       └── beta/
│           ├── SUSModder-2.3.0-beta-beta-full.nupkg
│           ├── RELEASES
│           └── releases.beta.json
├── susmodder-velopack/
│   ├── releases.release.json  ← API czyta stąd
│   └── releases.beta.json     ← API czyta stąd
└── susmodder-versions/
    └── SUSModder-2.2.0.zip     ← Główny link dla legacy users (2.0.2 → 2.2.0)
```

### Faza 3: Testing

#### Test 1: Migracja v2.0.2 → v2.2.0
```
1. Zainstaluj v2.0.2 (obecna wersja produkcyjna)
2. Kliknij "Sprawdź aktualizacje"
3. ✅ Powinien znaleźć v2.2.0
4. ✅ Pobrać ZIP z https://susmodder.boracik.pl/SUSModder-2.2.0.zip (~50MB)
5. ✅ Uruchomić Updater.exe
6. ✅ Zrestartować się do v2.2.0
7. ✅ Velopack framework zainstalowany (Update.exe w katalogu nadrzędnym)
```

#### Test 2: Update Velopack (v2.2.0 → v2.2.1)
```
1. Zbuduj testową wersję 2.2.1
2. Upload do serwera testowego
3. W aplikacji v2.2.0: "Sprawdź aktualizacje"
4. ✅ Powinien znaleźć v2.2.1
5. ✅ Pobrać DELTA package (~5-10MB, nie full)
6. ✅ Automatyczny restart
7. ✅ Wersja: 2.2.1
```

#### Test 3: Przełączanie kanałów
```
1. W aplikacji v2.2.0 (release)
2. Ustawienia → Zaawansowane → Kanał aktualizacji: Beta
3. Zapisz
4. "Sprawdź aktualizacje"
5. ✅ Powinien znaleźć v2.3.0-beta
6. ✅ Pobrać FULL package (~50MB)
7. ✅ Restart
8. ✅ Wersja: 2.3.0-beta
```

#### Test 4: Nowa instalacja
```
1. Pobierz Setup.exe z releases-release/
2. Uruchom instalator
3. ✅ Aplikacja w {InstallDir}\current\SUSModder.exe
4. ✅ Update.exe w {InstallDir}\
5. ✅ Start menu shortcut
```

### Faza 4: Deployment

**Checklist:**
- [ ] Zbuduj wszystkie pakiety (`build-release-2.2.0.ps1`)
- [ ] Zweryfikuj podpisy cyfrowe (jeśli signing enabled)
- [ ] Zaktualizuj backend endpoints
- [ ] Upload plików:
  - [ ] Legacy ZIP → `/releases/legacy/`
  - [ ] Release channel → `/releases/release/`
  - [ ] Beta channel → `/releases/beta/`
- [ ] Przetestuj wszystkie 4 testy na czystej maszynie
- [ ] Deploy backend na produkcję
- [ ] Ogłoszenie release (Discord, social media)
- [ ] Monitoruj logi przez pierwsze 24h

### Faza 5: Monitoring

**Metryki sukcesu:**

**Po 1 tygodniu:**
- ✅ >60% użytkowników na v2.2.0+
- ✅ <5% błędów aktualizacji
- ✅ <10% zgłoszeń problemów

**Po 2 tygodniach:**
- ✅ >80% użytkowników na v2.2.0+
- ✅ Brak krytycznych bugów

**Po 4 tygodniach:**
- ✅ >95% użytkowników na v2.2.0+
- ✅ Można rozważyć usunięcie legacy endpoint (zostawić dla grace period)

---

## 🔐 Code Signing

### Obecna Konfiguracja
- **Dostawca**: Certum (http://time.certum.pl)
- **Metoda**: Certyfikat w Windows Certificate Store
- **Thumbprint**: `YOUR_CERTIFICATE_THUMBPRINT_HERE`

### Dlaczego?
- ✅ Windows nie pokazuje "Unknown publisher"
- ✅ Mniejsza szansa na SmartScreen block
- ✅ Lepszy reputation w antywirusach
- ✅ Profesjonalny wizerunek

### Jak?
**Opcja 1: Interaktywny helper (najłatwiejsze)**
```powershell
.\SKRYPTY\Build\sign-and-build.ps1 -ReleaseVersion "2.2.0"
# Skrypt pokaże dostępne certyfikaty i zapyta o wybór
# Domyślny Certum: wystarczy nacisnąć ENTER
```

**Opcja 2: Bezpośrednio z thumbprint**
```powershell
# Użyj istniejącego certyfikatu Certum
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Znajdź certyfikaty w systemie
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert
```

**Opcja 3: Z plikiem PFX (jeśli zmienisz dostawcę)**
```powershell
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -CertificatePath "C:\Certs\new-cert.pfx" `
    -CertificatePassword "Password"
```

**Szczegóły:** Zobacz `DOC/Updater-Refactoring/CODE_SIGNING_GUIDE.md`

---

## 📊 Numeracja Wersji (Kernel Style)

**Stabilne:** Parzyste drugie cyfry
```
2.0.0 → 2.2.0 → 2.4.0 → 2.6.0
```

**Beta:** Nieparzyste drugie cyfry
```
2.1.0-beta → 2.3.0-beta → 2.5.0-beta
```

**Przykładowy cykl:**
```
2.2.0 (release)           ← Stabilna wersja
  ├─→ 2.2.1 (bugfix)      ← Drobne poprawki
  ├─→ 2.2.2 (bugfix)
  └─→ 2.3.0-beta (beta)   ← Nowe funkcje testowe
       ├─→ 2.3.1-beta     ← Beta bugfix
       └─→ 2.4.0 (release) ← Nowa stabilna wersja
            └─→ 2.5.0-beta (beta)
```

---

## 🚨 Rollback Plan

### Scenariusz: Krytyczny bug w v2.2.0

**Natychmiastowe działania:**

1. **Wycofaj Velopack manifest:**
   ```bash
   # Przywróć poprzednią wersję
   ssh server "cp /var/www/susmodder/releases/release/releases.release.json.backup \
               /var/www/susmodder/releases/release/releases.release.json"
   ```

2. **Zmień endpoint `/api/susmodder-current-version`:**
   ```javascript
   return res.json({
       currentVersion: "2.0.1", // Rollback
       downloadUrl: "/api/download-latest",
       updateType: "legacy"
   });
   ```

3. **Komunikat dla użytkowników:**
   - In-app notification: "Wykryto problem, pracujemy nad poprawką"
   - Discord/social media announcement

4. **Hotfix (v2.2.1):**
   - Napraw bug
   - Przetestuj dokładnie
   - Deploy jako emergency update

---

## 📚 Dokumentacja

Szczegółowe przewodniki:

- **`RELEASE_220_MIGRATION.md`** - Pełna instrukcja wydania v2.2.0
- **`CODE_SIGNING_GUIDE.md`** - Poradnik podpisywania cyfrowego
- **`UPDATE_CHANNELS.md`** - System kanałów release/beta
- **`MIGRATION_PLAN.md`** - Strategia migracji dla użytkowników
- **`VELOPACK_STATUS.md`** - Obecny status implementacji Velopack

---

## ✅ Quick Start Checklist

**Przed release:**
```bash
# 1. Upewnij się, że Velopack CLI jest zainstalowany
dotnet tool install -g vpk

# 2. Przygotuj certyfikat (opcjonalnie)
# - Kup certyfikat code signing
# - Zapisz jako PFX z hasłem

# 3. Zbuduj pakiety
.\SKRYPTY\Build\build-release-2.2.0.ps1 `
    -ReleaseVersion "2.2.0" `
    -NextBetaVersion "2.3.0-beta" `
    -CertificatePath "C:\Certs\susmodder.pfx" `
    -CertificatePassword "YourPassword"

# 4. Zweryfikuj output
dir releases-legacy, releases-release, releases-beta

# 5. Przetestuj lokalnie
# - Zainstaluj v2.0.1
# - Przetestuj update do v2.2.0

# 6. Upload na serwer
scp releases-legacy/* server:/var/www/susmodder/releases/legacy/
scp releases-release/* server:/var/www/susmodder/releases/release/
scp releases-beta/* server:/var/www/susmodder/releases/beta/

# 7. Zaktualizuj backend API

# 8. Deploy i monitoruj
```

**Po release:**
```bash
# Monitoruj logi
ssh server "tail -f /var/log/nginx/susmodder-access.log"

# Sprawdź telemetrię
# - Ile osób zaktualizowało się?
# - Czy są błędy aktualizacji?

# Reaguj szybko na problemy
# - Discord support channel
# - GitHub issues
```

---

## 💡 Tips

1. **Grace period dla legacy endpoint**: Trzymaj przez 3-6 miesięcy po release
2. **Telemetria**: Dodaj tracking updater type ("legacy" vs "velopack")
3. **Komunikacja**: Informuj użytkowników o nowym systemie (changelog, Discord)
4. **Beta testerzy**: Zachęć zaawansowanych użytkowników do testowania beta channel
5. **Backup**: Zawsze trzymaj poprzednie wersje na serwerze (rollback)

---

**Status:** ✅ Gotowe do wdrożenia
**Autor:** SUSModder Team
**Data:** 2025-11-06
