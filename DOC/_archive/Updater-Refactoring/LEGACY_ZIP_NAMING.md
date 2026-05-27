# Legacy ZIP - System Nazewnictwa i Lokalizacji

## Przegląd

Legacy ZIP (dla użytkowników na wersji 2.0.2 i starszych) jest uploadowany do **dwóch** lokalizacji z różnymi nazwami:

## Lokalizacje

### 1. `/susmodder-versions/` - Główna Lokalizacja ⭐

**Ścieżka:** `/srv/synapsekit-boracik/nginx/html/susmodder-versions/`

**Nazwa pliku:** `SUSModder-X.Y.Z.zip` (wersjonowana)

**Przykład:**
```
susmodder-versions/
├── SUSModder-2.2.0.zip
├── SUSModder-2.2.1.zip
└── SUSModder-2.4.0.zip
```

**URL:** `https://susmodder.app/SUSModder-2.2.0.zip`

**Przeznaczenie:**
- Główny link dla użytkowników na v2.0.2 i starszych
- Backend zwraca ten link w `/api/download-latest`
- **Nomenklatura ważna**: Musi być `SUSModder-X.Y.Z.zip` (bez sufixu "-legacy")

**Dlaczego?**
- Historyczny system nazewnictwa (używany od początku projektu)
- Użytkownicy 2.0.2 oczekują tej nazwy
- Czytelna wersja w URL

### 2. `/susmodder/releases/legacy/` - Backup/Archiwum

**Ścieżka:** `/srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/`

**Nazwa pliku:** `SUSModder-X.Y.Z-legacy.zip` (z sufiksem)

**Przykład:**
```
susmodder/releases/legacy/
├── SUSModder-2.2.0-legacy.zip
├── SUSModder-2.2.1-legacy.zip
└── SUSModder-2.4.0-legacy.zip
```

**URL:** `https://susmodder.app/releases/legacy/SUSModder-2.2.0-legacy.zip`

**Przeznaczenie:**
- Backup/archiwum
- Zgodność z nową strukturą Velopack
- Fallback w razie problemów

## Proces Deployment

### Skrypt `deploy-to-server.ps1`

Automatycznie uploaduje Legacy ZIP do obu lokalizacji:

```powershell
.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"
```

**Co się dzieje:**
1. Pobiera `releases-legacy/SUSModder-2.2.0-legacy.zip` (lokalnie)
2. **Upload #1**: Do `/susmodder/releases/legacy/` jako `SUSModder-2.2.0-legacy.zip`
3. **Upload #2**: Do `/susmodder-versions/` jako `SUSModder-2.2.0.zip` (zmienia nazwę!)

### Kod w Skrypcie

```powershell
# Upload #1: Backup
Upload-Files -LocalPath $legacyDir -RemotePath "$($serverPaths.Releases)/legacy"

# Upload #2: Versions (z nową nazwą)
$legacyZip = Get-ChildItem $legacyDir -Filter "*.zip" | Select-Object -First 1
$versionedName = "SUSModder-$ReleaseVersion.zip"  # Bez "-legacy"!

pscp $legacyZip.FullName "${Username}@${Server}:$($serverPaths.Versions)/$versionedName"
```

## Backend API

### Endpoint: `/api/download-latest`

**Dla użytkowników 2.0.2:**
```javascript
app.get('/api/download-latest', (req, res) => {
    const userVersion = req.query.current || '2.0.2';
    
    // Główna lokalizacja - susmodder-versions
    const versionedFile = `/srv/synapsekit-boracik/nginx/html/susmodder-versions/SUSModder-2.2.0.zip`;
    
    if (fs.existsSync(versionedFile)) {
        res.download(versionedFile);
        // URL zwrócony użytkownikowi: https://susmodder.app/SUSModder-2.2.0.zip
    } else {
        // Fallback do legacy folder
        const legacyFile = `/srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/SUSModder-2.2.0-legacy.zip`;
        res.download(legacyFile);
    }
});
```

## Przykład: Release v2.2.0

### Build Lokalny
```
releases-legacy/
└── SUSModder-2.2.0-legacy.zip  (~50 MB)
```

### Po Deployment
```
Serwer:
├── /srv/synapsekit-boracik/nginx/html/susmodder-versions/
│   └── SUSModder-2.2.0.zip                    ← Główny link
│
└── /srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/
    └── SUSModder-2.2.0-legacy.zip             ← Backup
```

### Użytkownik na v2.0.2
```
1. Aplikacja wysyła: GET /api/susmodder-current-version?current=2.0.2
2. Backend odpowiada: { "currentVersion": "2.2.0", "downloadUrl": "/api/download-latest" }
3. Aplikacja pobiera: GET /api/download-latest
4. Backend zwraca: https://susmodder.app/SUSModder-2.2.0.zip
5. Użytkownik pobiera ZIP (~50MB)
6. Updater.exe instaluje
7. Restart → v2.2.0 z Velopack framework
```

