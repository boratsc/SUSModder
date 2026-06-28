# Schemat Bazy Danych - Wersjonowanie Modów

## 🗄️ Przegląd Struktury

System wersjonowania składa się z:
- **Tabela `config`** - istniejąca, bez zmian, zawiera najnowsze wersje modów
- **Tabela `config_versions`** - NOWA, przechowuje historię wersji
- **Widok `vw_config_with_version_count`** - pomocniczy widok z liczbą wersji dla każdego moda

---

## 📋 Tabela: `config` (Istniejąca)

### Struktura

**Bez zmian!** Tabela `config` pozostaje dokładnie taka sama:

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
    ModVersion VARCHAR(50),            -- ← Wersjonowane
    LastUpdated DATETIME,
    AmongVersion VARCHAR(50),          -- ← Wersjonowane
    Description TEXT
);
```

### Zachowanie

- **Zawsze przechowuje najnowszą wersję** każdego moda
- Endpoint `/susmodder-config` zwraca tę tabelę (bez zmian!)
- `ModVersion` i `AmongVersion` reprezentują aktualną wersję

### Przykładowe Dane

```
Id | ModName           | ModType | ModVersion       | AmongVersion  | GitHubRepoOrLink
---|-------------------|---------|------------------|---------------|------------------
1  | Town of Us        | full    | 5.4.0            | 2024.10.29    | https://github...
2  | ToU - Wygon       | full    | 2.0.0            | 2024.10.01    | https://github...
4  | The Other Roles   | full    | 4.8.0            | 2024.09.15    | https://github...
5  | AleLuduMod        | dll     | latest           | 2024.10.29    | https://github...
8  | AUnlocker         | dll     | 1.2.3            | 2024.10.01    | https://github...
9  | LevelImposter     | dll     | Custom Beta 0.20.4 | 2024.09.01  | https://github...
```

**Kluczowe obserwacje:**
- Każdy mod ma JEDNĄ najnowszą wersję
- `ModVersion` może być stringiem ("latest", "Custom Beta 0.20.4")
- To jest to, co widzą użytkownicy w `/susmodder-config`

---

## 🆕 Tabela: `config_versions` (Nowa)

### Pełna Definicja SQL

```sql
CREATE TABLE config_versions (
    -- Klucz główny
    VersionId INT AUTO_INCREMENT PRIMARY KEY,

    -- Klucz obcy do config
    ModId INT NOT NULL COMMENT 'FK to config.Id',

    -- TYLKO 4 wersjonowane parametry (minimalizm!)
    ModVersion VARCHAR(50) NOT NULL COMMENT 'Wersja moda (np. "5.3.1", "latest")',
    AmongVersion VARCHAR(50) NOT NULL COMMENT 'Wersja Among Us (np. "2024.10.29")',
    GitHubRepoOrLink VARCHAR(255) COMMENT 'Link GitHub (Steam)',
    EpicGitHubRepoOrLink VARCHAR(255) COMMENT 'Link GitHub (Epic Games)',

    -- Metadata
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Kiedy utworzono wersję',
    CreatedBy VARCHAR(100) COMMENT 'Kto utworzył (opcjonalne)',
    Notes TEXT COMMENT 'Notatki o wersji (opcjonalne)',

    -- Foreign Key Constraint
    CONSTRAINT fk_config_versions_mod
        FOREIGN KEY (ModId) REFERENCES config(Id)
        ON DELETE CASCADE,

    -- Indeksy dla szybkich zapytań
    INDEX idx_mod_id (ModId),
    INDEX idx_created_at (CreatedAt),
    INDEX idx_mod_version (ModId, ModVersion),

    -- Unikalność: jedna kombinacja ModId + ModVersion + AmongVersion
    UNIQUE KEY unique_mod_version (ModId, ModVersion, AmongVersion)

) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Historia wersji modów (tylko 4 wersjonowane pola)';
```

### Szczegóły Kolumn

| Kolumna | Typ | NULL | Opis |
|---------|-----|------|------|
| `VersionId` | INT AUTO_INCREMENT | NO | Unikalny identyfikator wersji |
| `ModId` | INT | NO | FK do `config.Id` |
| `ModVersion` | VARCHAR(50) | NO | Wersja moda (string!) |
| `AmongVersion` | VARCHAR(50) | NO | Wersja Among Us |
| `GitHubRepoOrLink` | VARCHAR(255) | YES | Link GitHub (Steam) |
| `EpicGitHubRepoOrLink` | VARCHAR(255) | YES | Link GitHub (Epic Games) |
| `CreatedAt` | DATETIME | NO | Data utworzenia wersji |
| `CreatedBy` | VARCHAR(100) | YES | Kto utworzył (opcjonalne) |
| `Notes` | TEXT | YES | Notatki o wersji |

### Kluczowe Założenia

**1. Minimalizm - TYLKO 4 wersjonowane pola:**
- `ModVersion`
- `AmongVersion`
- `GitHubRepoOrLink`
- `EpicGitHubRepoOrLink`

**Pozostałe pola (ModName, PngFileName, Description) NIE są wersjonowane** - są w tabeli `config`.

**2. Unikalność:**
```sql
UNIQUE KEY unique_mod_version (ModId, ModVersion, AmongVersion)
```

**Dlaczego?**
- Jeden mod (ModId) może mieć wiele wersji (ModVersion)
- Jedna wersja moda może być kompatybilna z różnymi wersjami Among Us
- **Kombinacja (ModId, ModVersion, AmongVersion) jest unikalna**

**Przykład:**
```
VersionId | ModId | ModVersion | AmongVersion
----------|-------|------------|-------------
1         | 1     | 5.3.1      | 2024.09.01   ← ToU 5.3.1 dla AU 2024.09.01
2         | 1     | 5.3.1      | 2024.10.01   ← ToU 5.3.1 dla AU 2024.10.01 (możliwe!)
3         | 1     | 5.4.0      | 2024.10.29   ← ToU 5.4.0 dla AU 2024.10.29
```

**3. ON DELETE CASCADE:**
- Gdy mod zostanie usunięty z `config`, wszystkie jego wersje znikają automatycznie
- Zachowanie spójności danych

### Przykładowe Dane

```sql
-- Town of Us - 3 wersje
INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, CreatedAt) VALUES
(1, '5.3.0', '2024.09.01', 'https://github.com/tou/v5.3.0', '2024-09-01 10:00:00'),
(1, '5.3.1', '2024.10.01', 'https://github.com/tou/v5.3.1', '2024-10-01 14:30:00'),
(1, '5.4.0', '2024.10.29', 'https://github.com/tou/v5.4.0', '2024-10-29 09:15:00');

