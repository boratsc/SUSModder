# 🚀 START HERE - Compatibility Matrix API

**Witaj! To jest punkt startowy dla integracji z Compatibility Matrix API.**

---

## 📋 Dla kogo jest ten dokument?

- **Integrujesz API w swoim projekcie?** → Czytaj dalej! ⬇️
- **Szukasz dokumentacji technicznej?** → Zobacz [README.md](./README.md)
- **Jesteś adminem?** → Zobacz [QUICK_REFERENCE.md](./QUICK_REFERENCE.md)

---

## ⚡ Quick Start (3 minuty)

### 1. Najprostsza integracja

```javascript
// Sprawdź kompatybilność
const response = await fetch(
  'https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W'
);
const data = await response.json();

// Wyświetl wyniki
data.compatibilities.forEach(comp => {
  console.log(`${comp.fullMod.name}: ${comp.status}`);
});
```

**To wszystko! Nie potrzebujesz autoryzacji dla podstawowych zapytań.**

---

## 📚 Dokumentacja - Wybierz swoją ścieżkę

### 🎯 Chcę zacząć SZYBKO (5-10 minut)

1. **[FOR_EXTERNAL_PROJECTS.md](./FOR_EXTERNAL_PROJECTS.md)** ← **START TUTAJ**
   - Quick start
   - Podstawowe przykłady
   - Use cases
   - 5-10 minut do integracji

2. **[QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)**
   - Szybka ściąga (2 strony)
   - Wszystkie endpointy
   - Parametry i kody błędów

### 📖 Chcę PEŁNĄ dokumentację (30-60 minut)

1. **[API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)** ← **Kompletny przewodnik**
   - 45+ stron dokumentacji
   - Wszystkie endpointy ze szczegółami
   - Przykłady requestów/responses
   - Use cases i best practices
   - Przykłady UI (React)
   - Troubleshooting

2. **[CODE_EXAMPLES.md](./CODE_EXAMPLES.md)** ← **Gotowy kod**
   - JavaScript (Vanilla, Axios, TypeScript, React)
   - Python
   - C# / .NET
   - PHP
   - Go
   - Java
   - Wszystkie przykłady gotowe do skopiowania!

### 🔧 Jestem ADMINEM / DevOps

1. **[IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)**
   - Status wdrożenia
   - Co działa, co jest w planach
   - Metryki wydajności

2. **[README.md](./README.md)**
   - Pełny przegląd projektu
   - Architektura
   - Dokumentacja techniczna

---

## 🎯 Co to API robi?

**Sprawdza kompatybilność między:**
- **FULL mods** - Pełne modyfikacje Among Us (Town of Us, The Other Roles, etc.)
- **DLL mods** - Dodatki/rozszerzenia (AleLuduMod, AUnlocker, etc.)

**Zwraca statusy:**
- 🟢 **F** (Favorite) - Działa idealnie, polecane
- 🔵 **W** (Works) - Działa poprawnie
- ⚪ **NT** (Not Tested) - Nieprzetestowane
- 🔴 **NW** (Not Work) - Nie działa

---

## 📡 Podstawowe Endpointy

### GET `/api/compatibility`
Pobierz kompatybilności dla moda.

```bash
# Dla DLL moda
curl "https://api.susmodder.app/api/compatibility?dllModId=5"

# Tylko działające
curl "https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W"

# Dla FULL moda
curl "https://api.susmodder.app/api/compatibility?fullModId=1"
```

**Response:**
```json
{
  "success": true,
  "count": 2,
  "compatibilities": [
    {
      "id": 11,
      "status": "F",
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1"
      }
    }
  ]
}
```

---

## 💻 Przykład Integracji

### React Component

