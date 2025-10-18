# Frontend – Lista refaktorów i elementów do usunięcia

## Status: ⚠️ Wymaga działania

Ten dokument zawiera listę przestarzałych, nieużywanych lub problematycznych elementów w kodzie frontendu SUSModder, które powinny zostać usunięte lub zrefaktoryzowane.

---

## 🗑️ Do usunięcia (nieużywane elementy)

### 1. `SUSModder/Models/Mod.cs` ❌ NIEUŻYWANY
**Status:** Całkowicie nieużywany  
**Linie kodu:** ~11 linii  
**Powód:** Klasa `Mod` nie ma **żadnych** użyć w całej aplikacji. Prawdopodobnie została zastąpiona przez:
- `SUSModder.Core.Configuration.ModConfiguration` (model Core)
- `SUSModder.ViewModels.ModItem` (adapter UI)

**Zawartość:**
```csharp
namespace SUSModder.Models;

public class Mod
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GitHubUrl { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public string? InstallPath { get; set; }
    public bool IsInstalled => !string.IsNullOrEmpty(InstallPath);
}
```

**Akcja:**
- [ ] Usuń plik `SUSModder/Models/Mod.cs`
- [ ] Upewnij się, że nie ma żadnych referencji w `.csproj` czy innych miejscach
- [ ] Rozważ usunięcie folderu `Models/`, jeśli zawiera tylko `Role.cs` (który **jest używany**)

**Weryfikacja:**
```bash
grep -r "SUSModder.Models.Mod" --include="*.cs" --exclude-dir=bin --exclude-dir=obj
# Wynik: brak użyć (poza definicją)
```

---

### 2. `SUSModder/Converters/CategoryToClassConverter.cs` ❌ NIEUŻYWANY
**Status:** Całkowicie nieużywany w plikach XAML  
**Linie kodu:** ~33 linie  
**Powód:** Konwerter nie jest wykorzystywany w **żadnym** pliku `.axaml`. Prawdopodobnie był używany do stylowania ról (crewmate/impostor/neutral), ale funkcjonalność została przeniesiona lub usunięta.

**Zawartość (skrót):**
```csharp
public class CategoryToClassConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string category)
        {
            return category.ToLower() switch
            {
                "crewmate" => "category-crewmate",
                "impostor" => "category-impostor",
                "neutral" => "category-neutral",
                "modifier" => "category-modifier",
                _ => "category-neutral"
            };
        }
        return "category-neutral";
    }
    // ...
}
```

**Akcja:**
- [ ] Usuń plik `SUSModder/Converters/CategoryToClassConverter.cs`
- [ ] Upewnij się, że nie jest importowany w App.axaml lub innych plikach zasobów

**Weryfikacja:**
```bash
grep -r "CategoryToClassConverter" --include="*.axaml" --include="*.cs"
# Wynik: tylko definicja klasy, brak użyć w XAML
```

---

## 🔧 Do refaktoryzacji

### 3. Zduplikowana klasa `InstallationSilentUserInteraction` ⚠️ DUPLIKAT
**Status:** Klasa zdefiniowana w dwóch miejscach  
**Lokalizacje:**
1. `SUSModder/Services/InstallationSilentUserInteraction.cs` (właściwe miejsce)
2. `SUSModder/ViewModels/MainWindowViewModel.cs` – **LINIA ~2980** (na końcu pliku!)

**Powód:** Prawdopodobnie kopia-wklej podczas testowania/debugowania. Duplikat na końcu `MainWindowViewModel.cs` jest całkowicie zbędny.

**Fragment z MainWindowViewModel.cs (linia ~2980):**
```csharp
public class InstallationSilentUserInteraction : IUserInteraction
{
    public Task<bool> AskRetryAsync(string title, string message)
    {
        return Task.FromResult(false);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return Task.CompletedTask;
    }
}
```

**Akcja:**
- [ ] Usuń duplikat z końca pliku `MainWindowViewModel.cs` (linia ~2980)
- [ ] Sprawdź, czy w pliku jest odpowiedni `using SUSModder.Services;` na górze
- [ ] Zweryfikuj, że kod używa klasy z `Services/` (linie 943, 1934 w MainWindowViewModel)

**Weryfikacja:**
```csharp
// W MainWindowViewModel.cs linie 943, 1934:
var silentUserInteraction = new InstallationSilentUserInteraction();
// To powinno używać klasy z namespace SUSModder.Services
```

**Dlaczego to problem:**
- Duplikacja kodu (DRY principle)
- Ryzyko rozbieżności przy zmianach
- Zwiększony rozmiar pliku (MainWindowViewModel już ma 3081 linii!)

---

### 4. Błędna nazwa pliku `FileName.cs` ⚠️ WPROWADZA W BŁĄD
**Status:** Plik zawiera `EpicErrorDialogViewModel`, ale nazywa się `FileName.cs`  
**Lokalizacja:** `SUSModder/ViewModels/FileName.cs`

