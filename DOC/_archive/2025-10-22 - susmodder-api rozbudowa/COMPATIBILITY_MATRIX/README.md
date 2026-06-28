# Compatibility Matrix - Dokumentacja Projektu

## 📖 Wprowadzenie

System **Compatibility Matrix** to rozwiązanie do zarządzania kompatybilnością między modami **FULL** (pełne modyfikacje Among Us) a modami **DLL** (dodatki/rozszerzenia).

### Problem Biznesowy

Użytkownicy nie wiedzą, które mody DLL działają z konkretnymi modami FULL. Kompatybilność zmienia się z każdą nową wersją modów, co prowadzi do frustracji i problemów technicznych.

### Rozwiązanie

Stworzenie systemu, który:
- ✅ Śledzi kompatybilność między wszystkimi kombinacjami modów
- ✅ Obsługuje wersjonowanie (różne wersje = różna kompatybilność)
- ✅ Udostępnia API dla aplikacji i użytkowników
- ✅ Zapewnia intuicyjny interfejs dla administratorów

---

## 🗂️ Struktura Dokumentacji

### 📄 Główne Dokumenty

| Plik | Opis | Dla Kogo |
|------|------|----------|
| **[00_PROJECT_SUMMARY.md](./00_PROJECT_SUMMARY.md)** | Podsumowanie projektu, cele, metryki sukcesu | Wszyscy |
| **[01_DATABASE_DESIGN.md](./01_DATABASE_DESIGN.md)** | Projekt bazy danych, tabele, indeksy, migracje | Backend Dev, DBA |
| **[02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)** | Pełna specyfikacja API, endpointy, przykłady | Frontend Dev, API Users |
| **[03_VERSION_HANDLING.md](./03_VERSION_HANDLING.md)** | Strategia obsługi wersji modów | Backend Dev, Admin |
| **[04_ADMIN_INTERFACE.md](./04_ADMIN_INTERFACE.md)** | Projekt interfejsu administracyjnego | Frontend Dev, UX/UI |
| **[05_MIGRATION_PLAN.md](./05_MIGRATION_PLAN.md)** | Plan wdrożenia krok po kroku | DevOps, Project Manager |
| **[QUICK_REFERENCE.md](./QUICK_REFERENCE.md)** | Szybka ściąga, najważniejsze komendy | Wszyscy |

### 🆕 Dokumenty dla Integracji API

| Plik | Opis | Dla Kogo |
|------|------|----------|
| **[API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)** | ✨ **KOMPLETNY** przewodnik użycia API | Frontend Dev, API Users |
| **[QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)** | Szybka ściąga API, przykłady cURL | Wszyscy |
| **[CODE_EXAMPLES.md](./CODE_EXAMPLES.md)** | Gotowe przykłady w JS, Python, C#, PHP, Go, Java | Developerzy |

### 📚 Jak Czytać Dokumentację?

**Jeśli jesteś:**

- **👨‍💼 Project Manager:** Zacznij od `00_PROJECT_SUMMARY.md`, potem `05_MIGRATION_PLAN.md`
- **👨‍💻 Backend Developer:** Przeczytaj `01_DATABASE_DESIGN.md` → `02_API_SPECIFICATION.md` → `03_VERSION_HANDLING.md`
- **🎨 Frontend Developer:** Przeczytaj **`API_USAGE_GUIDE.md`** → **`CODE_EXAMPLES.md`** → `04_ADMIN_INTERFACE.md`
- **📱 Integrujesz API:** Zacznij od **`QUICK_API_REFERENCE.md`** → **`API_USAGE_GUIDE.md`** → **`CODE_EXAMPLES.md`**
- **🔧 DevOps:** Przeczytaj `05_MIGRATION_PLAN.md` → `01_DATABASE_DESIGN.md`
- **👨‍💼 Admin/Tester:** Przeczytaj `00_PROJECT_SUMMARY.md` → `04_ADMIN_INTERFACE.md` → `QUICK_REFERENCE.md`

---

## 🎯 Kluczowe Koncepcje

### Statusy Kompatybilności

| Status | Kod | Znaczenie |
|--------|-----|-----------|
| 🟢 **Favorite** | F | Działa idealnie, polecane przez społeczność |
| 🔵 **Works** | W | Działa poprawnie, bez większych problemów |
| ⚪ **Not Tested** | NT | Nieprzetestowane, nieznany status |
| 🔴 **Not Work** | NW | Nie działa, niekompatybilne |

### Typy Modów

- **FULL** (pełny mod) - Kompletna modyfikacja gry (np. Town of Us, The Other Roles)
- **DLL** (dodatek) - Rozszerzenie funkcjonalności (np. AleLuduMod, AUnlocker)

### Wersjonowanie

**Kluczowa zasada:** Każda kombinacja `(FullMod, DllMod, FullVersion, DllVersion)` jest osobnym wpisem.

**Dlaczego?**
- Town of Us v5.3.1 może działać z AleLuduMod ✅
- Town of Us v5.4.0 może NIE działać z AleLuduMod ❌
- Historia musi być zachowana!

