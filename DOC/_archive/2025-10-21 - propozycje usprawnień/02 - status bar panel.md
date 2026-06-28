# 📊 Panel Statusu - Status Bar

## 🎯 Cel
Dodanie eleganckiego panelu statusu na dole głównego okna aplikacji, zawierającego kluczowe informacje o stanie aplikacji, modach i połączeniu z serwerem.

## 📐 Specyfikacja wizualna

### Wymiary i pozycja
- **Wysokość**: 60-80px (elastycznie dostosowywana do treści)
- **Pozycja**: Na samym dole głównego okna, pod listą modów
- **Szerokość**: 100% szerokości okna
- **Styl**: Nowoczesny, z efektami glassmorphism/acrylic i subtelnymi cieniami

### Layout
```
┌─────────────────────────────────────────────────────────────────────┐
│  📦 Mody: 12 zainstalowanych  │  💾 Dysk: 2.4 GB / 50 GB  │  🟢 API  │
│     [Full: 8 | DLL: 4]        │    [═══════░░░░░░] 4.8%   │ Online   │
└─────────────────────────────────────────────────────────────────────┘
```

### Podział na sekcje (Grid z 3 kolumnami)

#### 1. **Sekcja modów** (lewa, ~40% szerokości)
- **Ikona**: 📦 emoji lub SVG
- **Tekst główny**: "Mody: {X} zainstalowanych"
- **Tekst pomocniczy**: "Full: {Y} | DLL: {Z}"
- **Hover tooltip**: Lista nazw zainstalowanych modów (max 5 + "i więcej...")

#### 2. **Sekcja przestrzeni dyskowej** (środek, ~40% szerokości)
- **Ikona**: 💾 emoji lub SVG
- **Tekst główny**: "{X.X} GB / {Total} GB"
- **Progress bar**: Wizualizacja zajętości (kolorowa: zielony < 50%, żółty 50-80%, czerwony > 80%)
- **Procent**: Np. "4.8%"
- **Hover tooltip**: Szczegółowy breakdown:
  - Vanilla: X.X GB
  - Mody Full: X.X GB (z listą top 3 największych)
  - Cache/Temp: X.X MB

#### 3. **Sekcja statusu API** (prawa, ~20% szerokości)
- **Ikona**: 🟢/🔴/🟡 (zielony = online, czerwony = offline, żółty = sprawdzanie)
- **Tekst**: "API: Online" / "Offline" / "Sprawdzanie..."
- **Ping/Latency**: "(120ms)" przy online
- **Hover tooltip**: 
  - URL serwera
  - Ostatnie sprawdzenie: [timestamp]
  - Status: HTTP 200 OK / błąd
- **Auto-refresh**: Co 30 sekund sprawdzanie w tle

---

## 🎨 Szczegóły stylistyczne

### Kolory i gradienty
```xml
<!-- Background z lekkim gradientem -->
<LinearGradientBrush StartPoint="0%,0%" EndPoint="100%,0%">
    <GradientStop Color="{DynamicResource StatusBarGradientStart}" Offset="0"/>
    <GradientStop Color="{DynamicResource StatusBarGradientEnd}" Offset="1"/>
</LinearGradientBrush>

<!-- Glassmorphism/Acrylic effect -->
<Border Background="{DynamicResource StatusBarAcrylicBrush}"
        BackdropMaterial="Acrylic"
        BorderBrush="{DynamicResource AccentBrush}"
        BorderThickness="0,1,0,0"
        CornerRadius="0"
        BoxShadow="0 -2 8 0 #15000000">
```

### Animacje
- **Fade-in**: Panel pojawia się z delikatnym fade-in (300ms) przy starcie aplikacji
- **Progress bar**: Animowane wypełnianie (smooth transition 500ms)
- **Status indicator**: Pulsujący efekt przy zmianie statusu (scale 1.0 → 1.1 → 1.0)
- **Hover effects**: Subtle scale (1.0 → 1.02) i brightness increase

### Ikony statusu API
- **Online (zielony)**: ✓ w kółku z delikatnym glow
- **Offline (czerwony)**: ✕ w kółku
- **Sprawdzanie (żółty)**: ⟳ z animacją obracania (spin)