-- ToU - Wygon - 2 wersje
INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, CreatedAt) VALUES
(2, '1.0.0', '2024.08.01', 'https://github.com/wygon/v1.0.0', '2024-08-01 12:00:00'),
(2, '2.0.0', '2024.10.01', 'https://github.com/wygon/v2.0.0', '2024-10-01 16:45:00');

-- AleLuduMod (DLL) - używa "latest"
INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, CreatedAt) VALUES
(5, 'latest', '2024.10.29', 'https://github.com/alelud/latest', '2024-10-29 11:00:00');
```

**Wynik zapytania:**
```
VersionId | ModId | ModVersion | AmongVersion | GitHubRepoOrLink                    | CreatedAt
----------|-------|------------|--------------|-------------------------------------|-------------------
1         | 1     | 5.3.0      | 2024.09.01   | https://github.com/tou/v5.3.0       | 2024-09-01 10:00
2         | 1     | 5.3.1      | 2024.10.01   | https://github.com/tou/v5.3.1       | 2024-10-01 14:30
3         | 1     | 5.4.0      | 2024.10.29   | https://github.com/tou/v5.4.0       | 2024-10-29 09:15
4         | 2     | 1.0.0      | 2024.08.01   | https://github.com/wygon/v1.0.0     | 2024-08-01 12:00
5         | 2     | 2.0.0      | 2024.10.01   | https://github.com/wygon/v2.0.0     | 2024-10-01 16:45
6         | 5     | latest     | 2024.10.29   | https://github.com/alelud/latest    | 2024-10-29 11:00
```

---

## 🔍 Widok: `vw_config_with_version_count`

### Definicja SQL

```sql
CREATE OR REPLACE VIEW vw_config_with_version_count AS
SELECT
    c.Id,
    c.ModName,
    c.ModType,
    c.ModVersion AS CurrentVersion,
    c.AmongVersion AS CurrentAmongVersion,
    c.GitHubRepoOrLink AS CurrentGitHubLink,
    c.EpicGitHubRepoOrLink AS CurrentEpicLink,
    COALESCE(v.VersionCount, 0) AS TotalVersions,
    v.FirstVersionDate,
    v.LastVersionDate
