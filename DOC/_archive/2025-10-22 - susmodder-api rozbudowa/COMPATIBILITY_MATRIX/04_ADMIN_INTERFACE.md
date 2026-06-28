# Interfejs Administracyjny - Compatibility Matrix

## 🎨 Przegląd Interfejsu

System zarządzania kompatybilnością będzie składał się z trzech głównych widoków:
1. **Matrix View** - Macierz kompatybilności (szybki przegląd)
2. **Detail View** - Szczegółowy widok edycji pojedynczego wpisu
3. **Bulk Edit** - Masowa edycja wielu wpisów

## 📊 Widok 1: Matrix View (Macierz)

### Layout

```
╔═══════════════════════════════════════════════════════════════════════╗
║  🧩 Compatibility Matrix                                    Admin     ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Full Mod: [Town of Us ▼]  Version: [5.4.0 (current) ▼]             ║
║                                                                        ║
║  🔍 Filter: [ All ▼ ]  📊 Show: [☑ Tested ☑ Untested ☐ Archived]    ║
║                                                                        ║
║  Quick Actions: [📋 Copy from v5.3.1] [✅ Mark All as NT]            ║
║                                                                        ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║    DLL Mod →        │ AleLudu │ AUnlocker │ LevelImp │ Crowded  │    ║
║    ────────────────┼─────────┼───────────┼──────────┼──────────┤    ║
║    Town of Us      │         │           │          │          │    ║
║    v5.4.0          │   NT ⚠️  │   NT ⚠️    │  NT ⚠️   │  NT ⚠️   │    ║
║    ────────────────┼─────────┼───────────┼──────────┼──────────┤    ║
║    The Other Roles │         │           │          │          │    ║
║    v4.8.0          │   F ✅   │   W ✅     │  NW ❌   │  NT ⚠️   │    ║
║    ────────────────┼─────────┼───────────┼──────────┼──────────┤    ║
║    ToH Enhanced    │         │           │          │          │    ║
║    v2.4.0          │   W ✅   │   NT ⚠️    │  NT ⚠️   │  F ✅    │    ║
║                                                                        ║
╠═══════════════════════════════════════════════════════════════════════╣
║  Legend: ✅ Works  ⚠️ Not Tested  ❌ Not Work                         ║
║  Stats: 12 combinations | 4 tested (33%) | 8 need testing            ║
╚═══════════════════════════════════════════════════════════════════════╝
```

### Interakcje

**1. Kliknięcie na komórkę:**
```javascript
// Otwiera modal z detalami
onClick(cell) {
  showModal({
    fullMod: "Town of Us v5.4.0",
    dllMod: "AleLuduMod latest",
    currentStatus: "NT",
    previousStatus: "F (v5.3.1)",
    lastTested: null,
    actions: ["Test", "Edit", "View History"]
  });
}
```

**Modal wygląd:**
```
┌─────────────────────────────────────────────────┐
│ Edit Compatibility                          ✕   │
├─────────────────────────────────────────────────┤
│                                                  │
│  Full Mod:  Town of Us v5.4.0                   │
│  DLL Mod:   AleLuduMod latest                   │
│                                                  │
│  Current Status: [Not Tested ▼]                 │
│    • Favorite (F)                                │
│    • Works (W)                                   │
│    • Not Tested (NT)  ← selected                 │
│    • Not Work (NW)                               │
│                                                  │
│  Previous Version Status:                        │
│    v5.3.1: Favorite ✅ (tested 2025-10-15)      │
│                                                  │
│  Tested By: [admin_________]                     │
│  Among Us: [2024.3.5_______]                     │
│                                                  │
│  Notes:                                          │
│  ┌────────────────────────────────────────────┐ │
│  │ [Type your notes here...]                  │ │
│  └────────────────────────────────────────────┘ │
│                                                  │
│  Issues URL: [____________________________]      │
│                                                  │
│  [Cancel]            [Save & Test Next] [Save]  │
│                                                  │
└─────────────────────────────────────────────────┘
```

**2. Zmiana wersji FULL mod:**
```javascript
onVersionChange(newVersion) {
  // Automatyczne przeładowanie macierzy dla nowej wersji
  reloadMatrix(fullModId, newVersion);
  
  // Highlight różnic względem poprzedniej wersji
  highlightChanges(prevVersion, newVersion);
}
```

**3. Copy from previous version:**
```javascript
async function copyFromPreviousVersion(currentVersion) {
  const prevVersion = getPreviousVersion(currentVersion); // np. 5.3.1
  
  const confirm = await showConfirm({
    title: "Copy Compatibilities",
    message: `Copy all compatibilities from ${prevVersion} to ${currentVersion}?
              All will be marked as "Not Tested" and need verification.`,
    action: "Copy"
  });
  
  if (confirm) {
    await api.post('/compatibility/copy-version', {
      fullModId,
      fromVersion: prevVersion,
      toVersion: currentVersion
    });
    
    reloadMatrix();
    showSuccess(`Copied ${count} compatibilities. Please test and update.`);
  }
}
```