### Typografia
- **Główne liczby**: FontSize 16-18, FontWeight SemiBold
- **Etykiety**: FontSize 12-14, FontWeight Regular
- **Tekst pomocniczy**: FontSize 10-12, Opacity 0.7

---

## 💻 Implementacja techniczna

### 1. ViewModel (MainWindowViewModel.StatusBar.cs)

Nowy partial class z property dla statusu:

```csharp
public partial class MainWindowViewModel
{
    #region Status Bar Properties

    // Statystyki modów
    private int _installedModsCount;
    private int _installedFullModsCount;
    private int _installedDllModsCount;

    public int InstalledModsCount
    {
        get => _installedModsCount;
        set => this.RaiseAndSetIfChanged(ref _installedModsCount, value);
    }

    public int InstalledFullModsCount
    {
        get => _installedFullModsCount;
        set => this.RaiseAndSetIfChanged(ref _installedFullModsCount, value);
    }

    public int InstalledDllModsCount
    {
        get => _installedDllModsCount;
        set => this.RaiseAndSetIfChanged(ref _installedDllModsCount, value);
    }

    // Przestrzeń dyskowa
    private double _modsFolderSizeGB;
    private double _totalDiskSpaceGB;
    private double _diskUsagePercentage;

    public double ModsFolderSizeGB
    {
        get => _modsFolderSizeGB;
        set => this.RaiseAndSetIfChanged(ref _modsFolderSizeGB, value);
    }

    public double TotalDiskSpaceGB
    {
        get => _totalDiskSpaceGB;
        set => this.RaiseAndSetIfChanged(ref _totalDiskSpaceGB, value);
    }

    public double DiskUsagePercentage
    {
        get => _diskUsagePercentage;
        set => this.RaiseAndSetIfChanged(ref _diskUsagePercentage, value);
    }

    // Status API
    private ApiConnectionStatus _apiStatus = ApiConnectionStatus.Checking;
    private int _apiPingMs;
    private DateTime _lastApiCheck;

    public ApiConnectionStatus ApiStatus
    {
        get => _apiStatus;
        set => this.RaiseAndSetIfChanged(ref _apiStatus, value);
    }

    public int ApiPingMs
    {
        get => _apiPingMs;
        set => this.RaiseAndSetIfChanged(ref _apiPingMs, value);
    }

    public DateTime LastApiCheck
    {
        get => _lastApiCheck;
        set => this.RaiseAndSetIfChanged(ref _lastApiCheck, value);
    }

    public string ApiStatusText => ApiStatus switch
    {
        ApiConnectionStatus.Online => $"Online ({ApiPingMs}ms)",
        ApiConnectionStatus.Offline => "Offline",
        ApiConnectionStatus.Checking => "Sprawdzanie...",
        _ => "Nieznany"
    };

    public string ApiStatusColor => ApiStatus switch
    {
        ApiConnectionStatus.Online => "#4CAF50", // Zielony
        ApiConnectionStatus.Offline => "#F44336", // Czerwony
        ApiConnectionStatus.Checking => "#FFC107", // Żółty
        _ => "#9E9E9E" // Szary
    };

    #endregion

    #region Status Bar Methods

    /// <summary>
    /// Odświeża wszystkie statystyki panelu statusu
    /// </summary>
    public async Task RefreshStatusBarAsync()
    {
        await Task.Run(() =>
        {
            UpdateModsStatistics();
            UpdateDiskSpaceStatistics();
        });

        await CheckApiConnectionAsync();
    }

    /// <summary>
    /// Aktualizuje statystyki zainstalowanych modów
    /// </summary>
    private void UpdateModsStatistics()
    {
        InstalledFullModsCount = Mods.Count(m => m.ModType == "full" && m.IsInstalled);
        InstalledDllModsCount = Mods.Count(m => m.ModType == "dll" && m.IsInstalled);
        InstalledModsCount = InstalledFullModsCount + InstalledDllModsCount;
    }

    /// <summary>
    /// Oblicza zajętość dysku przez folder z modami
    /// </summary>
    private void UpdateDiskSpaceStatistics()
    {
        try
        {
            var modsPath = PathSettings.ModsInstallPath;
            
            if (string.IsNullOrEmpty(modsPath) || !Directory.Exists(modsPath))
            {
                ModsFolderSizeGB = 0;
                TotalDiskSpaceGB = 0;
                DiskUsagePercentage = 0;
                return;
            }

            // Oblicz rozmiar folderu z modami
            var directoryInfo = new DirectoryInfo(modsPath);
            long totalSize = CalculateDirectorySize(directoryInfo);
            ModsFolderSizeGB = totalSize / (1024.0 * 1024.0 * 1024.0); // Bytes to GB

            // Pobierz dostępną przestrzeń na dysku
            var driveInfo = new DriveInfo(Path.GetPathRoot(modsPath) ?? "C:\\");
            TotalDiskSpaceGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
            
            // Oblicz procent zajętości (względem całego dysku)
            DiskUsagePercentage = (ModsFolderSizeGB / TotalDiskSpaceGB) * 100;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error calculating disk space: {ex.Message}");
            ModsFolderSizeGB = 0;
            TotalDiskSpaceGB = 0;
            DiskUsagePercentage = 0;
        }
    }

    /// <summary>
    /// Rekurencyjnie oblicza rozmiar katalogu
    /// </summary>
    private long CalculateDirectorySize(DirectoryInfo directory)
    {
        long size = 0;

        try
        {
            // Rozmiar plików w bieżącym katalogu
            FileInfo[] files = directory.GetFiles();
            foreach (FileInfo file in files)
            {
                size += file.Length;
            }

            // Rekurencyjnie dla podkatalogów
            DirectoryInfo[] subdirs = directory.GetDirectories();
            foreach (DirectoryInfo subdir in subdirs)
            {
                size += CalculateDirectorySize(subdir);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignoruj katalogi bez dostępu
        }

        return size;
    }

    /// <summary>
    /// Sprawdza status połączenia z API
    /// </summary>
    private async Task CheckApiConnectionAsync()
    {
        ApiStatus = ApiConnectionStatus.Checking;

        try
        {
            var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                ApiStatus = ApiConnectionStatus.Offline;
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"{baseUrl}/api/health"); // Endpoint do health check
            
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                ApiStatus = ApiConnectionStatus.Online;
                ApiPingMs = (int)stopwatch.ElapsedMilliseconds;
            }
            else
            {
                ApiStatus = ApiConnectionStatus.Offline;
            }

            LastApiCheck = DateTime.Now;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API connection check failed: {ex.Message}");
            ApiStatus = ApiConnectionStatus.Offline;
            LastApiCheck = DateTime.Now;
        }
    }

    /// <summary>
    /// Timer do auto-refresh statusu API (co 30 sekund)
    /// </summary>
    private async void StartApiStatusAutoRefresh()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CheckApiConnectionAsync();
        }
    }

    #endregion
}

/// <summary>
/// Enum dla statusu połączenia z API
/// </summary>
public enum ApiConnectionStatus
{
    Online,
    Offline,
    Checking
}
```