FROM config c
LEFT JOIN (
    SELECT
        ModId,
        COUNT(*) AS VersionCount,
        MIN(CreatedAt) AS FirstVersionDate,
        MAX(CreatedAt) AS LastVersionDate
    FROM config_versions
    GROUP BY ModId
) v ON c.Id = v.ModId;
```

### Zastosowanie

**Dla Admina:**
- Szybki przegląd ile wersji ma każdy mod
- Informacja kiedy był pierwszy/ostatni update

**Przykładowe zapytanie:**
```sql
SELECT * FROM vw_config_with_version_count;
```

**Wynik:**
```
Id | ModName      | CurrentVersion | TotalVersions | FirstVersionDate | LastVersionDate
---|--------------|----------------|---------------|------------------|------------------
1  | Town of Us   | 5.4.0          | 3             | 2024-09-01       | 2024-10-29
2  | ToU - Wygon  | 2.0.0          | 2             | 2024-08-01       | 2024-10-01
5  | AleLuduMod   | latest         | 1             | 2024-10-29       | 2024-10-29
```

---

## 📊 Zapytania Przykładowe

### 1. Pobierz wszystkie wersje konkretnego moda

```sql
-- Pobierz historię Town of Us (Id=1)
SELECT
    VersionId,
    ModVersion,
    AmongVersion,
    GitHubRepoOrLink,
    CreatedAt
FROM config_versions
WHERE ModId = 1
ORDER BY CreatedAt DESC;
```

**Wynik:**
```
VersionId | ModVersion | AmongVersion | GitHubRepoOrLink           | CreatedAt
----------|------------|--------------|----------------------------|-------------------
3         | 5.4.0      | 2024.10.29   | https://github.../v5.4.0   | 2024-10-29 09:15
2         | 5.3.1      | 2024.10.01   | https://github.../v5.3.1   | 2024-10-01 14:30
1         | 5.3.0      | 2024.09.01   | https://github.../v5.3.0   | 2024-09-01 10:00
```

### 2. Pobierz najnowszą wersję z historii

```sql
-- Najnowsza wersja Town of Us
SELECT *
FROM config_versions
WHERE ModId = 1
ORDER BY CreatedAt DESC
LIMIT 1;
```

### 3. Pobierz konkretną wersję

```sql
-- Pobierz Town of Us v5.3.1 dla Among Us 2024.10.01
SELECT *
FROM config_versions
WHERE ModId = 1
  AND ModVersion = '5.3.1'
  AND AmongVersion = '2024.10.01';
```

### 4. Sprawdź czy wersja już istnieje (przed INSERT)

```sql
-- Sprawdź czy ToU 5.4.0 + AU 2024.10.29 już istnieje
SELECT COUNT(*) AS Exists
FROM config_versions
WHERE ModId = 1
  AND ModVersion = '5.4.0'
  AND AmongVersion = '2024.10.29';

-- Wynik: Exists=1 (istnieje) lub Exists=0 (nie istnieje)
```

### 5. Pobierz statystyki wersjonowania

```sql
-- Statystyki dla wszystkich modów
SELECT
    c.ModName,
    c.ModType,
    COUNT(cv.VersionId) AS TotalVersions,
    MIN(cv.CreatedAt) AS FirstVersion,
    MAX(cv.CreatedAt) AS LatestVersion,
    DATEDIFF(MAX(cv.CreatedAt), MIN(cv.CreatedAt)) AS DaysSinceFirst