### Kolorowanie Komórek

```css
/* Favorite - zielony */
.cell-F {
  background: #22c55e;
  color: white;
  font-weight: bold;
}

/* Works - niebieski */
.cell-W {
  background: #3b82f6;
  color: white;
}

/* Not Tested - szary (pomarańczowy akcent) */
.cell-NT {
  background: #fbbf24;
  color: #000;
}

/* Not Work - czerwony */
.cell-NW {
  background: #ef4444;
  color: white;
}

/* Hover efekt */
.cell:hover {
  box-shadow: 0 0 10px rgba(0,0,0,0.3);
  cursor: pointer;
  transform: scale(1.05);
}
```

## 📝 Widok 2: Detail View (Edycja Pojedyncza)

### Layout dla Wszystkich Modów FULL

```
╔═══════════════════════════════════════════════════════════════════════╗
║  🎮 Town of Us - Compatibility Details                       Admin    ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Mod Info:                                                            ║
║    Name: Town of Us                                                   ║
║    Type: Full                                                         ║
║    Current Version: 5.4.0 (updated 2 days ago)                       ║
║    Among Us: 2024.3.5                                                 ║
║                                                                        ║
╠═══════════════════════════════════════════════════════════════════════╣
║  DLL Compatibility (v5.4.0)                                           ║
║                                                                        ║
║  ┌─────────────────────────────────────────────────────────────────┐ ║
║  │  AleLuduMod (latest)                           [NT] ⚠️ Not Tested│ ║
║  │    Last tested: v5.3.1 on 2025-10-15 (status: F)                 │ ║
║  │    [🔍 View Details] [✏️ Edit] [🧪 Mark as Tested]               │ ║
║  └─────────────────────────────────────────────────────────────────┘ ║
║                                                                        ║
║  ┌─────────────────────────────────────────────────────────────────┐ ║
║  │  AUnlocker (latest)                            [W] ✅ Works       │ ║
║  │    Tested: 2025-10-20 by admin                                   │ ║
║  │    Notes: Działa, ale czasami laguje                             │ ║
║  │    [🔍 View Details] [✏️ Edit]                                    │ ║
║  └─────────────────────────────────────────────────────────────────┘ ║
║                                                                        ║
║  ┌─────────────────────────────────────────────────────────────────┐ ║
║  │  LevelImposter (Beta 0.20.4)                   [NW] ❌ Not Work  │ ║
║  │    Tested: 2025-10-16 by tester123                               │ ║
║  │    Notes: Crash przy ładowaniu custom map                        │ ║
║  │    Issues: github.com/LevelImposter/issues/123                   │ ║
║  │    [🔍 View Details] [✏️ Edit] [🔁 Retest]                        │ ║
║  └─────────────────────────────────────────────────────────────────┘ ║
║                                                                        ║
║  [+ Add New Compatibility]                                            ║
║                                                                        ║
╠═══════════════════════════════════════════════════════════════════════╣
║  Version History                                                      ║
║    • v5.4.0 (current) - 4/12 tested (33%)                            ║
║    • v5.3.1 - 12/12 tested (100%) ✅                                 ║
║    • v5.3.0 - 8/12 tested (67%)                                      ║
║    [Show All Versions]                                                ║
║                                                                        ║
╚═══════════════════════════════════════════════════════════════════════╝
```

### Quick Actions

**Przycisk "Mark as Tested":**
```javascript
async function quickTest(matrixId, fullModId, dllModId) {
  const modal = await showQuickTestModal({
    title: "Quick Test",
    fields: {
      status: { type: "select", options: ["F", "W", "NW"], required: true },
      notes: { type: "textarea", placeholder: "Quick notes...", required: false }
    }
  });
  
  if (modal.confirmed) {
    await api.put(`/compatibility/${matrixId}`, {
      status: modal.data.status,
      testedBy: getCurrentUser(),
      testedDate: new Date(),
      notes: modal.data.notes
    });
    
    reloadList();
    showSuccess("Status updated!");
  }
}
```

## 🚀 Widok 3: Bulk Edit (Masowa Edycja)

### Use Case: Aktualizacja Wielu Modów Jednocześnie