**Powód:** Nazwa pliku nie odpowiada zawartości. Plik zawiera klasę `EpicErrorDialogViewModel`, która jest ViewModelem dla `EpicErrorDialog`.

**Zawartość (fragment):**
```csharp
namespace SUSModder.ViewModels
{
    public class EpicErrorDialogViewModel : ViewModelBase
    {
        private readonly Window _window;
        private string _modName;
        private string _logContent;
        // ...
    }
}
```

**Użycie:**
- `SUSModder/Views/EpicErrorDialog.axaml.cs` (linia 16):
```csharp
DataContext = new EpicErrorDialogViewModel(modName, logContent, this);
```

**Akcja:**
- [ ] Zmień nazwę pliku `FileName.cs` → `EpicErrorDialogViewModel.cs`
- [ ] Upewnij się, że Visual Studio/Rider zaktualizuje referencje w `.csproj`
- [ ] Zweryfikuj, że build nadal działa

**Polecenie (z użyciem git):**
```bash
cd d:\repos\SUSModder\SUSModder\ViewModels
git mv FileName.cs EpicErrorDialogViewModel.cs
```

---

## 📊 Podsumowanie statystyk

| Element | Status | Akcja | Priorytet |
|---------|--------|-------|-----------|
| `Models/Mod.cs` | ❌ Nieużywany | Usuń plik | **Wysoki** |
| `Converters/CategoryToClassConverter.cs` | ❌ Nieużywany | Usuń plik | **Wysoki** |
| Duplikat `InstallationSilentUserInteraction` | ⚠️ Zduplikowany | Usuń z MainWindowViewModel.cs | **Średni** |
| `FileName.cs` | ⚠️ Błędna nazwa | Zmień nazwę na `EpicErrorDialogViewModel.cs` | **Niski** |

---

## 🎯 Plan działania

### Faza 1: Usunięcie nieużywanych elementów (Priorytet: WYSOKI)
```bash
# 1. Usuń nieużywany model Mod.cs
rm d:\repos\SUSModder\SUSModder\Models\Mod.cs

# 2. Usuń nieużywany konwerter
rm d:\repos\SUSModder\SUSModder\Converters\CategoryToClassConverter.cs
```

### Faza 2: Usunięcie duplikatu (Priorytet: ŚREDNI)
1. Otwórz `SUSModder/ViewModels/MainWindowViewModel.cs`
2. Przejdź do linii ~2980 (koniec pliku)
3. Usuń całą klasę `InstallationSilentUserInteraction` (wraz z namespace, jeśli jest)
4. Upewnij się, że na górze pliku jest `using SUSModder.Services;`
5. Build i test aplikacji

### Faza 3: Rename pliku (Priorytet: NISKI)
```bash
cd d:\repos\SUSModder\SUSModder\ViewModels
git mv FileName.cs EpicErrorDialogViewModel.cs
```
Lub użyj funkcji Rename w IDE (Visual Studio/Rider).

---

## ✅ Checklist weryfikacji po refaktorze

- [ ] **Build bez błędów:** `dotnet build -c Release`
- [ ] **Brak ostrzeżeń kompilatora** związanych z usuniętymi elementami
- [ ] **Testy manualne:**
  - [ ] Uruchomienie aplikacji
  - [ ] Instalacja moda (test użycia `InstallationSilentUserInteraction`)
  - [ ] Wyświetlenie EpicErrorDialog (test użycia `EpicErrorDialogViewModel`)
- [ ] **Weryfikacja referencji:**
  ```bash
  grep -r "\\bMod\\b" --include="*.cs" | grep "SUSModder.Models"
  # Powinno być: brak wyników
  
  grep -r "CategoryToClassConverter" --include="*.axaml" --include="*.cs"
  # Powinno być: brak wyników
  ```

---

## 📝 Notatki

### Dlaczego `Models/Mod.cs` został zastąpiony?
Prawdopodobnie podczas refaktoryzacji architektury:
- Model domenowy przeniesiono do `SUSModder.Core.Configuration.ModConfiguration`
- Adapter UI (`ModItem`) został stworzony specjalnie dla ReactiveUI
- `Mod.cs` pozostał jako "martwy kod"

### Czy można usunąć cały folder `Models/`?
**Nie** – folder zawiera również `Role.cs`, który **jest aktywnie używany**:
- `RolesService.cs` (pobieranie ról z API)
- `RolesWindow.axaml.cs` (wyświetlanie listy ról)
- `RoleDetailWindow.axaml.cs` (szczegóły roli)

---

**Autor dokumentu:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Ostatnia aktualizacja:** 2025-10-19  
**Status refaktoru:** ❌ Nierozpoczęty – wymaga działania dewelopera
