# Diagramy Architektury - Compatibility Matrix

## 📐 Diagram ERD (Entity Relationship Diagram)

### Relacje Tabel

```mermaid
erDiagram
    config ||--o{ compatibility_matrix : "FullMod"
    config ||--o{ compatibility_matrix : "DllMod"
    
    config {
        INT Id PK
        VARCHAR ModName
        VARCHAR ModType "full/dll"
        VARCHAR ModVersion
        VARCHAR PngFileName
        VARCHAR InstallPath
        VARCHAR GitHubRepoOrLink
        VARCHAR EpicGitHubRepoOrLink
        VARCHAR DllInstallPath
        VARCHAR AmongVersion
        TEXT Description
        DATETIME LastUpdated
    }
    
    compatibility_matrix {
        INT Id PK
        INT FullModId FK
        INT DllModId FK
        VARCHAR FullModVersion
        VARCHAR DllModVersion
        ENUM CompatibilityStatus "F,W,NT,NW"
        DATETIME TestedDate
        VARCHAR TestedBy
        VARCHAR AmongUsVersion
        TEXT Notes
        VARCHAR IssuesUrl
        DATETIME CreatedAt
        DATETIME UpdatedAt
        VARCHAR CreatedBy
        VARCHAR UpdatedBy
    }
```

---

## 🔄 Diagram Przepływu Danych

### Use Case 1: Sprawdzenie Kompatybilności

```mermaid
sequenceDiagram
    participant User
    participant Frontend
    participant API
    participant DB
    
    User->>Frontend: Wybiera DLL Mod (AleLuduMod)
    Frontend->>API: GET /api/compatibility?dllModId=5
    API->>DB: SELECT FROM compatibility_matrix WHERE DllModId=5
    DB-->>API: Zwraca listę kompatybilności
    API-->>Frontend: JSON z kompatybilnymi FULL modami
    Frontend-->>User: Wyświetla listę z statusami (F, W, NT, NW)
```

### Use Case 2: Aktualizacja Statusu przez Admina

```mermaid
sequenceDiagram
    participant Admin
    participant UI
    participant API
    participant DB
    participant Discord
    
    Admin->>UI: Kliknięcie na komórkę macierzy
    UI->>Admin: Otwiera modal edycji
    Admin->>UI: Zmienia status NT → F, dodaje notatki
    UI->>API: PUT /api/compatibility/123 + Bearer Token
    API->>API: Walidacja tokena
    API->>DB: UPDATE compatibility_matrix SET Status='F'
    DB-->>API: Potwierdzenie
    API->>Discord: Webhook: Status zmieniony
    API-->>UI: Success response
    UI-->>Admin: Wyświetla potwierdzenie
```

---

## 🏗️ Diagram Architektury Systemu

```mermaid
graph TB
    subgraph "Warstwa Prezentacji"
        A[Frontend App<br/>React/Vue]
        B[Admin Panel<br/>susadmin]
        C[Discord Bot<br/>sustats]
    end
    
    subgraph "Warstwa API"
        D[Node.js API<br/>Express]
        E[Auth Middleware<br/>Bearer Token]
        F[Routes Handler<br/>/compatibility]
    end
    
    subgraph "Warstwa Danych"
        G[(MySQL Database<br/>compatibility_matrix)]
        H[(MySQL Database<br/>config)]
    end
    
    A -->|REST API| D
    B -->|REST API| D
    C -->|REST API| D
    
    D --> E
    E --> F
    
    F -->|SQL Query| G
    F -->|SQL Query| H
    
    style A fill:#4CAF50
    style B fill:#2196F3
    style C fill:#FF9800
    style D fill:#9C27B0
    style G fill:#F44336
    style H fill:#F44336
```

---

## 🔀 Diagram Obsługi Wersji

```mermaid
graph LR
    A[Town of Us v5.3.1<br/>+ AleLuduMod = F✅] 
    B[Admin Aktualizuje<br/>Town of Us → v5.4.0]
    C{Automatyczne<br/>Tworzenie Wpisu}
    D[Town of Us v5.4.0<br/>+ AleLuduMod = NT⚠️]
    E[Admin Testuje<br/>Nową Kombinację]
    F[Town of Us v5.4.0<br/>+ AleLuduMod = F✅]
    
    A -->|Zachowane| B
    B --> C
    C -->|INSERT| D
    D --> E
    E -->|UPDATE| F
    
    style A fill:#4CAF50
    style D fill:#FFC107
    style F fill:#4CAF50
```

---

## 📊 Diagram Stanów Kompatybilności

