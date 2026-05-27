# Projekt Bazy Danych - Compatibility Matrix

## 🗄️ Analiza Istniejącej Struktury

### Tabela: `config`
```sql
CREATE TABLE config (
    Id INT PRIMARY KEY,
    ModName VARCHAR(255),
    PngFileName VARCHAR(255),
    InstallPath VARCHAR(255),
    GitHubRepoOrLink VARCHAR(255),
    EpicGitHubRepoOrLink VARCHAR(255),
    ModType VARCHAR(50),              -- 'full' lub 'dll'
    DllInstallPath VARCHAR(255),
    ModVersion VARCHAR(50),
    LastUpdated DATETIME,
    AmongVersion VARCHAR(50),
    Description TEXT
);
```

### Przykładowe Dane
```
Id=1  ModName="Town of Us"      ModType="full"  ModVersion="5.3.1"
Id=2  ModName="ToU - Wygon"     ModType="full"  ModVersion="2.0.0"
Id=4  ModName="The Other Roles" ModType="full"  ModVersion="4.8.0"
Id=5  ModName="AleLuduMod"      ModType="dll"   ModVersion="latest"
Id=8  ModName="AUnlocker"       ModType="dll"   ModVersion="latest"
Id=9  ModName="LevelImposter"   ModType="dll"   ModVersion="Custom Beta 0.20.4"
```

## 🆕 Nowa Tabela: `compatibility_matrix`

### Pełna Definicja SQL

```sql
CREATE TABLE compatibility_matrix (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Referencje do modów
    FullModId INT NOT NULL,
    DllModId INT NOT NULL,
    
    -- Wersje modów (KLUCZOWE dla historii kompatybilności)
    FullModVersion VARCHAR(50) NOT NULL,
    DllModVersion VARCHAR(50) NOT NULL,
    
    -- Status kompatybilności
    CompatibilityStatus ENUM('F', 'W', 'NT', 'NW') NOT NULL DEFAULT 'NT',
    
    -- Audit trail
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Klucze obce
    FOREIGN KEY (FullModId) REFERENCES config(Id) ON DELETE CASCADE,
    FOREIGN KEY (DllModId) REFERENCES config(Id) ON DELETE CASCADE,
    
    -- Indeksy dla szybkich zapytań
    INDEX idx_full_mod (FullModId, FullModVersion),
    INDEX idx_dll_mod (DllModId, DllModVersion),
    INDEX idx_compatibility_status (CompatibilityStatus),
    INDEX idx_tested_date (TestedDate),
    
    -- Unikalność: jedna kombinacja mod+wersja może mieć tylko jeden wpis
    UNIQUE KEY unique_compatibility (FullModId, DllModId, FullModVersion, DllModVersion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## 📝 Szczegóły Kolumn

### Struktura Kolumn

| Kolumna | Typ | Opis |
|---------|-----|------|
| `Id` | INT AUTO_INCREMENT | Unikalny identyfikator wpisu |
| `FullModId` | INT NOT NULL | FK do `config.Id` gdzie `ModType='full'` |
| `DllModId` | INT NOT NULL | FK do `config.Id` gdzie `ModType='dll'` |
| `FullModVersion` | VARCHAR(50) NOT NULL | Wersja moda FULL (np. "5.3.1") |
| `DllModVersion` | VARCHAR(50) NOT NULL | Wersja moda DLL (np. "latest", "2.10.0") |
| `CompatibilityStatus` | ENUM('F','W','NT','NW') | Status kompatybilności |
| `CreatedAt` | DATETIME | Data utworzenia wpisu |

### Wersjonowanie (KLUCZOWE!)

**DLACZEGO TO WAŻNE:**
- Gdy Town of Us 5.3.1 działa z AleLuduMod (status: F)
- Potem Town of Us zostaje zaktualizowany do 5.4.0
- AleLuduMod może już NIE działać z 5.4.0
- Potrzebujemy osobnego wpisu dla każdej kombinacji wersji!
- **Przy update moda FULL lub DLL → wszystkie F/W stają się NT (Not Tested)**

### Statusy Kompatybilności

**Wartości:**
- **F** (Favorite): Działa idealnie, polecane
- **W** (Works): Działa poprawnie
- **NT** (Not Tested): Nieprzetestowane
- **NW** (Not Work): Nie działa, niekompatybilne

## 🔍 Widoki (Views) dla Uproszczenia Zapytań

### Widok 1: Najnowsze Kompatybilności
Zwraca najnowsze kompatybilności dla aktualnych wersji modów:

```sql
CREATE VIEW vw_current_compatibility AS
SELECT 
    cm.Id,
    cm.FullModId,
    fm.ModName AS FullModName,
    fm.ModVersion AS FullModCurrentVersion,
    cm.FullModVersion AS TestedFullModVersion,
    cm.DllModId,
    dm.ModName AS DllModName,
    dm.ModVersion AS DllModCurrentVersion,
    cm.DllModVersion AS TestedDllModVersion,
    cm.CompatibilityStatus,
    cm.TestedDate,
    cm.TestedBy,
    cm.AmongUsVersion,
    cm.Notes,
    CASE 
        WHEN fm.ModVersion = cm.FullModVersion 
         AND dm.ModVersion = cm.DllModVersion 
        THEN TRUE
        ELSE FALSE
    END AS IsCurrentVersion