### 2. View (MainWindow.axaml)

Dodanie panelu na dole, przed zamknięciem głównego Grid:

```xml
<!-- STATUS BAR - na samym dole -->
<Border Grid.Row="2" 
        Classes="status-bar"
        Height="70">
    
    <Grid ColumnDefinitions="2*,2*,1*" Margin="20,0">
        
        <!-- SEKCJA 1: Statystyki modów -->
        <Border Grid.Column="0" Classes="status-section">
            <StackPanel Orientation="Horizontal" Spacing="15">
                <!-- Ikona -->
                <Border Classes="status-icon-container" Background="#4A148C">
                    <TextBlock Text="📦" FontSize="24" VerticalAlignment="Center"/>
                </Border>
                
                <!-- Tekst -->
                <StackPanel VerticalAlignment="Center" Spacing="2">
                    <TextBlock Classes="status-main-text">
                        <Run Text="Mody: "/>
                        <Run Text="{Binding InstalledModsCount}" FontWeight="Bold"/>
                        <Run Text=" zainstalowanych"/>
                    </TextBlock>
                    <TextBlock Classes="status-sub-text">
                        <Run Text="Full: "/>
                        <Run Text="{Binding InstalledFullModsCount}"/>
                        <Run Text=" | DLL: "/>
                        <Run Text="{Binding InstalledDllModsCount}"/>
                    </TextBlock>
                </StackPanel>
            </StackPanel>
            
            <!-- Tooltip z listą modów -->
            <ToolTip.Tip>
                <StackPanel MaxWidth="300">
                    <TextBlock Text="Zainstalowane mody:" FontWeight="Bold" Margin="0,0,0,5"/>
                    <ItemsControl ItemsSource="{Binding InstalledModsList}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}" FontSize="11"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ToolTip.Tip>
        </Border>

        <!-- SEKCJA 2: Przestrzeń dyskowa -->
        <Border Grid.Column="1" Classes="status-section">
            <StackPanel Orientation="Horizontal" Spacing="15">
                <!-- Ikona -->
                <Border Classes="status-icon-container" Background="#0288D1">
                    <TextBlock Text="💾" FontSize="24" VerticalAlignment="Center"/>
                </Border>
                
                <!-- Tekst i progress bar -->
                <StackPanel VerticalAlignment="Center" Spacing="4" Width="250">
                    <TextBlock Classes="status-main-text">
                        <Run Text="{Binding ModsFolderSizeGB, StringFormat='{}{0:F2} GB'}"/>
                        <Run Text=" / "/>
                        <Run Text="{Binding TotalDiskSpaceGB, StringFormat='{}{0:F0} GB'}"/>
                    </TextBlock>
                    
                    <!-- Progress Bar z kolorowaniem -->
                    <Grid>
                        <ProgressBar Value="{Binding DiskUsagePercentage}"
                                     Maximum="100"
                                     Height="8"
                                     CornerRadius="4"
                                     Foreground="{Binding DiskUsagePercentage, Converter={StaticResource DiskUsageColorConverter}}"/>
                        <TextBlock Classes="status-sub-text" 
                                   HorizontalAlignment="Right"
                                   Margin="0,-2,0,0">
                            <Run Text="{Binding DiskUsagePercentage, StringFormat='{}{0:F1}%'}"/>
                        </TextBlock>
                    </Grid>
                </StackPanel>
            </StackPanel>
            
            <!-- Tooltip ze szczegółami -->
            <ToolTip.Tip>
                <StackPanel>
                    <TextBlock Text="Szczegóły przestrzeni:" FontWeight="Bold" Margin="0,0,0,5"/>
                    <TextBlock Text="{Binding DiskSpaceDetailsTooltip}" FontSize="11"/>
                </StackPanel>
            </ToolTip.Tip>
        </Border>

        <!-- SEKCJA 3: Status API -->
        <Border Grid.Column="2" Classes="status-section">
            <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Center">
                <!-- Status indicator (kolorowe kółko) -->
                <Ellipse Width="16" Height="16"
                         Fill="{Binding ApiStatusColor}"
                         Classes="status-pulse"/>
                
                <!-- Tekst statusu -->
                <StackPanel VerticalAlignment="Center">
                    <TextBlock Classes="status-main-text" Text="API"/>
                    <TextBlock Classes="status-sub-text" Text="{Binding ApiStatusText}"/>
                </StackPanel>
            </StackPanel>
            
            <!-- Tooltip ze szczegółami API -->
            <ToolTip.Tip>
                <StackPanel>
                    <TextBlock Text="Status serwera API" FontWeight="Bold" Margin="0,0,0,5"/>
                    <TextBlock FontSize="11">
                        <Run Text="URL: "/>
                        <Run Text="{Binding ApiBaseUrl}"/>
                    </TextBlock>
                    <TextBlock FontSize="11">
                        <Run Text="Ostatnie sprawdzenie: "/>
                        <Run Text="{Binding LastApiCheck, StringFormat='{}{0:HH:mm:ss}'}"/>
                    </TextBlock>
                    <TextBlock FontSize="11" IsVisible="{Binding IsApiOnline}">
                        <Run Text="Opóźnienie: "/>
                        <Run Text="{Binding ApiPingMs}"/>
                        <Run Text="ms"/>
                    </TextBlock>
                </StackPanel>
            </ToolTip.Tip>
        </Border>

    </Grid>
</Border>
```

