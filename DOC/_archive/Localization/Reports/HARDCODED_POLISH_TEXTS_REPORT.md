# 📋 Raport: Hardcodowane polskie teksty w SUSModder

**Data:** 2025-11-06  
**Wersja aplikacji:** 2.2.0+  
**Zakres:** Status Bar, Tooltips i inne hardcodowane teksty

---

## 🎯 Podsumowanie

Znaleziono **17 instancji** hardcodowanych polskich tekstów w pliku `MainWindowViewModel.StatusBar.cs`, które wymagają przeniesienia do systemu lokalizacji.

---

## 📍 Szczegółowe znaleziska

### **1. Status API (Linie 130-136)**

#### **Problem:**
```csharp
public string ApiStatusText => ApiStatus switch
{
    ApiConnectionStatus.Online => $"Online ({ApiPingMs}ms)",
    ApiConnectionStatus.Offline => "Offline",
    ApiConnectionStatus.Checking => "Sprawdzanie...",  // ❌ HARDCODED
    _ => "Nieznany"  // ❌ HARDCODED
};
```

#### **Rozwiązanie:**
Dodać klucze w `pl.json` i `en.json`:
```json
"UI": {
  "StatusBar": {
    "ApiStatusChecking": "Sprawdzanie..." / "Checking...",
    "ApiStatusUnknown": "Nieznany" / "Unknown"
  }
}
```

---

### **2. Lista zainstalowanych modów - "więcej" (Linia 239)**

#### **Problem:**
```csharp
if (Mods.Count(m => m.IsInstalled) > 10)
{
    installedMods.Add($"...i {Mods.Count(m => m.IsInstalled) - 10} więcej");  // ❌ HARDCODED
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "AndMoreFormat": "...i {0} więcej" / "...and {0} more"
  }
}
```

**Użycie:**
```csharp
installedMods.Add(_localizationService.GetString("UI.StatusBar.AndMoreFormat", 
    Mods.Count(m => m.IsInstalled) - 10));
```

---

### **3. Główny tekst statusu modów (Linie 262-272)**

#### **Problem:**
```csharp
if (AvailableUpdatesCount > 0)
{
    ModsStatusMainText = $"Dostępne aktualizacje: {AvailableUpdatesCount}";  // ❌ HARDCODED
    ModsStatusSubText = $"Zainstalowanych modów: {InstalledFullModsCount}";  // ❌ HARDCODED
}
else
{
    ModsStatusMainText = $"Zainstalowanych modów: {InstalledFullModsCount}";  // ❌ HARDCODED
    ModsStatusSubText = string.Empty;
}
```

#### **Rozwiązanie:**
Dodać klucze:
```json
"UI": {
  "StatusBar": {
    "AvailableUpdatesFormat": "Dostępne aktualizacje: {0}" / "Available updates: {0}",
    "InstalledModsFormat": "Zainstalowanych modów: {0}" / "Installed mods: {0}"
  }
}
```

**Użycie:**
```csharp
if (AvailableUpdatesCount > 0)
{
    ModsStatusMainText = _localizationService.GetString("UI.StatusBar.AvailableUpdatesFormat", 
        AvailableUpdatesCount);
    ModsStatusSubText = _localizationService.GetString("UI.StatusBar.InstalledModsFormat", 
        InstalledFullModsCount);
}
else
{
    ModsStatusMainText = _localizationService.GetString("UI.StatusBar.InstalledModsFormat", 
        InstalledFullModsCount);
    ModsStatusSubText = string.Empty;
}
```

---

### **4. Tooltip - Nagłówek zainstalowanych modów (Linia 288)**

#### **Problem:**
```csharp
if (InstalledModsList.Any())
{
    tooltipBuilder.AppendLine("📦 Zainstalowane mody:");  // ❌ HARDCODED
    foreach (var mod in InstalledModsList)
    {
        tooltipBuilder.AppendLine($"  • {mod}");
    }
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "InstalledModsListHeader": "📦 Zainstalowane mody:" / "📦 Installed mods:"
  }
}
```

---

### **5. Tooltip - Nagłówek aktualizacji (Linia 301)**

