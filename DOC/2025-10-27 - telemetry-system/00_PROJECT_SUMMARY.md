# Telemetry System - Podsumowanie Projektu

## 📋 Przegląd

Lightweightowy system telemetrii dla SUSModder umożliwiający zbieranie **anonimowych** statystyk użytkowania aplikacji.

## 🎯 Cele

### Główne cele biznesowe:
- Zbieranie liczby unikalnych użytkowników dziennie/tygodniowo/miesięcznie
- Monitorowanie popularności platform (Steam vs Epic)
- Śledzenie adopcji nowych wersji aplikacji
- Analiza popularności modów (które są najczęściej instalowane)
- Statystyki czasu używania aplikacji

### Cele techniczne:
- Minimalne zmiany w SUSModder (non-invasive)
- Zero impact na performance aplikacji
- Maksymalna prywatność użytkowników (opt-out + pełna anonimowość)
- Redis jako backend storage (szybki, prosty, z TTL)

## 🔒 Prywatność i Anonimowość

### Zasady podstawowe:
1. **NIE PRZECHOWUJEMY IP** - nawet tymczasowo
2. **Hash z Hardware ID** - generowany lokalnie, jednostronnie (SHA256)
3. **Opt-out domyślnie włączony** - użytkownik może wyłączyć w Settings
4. **Brak danych osobowych** - zero identyfikatorów użytkownika

### Co zbieramy:
- ✅ **Anonimowy hash użytkownika** (SHA256 z Hardware ID)
- ✅ **Wersja aplikacji** (`2.0.0`)
- ✅ **Platforma** (`steam` / `epic`)
- ✅ **Język UI** (`pl` / `en`)
- ✅ **Lista ID zainstalowanych modów** (np. `[1, 3, 7, 12]`)
- ✅ **Czas działania aplikacji** (w sekundach od startu)
- ✅ **Timestamp** (UTC)

### Czego NIE zbieramy:
- ❌ Adres IP
- ❌ Ścieżki instalacji
- ❌ Nazwy użytkowników
- ❌ Hardware specs (poza hashem ID)
- ❌ Historia działań w aplikacji

## 🏗️ Architektura

```
┌─────────────────┐
│  SUSModder C#   │
│                 │
│  TelemetryService
│  - Hash HW ID   │
│  - Heartbeat    │
│  - Session time │
└────────┬────────┘
         │ POST /api/telemetry/heartbeat
         │ (Fire & Forget, timeout 2s)
         ↓
┌─────────────────┐
│   API Server    │
│   (Node.js)     │
│                 │
│  Rate limiting  │
│  Validation     │
└────────┬────────┘
         │
         ↓
┌─────────────────┐
│     Redis       │
│                 │
│  - Heartbeats   │
│  - Daily stats  │
│  - TTL: 90 days │
└─────────────────┘
```

## 📊 Data Model (Redis)

### Heartbeat Entry
```json
{
  "userHash": "a1b2c3...",
  "appVersion": "2.0.0",
  "platform": "steam",
  "language": "pl",
  "installedModIds": [1, 3, 7, 12],
  "sessionTimeSeconds": 1234,
  "timestamp": "2025-10-27T12:34:56Z"
}
```

### Redis Keys Structure
```
telemetry:heartbeat:{userHash}:{timestamp}  → JSON (TTL: 90 days)
telemetry:daily:users:{date}                → Set of userHashes (TTL: 365 days)
telemetry:daily:stats:{date}                → Hash (platform, version, etc.)
```

## 📈 Metryki dostępne

### Dashboardy:
1. **Unikalni użytkownicy** (DAU, WAU, MAU)
2. **Popularność platform** (Steam vs Epic %)
3. **Adopcja wersji** (ile % użytkowników ma najnowszą wersję)
4. **Top 10 modów** (najczęściej instalowane)
5. **Średni czas sesji** (ile czasu użytkownicy spędzają w aplikacji)
6. **Język UI** (dystrybucja językowa)

## 🚀 Implementacja

### Fazy wdrożenia:

#### Faza 1: SUSModder (C#) - 1 dzień
- [x] `TelemetryService.cs` - generowanie hash, heartbeat
- [x] `HardwareIdProvider.cs` - pobieranie i hashowanie HW ID
- [x] Integracja w `App.axaml.cs` (startup + shutdown)
- [x] Opt-out UI w Settings
- [x] Testy lokalne

#### Faza 2: API Backend (Node.js) - 1 dzień
- [x] Endpoint `POST /api/telemetry/heartbeat`
- [x] Rate limiting (1 req/10min per userHash)
- [x] Redis integration (save + TTL)
- [x] Validation schema
- [x] Error handling

#### Faza 3: Dashboard (Admin) - 2 dni
- [x] Queries do Redis (agregacje)
- [x] REST API dla dashboardu
- [x] Prosty frontend (React/Vue) z wykresami
- [x] Eksport danych (CSV/JSON)

#### Faza 4: Monitoring i Optymalizacja - 1 dzień
- [x] Logging i monitoring
- [x] Performance testing
- [x] Dokumentacja dla zespołu

**Łączny czas: ~5 dni roboczych**

## 📁 Struktura dokumentacji

```
DOC/2025-10-27 - telemetry-system/
├── 00_PROJECT_SUMMARY.md          (ten plik)
├── 01_SUSMODDER_IMPLEMENTATION.md (C# implementation)
├── 02_API_SPECIFICATION.md        (Backend endpoint spec)
├── 03_REDIS_DATA_DESIGN.md        (Redis keys & queries)
├── 04_DASHBOARD_QUERIES.md        (Analytics & reporting)
├── 05_PRIVACY_COMPLIANCE.md       (GDPR, anonimowość)
├── 06_DEPLOYMENT_GUIDE.md         (Wdrożenie production)
└── README.md                       (Quick start)
```

## ⚠️ Ważne uwagi

### Security:
- Hash generowany lokalnie (client-side) - serwer nigdy nie widzi raw Hardware ID
- Fire-and-forget - błędy telemetrii nie blokują aplikacji
- Rate limiting - ochrona przed abuse

### Performance:
- Async POST z timeout 2s
- Brak retry logic (jeśli fail, to fail - nie blokujemy UI)
- Redis - ultra-szybki, in-memory storage

### Compliance:
- GDPR-friendly (anonimowe dane, brak możliwości identyfikacji)
- Opt-out dostępny w UI
- TTL 90 dni - automatyczne usuwanie starych danych

## 🎯 Success Metrics

Po wdrożeniu będziemy wiedzieć:
- Ile osób **faktycznie** używa SUSModder dziennie
- Czy ludzie aktualizują aplikację (adoption rate nowych wersji)
- Które mody są najbardziej popularne
- Jak długo trwa typowa sesja użytkownika
- Jaki jest podział Steam/Epic w community

---

**Status:** 🟡 Pending Implementation  
**Data utworzenia:** 2025-10-27  
**Autor:** AI + boratsc