```mermaid
stateDiagram-v2
    [*] --> NT: Nowa kombinacja<br/>modów
    
    NT --> F: Przetestowano<br/>Działa idealnie
    NT --> W: Przetestowano<br/>Działa poprawnie
    NT --> NW: Przetestowano<br/>Nie działa
    
    F --> W: Retestowano<br/>Drobne problemy
    F --> NW: Nowa wersja<br/>Przestało działać
    
    W --> F: Poprawki<br/>Teraz idealne
    W --> NW: Nowa wersja<br/>Przestało działać
    
    NW --> W: Poprawki<br/>Teraz działa
    NW --> F: Poprawki<br/>Teraz idealne
    
    F --> NT: Nowa wersja<br/>Do przetestowania
    W --> NT: Nowa wersja<br/>Do przetestowania
    NW --> NT: Nowa wersja<br/>Do przetestowania
    
    NT --> [*]: Mod usunięty
    F --> [*]: Mod usunięty
    W --> [*]: Mod usunięty
    NW --> [*]: Mod usunięty
```

---

## 🎯 Diagram Przypadków Użycia

```mermaid
graph TB
    subgraph "Aktorzy"
        U[Użytkownik]
        A[Administrator]
        D[Developer]
    end
    
    subgraph "Przypadki Użycia"
        UC1[Sprawdź kompatybilność<br/>DLL z FULL]
        UC2[Sprawdź kompatybilność<br/>FULL z DLL]
        UC3[Dodaj nową<br/>kompatybilność]
        UC4[Zaktualizuj<br/>status]
        UC5[Przetestuj<br/>nową wersję]
        UC6[Zobacz<br/>macierz]
        UC7[Export<br/>danych]
        UC8[Integracja<br/>API]
    end
    
    U --> UC1
    U --> UC2
    
    A --> UC3
    A --> UC4
    A --> UC5
    A --> UC6
    A --> UC7
    
    D --> UC8
    
    style U fill:#4CAF50
    style A fill:#2196F3
    style D fill:#FF9800
```

---

## 📈 Diagram Procesu Testowania

```mermaid
flowchart TD
    Start([Nowa Wersja Moda FULL])
    
    A[System tworzy wpisy NT<br/>dla wszystkich DLL]
    B[Powiadomienie Discord<br/>do adminów]
    C{Admin wchodzi<br/>w Testing Mode}
    
    D[Wybór pierwszej<br/>kombinacji DLL]
    E[Test in-game]
    
    F{Działa?}
    G[Oznacz jako F/W]
    H[Oznacz jako NW]
    I[Dodaj notatki<br/>i szczegóły]
    
    J{Więcej<br/>kombinacji?}
    K[Następna kombinacja]
    
    L[Podsumowanie testów]
    M[Powiadomienie Discord:<br/>Testowanie zakończone]
    End([Koniec])
    
    Start --> A
    A --> B
    B --> C
    C -->|Tak| D
    C -->|Nie| End
    
    D --> E
    E --> F
    
    F -->|Tak| G
    F -->|Nie| H
    
    G --> I
    H --> I
    
    I --> J
    J -->|Tak| K
    K --> D
    
    J -->|Nie| L
    L --> M
    M --> End
    
    style Start fill:#4CAF50
    style End fill:#F44336
    style G fill:#4CAF50
    style H fill:#F44336
```

---

## 🗺️ Diagram Interfejsu Użytkownika

```mermaid
graph TB
    subgraph "Admin Panel"
        A[Dashboard]
        
        subgraph "Compatibility Matrix"
            B[Matrix View<br/>Tabela z wszystkimi kombinacjami]
            C[Detail View<br/>Lista dla jednego moda]
            D[Testing Mode<br/>Sekwencyjne testowanie]
            E[Bulk Edit<br/>Edycja wielu wpisów]
        end
        
        F[Edit Modal<br/>Edycja pojedynczego wpisu]
    end
    
    A --> B
    A --> C
    B --> F
    C --> D
    C --> F
    B --> E
    
    F -.->|Zapisz| B
    F -.->|Zapisz| C
    D -.->|Zapisz wszystkie| C
    E -.->|Zapisz zaznaczone| B
    
    style B fill:#2196F3
    style C fill:#4CAF50
    style D fill:#FF9800
    style E fill:#9C27B0
    style F fill:#F44336
```

---

## 🔐 Diagram Autoryzacji

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant AuthMiddleware
    participant Handler
    participant DB
    
    Client->>API: POST /api/compatibility<br/>Header: Authorization Bearer TOKEN
    API->>AuthMiddleware: requireAuthToken()
    
    alt Token valid
        AuthMiddleware->>Handler: next()
        Handler->>DB: INSERT compatibility
        DB-->>Handler: Success
        Handler-->>Client: 201 Created
    else Token invalid
        AuthMiddleware-->>Client: 401 Unauthorized
    else No token
        AuthMiddleware-->>Client: 401 Unauthorized
    end