```
╔═══════════════════════════════════════════════════════════════════════╗
║  📋 Bulk Compatibility Edit                                  Admin    ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Mode: [Update Multiple Statuses ▼]                                  ║
║    • Update Multiple Statuses                                         ║
║    • Copy Version Compatibilities                                     ║
║    • Mass Test                                                        ║
║                                                                        ║
║  ┌─────────────────────────────────────────────────────────────────┐ ║
║  │ Select Combinations:                                             │ ║
║  │                                                                   │ ║
║  │  [☑] Town of Us 5.4.0 + AleLuduMod latest                       │ ║
║  │  [☑] Town of Us 5.4.0 + AUnlocker latest                        │ ║
║  │  [☐] Town of Us 5.4.0 + LevelImposter Beta 0.20.4              │ ║
║  │  [☑] Town of Us 5.4.0 + CrowdedMod 2.10.0                       │ ║
║  │                                                                   │ ║
║  │  Selected: 3 combinations                                        │ ║
║  └─────────────────────────────────────────────────────────────────┘ ║
║                                                                        ║
║  Action:                                                              ║
║    Set Status: [Works (W) ▼]                                         ║
║    Tested By: [admin_________]                                        ║
║    Among Us: [2024.3.5_______]                                        ║
║                                                                        ║
║    Notes (applied to all):                                            ║
║    ┌────────────────────────────────────────────────────────────┐   ║
║    │ Bulk tested on 2025-10-22, all working correctly          │   ║
║    └────────────────────────────────────────────────────────────┘   ║
║                                                                        ║
║  [Cancel]                          [Preview Changes] [Apply to All]  ║
║                                                                        ║
╚═══════════════════════════════════════════════════════════════════════╝
```

### Preview Before Apply

```javascript
async function previewBulkChanges(selectedIds, updates) {
  const preview = await api.post('/compatibility/preview-bulk', {
    ids: selectedIds,
    updates
  });
  
  showModal({
    title: "Preview Changes",
    content: `
      You are about to update ${preview.count} compatibility entries:
      
      ${preview.changes.map(c => `
        • ${c.fullModName} + ${c.dllModName}: ${c.oldStatus} → ${c.newStatus}
      `).join('\n')}
      
      This action cannot be undone (but history is preserved).
    `,
    actions: ["Cancel", "Confirm & Apply"]
  });
}
```

## 🔍 Widok 4: Search & Filter

### Advanced Search

```
╔═══════════════════════════════════════════════════════════════════════╗
║  🔍 Advanced Search                                                   ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Full Mod:      [Any ▼] or [Town of Us ▼]                           ║
║  DLL Mod:       [Any ▼] or [AleLuduMod ▼]                           ║
║                                                                        ║
║  Status:        [☑ F] [☑ W] [☑ NT] [☑ NW]                           ║
║                                                                        ║
║  Version:       [☐ Current only] [☑ All versions]                    ║
║                                                                        ║
║  Tested Date:   From: [2025-10-01] To: [2025-10-22]                 ║
║                                                                        ║
║  Tested By:     [Any ▼] or [admin ▼] or [tester123 ▼]              ║
║                                                                        ║
║  Among Us Ver:  [Any ▼] or [2024.3.5 ▼]                            ║
║                                                                        ║
║  [Clear Filters]                           [Search] [Export Results] ║
║                                                                        ║
╚═══════════════════════════════════════════════════════════════════════╝
```

## 📱 Responsywność

### Desktop (1920x1080)
- Pełna macierz z wszystkimi modami widoczna
- Hover tooltips z dodatkowymi info
- Drag & drop dla szybkiej edycji

### Tablet (768x1024)
- Macierz z scrollowaniem poziomym
- Kliknięcie otwiera full-screen modal
- Simplified controls

### Mobile (375x667)
- Lista zamiast macierzy
- Swipe gestures dla nawigacji
- Compact view z ikonami

```
┌─────────────────────────────┐
│ 🧩 Compatibility            │
├─────────────────────────────┤
│ Town of Us v5.4.0           │
│                             │
│ ┌─────────────────────────┐ │
│ │ AleLuduMod    NT ⚠️     │ │
│ └─────────────────────────┘ │
│                             │
│ ┌─────────────────────────┐ │
│ │ AUnlocker     W ✅      │ │
│ └─────────────────────────┘ │
│                             │
│ ┌─────────────────────────┐ │
│ │ LevelImposter NW ❌     │ │
│ └─────────────────────────┘ │
│                             │
│ [+ Add Compatibility]       │
└─────────────────────────────┘
```

## 🎯 UX Flow - Typowy Scenariusz

### Scenariusz: Admin Testuje Nową Wersję Moda

**Krok 1: Powiadomienie**
```
Discord Bot → #admin-channel:
"🔔 Town of Us updated to v5.4.0
12 DLL compatibilities need retesting.
[View in Admin Panel] [Start Testing]"
```

**Krok 2: Kliknięcie "Start Testing"**
→ Przekierowanie do `/admin/compatibility?fullModId=1&version=5.4.0&mode=test`