#### **Problem:**
```csharp
if (AvailableUpdatesList.Any())
{
    if (tooltipBuilder.Length > 0)
        tooltipBuilder.AppendLine();

    tooltipBuilder.AppendLine("⚠️ Wymagają aktualizacji:");  // ❌ HARDCODED
    foreach (var update in AvailableUpdatesList)
    {
        tooltipBuilder.AppendLine($"  • {update}");
    }
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "RequireUpdateHeader": "⚠️ Wymagają aktualizacji:" / "⚠️ Require update:"
  }
}
```

---

### **6. Przestrzeń dyskowa - Błąd folderu (Linia 325)**

#### **Problem:**
```csharp
if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
{
    ModsFolderSizeGB = 0;
    TotalDiskSpaceGB = 0;
    DiskUsagePercentage = 0;
    DiskSpaceDetailsTooltip = "Folder modów nie istnieje";  // ❌ HARDCODED
    return;
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "ModsFolderNotExist": "Folder modów nie istnieje" / "Mods folder does not exist"
  }
}
```

---

### **7. Przestrzeń dyskowa - Tooltip szczegółów (Linie 347-350)**

#### **Problem:**
```csharp
DiskSpaceDetailsTooltip = $"Folder modów: {ModsFolderSizeGB:F2} GB\n" +
                         $"Wolne miejsce na dysku: {FreeDiskSpaceGB:F2} GB\n" +
                         $"Całkowita przestrzeń: {TotalDiskSpaceGB:F2} GB\n" +
                         $"Ścieżka: {modsPath}";
```
**❌ WSZYSTKIE etykiety są hardcoded**

#### **Rozwiązanie:**
Dodać klucze:
```json
"UI": {
  "StatusBar": {
    "DiskSpaceTooltip": "Folder modów: {0:F2} GB\nWolne miejsce na dysku: {1:F2} GB\nCałkowita przestrzeń: {2:F2} GB\nŚcieżka: {3}",
    "DiskSpaceTooltipEn": "Mods folder: {0:F2} GB\nFree disk space: {1:F2} GB\nTotal space: {2:F2} GB\nPath: {3}"
  }
}
```

**Lub osobne klucze:**
```json
"UI": {
  "StatusBar": {
    "ModsFolderLabel": "Folder modów: {0:F2} GB" / "Mods folder: {0:F2} GB",
    "FreeSpaceLabel": "Wolne miejsce na dysku: {0:F2} GB" / "Free disk space: {0:F2} GB",
    "TotalSpaceLabel": "Całkowita przestrzeń: {0:F2} GB" / "Total space: {0:F2} GB",
    "PathLabel": "Ścieżka: {0}" / "Path: {0}"
  }
}
```

**Użycie (opcja 1 - jeden klucz):**
```csharp
DiskSpaceDetailsTooltip = _localizationService.GetString("UI.StatusBar.DiskSpaceTooltip",
    ModsFolderSizeGB, FreeDiskSpaceGB, TotalDiskSpaceGB, modsPath);
```

**Użycie (opcja 2 - osobne klucze):**
```csharp
DiskSpaceDetailsTooltip = 
    _localizationService.GetString("UI.StatusBar.ModsFolderLabel", ModsFolderSizeGB) + "\n" +
    _localizationService.GetString("UI.StatusBar.FreeSpaceLabel", FreeDiskSpaceGB) + "\n" +
    _localizationService.GetString("UI.StatusBar.TotalSpaceLabel", TotalDiskSpaceGB) + "\n" +
    _localizationService.GetString("UI.StatusBar.PathLabel", modsPath);
```

---

### **8. Przestrzeń dyskowa - Komunikat błędu (Linia 358)**

#### **Problem:**
```csharp
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"Error calculating disk space: {ex.Message}");
    ModsFolderSizeGB = 0;
    TotalDiskSpaceGB = 0;
    DiskUsagePercentage = 0;
    DiskSpaceDetailsTooltip = $"Błąd: {ex.Message}";  // ❌ HARDCODED
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "ErrorFormat": "Błąd: {0}" / "Error: {0}"
  }
}
```

---

### **9. Status API - "Nie ustawiono" (Linia 414)**

#### **Problem:**
```csharp
var baseUrl = configuration["Configuration:BaseUrl"]?.TrimEnd('/');
ApiBaseUrl = baseUrl ?? "Nie ustawiono";  // ❌ HARDCODED
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "NotConfigured": "Nie ustawiono" / "Not configured"
  }
}
```