### 3. Styles (StatusBarStyle.axaml)

Nowy plik ze stylami dedykowanymi dla panelu statusu:

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Status Bar Container -->
    <Style Selector="Border.status-bar">
        <Setter Property="Background">
            <LinearGradientBrush StartPoint="0%,0%" EndPoint="100%,0%">
                <GradientStop Color="#1A1A1A" Offset="0"/>
                <GradientStop Color="#252525" Offset="1"/>
            </LinearGradientBrush>
        </Setter>
        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
        <Setter Property="BorderThickness" Value="0,1,0,0"/>
        <Setter Property="BoxShadow" Value="0 -2 8 0 #15000000"/>
        <Setter Property="Padding" Value="0,10"/>
    </Style>

    <!-- Status Section -->
    <Style Selector="Border.status-section">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Padding" Value="10,5"/>
        <Setter Property="CornerRadius" Value="6"/>
        <Setter Property="Transitions">
            <Transitions>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.2"/>
                <DoubleTransition Property="Opacity" Duration="0:0:0.2"/>
            </Transitions>
        </Setter>
    </Style>

    <Style Selector="Border.status-section:pointerover">
        <Setter Property="Background" Value="#20FFFFFF"/>
        <Setter Property="RenderTransform" Value="scale(1.02)"/>
    </Style>

    <!-- Status Icon Container -->
    <Style Selector="Border.status-icon-container">
        <Setter Property="Width" Value="40"/>
        <Setter Property="Height" Value="40"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="HorizontalAlignment" Value="Center"/>
        <Setter Property="VerticalAlignment" Value="Center"/>
        <Setter Property="BoxShadow" Value="0 2 4 0 #40000000"/>
    </Style>

    <!-- Status Main Text -->
    <Style Selector="TextBlock.status-main-text">
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
    </Style>

    <!-- Status Sub Text -->
    <Style Selector="TextBlock.status-sub-text">
        <Setter Property="FontSize" Value="11"/>
        <Setter Property="FontWeight" Value="Regular"/>
        <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
        <Setter Property="Opacity" Value="0.7"/>
    </Style>

    <!-- Pulsating status indicator -->
    <Style Selector="Ellipse.status-pulse">
        <Setter Property="BoxShadow" Value="0 0 8 2 {Binding Fill}"/>
        <Setter Property="Transitions">
            <Transitions>
                <TransformOperationsTransition Property="RenderTransform" Duration="0:0:1" Easing="SineEaseInOut"/>
            </Transitions>
        </Setter>
    </Style>

    <Style Selector="Ellipse.status-pulse:not(:pointerover)">
        <Style.Animations>
            <Animation Duration="0:0:2" IterationCount="Infinite">
                <KeyFrame Cue="0%">
                    <Setter Property="Opacity" Value="1"/>
                    <Setter Property="RenderTransform" Value="scale(1)"/>
                </KeyFrame>
                <KeyFrame Cue="50%">
                    <Setter Property="Opacity" Value="0.6"/>
                    <Setter Property="RenderTransform" Value="scale(1.1)"/>
                </KeyFrame>
                <KeyFrame Cue="100%">
                    <Setter Property="Opacity" Value="1"/>
                    <Setter Property="RenderTransform" Value="scale(1)"/>
                </KeyFrame>
            </Animation>
        </Style.Animations>
    </Style>