FROM config c
LEFT JOIN config_versions cv ON c.Id = cv.ModId
GROUP BY c.Id, c.ModName, c.ModType
ORDER BY TotalVersions DESC;
```

---

## 🎯 Indeksowanie i Wydajność

### Główne Indeksy

**1. Primary Key: `VersionId`**
- Automatyczny AUTO_INCREMENT
- Szybkie wyszukiwanie po ID wersji

**2. Index: `idx_mod_id (ModId)`**
- Najbardziej używany indeks
- Zapytania typu: "pobierz wszystkie wersje moda X"

**3. Index: `idx_created_at (CreatedAt)`**
- Sortowanie chronologiczne
- Zapytania typu: "najnowsza wersja", "wersje z ostatniego miesiąca"

**4. Index: `idx_mod_version (ModId, ModVersion)`**
- Złożony indeks
- Szybkie zapytania typu: "pobierz wersję 5.3.1 moda Town of Us"

**5. Unique Key: `unique_mod_version (ModId, ModVersion, AmongVersion)`**
- Zapewnia unikalność kombinacji
- Zapobiega duplikatom

### Szacowana Wielkość Tabeli

**Założenia:**
- 20 modów (10 FULL + 10 DLL)
- Średnio 5 wersji każdy mod
- **Maksymalna liczba rekordów:** 20 × 5 = **100 wpisów**

**Rozmiar:**
- ~250 bytes na rekord (4 pola VARCHAR + metadata)
- 100 × 250 = ~25 KB (bardzo małe)

**Wzrost w czasie:**
- Nowa wersja co 2 tygodnie dla 20 modów = ~10 nowych wpisów/miesiąc
- Po roku: ~120 dodatkowych wpisów = +30 KB
- **Przez 5 lat:** ~750 wpisów = ~190 KB (nadal bardzo małe)

---

## 🛡️ Walidacja i Constraints

### Walidacja na Poziomie Bazy Danych

**1. NOT NULL Constraints:**
```sql
ModId INT NOT NULL
ModVersion VARCHAR(50) NOT NULL
AmongVersion VARCHAR(50) NOT NULL
```
- Wymusza kompletność danych
- Nie można utworzyć wersji bez podstawowych informacji

**2. FOREIGN KEY Constraint:**
```sql
FOREIGN KEY (ModId) REFERENCES config(Id) ON DELETE CASCADE
```
- Zapewnia integralność referencyjną
- Automatycznie usuwa wersje gdy mod zostanie usunięty

**3. UNIQUE Constraint:**
```sql
UNIQUE KEY unique_mod_version (ModId, ModVersion, AmongVersion)
```
- Zapobiega duplikatom
- Jedna kombinacja (mod + wersja + AU wersja) może istnieć tylko raz

### Walidacja na Poziomie Aplikacji

**PHP (save_mod.php):**
```php
// 1. Sprawdź czy ModId istnieje w config
$stmt = $conn->prepare("SELECT Id FROM config WHERE Id = ?");
$stmt->bind_param("i", $modId);
$stmt->execute();
if ($stmt->get_result()->num_rows === 0) {
    exit(json_encode(["success" => false, "message" => "Mod nie istnieje"]));
}

// 2. Sprawdź czy kombinacja (ModId, ModVersion, AmongVersion) już istnieje
$stmt = $conn->prepare("SELECT VersionId FROM config_versions WHERE ModId = ? AND ModVersion = ? AND AmongVersion = ?");
$stmt->bind_param("iss", $modId, $modVersion, $amongVersion);
$stmt->execute();
if ($stmt->get_result()->num_rows > 0) {
    exit(json_encode(["success" => false, "message" => "Ta wersja już istnieje"]));
}

// 3. Sprawdź czy zmieniono wersję
$stmt = $conn->prepare("SELECT ModVersion, AmongVersion FROM config WHERE Id = ?");
$stmt->bind_param("i", $modId);
$stmt->execute();
$result = $stmt->get_result()->fetch_assoc();