---

### **10. Aktualizacje modów - Tooltip nagłówek (Linia 500)**

#### **Problem:**
```csharp
var tooltipBuilder = new System.Text.StringBuilder();
tooltipBuilder.AppendLine("Dostępne aktualizacje modów:");  // ❌ HARDCODED
foreach (var update in result.InstalledModUpdates)
{
    tooltipBuilder.AppendLine($"• {update.ModName}");
}
```

#### **Rozwiązanie:**
Dodać klucz:
```json
"UI": {
  "StatusBar": {
    "AvailableModUpdatesHeader": "Dostępne aktualizacje modów:" / "Available mod updates:"
  }
}
```

---

## 📝 Kompletna lista nowych kluczy lokalizacyjnych

### **Dodać do `pl.json`:**
```json
"UI": {
  "StatusBar": {
    "ApiStatusChecking": "Sprawdzanie...",
    "ApiStatusUnknown": "Nieznany",
    "AndMoreFormat": "...i {0} więcej",
    "AvailableUpdatesFormat": "Dostępne aktualizacje: {0}",
    "InstalledModsFormat": "Zainstalowanych modów: {0}",
    "InstalledModsListHeader": "📦 Zainstalowane mody:",
    "RequireUpdateHeader": "⚠️ Wymagają aktualizacji:",
    "ModsFolderNotExist": "Folder modów nie istnieje",
    "ModsFolderLabel": "Folder modów: {0:F2} GB",
    "FreeSpaceLabel": "Wolne miejsce na dysku: {0:F2} GB",
    "TotalSpaceLabel": "Całkowita przestrzeń: {0:F2} GB",
    "PathLabel": "Ścieżka: {0}",
    "ErrorFormat": "Błąd: {0}",
    "NotConfigured": "Nie ustawiono",
    "AvailableModUpdatesHeader": "Dostępne aktualizacje modów:"
  }
}
```

### **Dodać do `en.json`:**
```json
"UI": {
  "StatusBar": {
    "ApiStatusChecking": "Checking...",
    "ApiStatusUnknown": "Unknown",
    "AndMoreFormat": "...and {0} more",
    "AvailableUpdatesFormat": "Available updates: {0}",
    "InstalledModsFormat": "Installed mods: {0}",
    "InstalledModsListHeader": "📦 Installed mods:",
    "RequireUpdateHeader": "⚠️ Require update:",
    "ModsFolderNotExist": "Mods folder does not exist",
    "ModsFolderLabel": "Mods folder: {0:F2} GB",
    "FreeSpaceLabel": "Free disk space: {0:F2} GB",
    "TotalSpaceLabel": "Total space: {0:F2} GB",
    "PathLabel": "Path: {0}",
    "ErrorFormat": "Error: {0}",
    "NotConfigured": "Not configured",
    "AvailableModUpdatesHeader": "Available mod updates:"
  }
}
```

---

## 🔧 Kroki implementacji

1. **Dodać nowe klucze** do plików `pl.json` i `en.json`
2. **Zmodyfikować `MainWindowViewModel.StatusBar.cs`** - zastąpić wszystkie hardcodowane stringi wywołaniami `_localizationService.GetString()`
3. **Przetestować** przełączanie języka w ustawieniach - sprawdzić czy status bar zmienia teksty
4. **Sprawdzić formatowanie** liczb z przecinkami (może różnić się w zależności od kultury)

---

## 🔍 Inne potencjalne miejsca do sprawdzenia

- **Tooltips w MainWindow.axaml** - sprawdź czy wszystkie używają `{loc:Localize ...}`
- **ViewModels/MainWindowViewModel.*.cs** - inne partial classes mogą zawierać hardcoded teksty
- **Dialogs i inne Views** - sprawdź czy mają hardcoded teksty w code-behind

---

## ✅ Zalecenia

1. **Priorytet 1:** Status Bar (ten raport)
2. **Priorytet 2:** Sprawdzić pozostałe ViewModels
3. **Priorytet 3:** Sprawdzić Views (code-behind .axaml.cs)
4. **Priorytet 4:** Sprawdzić MessageBox i dialogi systemowe

---

**Koniec raportu**
