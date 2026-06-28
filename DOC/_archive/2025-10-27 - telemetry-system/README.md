# README - Telemetry System

## 🚀 Quick Start

System telemetrii dla SUSModder - zbieranie anonimowych statystyk użytkowania aplikacji.

## 📚 Dokumentacja

| Plik | Opis |
|------|------|
| [00_PROJECT_SUMMARY.md](00_PROJECT_SUMMARY.md) | Podsumowanie projektu, cele, architektura |
| [01_SUSMODDER_IMPLEMENTATION.md](01_SUSMODDER_IMPLEMENTATION.md) | Implementacja w C# (SUSModder) |
| [02_API_SPECIFICATION.md](02_API_SPECIFICATION.md) | Specyfikacja endpointu `/api/telemetry/heartbeat` |
| [03_REDIS_DATA_DESIGN.md](03_REDIS_DATA_DESIGN.md) | Struktura danych w Redis, query patterns |
| [04_DASHBOARD_QUERIES.md](04_DASHBOARD_QUERIES.md) | Analytics API i dashboard queries |

## 🎯 Co zbieramy?

- ✅ Anonimowy hash użytkownika (SHA256 z Hardware ID)
- ✅ Wersja aplikacji
- ✅ Platforma (Steam / Epic)
- ✅ Język UI
- ✅ Lista ID zainstalowanych modów
- ✅ Czas sesji (w sekundach)

## 🔒 Prywatność

- **NIE przechowujemy IP**
- Hash generowany lokalnie (server nigdy nie widzi raw Hardware ID)
- Opt-out dostępny w Settings
- TTL 90 dni na raw data

## 📊 Metryki dostępne

- Daily/Weekly/Monthly Active Users (DAU/WAU/MAU)
- Popularność platform (Steam vs Epic %)
- Adopcja wersji aplikacji
- Top 10 najpopularniejszych modów
- Średni czas sesji
- Dystrybucja języków

## 🛠️ Stack technologiczny

### SUSModder (Client):
- C# / .NET 8
- Avalonia 11
- System.Management (WMI dla Hardware ID)

### Backend (Server):
- Node.js + Express
- Redis (data storage)
- Joi (validation)

## 📦 Instalacja

### 1. SUSModder (C#)

```bash
# Dodaj zależność
dotnet add SUSModder.Core package System.Management

# Skopiuj pliki z 01_SUSMODDER_IMPLEMENTATION.md:
# - HardwareIdProvider.cs → SUSModder.Core/Utilities/
# - SessionTracker.cs → SUSModder.Core/Services/
# - TelemetryService.cs → SUSModder.Core/Services/

# Zaktualizuj appsettings.json
# Dodaj TelemetryEnabled: true

# Zmodyfikuj App.axaml.cs (inicjalizacja)
# Dodaj opt-out UI w SettingsView
```

### 2. Backend API (Node.js)

```bash
# Install dependencies
npm install express redis joi dotenv

# Create .env file
REDIS_HOST=localhost
REDIS_PORT=6379
REDIS_PASSWORD=your_password
ADMIN_TOKEN=your_secret_admin_token

# Skopiuj kod z 02_API_SPECIFICATION.md
# - routes/telemetry.js
# - config/redis.js
# - middleware/auth.js

# Start server
node server.js
```

### 3. Redis

```bash
# Docker
docker run -d --name redis-telemetry \
  -p 6379:6379 \
  redis:latest

# Lub zainstaluj lokalnie
# https://redis.io/download
```

## 🧪 Testing

### Test 1: Generowanie Hash (C#)

```csharp
var hash = HardwareIdProvider.GetAnonymousUserHash();
Console.WriteLine($"User hash: {hash}");
// Output: 64-char hex string
```

### Test 2: Wysłanie Heartbeat (Curl)

```bash
curl -X POST http://localhost:3001/api/telemetry/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "userHash": "a1b2c3...",
    "appVersion": "2.0.0",
    "platform": "steam",
    "language": "pl",
    "installedModIds": [1, 3, 7],
    "sessionTimeSeconds": 100,
    "timestamp": "2025-10-27T12:00:00Z"
  }'
```

### Test 3: Analytics Query