---

## 🏗️ Architektura Systemu

```
┌─────────────────────────────────────────────────────────────┐
│                     UŻYTKOWNICY                             │
│                                                              │
│  Frontend App  │  Discord Bot  │  Admin Panel  │  API Users│
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    REST API (Node.js)                        │
│                                                              │
│  GET  /api/compatibility?fullModId=1                        │
│  POST /api/compatibility                                    │
│  PUT  /api/compatibility/:id                                │
│  GET  /api/compatibility/matrix                             │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   MySQL Database                             │
│                                                              │
│  compatibility_matrix                                        │
│  ├─ Id, FullModId, DllModId                                 │
│  ├─ FullModVersion, DllModVersion                           │
│  ├─ CompatibilityStatus (F, W, NT, NW)                      │
│  └─ TestedDate, TestedBy, Notes, ...                        │
│                                                              │
│  config (istniejąca)                                         │
│  ├─ Id, ModName, ModType, ModVersion                        │
│  └─ ...                                                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start

### Dla Developerów API

**1. Sprawdź kompatybilność:**
```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5"
```

**2. Pobierz tylko działające:**
```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W"
```

**3. Pełna macierz (wymaga auth):**
```bash
curl -H "Authorization: YOUR_TOKEN" \
  "https://api.susmodder.app/api/compatibility/matrix"
```

📚 **Więcej przykładów:** Zobacz [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)

### Dla Administratorów

**1. Zaloguj się do panelu:**
```
https://susadmin.susmodder.app/compatibility
```

**2. Wybierz mod i wersję:**
- Town of Us v5.4.0

**3. Testuj kompatybilności:**
- Kliknij "Testing Mode"
- Testuj każdą kombinację
- Zapisz wyniki

**4. Sprawdź macierz:**
- Pełny przegląd wszystkich kombinacji
- Kolorowe oznaczenia statusów

---

## 📊 Statystyki Projektu

### Rozmiar i Skala

- **Liczba modów FULL:** ~10 (średnio 3 wersje każdy) = 30 wersji
- **Liczba modów DLL:** ~15 (średnio 2 wersje każdy) = 30 wersji
- **Maksymalna liczba kombinacji:** 30 × 30 = **900 wpisów**
- **Rozmiar bazy danych:** ~180 KB (bardzo małe)

### Wydajność

- **Czas odpowiedzi API:** < 200ms (cel)
- **Czas ładowania macierzy:** < 500ms
- **Przepustowość:** 100 req/min (GET), 20 req/min (POST/PUT/DELETE)

---

## 🛠️ Technologie

### Backend
- **Node.js** + **Express** (susmodder-api)
- **MySQL 8.0** (baza danych)
- **Docker** + **Docker Compose** (deployment)

### Frontend (Admin Panel)
- **React** lub **Vue.js** (do ustalenia)
- **TailwindCSS** (stylowanie)
- **React Table** lub **AG Grid** (macierz)

### API
- **RESTful API**
- **Bearer Token Authentication**
- **Swagger/OpenAPI** (dokumentacja)

---

## 📅 Timeline Implementacji

| Faza | Czas | Status |
|------|------|--------|
| **Faza 0:** Przygotowanie i backup | 1 dzień | ⏳ Planowane |
| **Faza 1:** Migracja bazy danych | 2 dni | ⏳ Planowane |
| **Faza 2:** Implementacja API | 3 dni | ⏳ Planowane |
| **Faza 3:** Interfejs administracyjny | 4 dni | ⏳ Planowane |
| **Faza 4:** Testy i QA | 2 dni | ⏳ Planowane |
| **Faza 5:** Deployment produkcyjny | 1 dzień | ⏳ Planowane |
| **RAZEM** | **~2.5 tygodnia** | ⏳ W trakcie planowania |

---

## 📝 Przykłady Użycia

### JavaScript (Frontend)

```javascript
// Pobierz kompatybilności dla DLL
async function getDllCompatibilities(dllModId) {
  const response = await fetch(
    `/api/compatibility?dllModId=${dllModId}&status=F,W`
  );
  const data = await response.json();
  
  return data.compatibilities.map(c => ({
    modName: c.fullMod.name,
    status: c.status,
    isRecommended: c.status === 'F'
  }));
}

// Użycie
const compatible = await getDllCompatibilities(5);
console.log('Kompatybilne mody:', compatible);
```

### Python (Analytics)

```python
import requests

def analyze_compatibility_stats():
    response = requests.get('https://api.susmodder.app/api/compatibility/matrix')
    data = response.json()
    
    stats = {
        'total': len(data['matrix']),
        'tested': sum(1 for m in data['matrix'] if m['status'] != 'NT'),
        'working': sum(1 for m in data['matrix'] if m['status'] in ['F', 'W']),
    }
    
    print(f"Testing progress: {stats['tested']}/{stats['total']} ({stats['tested']/stats['total']*100:.1f}%)")
    print(f"Working combinations: {stats['working']}")
    
    return stats
