# 12 – Integracja voice chat / serwer Discord SUSModder + Clair

**Priorytet:** 🟢 P2
**Effort:** V1 ~1 dzień (Clair już istnieje!), V2 ~1-2 dni, V3 ~5-7 dni

## Cel

1. Stworzyć własny serwer Discord SUSModder jako alternatywę dla rozproszonych społeczności (np. "Wspólnicy" – 55k userów, największy polski serwer Among Us)
2. Zintegrować go z istniejącym botem **Clair** (już obsługuje Mira API, wyniki gier, SignalR)
3. Opcjonalnie: BetterCrewLink jako proximity voice

## Opcja A: Własny serwer Discord SUSModder + Clair ✨ (rekomendowana)

### Bot Clair – co już ma

Clair to nasz własny bot Discord (discord.js v14, Docker), który już obsługuje:
- **Wyniki gier Among Us** przez Mira API (SUSTATS → Clair API → Discord embed)
- **SignalR WebSocket** do real-time komunikacji
- System ekonomii, role, slash commands
- Kategoryzację ról z susmodder.app/api/roles-modifiers

### Co dodać do Clair (V1, ~1 dzień)

| Feature | Opis | Effort |
|---------|------|--------|
| Komenda /kod ABCDEF | Wysyła kod lobby na odpowiedni kanał | ~1h |
| Komenda /kody | Lista aktywnych kodów w kanale | ~1h |
| Auto-cleanup | Usuwanie kodów starszych niż 15 min (cron) | ~1h |
| Kanały #lobby-kody/{mod} | Per-mod kanały na serwerze Discord | ~30 min |
| Webhook z SUSModder | User wpisuje kod w apce -> Clair publikuje na Discord | ~1h |
| Endpoint w clair-api | GET /api/lobby-codes do odczytu z SUSModder | ~1h |

**Razem:** ~5-6h – bo bot już stoi, ma infrastrukturę, wystarczy dodać komendy.

### Struktura serwera Discord

`
📢 ogloszenia
📋 changelog
💬 general
🎮 lobby-kody
   ├── #town-of-us
   ├── #the-other-roles
   └── #pozostale
🔊 voice
   ├── General
   ├── Town of Us
   └── The Other Roles
`

### Integracja z SUSModder

- Panel "Serwer Discord SUSModder" (obok istniejącego panelu polecanych serwerów)
- Kliknięcie -> invite link -> dołącza do serwera
- W panelu moda: przycisk "Znajdź lobby" -> otwiera #town-of-us na Discordzie
- Kod wpisany w SUSModder -> opcjonalnie auto-post na Discord przez webhook Clair

### V2: SignalR real-time (~1-2 dni)

Clair już ma SignalR. Można dodać:
- SUSModder subskrybuje kanał SignalR lobby-codes/{modId}
- Gdy ktoś wrzuci kod przez /kod na Discordzie -> SUSModder widzi go na żywo w UI
- Dwukierunkowo: kod z SUSModder -> Discord, kod z Discorda -> SUSModder

## Opcja B: BetterCrewLink (proximity voice)

**Repo:** https://github.com/OhMyGuus/BetterCrewLink

- **V1:** Dodać jako DLL do katalogu (~1 dzień) – user instaluje jednym klikiem
- **V2:** Fork + integracja przez localhost API (~5-7 dni) – UI w SUSModder: lista graczy, volume

Nie przepisywać WebRTC na C# – zostawić BCL jako osobny proces Node.js.

## Opcja C: Discord Rich Presence

Pokazuje w profilu Discord: "Gra w Town of Us przez SUSModder". Proste, ~1 dzień.

## Rekomendowana strategia

`
V1 (1 dzień):   Serwer Discord + rozbudowa Clair
                (/kod, /kody, webhook, auto-cleanup)
                + BetterCrewLink jako DLL
V2 (1-2 dni):   SignalR integracja SUSModder <-> Clair
                (kody w czasie rzeczywistym w UI)
V3 (5-7 dni):   Fork BetterCrewLink -> voice (opcjonalnie)
`

## Decyzje

- [ ] Czy stawiać własny serwer Discord SUSModder?
- [ ] Rozbudowa Clair: od razu /kod + /kody + webhook?
- [ ] SignalR integracja (V2) – czy warto od razu?
- [ ] BetterCrewLink: tylko jako DLL (V1) czy fork (V3)?
- [ ] Rich Presence – przy okazji (1 dzień)?