**Krok 3: Testing Mode**
```
╔═══════════════════════════════════════════════════════════════════════╗
║  🧪 Testing Mode: Town of Us v5.4.0                          (1/12)   ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Currently Testing: AleLuduMod latest                                 ║
║                                                                        ║
║  Previous Status (v5.3.1): Favorite ✅                                ║
║                                                                        ║
║  ┌────────────────────────────────────────────────────────────────┐  ║
║  │ Test Result:                                                    │  ║
║  │   [●] Favorite - Works perfectly, recommended                   │  ║
║  │   [ ] Works - Functions correctly                               │  ║
║  │   [ ] Not Work - Incompatible or broken                         │  ║
║  │   [ ] Skip - Test later                                         │  ║
║  └────────────────────────────────────────────────────────────────┘  ║
║                                                                        ║
║  Quick Notes: [Tested with 10 players, no issues________]            ║
║                                                                        ║
║  [◀ Previous]           [Skip] [Save & Next ▶]                       ║
║                                                                        ║
║  Progress: [████████░░░░] 1/12 (8%)                                  ║
║                                                                        ║
╚═══════════════════════════════════════════════════════════════════════╝
```

**Krok 4: Automatyczne Przejście**
→ Po kliknięciu "Save & Next" automatycznie ładuje się następny mod

**Krok 5: Podsumowanie**
```
╔═══════════════════════════════════════════════════════════════════════╗
║  ✅ Testing Complete!                                                 ║
╠═══════════════════════════════════════════════════════════════════════╣
║                                                                        ║
║  Town of Us v5.4.0 - All DLL mods tested                             ║
║                                                                        ║
║  Results:                                                             ║
║    ✅ Favorite (F): 5 mods                                            ║
║    ✅ Works (W): 4 mods                                               ║
║    ❌ Not Work (NW): 2 mods                                           ║
║    ⚠️ Skipped: 1 mod                                                 ║
║                                                                        ║
║  [View Full Matrix] [Notify Discord] [Export Report]                 ║
║                                                                        ║
╚═══════════════════════════════════════════════════════════════════════╝
```

## 🔗 Integracja z Istniejącym Panelem

### Lokalizacja w Menu

```
susadmin/
├── Dashboard
├── Mods Management
│   ├── Full Mods
│   ├── DLL Mods
│   └── 🆕 Compatibility Matrix  ← NOWE
├── Users
├── Versions
└── Settings
```

### URL Structure

```
/admin/compatibility                          # Matrix view
/admin/compatibility/:id                      # Detail view
/admin/compatibility/bulk                     # Bulk edit
/admin/compatibility/test/:fullModId          # Testing mode
/admin/compatibility/history/:id              # History
```

## 🛠️ Technologie i Narzędzia

### Frontend
- **React** lub **Vue.js** (w zależności od istniejącego stack'u)
- **TailwindCSS** dla stylowania
- **React Table** lub **AG Grid** dla macierzy
- **React Hot Toast** dla powiadomień

### Backend
- **Istniejące Node.js/Express API** (susmodder-api)
- Nowe endpointy w `/routes/compatibility.js`

### State Management
```javascript
// Przykład ze store
const compatibilityStore = {
  fullMods: [],
  dllMods: [],
  matrix: [],
  currentVersion: null,
  filters: {
    status: ["F", "W", "NT"],
    showArchived: false
  },
  testingMode: {
    active: false,
    currentIndex: 0,
    results: []
  }
};
```

## 🎨 Design System

### Kolory (zgodne z Among Us theme)

```css
:root {
  --color-favorite: #22c55e;    /* Zielony */
  --color-works: #3b82f6;       /* Niebieski */
  --color-not-tested: #fbbf24;  /* Pomarańczowy */
  --color-not-work: #ef4444;    /* Czerwony */
  --color-bg: #1a1a2e;          /* Ciemne tło */
  --color-card: #16213e;        /* Karty */
  --color-text: #e4e4e7;        /* Jasny tekst */
}
```

### Ikony

- ✅ Favorite/Works: `CheckCircleIcon`
- ⚠️ Not Tested: `ExclamationTriangleIcon`
- ❌ Not Work: `XCircleIcon`
- 🔍 View: `EyeIcon`
- ✏️ Edit: `PencilIcon`
- 🧪 Test: `BeakerIcon`
- 📋 Copy: `DocumentDuplicateIcon`

## 🚀 Implementacja - Priorytet

### Faza 1 (MVP - 1 tydzień)
- ✅ Matrix View (podstawowy)
- ✅ Edit Modal
- ✅ Basic CRUD operations

### Faza 2 (2-3 dni)
- ✅ Detail View
- ✅ Version switching
- ✅ Quick actions

### Faza 3 (2-3 dni)
- ✅ Testing Mode
- ✅ Bulk Edit
- ✅ Discord integration

### Faza 4 (1-2 dni)
- ✅ Advanced filters
- ✅ Export/import
- ✅ Mobile responsive