```

### Discord Bot Integration

```javascript
// Powiadomienie o nowej wersji moda
async function notifyNewVersion(mod, oldVersion, newVersion) {
  const embed = {
    title: `🔄 ${mod.name} Updated`,
    description: `Version ${oldVersion} → ${newVersion}`,
    color: 0xFFA500,
    fields: [
      {
        name: '⚠️ Compatibility Status',
        value: 'All DLL compatibilities need retesting'
      },
      {
        name: '📋 Action',
        value: `[Test Compatibilities](https://susadmin.susmodder.app/compatibility?fullModId=${mod.id})`
      }
    ]
  };
  
  await sendDiscordWebhook(ADMIN_WEBHOOK, { embeds: [embed] });
}
```

---

## 🐛 Troubleshooting

### Częste Problemy

**Problem:** API zwraca puste wyniki
```bash
# Sprawdź czy dane istnieją w bazie
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT COUNT(*) FROM compatibility_matrix;"
```

**Problem:** Błąd 401 Unauthorized
```bash
# Sprawdź token autoryzacji
echo $HTTP_TOKEN
# Porównaj z .env
```

**Problem:** Wolne zapytania
```sql
-- Sprawdź indeksy
SHOW INDEX FROM compatibility_matrix;

-- Sprawdź plan zapytania
EXPLAIN SELECT * FROM compatibility_matrix WHERE FullModId = 1;
```

---

## 🔗 Powiązane Projekty

- **susmodder-api** - Backend API
- **susadmin** - Panel administracyjny
- **discord-bots/sustats** - Bot Discord z integracją
- **nginx** - Reverse proxy i hosting

---

## 📞 Kontakt i Wsparcie

### Dla Deweloperów
- **Kod źródłowy:** `/srv/synapsekit-boracik/susmodder-api`
- **Dokumentacja API:** `https://api.susmodder.app/api-docs`
- **Testy:** `susmodder-api/test/`

### Dla Administratorów
- **Panel admina:** `https://susadmin.susmodder.app`
- **Quick Reference:** [QUICK_REFERENCE.md](./QUICK_REFERENCE.md)

### W Razie Problemów
1. Sprawdź logi: `docker logs nginx-api-susmodder`
2. Przeczytaj dokumentację: `DOC/COMPATIBILITY_MATRIX/`
3. Sprawdź testy: `npm test`
4. Kontakt z zespołem DevOps

---

## 🎓 Dalsze Rozszerzenia

### Wersja 2.0 (przyszłość)
- 🔔 Automatyczne powiadomienia Discord przy zmianach
- 👥 System feedbacku społeczności
- 📊 Zaawansowane statystyki i wykresy
- 🤖 Automatyczne testowanie przy CI/CD
- 📤 Export/Import danych (CSV, JSON)

### Wersja 3.0 (wizja)
- 🧠 AI-powered compatibility prediction
- 🌐 Publiczny API dla community
- 📱 Aplikacja mobilna
- 🎮 Integracja z launcherem modów

---

## 📜 Licencja i Autorzy

**Projekt:** Compatibility Matrix for Among Us Mods  
**Data utworzenia:** Październik 2025  
**Wersja dokumentacji:** 1.0  
**Autorzy:** SysAdmin Team, susmodder.app  
**Status:** 📝 W trakcie implementacji

---

## ✅ Checklist Implementacji

### Baza Danych
- [ ] Utworzenie tabeli `compatibility_matrix`
- [ ] Utworzenie widoków (views)
- [ ] Wypełnienie danymi testowymi
- [ ] Utworzenie indeksów

### API
- [ ] GET /api/compatibility (lista)
- [ ] GET /api/compatibility/:id (szczegóły)
- [ ] POST /api/compatibility (create)
- [ ] PUT /api/compatibility/:id (update)
- [ ] DELETE /api/compatibility/:id (delete)
- [ ] GET /api/compatibility/matrix (macierz)
- [ ] Testy jednostkowe
- [ ] Dokumentacja Swagger

### Frontend
- [ ] Matrix View (macierz)
- [ ] Detail View (szczegóły)
- [ ] Edit Modal (edycja)
- [ ] Testing Mode (tryb testowania)
- [ ] Bulk Edit (edycja masowa)
- [ ] Mobile responsive

### Deployment
- [ ] Migracja bazy produkcyjnej
- [ ] Deploy API
- [ ] Deploy frontend
- [ ] Testy smoke
- [ ] Monitoring

### Dokumentacja
- [x] PROJECT_SUMMARY.md
- [x] DATABASE_DESIGN.md
- [x] API_SPECIFICATION.md
- [x] VERSION_HANDLING.md
- [x] ADMIN_INTERFACE.md
- [x] MIGRATION_PLAN.md
- [x] QUICK_REFERENCE.md
- [x] README.md

---

**Ostatnia aktualizacja:** 2025-10-22  
**Następna review:** Po zakończeniu Fazy 1 (migracja bazy)