```

---

## 📦 Diagram Deploymentu

```mermaid
graph TB
    subgraph "Docker Compose"
        subgraph "nginx Container"
            A[Nginx<br/>Reverse Proxy]
        end
        
        subgraph "susmodder-api Container"
            B[Node.js API<br/>Port 3001]
        end
        
        subgraph "mysql Container"
            C[(MySQL 8.0<br/>Port 3306)]
        end
        
        subgraph "susadmin Container" 
            D[Admin Panel<br/>React App]
        end
    end
    
    Internet[Internet] -->|HTTPS| A
    
    A -->|/api/*| B
    A -->|/admin/*| D
    
    B -->|TCP 3306| C
    D -->|REST API| B
    
    C -.->|Volume Mount| E[/mysql/data]
    B -.->|Volume Mount| F[/nginx/html]
    
    style A fill:#4CAF50
    style B fill:#2196F3
    style C fill:#F44336
    style D fill:#FF9800
```

---

## 🔄 Diagram CI/CD (Przyszłość)

```mermaid
flowchart LR
    A[Git Push] --> B[GitHub Actions]
    
    B --> C{Branch?}
    
    C -->|feature/*| D[Run Tests]
    D --> E[Code Review]
    
    C -->|main| F[Run Tests]
    F --> G[Build Docker]
    G --> H[Push to Registry]
    H --> I[Deploy to Staging]
    I --> J{Tests Pass?}
    
    J -->|Yes| K[Deploy to Production]
    J -->|No| L[Rollback]
    
    K --> M[Health Check]
    M --> N[Notify Discord]
    
    style A fill:#4CAF50
    style K fill:#4CAF50
    style L fill:#F44336
    style N fill:#2196F3
```

---

## 📊 Diagram Metryk i Monitoringu

```mermaid
graph TB
    subgraph "Metryki Systemu"
        A[API Response Time]
        B[Database Query Time]
        C[Error Rate]
        D[Request Rate]
    end
    
    subgraph "Metryki Biznesowe"
        E[Testing Progress<br/>Tested / Total]
        F[Compatibility Rate<br/>Working / Total]
        G[Update Frequency<br/>Changes per Week]
        H[User Engagement<br/>API Calls]
    end
    
    subgraph "Monitoring Tools"
        I[Docker Logs]
        J[MySQL Slow Query Log]
        K[Custom Dashboard]
        L[Discord Alerts]
    end
    
    A --> K
    B --> J
    C --> L
    D --> K
    
    E --> K
    F --> K
    G --> K
    H --> K
    
    style K fill:#4CAF50
    style L fill:#FF9800
```

---

## 🎨 Diagram Kolorów UI

```mermaid
graph LR
    subgraph "Status Colors"
        F[F - Favorite<br/>#22c55e]
        W[W - Works<br/>#3b82f6]
        NT[NT - Not Tested<br/>#fbbf24]
        NW[NW - Not Work<br/>#ef4444]
    end
    
    style F fill:#22c55e,color:#fff
    style W fill:#3b82f6,color:#fff
    style NT fill:#fbbf24,color:#000
    style NW fill:#ef4444,color:#fff
```

---

## 🗂️ Diagram Struktury Plików

```mermaid
graph TB
    Root["/srv/synapsekit-boracik"]
    
    Root --> Doc["DOC/"]
    Doc --> CM["COMPATIBILITY_MATRIX/"]
    
    CM --> F1["00_PROJECT_SUMMARY.md"]
    CM --> F2["01_DATABASE_DESIGN.md"]
    CM --> F3["02_API_SPECIFICATION.md"]
    CM --> F4["03_VERSION_HANDLING.md"]
    CM --> F5["04_ADMIN_INTERFACE.md"]
    CM --> F6["05_MIGRATION_PLAN.md"]
    CM --> F7["QUICK_REFERENCE.md"]
    CM --> F8["README.md"]
    CM --> F9["DIAGRAMS.md"]
    
    Root --> API["susmodder-api/"]
    API --> Routes["routes/compatibility.js"]
    API --> Migrations["migrations/"]
    
    Migrations --> M1["001_create_compatibility_matrix.sql"]
    Migrations --> M2["002_populate_initial_data.sql"]
    
    style CM fill:#4CAF50
    style Routes fill:#2196F3
    style Migrations fill:#FF9800
```

---

**Wszystkie diagramy można renderować w:**
- GitHub (natywnie wspiera Mermaid)
- VS Code (z rozszerzeniem Markdown Preview Mermaid)
- Mermaid Live Editor (https://mermaid.live)
- Dokumentacja online

**Ostatnia aktualizacja:** 2025-10-22
