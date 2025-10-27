# Przewodnik Tłumaczeń

## Spis treści
1. [Dodawanie nowego języka](#1-dodawanie-nowego-języka)
2. [Tłumaczenie pl.json → en.json](#2-tłumaczenie-pljson--enjson)
3. [Struktura pliku JSON](#3-struktura-pliku-json)
4. [Konwencje tłumaczeniowe](#4-konwencje-tłumaczeniowe)
5. [Formatowanie i pluralizacja](#5-formatowanie-i-pluralizacja)
6. [Testowanie tłumaczeń](#6-testowanie-tłumaczeń)
7. [Narzędzia i workflow](#7-narzędzia-i-workflow)
8. [Przykłady tłumaczeń](#8-przykłady-tłumaczeń)

---

## 1. Dodawanie nowego języka

### Krok 1: Stwórz nowy plik JSON

Skopiuj `pl.json` jako bazę:

```bash
cd SUSModder/Localization/
copy pl.json de.json    # Dla niemieckiego
copy pl.json fr.json    # Dla francuskiego
copy pl.json es.json    # Dla hiszpańskiego
```

### Krok 2: Przetłumacz wszystkie wartości

**WAŻNE**: Zachowaj dokładnie tę samą strukturę kluczy!

```json
// pl.json (oryginalny)
{
  "UI": {
    "Buttons": {
      "Install": "Instaluj",
      "Launch": "Uruchom"
    }
  }
}

// de.json (niemiecki)
{
  "UI": {
    "Buttons": {
      "Install": "Installieren",
      "Launch": "Starten"
    }
  }
}
```

### Krok 3: Język zostanie automatycznie wykryty

LocalizationService automatycznie wykrywa wszystkie pliki `.json` w folderze `/Localization/`.

Nie musisz nic zmieniać w kodzie! Język pojawi się w `GetAvailableCultures()`.

### Krok 4: Dodaj do UI wyboru języka (opcjonalnie)

Jeśli chcesz wyświetlić nazwę języka w ComboBox:

```csharp
// AppSettingsViewModel.cs
public List<LanguageOption> AvailableLanguages => new()
{
    new LanguageOption { Code = "pl", DisplayName = "Polski" },
    new LanguageOption { Code = "en", DisplayName = "English" },
    new LanguageOption { Code = "de", DisplayName = "Deutsch" },  // ← DODANE
    new LanguageOption { Code = "fr", DisplayName = "Français" }  // ← DODANE
};
```

**GOTOWE!** Nowy język działa od razu.

---

## 2. Tłumaczenie pl.json → en.json

### Kompletny przykład tłumaczenia

#### pl.json (fragment)
```json
{
  "UI": {
    "Buttons": {
      "Install": "Instaluj",
      "Launch": "Uruchom",
      "Update": "Aktualizuj",
      "Delete": "Usuń",
      "Cancel": "Anuluj",
      "Browse": "Przeglądaj...",
      "OpenFolder": "Otwórz folder",
      "CreateShortcut": "Stwórz skrót",
      "Yes": "Tak",
      "No": "Nie",
      "OK": "OK"
    },
    "Labels": {
      "InstalledMods": "Zainstalowanych modów",
      "InstalledIn": "Zainstalowano w",
      "Version": "Wersja",
      "NoMods": "Brak zainteresowanych modów",
      "SpaceDetails": "Szczegóły przestrzeni"
    },
    "Menu": {
      "ToUConfigs": "Konfiguracje ToU",
      "DllMods": "Modyfikacje DLL",
      "SUStats": "SUStats - konfiguracje",
      "RepairGame": "Napraw Amonga",
      "Settings": "Ustawienia aplikacji"
    }
  },
  "Dialogs": {
    "Error": {
      "Title": "Błąd",
      "Header": "Wystąpił błąd:",
      "InstallFailed": "Nie udało się zainstalować moda",
      "UpdateFailed": "Nie udało się zaktualizować moda",
      "ConfigNotFound": "Nie znaleziono konfiguracji moda",
      "NetworkError": "Błąd połączenia sieciowego",
      "InstallFailedWithDetails": "Błąd podczas instalacji: {0}"
    },
    "Confirm": {
      "Title": "Potwierdzenie",
      "UninstallMessage": "Czy na pewno chcesz odinstalować {0}?",
      "UninstallPath": "Ścieżka: {0}",
      "DeleteMessage": "Ta operacja jest nieodwracalna. Kontynuować?",
      "RestartApp": "Czy chcesz teraz uruchomić ponownie aplikację?"
    },
    "Info": {
      "Title": "Informacja",
      "UpdateAvailable": "Dostępna jest nowa wersja",
      "RestartRequired": "Restart wymagany. Zmiana wymaga ponownego uruchomienia aplikacji.",
      "InstallSuccess": "Instalacja zakończona pomyślnie"
    }
  },
  "Settings": {
    "WindowTitle": "Ustawienia aplikacji",
    "Language": {
      "Label": "Język",
      "Description": "Zmiana języka nastąpi natychmiast",
      "Polish": "Polski",
      "English": "English"
    },
    "Theme": {
      "Label": "Motyw",
      "Dark": "Ciemny",
      "Pink": "Różowy"
    },
    "Paths": {
      "Label": "Ścieżka instalacji modów",
      "Browse": "Przeglądaj...",
      "Reset": "Przywróć domyślne"
    }
  },
  "Messages": {
    "RestartRequired": "Restart wymagany",
    "UpdateAvailable": "Dostępna aktualizacja",
    "InstallComplete": "Instalacja zakończona",
    "ModInstalled": "Zainstalowano mod: {0}",
    "ModRequirements": "Mod {0} (wersja {1}) wymaga {2} MB"
  },
  "Status": {
    "FetchingMods": "Pobieranie listy modów...",
    "Ready": "Gotowe",
    "Installing": "Instalowanie...",
    "Updating": "Aktualizowanie...",
    "Downloading": "Pobieranie..."
  },
  "Tooltips": {
    "InstallMod": "Kliknij aby zainstalować mod",
    "LaunchMod": "Uruchom mod",
    "UpdateMod": "Aktualizuj mod do najnowszej wersji",
    "DeleteMod": "Odinstaluj mod"
  }
}
```

#### en.json (tłumaczenie)
```json
{
  "UI": {
    "Buttons": {
      "Install": "Install",
      "Launch": "Launch",
      "Update": "Update",
      "Delete": "Delete",
      "Cancel": "Cancel",
      "Browse": "Browse...",
      "OpenFolder": "Open Folder",
      "CreateShortcut": "Create Shortcut",
      "Yes": "Yes",
      "No": "No",
      "OK": "OK"
    },
    "Labels": {
      "InstalledMods": "Installed mods",
      "InstalledIn": "Installed in",
      "Version": "Version",
      "NoMods": "No mods found",
      "SpaceDetails": "Space details"
    },
    "Menu": {
      "ToUConfigs": "Town of Us Configs",
      "DllMods": "DLL Modifications",
      "SUStats": "SUStats - configurations",
      "RepairGame": "Repair Among Us",
      "Settings": "Application Settings"
    }
  },
  "Dialogs": {
    "Error": {
      "Title": "Error",
      "Header": "An error occurred:",
      "InstallFailed": "Failed to install mod",
      "UpdateFailed": "Failed to update mod",
      "ConfigNotFound": "Mod configuration not found",
      "NetworkError": "Network connection error",
      "InstallFailedWithDetails": "Installation error: {0}"
    },
    "Confirm": {
      "Title": "Confirmation",
      "UninstallMessage": "Are you sure you want to uninstall {0}?",
      "UninstallPath": "Path: {0}",
      "DeleteMessage": "This operation is irreversible. Continue?",
      "RestartApp": "Do you want to restart the application now?"
    },
    "Info": {
      "Title": "Information",
      "UpdateAvailable": "A new version is available",
      "RestartRequired": "Restart required. This change requires application restart.",
      "InstallSuccess": "Installation completed successfully"
    }
  },
  "Settings": {
    "WindowTitle": "Application Settings",
    "Language": {
      "Label": "Language",
      "Description": "Language change will take effect immediately",
      "Polish": "Polski",
      "English": "English"
    },
    "Theme": {
      "Label": "Theme",
      "Dark": "Dark",
      "Pink": "Pink"
    },
    "Paths": {
      "Label": "Mods installation path",
      "Browse": "Browse...",
      "Reset": "Reset to default"
    }
  },
  "Messages": {
    "RestartRequired": "Restart required",
    "UpdateAvailable": "Update available",
    "InstallComplete": "Installation complete",
    "ModInstalled": "Mod installed: {0}",
    "ModRequirements": "Mod {0} (version {1}) requires {2} MB"
  },
  "Status": {
    "FetchingMods": "Fetching mods list...",
    "Ready": "Ready",
    "Installing": "Installing...",
    "Updating": "Updating...",
    "Downloading": "Downloading..."
  },
  "Tooltips": {
    "InstallMod": "Click to install mod",
    "LaunchMod": "Launch mod",
    "UpdateMod": "Update mod to latest version",
    "DeleteMod": "Uninstall mod"
  }
}
```

---

## 3. Struktura pliku JSON

### Zasady struktury

1. **Hierarchia 3-poziomowa** (zalecane)
   ```json
   {
     "Category": {          // Kategoria główna (UI, Dialogs, Settings)
       "Subcategory": {     // Podkategoria (Buttons, Error, Paths)
         "Key": "Value"     // Klucz i wartość
       }
     }
   }
   ```

2. **Zachowaj identyczne klucze we wszystkich językach**
   ```json
   // ✅ DOBRZE - identyczne struktury
   pl.json: { "UI": { "Buttons": { "Install": "Instaluj" } } }
   en.json: { "UI": { "Buttons": { "Install": "Install" } } }

   // ❌ ŹLE - różne struktury
   pl.json: { "UI": { "Buttons": { "Install": "Instaluj" } } }
   en.json: { "UI": { "Btns": { "Install": "Install" } } }  // Btns ≠ Buttons!
   ```

3. **Zachowaj formatowanie placeholders**
   ```json
   // ✅ DOBRZE
   pl.json: "ModInstalled": "Zainstalowano mod: {0}"
   en.json: "ModInstalled": "Mod installed: {0}"

   // ❌ ŹLE - brak {0}
   en.json: "ModInstalled": "Mod installed"
   ```

4. **Sortowanie alfabetyczne** (dla czytelności)
   ```json
   {
     "UI": {
       "Buttons": {      // B przed L
         "Cancel": "...",
         "Install": "...",
         "Launch": "..."
       },
       "Labels": {       // L po B
         ...
       }
     }
   }
   ```

---

## 4. Konwencje tłumaczeniowe

### Ogólne zasady

1. **Zachowaj kontekst**
   - "Launch" (mod) → "Uruchom" (nie "Wystartuj")
   - "Settings" (ustawienia) → "Settings" (nie "Configuration")

2. **Spójność terminologii**
   - Jeśli raz użyjesz "Install" → zawsze "Install" (nie "Setup", "Add")
   - Stwórz glossary kluczowych terminów

3. **Wielkość liter**
   ```
   Polski: "Zainstaluj mod"           (małe litery w środku zdania)
   English: "Install Mod"             (Title Case dla przycisków)
   Niemiecki: "Mod installieren"      (rzeczownik wielką literą)
   ```

4. **Kropki i wielokropki**
   ```
   Polski: "Pobieranie..."            (wielokropek dla procesów)
   English: "Downloading..."
   Polski: "Przeglądaj..."            (dla akcji otwierających dialog)
   English: "Browse..."
   ```

5. **Dwukropki**
   ```
   Polski: "Wersja:"                  (dwukropek po etykiecie)
   English: "Version:"
   ```

### Specyficzne dla gier

- **Among Us** → nie tłumaczymy (nazwa własna)
- **Mod** → nie tłumaczymy (powszechnie używany termin)
- **Town of Us** → nie tłumaczymy (nazwa moda)
- **DLL** → nie tłumaczymy (termin techniczny)

### Akcje i przyciski

| Polski | English | Deutsch | Français |
|--------|---------|---------|----------|
| Instaluj | Install | Installieren | Installer |
| Uruchom | Launch | Starten | Lancer |
| Aktualizuj | Update | Aktualisieren | Mettre à jour |
| Usuń | Delete | Löschen | Supprimer |
| Anuluj | Cancel | Abbrechen | Annuler |
| Przeglądaj | Browse | Durchsuchen | Parcourir |
| Zapisz | Save | Speichern | Enregistrer |

---

## 5. Formatowanie i pluralizacja

### 5.1 Placeholders ({0}, {1}, {2})

Placeholders **MUSZĄ** być zachowane w tłumaczeniu:

```json
// pl.json
"ModInstalled": "Zainstalowano mod: {0}"

// en.json
"ModInstalled": "Mod installed: {0}"

// de.json
"ModInstalled": "Mod installiert: {0}"
```

**UWAGA**: Kolejność placeholders może być inna w różnych językach!

```json
// pl.json (imię przed nazwiskiem)
"UserInfo": "Witaj {0} {1}!"  // {0}=imię, {1}=nazwisko

// en.json (można odwrócić)
"UserInfo": "Welcome {0} {1}!"  // {0}=imię, {1}=nazwisko

// Niektóre języki mogą wymagać odwrócenia:
// "UserInfo": "Welcome {1}, {0}!"  // Nazwisko, Imię
```

### 5.2 Pluralizacja (uproszczona)

Dla prostych przypadków używamy oddzielnych kluczy:

```json
{
  "UI": {
    "ModCount": {
      "Zero": "Brak modów",
      "One": "1 mod",
      "Few": "{0} mody",      // 2-4 mody
      "Many": "{0} modów"     // 5+ modów
    }
  }
}

// Angielski (prostszy)
{
  "UI": {
    "ModCount": {
      "Zero": "No mods",
      "One": "1 mod",
      "Many": "{0} mods"
    }
  }
}
```

Użycie w kodzie:
```csharp
string GetModCountText(int count)
{
    if (count == 0)
        return _loc.Get("UI.ModCount.Zero");
    if (count == 1)
        return _loc.Get("UI.ModCount.One");
    if (count < 5 && CurrentCulture == "pl")
        return _loc.GetFormatted("UI.ModCount.Few", count);
    return _loc.GetFormatted("UI.ModCount.Many", count);
}
```

### 5.3 Formatowanie dat i liczb

Używaj `CultureInfo` dla dat i liczb:

```csharp
// Automatyczne formatowanie wg języka
var dateStr = DateTime.Now.ToString("d", new CultureInfo(_loc.CurrentCulture));
// pl: 23.10.2025
// en: 10/23/2025
// de: 23.10.2025

var numberStr = 1234.56.ToString("N2", new CultureInfo(_loc.CurrentCulture));
// pl: 1 234,56
// en: 1,234.56
// de: 1.234,56
```

---

## 6. Testowanie tłumaczeń

### Checklist testowania

- [ ] **Wszystkie klucze istnieją** - porównaj struktury pl.json i en.json
- [ ] **Placeholders się zgadzają** - każdy {0}, {1} jest zachowany
- [ ] **Teksty się mieszczą w UI** - długie tłumaczenia mogą przekraczać szerokość
- [ ] **Znaki specjalne działają** - ä, ö, ü, é, è, ñ, etc.
- [ ] **Encoding UTF-8** - wszystkie pliki JSON muszą być UTF-8
- [ ] **Live switching działa** - zmiana języka odświeża wszystkie teksty
- [ ] **Brak [KEY_NOT_FOUND]** - sprawdź czy nie ma błędnych kluczy

### Narzędzie: JSON Diff

Użyj narzędzia do porównania struktury:

```bash
# Wyciągnij wszystkie klucze z pl.json
jq -r 'paths | join(".")' pl.json | sort > pl_keys.txt

# Wyciągnij wszystkie klucze z en.json
jq -r 'paths | join(".")' en.json | sort > en_keys.txt

# Porównaj
diff pl_keys.txt en_keys.txt
```

### Skrypt walidacyjny (Python)

```python
import json

def validate_translation(pl_file, en_file):
    with open(pl_file) as f:
        pl_data = json.load(f)
    with open(en_file) as f:
        en_data = json.load(f)

    def get_keys(d, prefix=''):
        keys = []
        for k, v in d.items():
            if isinstance(v, dict):
                keys.extend(get_keys(v, f"{prefix}{k}."))
            else:
                keys.append(f"{prefix}{k}")
        return keys

    pl_keys = set(get_keys(pl_data))
    en_keys = set(get_keys(en_data))

    missing_in_en = pl_keys - en_keys
    extra_in_en = en_keys - pl_keys

    if missing_in_en:
        print("❌ Missing in en.json:")
        for key in sorted(missing_in_en):
            print(f"  - {key}")

    if extra_in_en:
        print("⚠️ Extra keys in en.json:")
        for key in sorted(extra_in_en):
            print(f"  - {key}")

    if not missing_in_en and not extra_in_en:
        print("✅ All keys match!")

validate_translation('pl.json', 'en.json')
```

---

## 7. Narzędzia i workflow

### Zalecane narzędzia

1. **Visual Studio Code** z rozszerzeniami:
   - **JSON Tools** - formatowanie i walidacja
   - **i18n Ally** - zarządzanie tłumaczeniami
   - **Error Lens** - błędy inline

2. **Online JSON validators**:
   - https://jsonlint.com/
   - https://jsonformatter.org/

3. **Tłumaczenie wspomagane**:
   - **DeepL** (lepsze niż Google dla kontekstu)
   - **Google Translate**
   - **ChatGPT/Claude** (dla spójności kontekstu)

### Workflow tłumaczenia

```
1. Ekstrakcja stringów z kodu → pl.json
   ↓
2. Skopiuj pl.json → en.json
   ↓
3. Tłumacz sekcja po sekcji:
   - UI.Buttons (10 min)
   - UI.Labels (10 min)
   - UI.Menu (5 min)
   - Dialogs.Error (15 min)
   - ... etc
   ↓
4. Walidacja struktury (skrypt Python)
   ↓
5. Test w aplikacji (zmiana języka)
   ↓
6. Poprawki (długie teksty, błędy)
   ↓
7. Finalna weryfikacja
```

### Git workflow

```bash
# Branch dla tłumaczeń
git checkout -b feature/localization-system

# Commit po każdej sekcji
git add Localization/pl.json
git commit -m "feat(i18n): Add Polish strings - UI section"

git add Localization/en.json
git commit -m "feat(i18n): Add English translation - UI section"

# Kolejne commity dla każdej sekcji
git commit -m "feat(i18n): Add Dialogs translations (pl/en)"
git commit -m "feat(i18n): Add Settings translations (pl/en)"
```

---

## 8. Przykłady tłumaczeń

### Przykład 1: Komunikat błędu z szczegółami

```json
// pl.json
"Dialogs": {
  "Error": {
    "NetworkErrorDetails": "Nie można połączyć się z serwerem. Szczegóły: {0}"
  }
}

// en.json
"Dialogs": {
  "Error": {
    "NetworkErrorDetails": "Unable to connect to server. Details: {0}"
  }
}

// de.json
"Dialogs": {
  "Error": {
    "NetworkErrorDetails": "Verbindung zum Server nicht möglich. Details: {0}"
  }
}
```

### Przykład 2: Pytanie o potwierdzenie

```json
// pl.json
"Dialogs": {
  "Confirm": {
    "DeleteModConfirmation": "Czy na pewno chcesz usunąć mod \"{0}\"? Ta operacja jest nieodwracalna."
  }
}

// en.json
"Dialogs": {
  "Confirm": {
    "DeleteModConfirmation": "Are you sure you want to delete mod \"{0}\"? This operation is irreversible."
  }
}
```

### Przykład 3: Status z progresem

```json
// pl.json
"Status": {
  "DownloadProgress": "Pobieranie {0}... ({1}%)"
}

// en.json
"Status": {
  "DownloadProgress": "Downloading {0}... ({1}%)"
}

// Użycie w kodzie:
_loc.GetFormatted("Status.DownloadProgress", fileName, progress)
// PL: "Pobieranie TownOfUs.zip... (45%)"
// EN: "Downloading TownOfUs.zip... (45%)"
```

### Przykład 4: Informacja o wymaganym restarcie

```json
// pl.json
"Messages": {
  "RestartRequiredForChange": "Zmiana {0} wymaga ponownego uruchomienia aplikacji. Czy chcesz uruchomić ponownie teraz?"
}

// en.json
"Messages": {
  "RestartRequiredForChange": "Changing {0} requires application restart. Do you want to restart now?"
}
```

---

## Podsumowanie

### Kluczowe zasady tłumaczenia

1. ✅ **Zachowaj strukturę kluczy** (identyczna we wszystkich językach)
2. ✅ **Zachowaj placeholders** ({0}, {1}, {2})
3. ✅ **Testuj często** (po każdej sekcji)
4. ✅ **Używaj spójnej terminologii** (glossary)
5. ✅ **UTF-8 encoding** (dla znaków specjalnych)
6. ✅ **Waliduj JSON** (przed commitem)
7. ✅ **Testuj live switching** (czy działa odświeżanie)

### Szacowany czas tłumaczenia

- **Ekstrakcja stringów** (500-700 stringów do pl.json): ~4-6h
- **Tłumaczenie en.json** (Google Translate + poprawki): ~3-4h
- **Weryfikacja i testy**: ~1-2h
- **Poprawki długich tekstów**: ~1h
- **ŁĄCZNIE**: ~9-13h

### Co dalej?

Po zakończeniu tłumaczeń:
1. Stwórz PR z tłumaczeniami
2. Poproś native speakera o review (jeśli możliwe)
3. Dodaj tłumaczenia do dokumentacji (changelog)
4. Rozważ crowdsourcing dla kolejnych języków (GitHub Issues)

---

**Dokumentacja kompletna!** Wszystkie 4 pliki gotowe do użycia przy implementacji systemu lokalizacji.