FROM compatibility_matrix cm
JOIN config fm ON cm.FullModId = fm.Id AND fm.ModType = 'full'
JOIN config dm ON cm.DllModId = dm.Id AND dm.ModType = 'dll';
```

### Widok 2: Pełna Macierz Kompatybilności
Dla interfejsu administracyjnego:

```sql
CREATE VIEW vw_compatibility_matrix_full AS
SELECT 
    fm.Id AS FullModId,
    fm.ModName AS FullModName,
    fm.ModVersion AS FullModVersion,
    dm.Id AS DllModId,
    dm.ModName AS DllModName,
    dm.ModVersion AS DllModVersion,
    COALESCE(cm.CompatibilityStatus, 'NT') AS Status,
    cm.TestedDate,
    cm.Id AS MatrixId
FROM config fm
CROSS JOIN config dm
LEFT JOIN compatibility_matrix cm 
    ON cm.FullModId = fm.Id 
    AND cm.DllModId = dm.Id
    AND cm.FullModVersion = fm.ModVersion
    AND cm.DllModVersion = dm.ModVersion
WHERE fm.ModType = 'full' 
  AND dm.ModType = 'dll';
```

## 📊 Przykładowe Dane

### Scenariusz: Town of Us + różne DLL

```sql
-- Town of Us 5.3.1 + AleLuduMod latest = Favorite
INSERT INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
VALUES 
(1, 5, '5.3.1', 'latest', 'F');

-- Town of Us 5.3.1 + AUnlocker latest = Works
INSERT INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
VALUES 
(1, 8, '5.3.1', 'latest', 'W');

-- Town of Us 5.3.1 + LevelImposter Beta 0.20.4 = Not Work
INSERT INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
VALUES 
(1, 9, '5.3.1', 'Custom Beta 0.20.4', 'NW');
```

## 🔄 Strategia Migracji Danych

### Krok 1: Utworzenie tabeli
```sql
-- Wykonaj powyższy CREATE TABLE
```

### Krok 2: Wypełnienie domyślnymi wartościami (opcjonalne)
```sql
-- Dodaj wszystkie kombinacje jako NT (Not Tested)
INSERT INTO compatibility_matrix (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
SELECT 
    fm.Id, 
    dm.Id, 
    fm.ModVersion, 
    dm.ModVersion, 
    'NT'
FROM config fm
CROSS JOIN config dm
WHERE fm.ModType = 'full' 
  AND dm.ModType = 'dll';
```

### Krok 3: Stopniowe uzupełnianie przez adminów
- Administratorzy będą stopniowo testować i aktualizować statusy
- Priorytet: najpopularniejsze kombinacje

## 🎯 Indeksowanie i Wydajność

### Główne Zapytania i Ich Optymalizacja

**Zapytanie 1**: Pobierz wszystkie DLL kompatybilne z konkretnym FULL modem
```sql
SELECT * FROM compatibility_matrix 
WHERE FullModId = ? AND FullModVersion = ?
AND CompatibilityStatus IN ('F', 'W');
```
**Indeks**: `idx_full_mod (FullModId, FullModVersion)`

**Zapytanie 2**: Pobierz wszystkie FULL kompatybilne z konkretnym DLL
```sql
SELECT * FROM compatibility_matrix 
WHERE DllModId = ? AND DllModVersion = ?
AND CompatibilityStatus IN ('F', 'W');
```
**Indeks**: `idx_dll_mod (DllModId, DllModVersion)`

### Szacowana Wielkość Tabeli

**Założenia:**
- 10 modów FULL (średnio 3 wersje każdy) = 30 wersji FULL
- 15 modów DLL (średnio 2 wersje każdy) = 30 wersji DLL
- **Maksymalna liczba rekordów**: 30 × 30 = 900 wpisów

**Rozmiar:**
- ~200 bytes na rekord
- 900 × 200 = ~180 KB (bardzo małe, bez problemu)

## 🛡️ Walidacja i Constraints

### Walidacja na poziomie aplikacji (przed INSERT)
```javascript
// Sprawdź czy FullModId ma ModType='full'
// Sprawdź czy DllModId ma ModType='dll'
// Sprawdź czy wersje modów istnieją w historii
// Sprawdź czy użytkownik ma uprawnienia do edycji
```

### Walidacja na poziomie bazy danych
- ✅ FOREIGN KEY zapewnia istnienie modów
- ✅ UNIQUE KEY zapewnia brak duplikatów
- ✅ ENUM zapewnia poprawne wartości statusu
- ✅ NOT NULL zapewnia kompletność danych

## 📈 Rozszerzenia Przyszłościowe

### Wersja 2.0: Historia zmian
```sql
CREATE TABLE compatibility_matrix_history (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    MatrixId INT,
    OldStatus ENUM('F', 'W', 'NT', 'NW'),
    NewStatus ENUM('F', 'W', 'NT', 'NW'),
    ChangedAt DATETIME,
    ChangedBy VARCHAR(100),
    Reason TEXT,
    FOREIGN KEY (MatrixId) REFERENCES compatibility_matrix(Id)
);
```

### Wersja 3.0: Community Feedback
```sql
CREATE TABLE compatibility_feedback (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    MatrixId INT,
    UserId VARCHAR(100),
    UserStatus ENUM('F', 'W', 'NT', 'NW'),
    Comment TEXT,
    CreatedAt DATETIME,
    FOREIGN KEY (MatrixId) REFERENCES compatibility_matrix(Id)
);
```

## 🔐 Uprawnienia

```sql
-- Tylko administratorzy mogą modyfikować
GRANT SELECT ON susfuckr.compatibility_matrix TO 'api_user'@'%';
GRANT INSERT, UPDATE ON susfuckr.compatibility_matrix TO 'admin_user'@'%';

-- Widoki dostępne dla wszystkich
GRANT SELECT ON susfuckr.vw_current_compatibility TO 'api_user'@'%';
GRANT SELECT ON susfuckr.vw_compatibility_matrix_full TO 'admin_user'@'%';
```
