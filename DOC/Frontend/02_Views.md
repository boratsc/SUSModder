# Frontend – Views (Interfejs użytkownika)

## Spis treści
1. [Wprowadzenie](#wprowadzenie)
2. [Główne okno aplikacji](#główne-okno-aplikacji)
3. [Panele pomocnicze](#panele-pomocnicze)
4. [Dialogi ogólne](#dialogi-ogólne)
5. [Dialogi aktualizacji](#dialogi-aktualizacji)
6. [Okna konfiguracyjne](#okna-konfiguracyjne)
7. [Okna specjalistyczne](#okna-specjalistyczne)
8. [Dialogi ToU](#dialogi-tou)
9. [Wzorce i struktury XAML](#wzorce-i-struktury-xaml)

---

## Wprowadzenie

Widoki (Views) w SUSModder to pliki AXAML (Avalonia XAML) wraz z kodem-behind (`.cs`). Definiują strukturę interfejsu użytkownika i wiążą się z ViewModelami poprzez data binding.

**Konwencja nazewnicza:**
- **Window** – samodzielne okno (`MainWindow`, `AppSettingsWindow`)
- **Dialog** – okno modalne (`ConfirmDialog`, `ErrorDialog`)
- **Panel/View** – kontrolka kompozytowa (`InfoPanel`, `DllModSelectionView`)

**Technologia:** Avalonia UI 11 + Fluent Design

---

## Główne okno aplikacji

### MainWindow

**Pliki:** `MainWindow.axaml` + `MainWindow.axaml.cs`  
**ViewModel:** `MainWindowViewModel`  
**Rozmiar XAML:** ~850 linii (!)

#### Odpowiedzialności

Główne okno aplikacji, zawiera:
- **Pasek tytułowy** z przyciskami nawigacji
- **Panel boczny (Pane)** – informacje o aplikacji, ustawienia
- **Lista modów** (ListBox) – wyświetlanie dostępnych modów
- **Panel szczegółów moda** – opis, przyciski akcji (Instaluj, Uruchom, Usuń, Role, etc.)
- **Panele modalne** – InfoPanel, AdditionalActionsPanel, DllModifications

#### Struktura XAML (uproszczona)

```xml
<Window x:Class="SUSModder.Views.MainWindow"
        Title="{Binding WindowTitle}"
        Width="1200" Height="700">
    
    <Grid>
        <!-- Główny SplitView z Pane -->
        <SplitView IsPaneOpen="{Binding IsPaneOpen}" DisplayMode="Inline">
            
            <!-- Pane (panel boczny) -->
            <SplitView.Pane>
                <StackPanel>
                    <TextBlock Text="SUSModder" />
                    <Button Content="Ustawienia" Command="{Binding ShowAppSettingsCommand}" />
                    <Button Content="Info" Command="{Binding ShowInfoCommand}" />
                    <!-- ... inne przyciski -->
                </StackPanel>
            </SplitView.Pane>
            
            <!-- Główna zawartość -->
            <Grid>
                <!-- Panel modów (lewa strona) -->
                <Border Grid.Column="0">
                    <ListBox ItemsSource="{Binding Mods}"
                             SelectedItem="{Binding SelectedMod}">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <!-- Kafelek moda: ikona, nazwa, wersja, badge -->
                                <Grid>
                                    <Image Source="{Binding PngFileName, Converter={StaticResource StringToBitmapConverter}}" />
                                    <TextBlock Text="{Binding Name}" />
                                    <TextBlock Text="{Binding ModVersion}" />
                                    <!-- Badge "Zainstalowano" -->
                                    <Border IsVisible="{Binding InstallPath, Converter={StaticResource StringNotNullOrEmptyToBoolConverter}}" />
                                </Grid>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </Border>
                
                <!-- Panel szczegółów (prawa strona) -->
                <Border Grid.Column="1" IsVisible="{Binding IsModPanelVisible}">
                    <StackPanel>
                        <TextBlock Text="{Binding SelectedMod.Name}" FontSize="24" />
                        <TextBlock Text="{Binding SelectedMod.Description}" />
                        
                        <!-- Przyciski akcji -->
                        <StackPanel Orientation="Horizontal">
                            <Button Content="Instaluj" Command="{Binding InstallCommand}" 
                                    IsVisible="{Binding SelectedMod.CanInstall}" />
                            <Button Content="Uruchom" Command="{Binding LaunchCommand}" 
                                    IsVisible="{Binding SelectedMod.IsInstalled}" />
                            <Button Content="Usuń" Command="{Binding UninstallCommand}" 
                                    IsVisible="{Binding SelectedMod.CanUninstall}" />
                            <Button Content="Role" Command="{Binding ShowRolesCommand}" />
                            <Button Content="Otwórz folder" Command="{Binding OpenFolderCommand}" />
                            <Button Content="Dodatkowe akcje" Command="{Binding ShowAdditionalActionsCommand}" />
                            <Button Content="Dodatkowe DLL" Command="{Binding ShowDllSelectionCommand}" />
                        </StackPanel>
                        
                        <!-- ProgressBar instalacji -->
                        <ProgressBar Value="{Binding SelectedMod.InstallProgress}" 
                                     IsVisible="{Binding SelectedMod.ShowProgress}" />
                        <TextBlock Text="{Binding SelectedMod.InstallStatusMessage}" />
                    </StackPanel>
                </Border>
                
                <!-- Nakładki modalne (Panel InfoPanel, AdditionalActionsPanel, DllModifications) -->
                <views:InfoPanel IsVisible="{Binding IsInfoPanelVisible}" />
                <views:AdditionalActionsPanel IsVisible="{Binding IsAdditionalActionsVisible}" />
                <Border IsVisible="{Binding IsDllModificationsVisible}">
                    <!-- Panel wyboru DLL -->
                </Border>
            </Grid>
        </SplitView>
    </Grid>
</Window>
```

#### Kluczowe elementy

##### ListBox modów (lewa kolumna)
- **ItemsSource:** `{Binding Mods}` (ObservableCollection<ModItem>)
- **SelectedItem:** `{Binding SelectedMod}` (two-way binding)
- **ItemTemplate:** Kafelek z ikoną, nazwą, wersją, badgem "Zainstalowano"

**Konwertery używane:**
- `StringToBitmapConverter` – konwersja `PngFileName` na obraz
- `StringNotNullOrEmptyToBoolConverter` – widoczność badge'a "Zainstalowano"
- `InstallStatusToOpacityConverter` – przezroczystość badge'a

##### Panel szczegółów (prawa kolumna)
- **IsVisible:** `{Binding IsModPanelVisible}` (tylko gdy mod wybrany i nie widać innych paneli)
- **Przyciski akcji:** Instaluj, Uruchom, Usuń, Role, Otwórz folder, Dodatkowe akcje, Dodatkowe DLL
- **Widoczność przycisków:** `IsVisible="{Binding SelectedMod.CanInstall}"` (derived properties z ModItem)

##### ProgressBar instalacji
- **Value:** `{Binding SelectedMod.InstallProgress}` (0-100)
- **IsVisible:** `{Binding SelectedMod.ShowProgress}`
- **StatusMessage:** `{Binding SelectedMod.InstallStatusMessage}` ("Pobieranie...", "Rozpakowywanie...")

#### Code-behind (`MainWindow.axaml.cs`)

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

**Uwaga:** Cała logika jest w `MainWindowViewModel`, code-behind jest minimalny (best practice MVVM).

---

## Panele pomocnicze

### InfoPanel

**Pliki:** `InfoPanel.axaml` + `InfoPanel.axaml.cs`  
**Typ:** UserControl (osadzany w MainWindow)  
**ViewModel:** Dziedziczy DataContext z MainWindow

#### Odpowiedzialności

Panel informacyjny wyświetlany jako nakładka modalna w MainWindow. Zawiera:
- Informacje o aplikacji (wersja, autorzy)
- Linki do social media (Discord, YouTube, Kick, Twitch)
- Przycisk "Wsparcie" (Liberapay)

#### Struktura

```xml
<UserControl x:Class="SUSModder.Views.InfoPanel">
    <Border Background="#CC000000" IsVisible="{Binding IsInfoPanelVisible}">
        <Border Background="#1E1E1E" CornerRadius="10">
            <StackPanel>
                <TextBlock Text="O aplikacji" FontSize="24" />
                <TextBlock Text="SUSModder v{AppVersion}" />
                <TextBlock Text="Autor: boracik" />
                
                <!-- Linki social media -->
                <Button Command="{Binding Source='https://discord.gg/psychopaci', 
                                           Converter={StaticResource UrlToCommandConverter}}">
                    <StackPanel Orientation="Horizontal">
                        <Image Source="{Binding Source='discord.png', 
                                                 Converter={StaticResource StringToBitmapConverter}}" />
                        <TextBlock Text="Discord" />
                    </StackPanel>
                </Button>
                <!-- YouTube, Kick, Twitch... -->
            </StackPanel>
        </Border>
    </Border>
</UserControl>
```

**Konwertery używane:**
- `UrlToCommandConverter` – konwersja URL na ReactiveCommand (otwórz w przeglądarce)
- `StringToBitmapConverter` – ikony social media

---

### AdditionalActionsPanel

**Pliki:** `AdditionalActionsPanel.axaml` + `AdditionalActionsPanel.axaml.cs`  
**Typ:** UserControl (osadzany w MainWindow)  
**ViewModel:** Ma własny code-behind z logiką (hybryda MVVM + code-behind)

#### Odpowiedzialności

Panel dodatkowych akcji dla modów Town of Us. Funkcje:
- **Fix Black Screen** – usunięcie pliku regionInfo (naprawa błędu)
- **Lobby Size** – ustawienie maksymalnej liczby graczy
- **SUStats Config** – konfiguracja systemu statystyk
- **AmongToken** – konfiguracja tokenów Among Us
- **Save/Load Config** – zapis/wczytanie konfiguracji z serwera
- **Change Preset Names** – zmiana nazw presetów

#### Struktura

```xml
<UserControl x:Class="SUSModder.Views.AdditionalActionsPanel">
    <Border Background="#CC000000" IsVisible="{Binding IsAdditionalActionsVisible}">
        <Border Background="#1E1E1E">
            <StackPanel>
                <TextBlock Text="Dodatkowe akcje - Town of Us" />
                
                <Button Content="Fix Black Screen" Command="{Binding FixBlackScreenCommand}" />
                <Button Content="Ustaw wielkość lobby" Command="{Binding LobbySetCommand}" />
                <Button Content="Konfiguracja SUStats" Click="OnSUStatsConfigClick" />
                <Button Content="Wczytaj konfigurację z serwera" Click="OnLoadServerConfigClick" />
                <!-- ... inne przyciski -->
            </StackPanel>
        </Border>
    </Border>
</UserControl>
```

**Code-behind (`AdditionalActionsPanel.axaml.cs`):**

Zawiera event handlery (np. `OnSUStatsConfigClick`) otwierające dialogi/okna konfiguracyjne.

**Przykład:**
```csharp
private void OnSUStatsConfigClick(object? sender, RoutedEventArgs e)
{
    var configWindow = new SUStatsConfigWindow();
    configWindow.Show();
}
```

---

## Dialogi ogólne

### ConfirmDialog

**Pliki:** `ConfirmDialog.axaml` + `ConfirmDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog potwierdzenia z dwoma przyciskami: **Tak** / **Nie**.

#### Właściwości

```csharp
public string DialogTitle { get; set; }
public string DialogMessage { get; set; }
public bool Result { get; private set; } // true = Tak, false = Nie
```

#### Użycie

```csharp
var dialog = new ConfirmDialog("Potwierdź", "Czy na pewno chcesz usunąć ten mod?");
await dialog.ShowDialog(parentWindow);
bool confirmed = dialog.Result;
```

#### Struktura XAML

```xml
<Window Title="{Binding DialogTitle}" Width="400" Height="200">
    <StackPanel>
        <TextBlock Text="{Binding DialogMessage}" TextWrapping="Wrap" />
        <StackPanel Orientation="Horizontal">
            <Button Content="Tak" Click="OnYesClick" />
            <Button Content="Nie" Click="OnNoClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

---

### MessageDialog

**Pliki:** `MessageDialog.axaml` + `MessageDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog informacyjny z jednym przyciskiem: **OK**.

#### Właściwości

```csharp
public string DialogTitle { get; set; }
public string DialogMessage { get; set; }
```

#### Użycie

```csharp
var dialog = new MessageDialog("Sukces", "Mod został pomyślnie zainstalowany.");
await dialog.ShowDialog(parentWindow);
```

---

### ErrorDialog

**Pliki:** `ErrorDialog.axaml` + `ErrorDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog błędu z przyciskiem **OK** i czerwonym stylem.

#### Właściwości

```csharp
public string ErrorTitle { get; set; }
public string ErrorMessage { get; set; }
```

#### Użycie

```csharp
var dialog = new ErrorDialog("Błąd", "Nie udało się zainstalować moda: brak uprawnień.");
await dialog.ShowDialog(parentWindow);
```

---

### PromptDialog

**Pliki:** `PromptDialog.axaml` + `PromptDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog z polem tekstowym (TextBox) do wprowadzenia wartości przez użytkownika.

#### Właściwości

```csharp
public string DialogTitle { get; set; }
public string DialogMessage { get; set; }
public string InputValue { get; set; } // Wprowadzona wartość
public bool Result { get; private set; } // true = OK, false = Cancel
```

#### Użycie

```csharp
var dialog = new PromptDialog("Podaj nazwę", "Wprowadź nową nazwę presetu:");
await dialog.ShowDialog(parentWindow);
if (dialog.Result)
{
    string newName = dialog.InputValue;
}
```

---

### EpicErrorDialog

**Pliki:** `EpicErrorDialog.axaml` + `EpicErrorDialog.axaml.cs`  
**ViewModel:** `EpicErrorDialogViewModel` (w pliku `FileName.cs` ⚠️ – błędna nazwa)

#### Odpowiedzialności

Dialog błędu dla instalacji Epic Games z pełnym logiem błędu i przyciskiem kopiowania do schowka.

#### Właściwości

```csharp
public string ModName { get; set; } // Nazwa moda, który wywołał błąd
public string LogContent { get; set; } // Pełny log błędu
```

#### Komendy

- **CopyLogCommand** – kopiowanie logu do schowka
- **CloseCommand** – zamknięcie dialogu

#### Użycie

```csharp
var dialog = new EpicErrorDialog("Town of Us", fullLogContent);
await dialog.ShowDialog(parentWindow);
```

---

## Dialogi aktualizacji

### UpdateDialog

**Pliki:** `UpdateDialog.axaml` + `UpdateDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog wyświetlający listę dostępnych aktualizacji modów. Użytkownik może:
- Zaznaczyć mody do aktualizacji (checkboxy)
- Kliknąć "Aktualizuj" – rozpoczęcie instalacji aktualizacji
- Kliknąć "Anuluj" – zamknięcie dialogu

#### Struktura danych

```csharp
public class ModUpdateInfo
{
    public string ModName { get; set; }
    public string CurrentVersion { get; set; }
    public string NewVersion { get; set; }
    public bool IsSelected { get; set; } // Checkbox
}

public ObservableCollection<ModUpdateInfo> AvailableUpdates { get; }
```

#### Struktura XAML

```xml
<Window Title="Dostępne aktualizacje" Width="600" Height="500">
    <StackPanel>
        <TextBlock Text="Znaleziono aktualizacje:" />
        
        <ListBox ItemsSource="{Binding AvailableUpdates}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <CheckBox IsChecked="{Binding IsSelected}" />
                        <TextBlock Text="{Binding ModName}" />
                        <TextBlock Text="{Binding CurrentVersion}" />
                        <TextBlock Text="→" />
                        <TextBlock Text="{Binding NewVersion}" />
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        
        <StackPanel Orientation="Horizontal">
            <Button Content="Aktualizuj" Click="OnUpdateClick" />
            <Button Content="Anuluj" Click="OnCancelClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

#### Code-behind

```csharp
private async void OnUpdateClick(object? sender, RoutedEventArgs e)
{
    var selectedUpdates = AvailableUpdates.Where(u => u.IsSelected).ToList();
    
    if (selectedUpdates.Count == 0)
    {
        // Pokaż MessageDialog "Nie wybrano żadnych modów"
        return;
    }
    
    // Instalacja aktualizacji (sekwencja wywołań ModService.InstallModAsync)
    foreach (var update in selectedUpdates)
    {
        await InstallUpdateAsync(update);
    }
    
    Close();
}
```

---

### AppUpdateDialog

**Pliki:** `AppUpdateDialog.axaml` + `AppUpdateDialog.axaml.cs`  
**Typ:** Window (modal)

#### Odpowiedzialności

Dialog aktualizacji aplikacji SUSModder. Wyświetla:
- Numer najnowszej wersji
- ProgressBar pobierania/instalacji
- Status aktualizacji

#### Właściwości

```csharp
public string CurrentVersion { get; set; }
public string NewVersion { get; set; }
public int DownloadProgress { get; set; } // 0-100
public string StatusMessage { get; set; }
public bool IsUpdating { get; set; }
```

#### Kluczowe metody (code-behind)

```csharp
private async Task DownloadAndInstallUpdateAsync()
{
    // 1. Pobranie paczki aktualizacji do %TEMP%
    StatusMessage = "Pobieranie aktualizacji...";
    await AppUpdateService.DownloadUpdateAsync(/* ... */, progress =>
    {
        DownloadProgress = progress;
    });
    
    // 2. Uruchomienie Updater.exe
    StatusMessage = "Instalowanie aktualizacji...";
    AppUpdateService.LaunchUpdater(/* ... */);
    
    // 3. Zamknięcie aplikacji (restart przez Updater)
    Application.Current.Shutdown();
}
```

---

## Okna konfiguracyjne

### AppSettingsWindow

**Pliki:** `AppSettingsWindow.axaml` + `AppSettingsWindow.axaml.cs`  
**ViewModel:** `AppSettingsViewModel`

#### Odpowiedzialności

Okno ustawień aplikacji. Użytkownik może:
- Wybrać ścieżkę instalacji modów (Browse)
- Zmienić tryb gry (Steam / Epic)
- Włączyć/wyłączyć tryb deweloperski
- Przywrócić ustawienia domyślne
- Wykonać Factory Reset (usuwa `config.json`!)

#### Struktura XAML

```xml
<Window Title="Ustawienia" Width="600" Height="400"
        DataContext="{Binding Source={StaticResource AppSettingsViewModel}}">
    <StackPanel>
        <TextBlock Text="Ścieżka instalacji modów:" />
        <Grid>
            <TextBox Text="{Binding ModsInstallPath}" IsReadOnly="True" />
            <Button Content="Przeglądaj" Command="{Binding BrowseFolderCommand}" />
        </Grid>
        
        <TextBlock Text="Tryb gry:" />
        <ComboBox SelectedItem="{Binding GameMode}">
            <ComboBoxItem Content="Steam" Tag="steam" />
            <ComboBoxItem Content="Epic Games" Tag="epic" />
        </ComboBox>
        
        <CheckBox Content="Tryb deweloperski" IsChecked="{Binding DeveloperMode}" />
        
        <StackPanel Orientation="Horizontal">
            <Button Content="Zapisz" Command="{Binding SaveCommand}" />
            <Button Content="Anuluj" Command="{Binding CancelCommand}" />
            <Button Content="Reset" Command="{Binding ResetToDefaultCommand}" />
            <Button Content="Factory Reset" Command="{Binding FactoryResetCommand}" />
        </StackPanel>
    </StackPanel>
</Window>
```

#### Code-behind

```csharp
public AppSettingsWindow()
{
    InitializeComponent();
    DataContext = new AppSettingsViewModel(this);
}
```

---

### SUStatsConfigWindow

**Pliki:** `SUStatsConfigWindow.axaml` + `SUStatsConfigWindow.axaml.cs`  
**ViewModel:** `SUStatsConfigViewModel`

#### Odpowiedzialności

Okno konfiguracji SUStats (system statystyk Town of Us). Użytkownik może:
- Dodać serwer SUStats (URL, nazwa)
- Usunąć serwer
- Wybrać aktywny serwer (radio button)
- Zapisać konfigurację

#### Struktura XAML

```xml
<Window Title="Konfiguracja SUStats" Width="500" Height="400"
        DataContext="{Binding Source={StaticResource SUStatsConfigViewModel}}">
    <StackPanel>
        <TextBlock Text="Serwery SUStats:" />
        
        <ListBox ItemsSource="{Binding Servers}" SelectedItem="{Binding SelectedServer}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <RadioButton GroupName="ServerSelection" 
                                     IsChecked="{Binding IsSelected}" />
                        <TextBlock Text="{Binding ServerName}" />
                        <TextBlock Text="{Binding ServerUrl}" />
                        <Button Content="Usuń" Command="{Binding RemoveServerCommand}" 
                                CommandParameter="{Binding}" />
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        
        <Button Content="Dodaj serwer" Command="{Binding AddServerCommand}" />
        
        <StackPanel Orientation="Horizontal">
            <Button Content="Zapisz" Command="{Binding SaveCommand}" />
            <Button Content="Anuluj" Command="{Binding CancelCommand}" />
        </StackPanel>
    </StackPanel>
</Window>
```

---

### RecommendedDiscordsWindow

**Pliki:** `RecommendedDiscordsWindow.axaml` + `RecommendedDiscordsWindow.axaml.cs`  
**ViewModel:** `RecommendedDiscordsViewModel`

#### Odpowiedzialności

Okno wyświetlające listę polecanych serwerów Discord (pobieraną z API lub cache).

#### Struktura XAML

```xml
<Window Title="Polecane serwery Discord" Width="700" Height="600"
        DataContext="{Binding Source={StaticResource RecommendedDiscordsViewModel}}">
    <StackPanel>
        <TextBlock Text="Polecane serwery Discord" FontSize="20" />
        
        <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsLoading}" />
        
        <ListBox ItemsSource="{Binding DiscordServers}">
            <ListBox.ItemTemplate>
                <DataTemplate DataType="vm:DiscordServerViewModel">
                    <Grid>
                        <Image Source="{Binding ServerIconUrl, Converter={StaticResource AsyncUrlToBitmapConverter}}" />
                        <TextBlock Text="{Binding ServerName}" />
                        <TextBlock Text="{Binding OwnerName}" />
                        <Button Content="Dołącz" Command="{Binding OpenInviteCommand}" />
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </StackPanel>
</Window>
```

**Konwerter:** `AsyncUrlToBitmapConverter` – async ładowanie avatarów z URL

---

### DllModSelectionView

**Pliki:** `DllModSelectionView.axaml` + `DllModSelectionView.axaml.cs`  
**ViewModel:** `DllModSelectionViewModel`

#### Odpowiedzialności

Widok wyboru i instalacji modyfikacji DLL do wybranego moda full.

#### Struktura XAML

```xml
<UserControl DataContext="{Binding Source={StaticResource DllModSelectionViewModel}}">
    <StackPanel>
        <TextBlock Text="Dostępne modyfikacje DLL:" />
        
        <ListBox ItemsSource="{Binding DllMods}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <CheckBox IsChecked="{Binding IsSelected}" />
                        <Image Source="{Binding PngFileName, Converter={StaticResource StringToBitmapConverter}}" />
                        <TextBlock Text="{Binding ModName}" />
                        <TextBlock Text="{Binding Description}" />
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        
        <Button Content="Zainstaluj wybrane modyfikacje" 
                Command="{Binding InstallSelectedDllsCommand}"
                IsEnabled="{Binding SelectedDllMods.Count, Converter={StaticResource GreaterThanConverter}, ConverterParameter=0}" />
        
        <!-- Panel podsumowania po instalacji -->
        <Border IsVisible="{Binding IsInstallationComplete}">
            <StackPanel>
                <TextBlock Text="Instalacja zakończona!" />
                <TextBlock Text="{Binding InstallationSummary}" />
                <Button Content="OK" Command="{Binding OkCommand}" />
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

**Konwerter:** `GreaterThanConverter` – wyłączenie przycisku, gdy `SelectedDllMods.Count == 0`

---

## Okna specjalistyczne

### RolesWindow

**Pliki:** `RolesWindow.axaml` + `RolesWindow.axaml.cs`  
**Serwis:** `RolesService` (pobieranie ról z API)

#### Odpowiedzialności

Okno wyświetlające listę ról w modzie Town of Us / innych modach. Użytkownik może:
- Przeglądać role (filtrowanie po kategorii: Crewmate, Impostor, Neutral, Modifier)
- Kliknąć rolę → otwiera szczegóły w prawym side sheet (overlay z blurem), bez osobnego okna

#### Struktura XAML

```xml
<Window Title="Role - {ModName}" Width="1200" Height="800">
    <Grid>
        <!-- Filtry kategorii -->
        <StackPanel Orientation="Horizontal">
            <RadioButton Content="Wszystkie" GroupName="CategoryFilter" IsChecked="True" />
            <RadioButton Content="Crewmate" GroupName="CategoryFilter" />
            <RadioButton Content="Impostor" GroupName="CategoryFilter" />
            <RadioButton Content="Neutral" GroupName="CategoryFilter" />
            <RadioButton Content="Modifier" GroupName="CategoryFilter" />
        </StackPanel>
        
        <!-- Lista ról -->
        <ItemsControl ItemsSource="{Binding FilteredRoles}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Classes="role-card"
                            PointerPressed="OnRoleCardClick">
                        <!-- karta roli z nagłówkiem, opisem, abilities tagami, mod name -->
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Grid>
</Window>
```

#### Code-behind

```csharp
private void OnRoleCardClick(object? sender, PointerPressedEventArgs e)
{
    if (sender is Border { DataContext: Role role })
    {
        ShowRoleDetails(role); // otwiera side sheet (overlay)
    }
}
```

---

### Side sheet szczegółów roli (wbudowany w RolesWindow)

**Plik:** `RolesWindow.axaml` (sekcja overlay + side sheet) + `RolesWindow.axaml.cs` (logika otwierania/zamykania)

#### Odpowiedzialności

Pływający panel (overlay z blurem) pokazujący szczegóły roli w tym samym oknie. Wyświetla:
- Nazwę roli
- Kategorię/typ z kolorowym nagłówkiem
- Opis
- Listę umiejętności (Abilities) – ukrywana, jeśli brak
- Nazwę moda

#### Struktura (skrót)

```xml
<Border x:Name="DetailOverlay" Background="#B3000000" Effect="Blur(10)" />
<Border x:Name="DetailSheet" HorizontalAlignment="Right" MinWidth="360" MaxWidth="520">
    <StackPanel>
        <!-- nagłówek z kategorią/typem i przyciskiem zamknięcia -->
        <!-- opis -->
        <!-- abilities list (ItemsControl) -->
        <!-- mod info -->
    </StackPanel>
</Border>
```
Zamykanie: klik tła, przycisk ✕, klawisz Esc. Animacje: fade + lekki slide (Margin).```

### ConsoleWindow

**Pliki:** `ConsoleWindow.axaml` + `ConsoleWindow.axaml.cs`

#### Odpowiedzialności

Okno konsoli debug (widoczne tylko w trybie deweloperskim). Wyświetla logi aplikacji.

#### Właściwości

```csharp
public ObservableCollection<string> LogEntries { get; } = new();
```

#### Struktura XAML

```xml
<Window Title="Konsola" Width="800" Height="500">
    <ListBox ItemsSource="{Binding LogEntries}" FontFamily="Consolas" />
</Window>
```

**Integracja z `ConsoleLogger`:**

```csharp
ConsoleLogger.LogAdded += (message) =>
{
    Dispatcher.UIThread.Post(() => LogEntries.Add(message));
};
```

---

### LobbySetDialog

**Pliki:** `LobbySetDialog.axaml` + `LobbySetDialog.axaml.cs`

#### Odpowiedzialności

Dialog ustawiania maksymalnej wielkości lobby (Town of Us).

#### Właściwości

```csharp
public int MaxPlayers { get; set; } // Zakres: 4-15
public bool Result { get; private set; } // true = OK, false = Cancel
```

#### Struktura XAML

```xml
<Window Title="Ustaw wielkość lobby" Width="300" Height="200">
    <StackPanel>
        <TextBlock Text="Maksymalna liczba graczy:" />
        <Slider Minimum="4" Maximum="15" Value="{Binding MaxPlayers}" />
        <TextBlock Text="{Binding MaxPlayers}" />
        
        <StackPanel Orientation="Horizontal">
            <Button Content="OK" Click="OnOkClick" />
            <Button Content="Anuluj" Click="OnCancelClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

---

### HashDisplayDialog

**Pliki:** `HashDisplayDialog.axaml` + `HashDisplayDialog.axaml.cs`

#### Odpowiedzialności

Dialog wyświetlający hash pliku (MD5/SHA256) z przyciskiem kopiowania do schowka.

#### Właściwości

```csharp
public string FileName { get; set; }
public string HashValue { get; set; }
```

#### Komendy

- **CopyHashCommand** – kopiowanie hashu do schowka

---

## Dialogi ToU

### LoadServerConfigDialog

**Pliki:** `LoadServerConfigDialog.axaml` + `LoadServerConfigDialog.axaml.cs`

#### Odpowiedzialności

Dialog wczytywania zapisanej konfiguracji SUStats/AmongToken z serwera.

#### Właściwości

```csharp
public ObservableCollection<SavedConfigItem> SavedConfigs { get; } // Lista zapisanych konfiguracji
public SavedConfigItem? SelectedConfig { get; set; } // Wybrana konfiguracja
public bool Result { get; private set; } // true = Load, false = Cancel
```

---

### SUStatsConfirmDialog

**Pliki:** `SUStatsConfirmDialog.axaml` + `SUStatsConfirmDialog.axaml.cs`

#### Odpowiedzialności

Dialog potwierdzenia zapisu konfiguracji SUStats na serwer.

---

### ChangePresetNamesDialog

**Pliki:** `ChangePresetNamesDialog.axaml` + `ChangePresetNamesDialog.axaml.cs`

#### Odpowiedzialności

Dialog zmiany nazw presetów Town of Us. Użytkownik może:
- Zobaczyć listę plików presetów (`ModStamp0.dat`, `ModStamp1.dat`, ...)
- Wprowadzić nowe nazwy dla każdego presetu
- Zapisać zmiany (rename plików)

#### Właściwości

```csharp
public ObservableCollection<PresetFileItem> PresetFiles { get; } // Lista presetów
```

**PresetFileItem:**
```csharp
public string FileName { get; set; } // Obecna nazwa pliku
public string NewName { get; set; } // Nowa nazwa (TextBox binding)
```

---

## Wzorce i struktury XAML

### 1. Data Binding

```xml
<!-- One-way binding (tylko odczyt) -->
<TextBlock Text="{Binding ModName}" />

<!-- Two-way binding (edycja) -->
<TextBox Text="{Binding ModName, Mode=TwoWay}" />

<!-- Binding z konwerterem -->
<Image Source="{Binding PngFileName, Converter={StaticResource StringToBitmapConverter}}" />

<!-- Binding z parametrem konwertera -->
<Border IsVisible="{Binding InstallPath, Converter={StaticResource StringNotNullOrEmptyToBoolConverter}}" />
```

### 2. Command Binding

```xml
<!-- Komenda bez parametru -->
<Button Content="Instaluj" Command="{Binding InstallCommand}" />

<!-- Komenda z parametrem -->
<Button Content="Usuń" Command="{Binding RemoveServerCommand}" 
        CommandParameter="{Binding}" />

<!-- Event handler (code-behind) -->
<Button Content="Konfiguracja" Click="OnConfigClick" />
```

### 3. ItemsControl / ListBox

```xml
<ListBox ItemsSource="{Binding Mods}" SelectedItem="{Binding SelectedMod}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Grid>
                <!-- Struktura pojedynczego elementu -->
                <TextBlock Text="{Binding Name}" />
            </Grid>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### 4. Widoczność warunkowa

```xml
<!-- IsVisible binding -->
<StackPanel IsVisible="{Binding IsModSelected}">
    <!-- Zawartość widoczna tylko gdy mod wybrany -->
</StackPanel>

<!-- IsVisible z konwerterem -->
<Border IsVisible="{Binding InstallPath, Converter={StaticResource StringNotNullOrEmptyToBoolConverter}}">
    <!-- Widoczne tylko gdy InstallPath nie jest puste -->
</Border>
```

### 5. Zasoby (Resources)

```xml
<Window.Resources>
    <converters:StringToBitmapConverter x:Key="StringToBitmapConverter"/>
    <converters:UrlToCommandConverter x:Key="UrlToCommandConverter"/>
</Window.Resources>

<!-- Użycie -->
<Image Source="{Binding IconName, Converter={StaticResource StringToBitmapConverter}}" />
```

### 6. Style i tematy

```xml
<Window.Styles>
    <StyleInclude Source="/Styles/LinkButtonStyle.axaml"/>
    <StyleInclude Source="/Themes/DarkTheme.axaml"/>
</Window.Styles>
```

---

## Statystyki Views

| Kategoria | Liczba | Przykłady |
|-----------|--------|-----------|
| **Okna główne** | 1 | MainWindow |
| **Panele** | 2 | InfoPanel, AdditionalActionsPanel |
| **Dialogi ogólne** | 5 | ConfirmDialog, MessageDialog, ErrorDialog, PromptDialog, EpicErrorDialog |
| **Dialogi aktualizacji** | 2 | UpdateDialog, AppUpdateDialog |
| **Okna konfiguracyjne** | 4 | AppSettingsWindow, SUStatsConfigWindow, RecommendedDiscordsWindow, DllModSelectionView |
| **Okna specjalistyczne** | 4 | RolesWindow, RoleDetailWindow, ConsoleWindow, LobbySetDialog, HashDisplayDialog |
| **Dialogi ToU** | 3 | LoadServerConfigDialog, SUStatsConfirmDialog, ChangePresetNamesDialog |
| **RAZEM** | **21** dokumentowanych widoków | (z 40 plików .axaml w projekcie) |

---

## Best practices

### ✅ DO:
- Używaj data bindingu zamiast bezpośredniej manipulacji UI w code-behind
- Stosuj konwertery wartości dla transformacji danych (string → bool, int → visibility)
- Minimalizuj logikę w code-behind – przenieś do ViewModel
- Używaj `ReactiveCommand` zamiast event handlerów `Click`
- Dbaj o responsywność UI – operacje async w CommandAsync

### ❌ NIE:
- Nie umieszczaj logiki biznesowej w code-behind
- Nie blokuj UI thread – używaj `async/await`
- Nie duplikuj kodu XAML – używaj Style, ControlTemplate, Resources
- Nie hardcoduj wartości – używaj bindingu do ViewModel

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Status:** Wersja robocza