## Historia Wersji

### v2.0.2 (Obecna Produkcja)
- Używa `/susmodder-versions/SUSModder-X.Y.Z.zip`
- Legacy updater (Updater.exe)

### v2.2.0 (Nowa Wersja)
- **Dual system**:
  - Legacy users → `/susmodder-versions/SUSModder-2.2.0.zip`
  - Nowi users → Velopack Setup.exe
- Po update z 2.0.2 → 2.2.0: Velopack framework zainstalowany

### v2.2.1+ (Przyszłe Wersje)
- Użytkownicy z v2.2.0+ używają Velopack (delta updates)
- Legacy endpoint pozostaje dla spóźnialskich z 2.0.2

## Manualne Upload (Alternatywa)

```bash
# SSH do serwera
ssh debian@vps-b99a39c3.vps.ovh.net

# Utwórz katalogi (jeśli nie istnieją)
mkdir -p /srv/synapsekit-boracik/nginx/html/susmodder-versions
mkdir -p /srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy

# Exit z SSH
exit

# Upload przez SCP
cd releases-legacy

# Upload #1: Versions (zmień nazwę!)
scp SUSModder-2.2.0-legacy.zip debian@vps-b99a39c3.vps.ovh.net:/tmp/
ssh debian@vps-b99a39c3.vps.ovh.net "mv /tmp/SUSModder-2.2.0-legacy.zip /srv/synapsekit-boracik/nginx/html/susmodder-versions/SUSModder-2.2.0.zip"

# Upload #2: Legacy (bez zmiany nazwy)
scp SUSModder-2.2.0-legacy.zip debian@vps-b99a39c3.vps.ovh.net:/srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/

# Weryfikacja
ssh debian@vps-b99a39c3.vps.ovh.net "ls -lh /srv/synapsekit-boracik/nginx/html/susmodder-versions/"
ssh debian@vps-b99a39c3.vps.ovh.net "ls -lh /srv/synapsekit-boracik/nginx/html/susmodder/releases/legacy/"
```

## Weryfikacja Po Deployment

```bash
# Test download (versions)
curl -I https://susmodder.app/SUSModder-2.2.0.zip
# Oczekiwane: HTTP/1.1 200 OK, Content-Length: ~52MB

# Test download (legacy backup)
curl -I https://susmodder.app/releases/legacy/SUSModder-2.2.0-legacy.zip
# Oczekiwane: HTTP/1.1 200 OK, Content-Length: ~52MB

# Test API
curl "https://susmodder.app/api/download-latest?current=2.0.2"
# Powinien przekierować do /susmodder-versions/SUSModder-2.2.0.zip
```

## FAQ

**Q: Dlaczego dwie lokalizacje?**
A: Historyczne powody. `/susmodder-versions/` istnieje od początku projektu. Nowa struktura (`/releases/`) została dodana z Velopack, więc zachowujemy obie dla kompatybilności.

**Q: Czy mogę pominąć jedną lokalizację?**
A: Nie zalecane. `/susmodder-versions/` jest krytyczna dla użytkowników 2.0.2. `/releases/legacy/` to backup.

**Q: Co jeśli zapomnę zmienić nazwę?**
A: Użytkownicy nie będą mogli pobrać aktualizacji. Backend oczekuje `SUSModder-X.Y.Z.zip` bez sufixu "-legacy".

**Q: Kiedy mogę usunąć `/susmodder-versions/`?**
A: Gdy >95% użytkowników będzie na v2.2.0+. Prawdopodobnie za 3-6 miesięcy po release.

**Q: Co z przyszłymi wersjami?**
A: Każda nowa wersja stable (np. 2.4.0) powinna mieć swój ZIP w `/susmodder-versions/SUSModder-2.4.0.zip`.

## Checklist Deployment

- [ ] Build: `.\SKRYPTY\Build\sign-and-build.ps1 -ReleaseVersion "2.2.0"`
- [ ] Weryfikacja: Sprawdź `releases-legacy/SUSModder-2.2.0-legacy.zip`
- [ ] Deploy: `.\SKRYPTY\Build\deploy-to-server.ps1 -ReleaseVersion "2.2.0"`
- [ ] Weryfikacja upload #1: `curl -I https://susmodder.app/SUSModder-2.2.0.zip`
- [ ] Weryfikacja upload #2: `curl -I https://susmodder.app/releases/legacy/SUSModder-2.2.0-legacy.zip`
- [ ] Test API: `curl "https://susmodder.app/api/download-latest?current=2.0.2"`
- [ ] Test w aplikacji: Uruchom v2.0.2 → "Sprawdź aktualizacje"

---

**Data utworzenia:** 2025-01-06
**Wersja dokumentu:** 1.0
**Autor:** SUSModder Team