```bash
curl -X GET "http://localhost:3001/api/telemetry/analytics/dau" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

## 📈 Dashboard Endpoints

| Endpoint | Opis |
|----------|------|
| `GET /api/telemetry/analytics/dau` | Daily Active Users |
| `GET /api/telemetry/analytics/active-users?period=week` | WAU/MAU |
| `GET /api/telemetry/analytics/platforms` | Steam vs Epic |
| `GET /api/telemetry/analytics/versions` | Version adoption |
| `GET /api/telemetry/analytics/top-mods` | Top 10 mods |
| `GET /api/telemetry/analytics/languages` | Language distribution |
| `GET /api/telemetry/analytics/sessions` | Session statistics |
| `GET /api/telemetry/analytics/overview` | Full dashboard |

Wszystkie wymagają admin tokenu: `Authorization: Bearer {ADMIN_TOKEN}`

## 🔧 Redis Queries (Manual)

```bash
# DAU dzisiaj
redis-cli SCARD "telemetry:daily:users:2025-10-27"

# Platform stats
redis-cli HGETALL "telemetry:daily:stats:2025-10-27"

# Top 10 modów
redis-cli ZREVRANGE "telemetry:mods:popularity:2025-10-27" 0 9 WITHSCORES

# Memory usage
redis-cli INFO memory
```

## 📝 Checklist Implementacji

### SUSModder (C#):
- [ ] Dodać `HardwareIdProvider.cs`
- [ ] Dodać `SessionTracker.cs`
- [ ] Dodać `TelemetryService.cs`
- [ ] Zmodyfikować `App.axaml.cs` (inicjalizacja)
- [ ] Dodać `TelemetryEnabled` do `appsettings.json`
- [ ] Dodać opt-out UI w Settings
- [ ] Testy lokalne (hash generation, heartbeat)

### Backend API (Node.js):
- [ ] Setup Express + Redis
- [ ] Implementować `POST /api/telemetry/heartbeat`
- [ ] Dodać validation (Joi)
- [ ] Dodać rate limiting
- [ ] Implementować analytics endpoints
- [ ] Dodać auth middleware
- [ ] Testy (unit + integration)

### Deployment:
- [ ] Deploy Redis (Docker lub managed service)
- [ ] Deploy API (production server)
- [ ] Configure environment variables
- [ ] Setup monitoring (logs, metrics)
- [ ] Create admin dashboard (optional)

## 🎨 Opcjonalny Frontend Dashboard

Możesz stworzyć prosty React/Vue dashboard do wyświetlania metryk:

```javascript
// Example: Fetch DAU
const response = await fetch('/api/telemetry/analytics/dau', {
  headers: {
    'Authorization': `Bearer ${ADMIN_TOKEN}`
  }
});

const data = await response.json();
console.log(`DAU: ${data.data.dau}`);

// Render chart using Chart.js or similar
```

## 📊 Performance & Scaling

### Dla 10,000 DAU:
- Redis memory: ~300 MB
- API response time: < 50ms
- Rate limiting: 1 req/10min per user (nie przeciąży)

### Dla 100,000 DAU:
- Redis memory: ~3 GB
- Rozważ sharding lub Redis Cluster
- Cache analytics queries (5-15 min TTL)

## 🔒 GDPR Compliance

✅ **Zgodność z GDPR:**
- Zbieramy tylko anonimowe dane
- Brak PII (Personally Identifiable Information)
- Hash jednostronnie zahashowany (nie da się odwrócić)
- Opt-out dostępny
- TTL 90 dni (automatic cleanup)

## 📞 Support

Pytania? Sprawdź:
1. [01_SUSMODDER_IMPLEMENTATION.md](01_SUSMODDER_IMPLEMENTATION.md) - dla C# dev
2. [02_API_SPECIFICATION.md](02_API_SPECIFICATION.md) - dla backend dev
3. [03_REDIS_DATA_DESIGN.md](03_REDIS_DATA_DESIGN.md) - dla Redis queries
4. [04_DASHBOARD_QUERIES.md](04_DASHBOARD_QUERIES.md) - dla analytics

---

**Status:** 🟢 Ready for Implementation  
**Estimated total time:** 5 dni roboczych  
**Data utworzenia:** 2025-10-27
