# Przewodnik Migracji Stringów

## Spis treści
1. [Przegląd procesu migracji](#1-przegląd-procesu-migracji)
2. [Migracja stringów w AXAML](#2-migracja-stringów-w-axaml)
3. [Migracja stringów w ViewModels](#3-migracja-stringów-w-viewmodels)
4. [Migracja dialogów](#4-migracja-dialogów)
5. [Formatowanie stringów z parametrami](#5-formatowanie-stringów-z-parametrami)
6. [Checklist i strategia](#6-checklist-i-strategia)
7. [Przykłady przed/po](#7-przykłady-przed-po)

---

## 1. Przegląd procesu migracji

### Etapy migracji (zalecane)

```
Faza 1: INFRASTRUKTURA
├── Implementacja LocalizationService
├── Rejestracja w DI
├── Utworzenie pustych plików pl.json i en.json
└── Test na 1-2 przykładowych stringach

Faza 2: EKSTRAKCJA STRINGÓW
├── Przejrzyj wszystkie pliki AXAML
├── Wyciągnij wszystkie Text=, Content=, ToolTip.Tip= do Excela/CSV
├── Nadaj klucze zgodnie z konwencją (UI.Buttons.Install)
└── Wypełnij pl.json wszystkimi stringami

Faza 3: MIGRACJA AXAML (~400 stringów)
├── MainWindow.axaml (najważniejszy, ~80 stringów)
├── AppSettingsView.axaml (~30 stringów)
├── InfoPanel.axaml (~20 stringów)
├── Dialogi małe (<10 stringów każdy)
│   ├── ErrorDialog.axaml
│   ├── ConfirmDialog.axaml
│   ├── UninstallConfirmDialog.axaml
│   └── ... (30+ dialogów)
└── Pozostałe widoki

Faza 4: MIGRACJA VIEWMODELS (~200 stringów)
├── MainWindowViewModel (największy)
│   ├── ModOperations.cs
│   ├── DllManagement.cs
│   ├── Initialization.cs
│   └── Dialogs.cs
├── AppSettingsViewModel
└── Pozostałe ViewModele

Faza 5: TŁUMACZENIE
├── Wypełnij en.json (tłumaczenie pl.json)
└── Weryfikacja wszystkich kluczy

Faza 6: TESTY
├── Test przełączania języków
├── Weryfikacja brakujących kluczy
└── Sprawdzenie formatowania stringów
```

### Konwencja nazewnictwa kluczy

```
Format: {Category}.{Subcategory}.{Name}

Kategorie główne:
- UI          → Elementy interfejsu (przyciski, etykiety, menu)
- Dialogs     → Dialogi (błędy, potwierdzenia, info)
- Settings    → Ustawienia aplikacji
- Messages    → Komunikaty systemowe
- Errors      → Błędy i ostrzeżenia
- Tooltips    → Podpowiedzi
- Status      → Statusy i komunikaty postępu

Przykłady dobrych kluczy:
✅ UI.Buttons.Install
✅ UI.Labels.InstalledMods
✅ Dialogs.Error.Title
✅ Dialogs.Confirm.UninstallMessage
✅ Settings.Paths.ModsFolder
✅ Messages.RestartRequired
✅ Tooltips.InstallButton

Przykłady złych kluczy:
❌ btn_install (za krótki, niejasny)
❌ InstallModButton (brak kategorii)
❌ UI.Install (za ogólny)
❌ error_msg (niekonsystentny z konwencją)
```

---

## 2. Migracja stringów w AXAML

### 2.1 Podstawowe użycie

#### Przed:
```xml
<Button Content="Instaluj"/>
```

#### Po:
```xml
<Button Content="{local:Localize UI.Buttons.Install}"/>
```

#### pl.json:
```json
{
  "UI": {
    "Buttons": {
      "Install": "Instaluj"
    }
  }
}
```

### 2.2 TextBlock z długim tekstem

#### Przed:
```xml
<TextBlock Text="Zainstalowanych modów: "
           FontSize="14"
           Foreground="White"/>
```

#### Po:
```xml
<TextBlock Text="{local:Localize UI.Labels.InstalledModsCount}"
           FontSize="14"
           Foreground="White"/>
```

#### pl.json:
```json
{
  "UI": {
    "Labels": {
      "InstalledModsCount": "Zainstalowanych modów: "
    }
  }
}
```

### 2.3 Menu items

#### Przed (MainWindow.axaml):
```xml
<StackPanel Orientation="Horizontal" Spacing="5">
    <PathIcon Data="{StaticResource SettingsIcon}"/>
    <TextBlock Text="Konfiguracje ToU" VerticalAlignment="Center"/>
</StackPanel>
```

#### Po:
```xml
<StackPanel Orientation="Horizontal" Spacing="5">
    <PathIcon Data="{StaticResource SettingsIcon}"/>
    <TextBlock Text="{local:Localize UI.Menu.ToUConfigs}" VerticalAlignment="Center"/>
</StackPanel>
```

#### pl.json:
```json
{
  "UI": {
    "Menu": {
      "ToUConfigs": "Konfiguracje ToU",
      "DllMods": "Modyfikacje DLL",
      "SUStats": "SUStats - konfiguracje",
      "RepairGame": "Napraw Amonga",
      "Settings": "Ustawienia aplikacji"
    }
  }
}
```

### 2.4 Tooltips

#### Przed:
```xml
<Button ToolTip.Tip="Kliknij aby zainstalować mod">
    <PathIcon Data="{StaticResource DownloadIcon}"/>
</Button>
```

#### Po:
```xml
<Button ToolTip.Tip="{local:Localize Tooltips.InstallMod}">
    <PathIcon Data="{StaticResource DownloadIcon}"/>
</Button>
```

#### pl.json:
```json
{
  "Tooltips": {
    "InstallMod": "Kliknij aby zainstalować mod",
    "LaunchMod": "Uruchom mod",
    "UpdateMod": "Aktualizuj mod do najnowszej wersji"
  }
}
```

### 2.5 Window titles

#### Przed:
```xml
<Window Title="Ustawienia aplikacji"
        Width="800" Height="600">
```

#### Po:
```xml
<Window Title="{local:Localize Settings.WindowTitle}"
        Width="800" Height="600">
```

#### pl.json:
```json
{
  "Settings": {
    "WindowTitle": "Ustawienia aplikacji"
  }
}
```

### 2.6 Placeholder text (Watermark)

#### Przed:
```xml
<TextBox Watermark="Wpisz nazwę moda..."
         Text="{Binding SearchQuery}"/>
```

#### Po:
```xml
<TextBox Watermark="{local:Localize UI.Search.Placeholder}"
         Text="{Binding SearchQuery}"/>
```

#### pl.json:
```json
{
  "UI": {
    "Search": {
      "Placeholder": "Wpisz nazwę moda...",
      "NoResults": "Nie znaleziono modów"
    }
  }
}
```

---

## 3. Migracja stringów w ViewModels

### 3.1 Dependency Injection

Najpierw upewnij się, że ViewModel ma dostęp do `ILocalizationService`:

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly ILocalizationService _loc;
    private readonly IUserInteraction _userInteraction;

    public MainWindowViewModel(
        ILocalizationService localization,
        IUserInteraction userInteraction)
    {
        _loc = localization;
        _userInteraction = userInteraction;
    }
}
```

### 3.2 Proste komunikaty błędów

#### Przed:
```csharp
await _userInteractionService.ShowErrorAsync("Nie znaleziono konfiguracji moda.", "Błąd");
```

#### Po:
```csharp
await _userInteractionService.ShowErrorAsync(
    _loc.Get("Dialogs.Error.ConfigNotFound"),
    _loc.Get("Dialogs.Error.Title")
);
```

#### pl.json:
```json
{
  "Dialogs": {
    "Error": {
      "Title": "Błąd",
      "ConfigNotFound": "Nie znaleziono konfiguracji moda."
    }
  }
}
```

### 3.3 Komunikaty z interpolacją

#### Przed:
```csharp
await ShowErrorDialogAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd");
```

#### Po:
```csharp
await ShowErrorDialogAsync(
    _loc.GetFormatted("Dialogs.Error.InstallFailedWithDetails", ex.Message),
    _loc.Get("Dialogs.Error.Title")
);
```

#### pl.json:
```json
{
  "Dialogs": {
    "Error": {
      "Title": "Błąd",
      "InstallFailedWithDetails": "Błąd podczas instalacji: {0}"
    }
  }
}
```

### 3.4 Status messages

#### Przed:
```csharp
StatusMessage = "Pobieranie listy modów...";
// później:
StatusMessage = "Gotowe";
```

#### Po:
```csharp
StatusMessage = _loc.Get("Status.FetchingMods");
// później:
StatusMessage = _loc.Get("Status.Ready");
```

#### pl.json:
```json
{
  "Status": {
    "FetchingMods": "Pobieranie listy modów...",
    "Ready": "Gotowe",
    "Installing": "Instalowanie...",
    "Updating": "Aktualizowanie..."
  }
}
```

### 3.5 Observable properties (dla live switching)

Jeśli masz property który wyświetla przetłumaczony tekst:

#### Przed:
```csharp
public string WelcomeMessage => "Witaj w SUSModder!";
```

#### Po (Reactive property):
```csharp
private string _welcomeMessage;
public string WelcomeMessage
{
    get => _loc.Get("UI.WelcomeMessage");
}

// W konstruktorze: nasłuchuj zmian języka
public MainWindowViewModel(ILocalizationService localization)
{
    _loc = localization;

    // Gdy zmieni się język, odśwież property
    _loc.PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || string.IsNullOrEmpty(e.PropertyName))
        {
            this.RaisePropertyChanged(nameof(WelcomeMessage));
        }
    };
}
```

**ALBO** prostsze rozwiązanie - bind bezpośrednio do LocalizationService w AXAML:

```xml
<!-- Zamiast binding do ViewModel property -->
<TextBlock Text="{Binding WelcomeMessage}"/>

<!-- Użyj LocalizeExtension -->
<TextBlock Text="{local:Localize UI.WelcomeMessage}"/>
```

---

## 4. Migracja dialogów

### 4.1 Dialogi z fixed content

#### Przed (ErrorDialog.axaml):
```xml
<Window Title="Błąd" Width="400" Height="200">
    <StackPanel>
        <TextBlock Text="Wystąpił błąd:" FontWeight="Bold"/>
        <TextBlock Text="{Binding ErrorMessage}"/>
        <Button Content="OK" Command="{Binding CloseCommand}"/>
    </StackPanel>
</Window>
```

#### Po:
```xml
<Window Title="{local:Localize Dialogs.Error.Title}" Width="400" Height="200">
    <StackPanel>
        <TextBlock Text="{local:Localize Dialogs.Error.Header}" FontWeight="Bold"/>
        <TextBlock Text="{Binding ErrorMessage}"/>
        <Button Content="{local:Localize UI.Buttons.OK}" Command="{Binding CloseCommand}"/>
    </StackPanel>
</Window>
```

#### pl.json:
```json
{
  "Dialogs": {
    "Error": {
      "Title": "Błąd",
      "Header": "Wystąpił błąd:"
    }
  },
  "UI": {
    "Buttons": {
      "OK": "OK"
    }
  }
}
```

### 4.2 UninstallConfirmDialog (z parametrem)

#### Przed (code-behind):
```csharp
public UninstallConfirmDialog(string modName, string installPath)
{
    InitializeComponent();
    MessageTextBlock.Text = $"Czy na pewno chcesz odinstalować {modName}?";
    PathTextBlock.Text = $"Ścieżka: {installPath}";
}
```

#### Po:
```csharp
public UninstallConfirmDialog(string modName, string installPath, ILocalizationService localization)
{
    InitializeComponent();
    _loc = localization;

    MessageTextBlock.Text = _loc.GetFormatted("Dialogs.Confirm.UninstallMessage", modName);
    PathTextBlock.Text = _loc.GetFormatted("Dialogs.Confirm.UninstallPath", installPath);
}
```

#### pl.json:
```json
{
  "Dialogs": {
    "Confirm": {
      "UninstallMessage": "Czy na pewno chcesz odinstalować {0}?",
      "UninstallPath": "Ścieżka: {0}"
    }
  }
}
```

---

## 5. Formatowanie stringów z parametrami

### 5.1 Jeden parametr

#### Przed:
```csharp
var message = $"Zainstalowano mod: {modName}";
```

#### Po:
```csharp
var message = _loc.GetFormatted("Messages.ModInstalled", modName);
```

#### pl.json:
```json
{
  "Messages": {
    "ModInstalled": "Zainstalowano mod: {0}"
  }
}
```

### 5.2 Wiele parametrów

#### Przed:
```csharp
var info = $"Mod {modName} (wersja {version}) wymaga {requiredSpace} MB";
```

#### Po:
```csharp
var info = _loc.GetFormatted("Messages.ModRequirements", modName, version, requiredSpace);
```

#### pl.json:
```json
{
  "Messages": {
    "ModRequirements": "Mod {0} (wersja {1}) wymaga {2} MB"
  }
}
```

### 5.3 Pluralizacja (wersja prosta)

Dla prostych przypadków można użyć warunkowego formatowania:

#### Przed:
```csharp
var text = installedCount == 1
    ? "Zainstalowany 1 mod"
    : $"Zainstalowanych {installedCount} modów";
```

#### Po:
```csharp
var key = installedCount == 1
    ? "UI.Labels.InstalledModSingular"
    : "UI.Labels.InstalledModPlural";
var text = _loc.GetFormatted(key, installedCount);
```

#### pl.json:
```json
{
  "UI": {
    "Labels": {
      "InstalledModSingular": "Zainstalowany 1 mod",
      "InstalledModPlural": "Zainstalowanych {0} modów"
    }
  }
}
```

---

## 6. Checklist i strategia

### Przygotowanie

- [ ] Infrastruktura gotowa (LocalizationService, DI, extension)
- [ ] Utworzone puste pliki pl.json i en.json
- [ ] Namespace `xmlns:local="using:SUSModder.Services.Localization"` dodany do głównych widoków
- [ ] Test na 2-3 przykładowych stringach działa

### Ekstrakcja stringów (zalecane narzędzie: Excel/Google Sheets)

Stwórz arkusz z kolumnami:

| Plik | Lokalizacja | Oryginalny tekst (PL) | Klucz | Kategoria | Status |
|------|-------------|----------------------|-------|-----------|--------|
| MainWindow.axaml | Line 45, Button | Instaluj | UI.Buttons.Install | UI | ✅ Done |
| MainWindow.axaml | Line 67, TextBlock | Zainstalowano w: | UI.Labels.InstalledIn | UI | ⏳ Pending |

### Kolejność migracji (zalecane)

1. **UI.Buttons** - wszystkie przyciski (najłatwiejsze, wszędzie używane)
2. **Dialogs.Error/Confirm/Info** - tytuły i podstawowe wiadomości
3. **MainWindow.axaml** - główny interfejs
4. **UI.Menu** - elementy menu
5. **Settings** - ustawienia aplikacji
6. **InfoPanel** - panel informacyjny
7. **Małe dialogi** - każdy po kolei (szybkie, 5-15 stringów każdy)
8. **MainWindowViewModel** - komunikaty w C#
9. **Pozostałe ViewModele**
10. **Tooltips i pomocnicze teksty**

### Weryfikacja

- [ ] Uruchom aplikację po każdych ~20 migrowanych stringach
- [ ] Sprawdź czy wszystkie teksty się wyświetlają
- [ ] Przetestuj zmianę języka (czy live switch działa)
- [ ] Szukaj `[KEY_NOT_FOUND:]` w UI (oznacza błędny klucz)
- [ ] Sprawdź logi w konsoli

---

## 7. Przykłady przed/po

### 7.1 MainWindow.axaml - FAB Menu

#### Przed:
```xml
<!-- Konfiguracje ToU -->
<Border Background="#2C2C2E" CornerRadius="12" Padding="15,10">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <PathIcon Data="{StaticResource SettingsIcon}" Width="20" Height="20"/>
        <TextBlock Text="Konfiguracje ToU" VerticalAlignment="Center"/>
    </StackPanel>
</Border>

<!-- Modyfikacje DLL -->
<Border Background="#2C2C2E" CornerRadius="12" Padding="15,10">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <PathIcon Data="{StaticResource DllIcon}" Width="20" Height="20"/>
        <TextBlock Text="Modyfikacje DLL" VerticalAlignment="Center"/>
    </StackPanel>
</Border>
```

#### Po:
```xml
<!-- Konfiguracje ToU -->
<Border Background="#2C2C2E" CornerRadius="12" Padding="15,10">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <PathIcon Data="{StaticResource SettingsIcon}" Width="20" Height="20"/>
        <TextBlock Text="{local:Localize UI.Menu.ToUConfigs}" VerticalAlignment="Center"/>
    </StackPanel>
</Border>

<!-- Modyfikacje DLL -->
<Border Background="#2C2C2E" CornerRadius="12" Padding="15,10">
    <StackPanel Orientation="Horizontal" Spacing="5">
        <PathIcon Data="{StaticResource DllIcon}" Width="20" Height="20"/>
        <TextBlock Text="{local:Localize UI.Menu.DllMods}" VerticalAlignment="Center"/>
    </StackPanel>
</Border>
```

### 7.2 AppSettingsView.axaml - Sekcja paths

#### Przed:
```xml
<StackPanel Spacing="10">
    <TextBlock Text="Ścieżka instalacji modów" FontWeight="Bold"/>
    <TextBox Text="{Binding ModsInstallPath}"/>
    <Button Content="Przeglądaj..." Command="{Binding BrowseCommand}"/>
    <Button Content="Przywróć domyślne" Command="{Binding ResetPathCommand}"/>
</StackPanel>
```

#### Po:
```xml
<StackPanel Spacing="10">
    <TextBlock Text="{local:Localize Settings.Paths.Label}" FontWeight="Bold"/>
    <TextBox Text="{Binding ModsInstallPath}"/>
    <Button Content="{local:Localize Settings.Paths.Browse}" Command="{Binding BrowseCommand}"/>
    <Button Content="{local:Localize Settings.Paths.Reset}" Command="{Binding ResetPathCommand}"/>
</StackPanel>
```

### 7.3 MainWindowViewModel.ModOperations.cs

#### Przed:
```csharp
try
{
    await ModService.InstallModAsync(selectedMod.Id);
    await _userInteractionService.ShowInfoAsync("Instalacja zakończona pomyślnie", "Sukces");
}
catch (Exception ex)
{
    await _userInteractionService.ShowErrorAsync($"Błąd podczas instalacji: {ex.Message}", "Błąd");
}
```

#### Po:
```csharp
try
{
    await ModService.InstallModAsync(selectedMod.Id);
    await _userInteractionService.ShowInfoAsync(
        _loc.Get("Messages.InstallSuccess"),
        _loc.Get("Dialogs.Info.Title")
    );
}
catch (Exception ex)
{
    await _userInteractionService.ShowErrorAsync(
        _loc.GetFormatted("Dialogs.Error.InstallFailedWithDetails", ex.Message),
        _loc.Get("Dialogs.Error.Title")
    );
}
```

### 7.4 InfoPanel.axaml - Szczegóły moda

#### Przed:
```xml
<StackPanel Spacing="8">
    <TextBlock Text="Szczegóły moda:" FontSize="16" FontWeight="Bold"/>
    <TextBlock Text="Nazwa:" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.Name}"/>
    <TextBlock Text="Wersja:" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.Version}"/>
    <TextBlock Text="Zainstalowano w:" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.InstallPath}"/>
</StackPanel>
```

#### Po:
```xml
<StackPanel Spacing="8">
    <TextBlock Text="{local:Localize UI.ModDetails.Header}" FontSize="16" FontWeight="Bold"/>
    <TextBlock Text="{local:Localize UI.ModDetails.Name}" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.Name}"/>
    <TextBlock Text="{local:Localize UI.ModDetails.Version}" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.Version}"/>
    <TextBlock Text="{local:Localize UI.ModDetails.InstalledIn}" FontWeight="SemiBold"/>
    <TextBlock Text="{Binding SelectedMod.InstallPath}"/>
</StackPanel>
```

---

## Podsumowanie

### Najczęstsze błędy

❌ **Zapomnienie o namespace w AXAML**
```xml
<!-- Brak xmlns:local -->
<Button Content="{local:Localize ...}"/>  <!-- Nie zadziała! -->
```

❌ **Błędny klucz (literówka)**
```csharp
_loc.Get("UI.Butttons.Install")  // Butttons zamiast Buttons
```

❌ **Brak parametru w GetFormatted**
```csharp
_loc.GetFormatted("Messages.ModInstalled")  // Brakuje parametru modName!
```

❌ **Formatowanie w złej kolejności**
```json
"Message": "Wersja {1} moda {0}"  // Parametry w złej kolejności
```

### Wskazówki

✅ Migruj po kolei, testuj często
✅ Używaj Excel/Sheets do śledzenia postępu
✅ Nadawaj konsystentne klucze (UI.Category.Name)
✅ Grupuj podobne stringi (wszystkie przyciski razem)
✅ Kopiuj dokładny format stringów (spacje, kropki, wielkie litery)
✅ Testuj zmianę języka po każdym większym etapie

---

**Następny krok**: Translation Guide - jak tłumaczyć i dodawać nowe języki.