$versionChanged = ($result['ModVersion'] !== $modVersion || $result['AmongVersion'] !== $amongVersion);
```

---

## 📈 Migracja Danych (Initial Seed)

### Krok 1: Utworzenie Tabeli

```sql
-- Uruchom pełny CREATE TABLE z powyższej sekcji
CREATE TABLE config_versions (...);
```

### Krok 2: Import Obecnych Wersji jako Punkt Startowy

```sql
-- Zaimportuj obecne wersje z config jako pierwszą wersję każdego moda
INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, EpicGitHubRepoOrLink, CreatedAt, Notes)
SELECT
    Id AS ModId,
    ModVersion,
    AmongVersion,
    GitHubRepoOrLink,
    EpicGitHubRepoOrLink,
    COALESCE(LastUpdated, NOW()) AS CreatedAt,
    'Initial version imported from config table' AS Notes
FROM config;
```

**Co to robi:**
- Tworzy pierwszy wpis w `config_versions` dla każdego moda z tabeli `config`
- Używa `LastUpdated` jako `CreatedAt` (jeśli dostępne)
- Dodaje notatkę o imporcie

**Wynik:**
```
VersionId | ModId | ModVersion       | AmongVersion | CreatedAt           | Notes
----------|-------|------------------|--------------|---------------------|-------------------------
1         | 1     | 5.4.0            | 2024.10.29   | 2024-10-29 09:15:00 | Initial version...
2         | 2     | 2.0.0            | 2024.10.01   | 2024-10-01 16:45:00 | Initial version...
3         | 4     | 4.8.0            | 2024.09.15   | 2024-09-15 11:30:00 | Initial version...
4         | 5     | latest           | 2024.10.29   | 2024-10-29 11:00:00 | Initial version...
5         | 8     | 1.2.3            | 2024.10.01   | 2024-10-01 10:20:00 | Initial version...
6         | 9     | Custom Beta 0.20.4 | 2024.09.01 | 2024-09-01 08:15:00 | Initial version...
```

### Krok 3: Utworzenie Widoku

```sql
-- Uruchom CREATE VIEW z powyższej sekcji
CREATE OR REPLACE VIEW vw_config_with_version_count AS ...
```

---

## 🔄 Aktualizacje Schematu (Przyszłość)

### Rozszerzenie 1: Dodanie `UpdatedAt` i `UpdatedBy`

Jeśli w przyszłości będziemy chcieli śledzić edycje wersji:

```sql
ALTER TABLE config_versions
ADD COLUMN UpdatedAt DATETIME NULL COMMENT 'Kiedy ostatnio edytowano',
ADD COLUMN UpdatedBy VARCHAR(100) NULL COMMENT 'Kto edytował';
```

### Rozszerzenie 2: Soft Delete

Zamiast DELETE, oznaczanie jako usunięte:

```sql
ALTER TABLE config_versions
ADD COLUMN IsDeleted BOOLEAN DEFAULT FALSE COMMENT 'Czy wersja została usunięta',
ADD COLUMN DeletedAt DATETIME NULL,
ADD COLUMN DeletedBy VARCHAR(100) NULL;

-- Zapytania pomijają usunięte wersje
SELECT * FROM config_versions WHERE IsDeleted = FALSE;
```

---

## 📋 Checklist Implementacji

### Baza Danych

- [ ] Utworzenie tabeli `config_versions` z pełnym schematem
- [ ] Dodanie foreign key constraint (ModId → config.Id)
- [ ] Utworzenie indeksów (idx_mod_id, idx_created_at, idx_mod_version)
- [ ] Dodanie unique constraint (ModId, ModVersion, AmongVersion)
- [ ] Utworzenie widoku `vw_config_with_version_count`
- [ ] Import obecnych danych z `config` jako seed
- [ ] Weryfikacja integralności danych

### Testy

- [ ] Test INSERT nowej wersji
- [ ] Test UNIQUE constraint (próba duplikatu)
- [ ] Test CASCADE DELETE (usunięcie moda z config)
- [ ] Test indeksów (EXPLAIN zapytań)
- [ ] Test widoku (poprawne liczby wersji)

---

**Ostatnia aktualizacja:** 2025-10-22
**Następny krok:** Specyfikacja API (02_API_SPECIFICATION.md)