</Styles>
```

### 4. Converter (DiskUsageColorConverter.cs)

Konwerter do kolorowania progress bara w zależności od zajętości:

```csharp
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    /// <summary>
    /// Konwertuje procent zajętości dysku na kolor (zielony/żółty/czerwony)
    /// </summary>
    public class DiskUsageColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                // Zielony < 50%
                if (percentage < 50)
                    return new SolidColorBrush(Color.Parse("#4CAF50"));
                
                // Żółty 50-80%
                if (percentage < 80)
                    return new SolidColorBrush(Color.Parse("#FFC107"));
                
                // Czerwony > 80%
                return new SolidColorBrush(Color.Parse("#F44336"));
            }

            return new SolidColorBrush(Color.Parse("#4CAF50")); // Domyślnie zielony
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
```

---

## 🔧 Integracja z istniejącym kodem

### Miejsca do modyfikacji:

1. **MainWindow.axaml**
   - Dodaj nowy Grid.Row="2" dla status bara (zmień RowDefinitions z "Auto,*" na "Auto,*,Auto")
   - Dodaj import stylu StatusBarStyle.axaml w sekcji Window.Styles

2. **MainWindowViewModel.Initialization.cs**
   - W metodzie `InitializeAsync()` wywołaj `RefreshStatusBarAsync()` po załadowaniu modów
   - Dodaj `StartApiStatusAutoRefresh()` w osobnym Task, aby działał w tle

3. **MainWindowViewModel.ModOperations.cs**
   - Po każdej operacji instalacji/usunięcia moda wywołaj `UpdateModsStatistics()` i `UpdateDiskSpaceStatistics()`

4. **App.axaml**
   - Dodaj `<StyleInclude Source="/Styles/StatusBarStyle.axaml"/>` do głównej sekcji stylów

---

## 🎁 Dodatkowe pomysły (opcjonalne)

### Rozszerzenia funkcjonalne:
1. **Mini wykres**: Mały wykres liniowy pokazujący historię zajętości dysku (ostatnie 7 dni)
2. **Quick actions**: Kliknięcie na sekcję modów otwiera okno zarządzania, na dysk - folder z modami
3. **Notifications badge**: Licznik przy API status, jeśli są dostępne aktualizacje
4. **Animowany progress**: Przy instalacji/aktualizacji modów wyświetl progress bar w miejscu normalnego stanu
5. **Collapse mode**: Możliwość zwinięcia status bara do mini-wersji (tylko ikony, 40px wysokości)

### Informacje dodatkowe do wyświetlenia:
- **Liczba dostępnych aktualizacji** (badge przy ikonie API lub osobna sekcja)
- **Ostatnia aktywność**: "Zainstalowano [ModName] 5 minut temu"
- **Wersja aplikacji**: Mała etykieta "v2.0.0" po prawej stronie
- **Steam/Epic indicator**: Ikona platformy z nazwą aktywnego trybu
- **Szybki switch**: Toggle Steam ↔ Epic bezpośrednio z status bara

---

## 📝 Checklist implementacji

- [ ] Utworzyć `MainWindowViewModel.StatusBar.cs` z properties i metodami
- [ ] Dodać `ApiConnectionStatus` enum do Models
- [ ] Utworzyć `StatusBarStyle.axaml` w folderze Styles
- [ ] Utworzyć `DiskUsageColorConverter.cs` w folderze Converters
- [ ] Zmodyfikować `MainWindow.axaml` (dodać Grid.Row i status bar UI)
- [ ] Dodać import stylu StatusBarStyle do App.axaml
- [ ] Zintegrować `RefreshStatusBarAsync()` w `InitializeAsync()`
- [ ] Dodać wywołania update po operacjach na modach
- [ ] Przetestować obliczanie rozmiaru folderu (performance przy dużych katalogach)
- [ ] Dodać endpoint `/api/health` do API (lub użyć istniejącego)
- [ ] Przetestować auto-refresh API status (30s intervals)
- [ ] Dodać tooltips z dodatkowymi informacjami
- [ ] Przetestować responsywność (różne rozdzielczości)
- [ ] Zoptymalizować animacje (GPU acceleration)

---

## 🚀 Przewidywany impact

### UX/UI:
- ✅ **Instant feedback**: Użytkownik natychmiast widzi stan aplikacji
- ✅ **Space awareness**: Świadomość zajętości dysku pomaga w zarządzaniu
- ✅ **Connection transparency**: Jasny status połączenia redukuje frustrację
- ✅ **Professional look**: Nowoczesny wygląd podnosi postrzeganą jakość aplikacji

### Techniczne:
- ⚠️ **Performance**: Obliczanie rozmiaru folderu może być kosztowne (cache wyników co 5-10 min)
- ⚠️ **Network overhead**: Auto-refresh API co 30s (minimalne, tylko HEAD request lub ping endpoint)
- ✅ **Modular code**: Łatwe dodawanie nowych sekcji statusu w przyszłości

---

## 📚 Referencje i inspiracje

- **VS Code Status Bar**: Minimalny, funkcjonalny design
- **Discord**: Kolorowe status indicators z tooltipami
- **Spotify**: Smooth animations i glassmorphism effects
- **Material Design**: Elevation levels i color semantics
- **Windows 11**: Acrylic materials i modern UI patterns

---

**Autor**: AI Assistant  
**Data**: 2025-10-21  
**Status**: 📋 Do implementacji