```jsx
import React, { useEffect, useState } from 'react';

function CompatibilityChecker({ dllModId }) {
  const [compatibilities, setCompatibilities] = useState([]);

  useEffect(() => {
    fetch(`https://api.susmodder.app/api/compatibility?dllModId=${dllModId}&status=F,W`)
      .then(res => res.json())
      .then(data => setCompatibilities(data.compatibilities));
  }, [dllModId]);

  return (
    <div>
      <h3>Kompatybilne mody:</h3>
      {compatibilities.map(comp => (
        <div key={comp.id}>
          ✅ {comp.fullMod.name} - {comp.status === 'F' ? 'Polecane' : 'Działa'}
        </div>
      ))}
    </div>
  );
}
```

---

## 📁 Struktura Dokumentacji

```
DOC/COMPATIBILITY_MATRIX/
├── START_HERE.md                    ← ⭐ Czytasz teraz
│
├── FOR_EXTERNAL_PROJECTS.md         ← 🚀 Quick start (10 min)
├── QUICK_API_REFERENCE.md           ← ⚡ Ściąga (2 strony)
│
├── API_USAGE_GUIDE.md               ← 📖 Pełna dokumentacja (45+ stron)
├── CODE_EXAMPLES.md                 ← 💻 Gotowe przykłady kodu
│
├── IMPLEMENTATION_STATUS.md         ← ✅ Status wdrożenia
├── README.md                        ← 📚 Główny dokument projektu
│
└── [Pozostałe dokumenty techniczne]
```

---

## 🎯 Twoja ścieżka (zalecana)

### Początkujący / Szybki start:
1. ✅ Przeczytaj: [FOR_EXTERNAL_PROJECTS.md](./FOR_EXTERNAL_PROJECTS.md) (10 min)
2. ✅ Skopiuj: Przykład z [CODE_EXAMPLES.md](./CODE_EXAMPLES.md) (2 min)
3. ✅ Testuj: Wyślij pierwsze zapytanie (1 min)
4. ✅ Zacznij kodować! 🚀

### Średnio zaawansowany:
1. ✅ Quick reference: [QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md) (5 min)
2. ✅ Pełna dokumentacja: [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md) (30 min)
3. ✅ Wybierz przykład dla swojego języka: [CODE_EXAMPLES.md](./CODE_EXAMPLES.md)
4. ✅ Implementuj wszystkie use cases 💪

### Ekspert / DevOps:
1. ✅ Status: [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)
2. ✅ Architektura: [README.md](./README.md) + [01_DATABASE_DESIGN.md](./01_DATABASE_DESIGN.md)
3. ✅ Pełna spec: [02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)
4. ✅ Wszystko inne 🎓

---

## ✨ Najważniejsze Linki

| Link | Opis | Czas czytania |
|------|------|---------------|
| **[FOR_EXTERNAL_PROJECTS.md](./FOR_EXTERNAL_PROJECTS.md)** | Quick start dla projektów | 10 min |
| **[QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)** | Szybka ściąga | 2 min |
| **[API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)** | Pełny przewodnik | 45 min |
| **[CODE_EXAMPLES.md](./CODE_EXAMPLES.md)** | Gotowe przykłady (7 języków) | 15 min |

---

## 🔗 Dodatkowe Zasoby

**API:**
- Swagger UI: `https://api.susmodder.app/api-docs`
- Health check: `https://api.susmodder.app/health`
- Base URL: `https://api.susmodder.app`

**Wsparcie:**
- Dokumentacja techniczna: [README.md](./README.md)
- Status wdrożenia: [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)
- Troubleshooting: Zobacz [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md) sekcja "Debugowanie"

---

## ❓ FAQ

### Czy potrzebuję autoryzacji?
**Nie!** Podstawowe endpointy są publiczne. Autoryzacja tylko dla `/api/compatibility/matrix`.

### Jakie są limity API?
**100 req/min** dla GET. Więcej w [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md).

### Gdzie znaleźć ID modów?
```bash
curl "https://api.susmodder.app/susmodder-config" | jq '.[] | {Id, ModName, ModType}'
```

### Jak cacheować odpowiedzi?
Zobacz przykład w [FOR_EXTERNAL_PROJECTS.md](./FOR_EXTERNAL_PROJECTS.md) sekcja "Best Practices".

---

## 🎉 Gotowy do startu?

### Opcja A: Szybki start (10 minut)
👉 Idź do **[FOR_EXTERNAL_PROJECTS.md](./FOR_EXTERNAL_PROJECTS.md)**

### Opcja B: Pełna dokumentacja (1 godzina)
👉 Idź do **[API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)**

### Opcja C: Tylko ściąga (2 minuty)
👉 Idź do **[QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)**

---

**Powodzenia! 🚀**

*Masz pytania? Zobacz pełną dokumentację lub sprawdź Swagger UI.*

---

**Wersja:** 1.0.0
**Status:** ✅ Production Ready
**Data:** 2025-10-22
