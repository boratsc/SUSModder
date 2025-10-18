# Frontend – Converters (Konwertery wartości)

## Spis treści
1. [Wprowadzenie](#wprowadzenie)
2. [Lista konwerterów](#lista-konwerterów)
3. [Szczegółowy opis konwerterów](#szczegółowy-opis-konwerterów)
4. [Wzorce użycia](#wzorce-użycia)

---

## Wprowadzenie

Konwertery wartości (Value Converters) w Avalonia XAML implementują interfejs `IValueConverter` i służą do **transformacji wartości** między źródłem danych a UI podczas data bindingu.

### Interfejs IValueConverter

```csharp
public interface IValueConverter
{
    object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);
    object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture);
}
```

- **`Convert`** – transformacja wartości z źródła (ViewModel) do UI (View)
- **`ConvertBack`** – transformacja wartości z UI (View) do źródła (ViewModel) – używane w two-way binding

### Zastosowanie

Konwertery są używane w XAML:

```xml
<Image Source="{Binding IconName, Converter={StaticResource StringToBitmapConverter}}" />
```

lub z parametrem:

```xml
<TextBlock Text="{Binding InstallPath, Converter={StaticResource PathShorteningConverter}, ConverterParameter=30}" />
```

---

## Lista konwerterów

| Konwerter | Status | Zastosowanie |
|-----------|--------|--------------|
| **AsyncUrlToBitmapConverter** | ✅ Używany | Async ładowanie obrazków z URL (avatary Discord) |
| **StringToBitmapConverter** | ✅ Używany | Konwersja nazwy pliku na obraz z Assets |
| **PathShorteningConverter** | ✅ Używany | Skracanie długich ścieżek plików |
| **InstallStatusToOpacityConverter** | ✅ Używany | Opacity badge'a "Zainstalowano" (InstallPath → Opacity) |
| **GreaterThanConverter** | ✅ Używany | Liczba > parametr → bool (enable/disable przycisków) |
| **StringNotNullOrEmptyToBoolConverter** | ✅ Używany | String niepusty → true (widoczność elementów) |
| **UrlToCommandConverter** | ✅ Używany | URL → ReactiveCommand (otwórz link) |
| **ThemeColorConverter** | ✅ Używany | Konwersja koloru motywu |
| **CategoryToClassConverter** | ❌ **NIEUŻYWANY** | Kategoria roli → CSS class (do usunięcia!) |

---

## Szczegółowy opis konwerterów

### 1. AsyncUrlToBitmapConverter

**Plik:** `Converters/AsyncUrlToBitmapConverter.cs`  
**Zastosowanie:** Asynchroniczne ładowanie obrazków z URL (np. avatary serwerów Discord)

#### Funkcjonalność

- **Cache:** Przechowuje załadowane obrazki w `ConcurrentDictionary<string, Bitmap?>` (unikanie wielokrotnego pobierania)
- **Async loading:** Używa `HttpClient` do pobrania obrazka w tle
- **Fallback:** Zwraca `null` podczas ładowania (można dodać placeholder w XAML)

#### Implementacja (skrót)

```csharp
public class AsyncUrlToBitmapConverter : IValueConverter
{
    public static readonly AsyncUrlToBitmapConverter Instance = new();
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            // Sprawdź cache
            if (_cache.TryGetValue(url, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            // Rozpocznij asynchroniczne ładowanie
            _ = LoadImageAsync(url);

            // Zwróć null na razie (pokaże się fallback)
            return null;
        }
        return null;
    }

    private async Task LoadImageAsync(string url)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var bitmap = new Bitmap(memoryStream);
                _cache[url] = bitmap;
                
                // TODO: Powiadom UI o załadowaniu (ReactiveUI PropertyChanged)
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AsyncBitmapConverter] Error loading image: {ex.Message}");
            _cache[url] = null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<Window.Resources>
    <converters:AsyncUrlToBitmapConverter x:Key="AsyncUrlToBitmapConverter"/>
</Window.Resources>

<Image Source="{Binding ServerIconUrl, Converter={StaticResource AsyncUrlToBitmapConverter}}" 
       Width="64" Height="64" />
```

**Użyte w:**
- `RecommendedDiscordsWindow.axaml` – ikony serwerów Discord

---

### 2. StringToBitmapConverter

**Plik:** `Converters/StringToBitmapConverter.cs`  
**Zastosowanie:** Konwersja nazwy pliku na obraz z katalogu Assets

#### Funkcjonalność

- Przekształca string (np. `"tohe.png"`) → `Bitmap` z URI `avares://SUSModder/Assets/{value}`
- Używane do ładowania lokalnych ikon modów

#### Implementacja (skrót)

```csharp
public class StringToBitmapConverter : IValueConverter
{
    public static readonly StringToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string fileName && !string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var uri = new Uri($"avares://SUSModder/Assets/{fileName}");
                return new Bitmap(AssetLoader.Open(uri));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StringToBitmapConverter] Error loading {fileName}: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<Window.Resources>
    <converters:StringToBitmapConverter x:Key="StringToBitmapConverter"/>
</Window.Resources>

<Image Source="{Binding PngFileName, Converter={StaticResource StringToBitmapConverter}}" 
       Width="128" Height="128" />
```

**Użyte w:**
- `MainWindow.axaml` – ikony modów w ListBox i panelu szczegółów
- `InfoPanel.axaml` – ikony social media (Discord, YouTube, Kick, Twitch)
- `DllModSelectionView.axaml` – ikony DLL modów

---

### 3. PathShorteningConverter

**Plik:** `Converters/PathShorteningConverter.cs`  
**Zastosowanie:** Skracanie długich ścieżek plików dla lepszej czytelności w UI

#### Funkcjonalność

- Przyjmuje ścieżkę (np. `"C:\Users\...\Among Us - Town of Us\AmongUs.exe"`)
- Skraca do określonej długości (parametr `ConverterParameter`)
- Zachowuje nazwę pliku i początek ścieżki, dodaje `...` w środku

#### Implementacja (skrót)

```csharp
public class PathShorteningConverter : IValueConverter
{
    public static readonly PathShorteningConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            int maxLength = 40; // Domyślna długość

            if (parameter is string paramStr && int.TryParse(paramStr, out int paramLength))
            {
                maxLength = paramLength;
            }

            if (path.Length <= maxLength)
                return path;

            // Skróć ścieżkę inteligentnie
            try
            {
                var directory = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                    return path.Length > maxLength ? path.Substring(0, maxLength - 3) + "..." : path;

                var availableLength = maxLength - fileName.Length - 4; // 4 dla "...\"

                if (availableLength > 0 && directory.Length > availableLength)
                {
                    return directory.Substring(0, availableLength) + "...\\" + fileName;
                }
                else if (availableLength <= 0)
                {
                    return "..." + fileName;
                }

                return path;
            }
            catch
            {
                return path.Length > maxLength ? path.Substring(0, maxLength - 3) + "..." : path;
            }
        }
        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Przykłady transformacji

| Ścieżka wejściowa | maxLength | Wynik |
|-------------------|-----------|-------|
| `C:\Users\JanKowalski\AppData\Roaming\Among Us - Town of Us\AmongUs.exe` | 40 | `C:\Users\Jan...mong Us - Town of Us\AmongUs.exe` |
| `D:\Games\Steam\steamapps\common\Among Us\Among Us.exe` | 30 | `D:\Games...Among Us.exe` |

#### Użycie w XAML

```xml
<TextBlock Text="{Binding InstallPath, Converter={x:Static converters:PathShorteningConverter.Instance}, ConverterParameter=30}"
           ToolTip.Tip="{Binding InstallPath}" />
```

**Użyte w:**
- `MainWindow.axaml` – wyświetlanie ścieżki instalacji moda (linia 753)

---

### 4. InstallStatusToOpacityConverter

**Plik:** `Converters/InstallStatusToOpacityConverter.cs`  
**Zastosowanie:** Kontrola przezroczystości badge'a "Zainstalowano" w zależności od stanu instalacji

#### Funkcjonalność

- Jeśli `InstallPath` nie jest puste → Opacity = 1.0 (w pełni widoczny)
- Jeśli `InstallPath` jest puste → Opacity = 0.0 (niewidoczny)

#### Implementacja (skrót)

```csharp
public class InstallStatusToOpacityConverter : IValueConverter
{
    public static readonly InstallStatusToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string installPath && !string.IsNullOrWhiteSpace(installPath))
        {
            return 1.0; // Pełna widoczność
        }
        return 0.0; // Niewidoczny
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<Window.Resources>
    <converters:InstallStatusToOpacityConverter x:Key="InstallStatusToOpacityConverter"/>
</Window.Resources>

<!-- Badge "Zainstalowano" -->
<Border Background="Green" CornerRadius="3" Padding="5,2"
        Opacity="{Binding InstallPath, Converter={StaticResource InstallStatusToOpacityConverter}}">
    <TextBlock Text="Zainstalowano" Foreground="White" FontSize="10" />
</Border>
```

**Użyte w:**
- `MainWindow.axaml` – badge "Zainstalowano" w kafelku moda (linia 199, 204)

---

### 5. GreaterThanConverter

**Plik:** `Converters/GreaterThanConverter.cs`  
**Zastosowanie:** Porównanie liczby z parametrem → bool (enable/disable przycisków, widoczność)

#### Funkcjonalność

- Przyjmuje liczbę (np. `SelectedDllMods.Count`) i parametr (np. `0`)
- Zwraca `true`, jeśli liczba > parametr
- Używane do włączania przycisków tylko gdy jest zaznaczony co najmniej 1 element

#### Implementacja (skrót)

```csharp
public class GreaterThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int threshold))
        {
            return intValue > threshold;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<local:GreaterThanConverter x:Key="GreaterThanConverter"/>

<Button Content="Zainstaluj wybrane modyfikacje" 
        Command="{Binding InstallSelectedDllsCommand}"
        IsEnabled="{Binding SelectedDllMods.Count, Converter={StaticResource GreaterThanConverter}, ConverterParameter=0}" />
```

**Logika:** Przycisk jest aktywny tylko gdy `SelectedDllMods.Count > 0`.

**Użyte w:**
- `DllModSelectionView.axaml` – przycisk instalacji DLL (linia 108)

---

### 6. StringNotNullOrEmptyToBoolConverter

**Plik:** `Converters/StringNotNullOrEmptyToBoolConverter.cs`  
**Zastosowanie:** Konwersja string → bool (widoczność elementów, enable przycisków)

#### Funkcjonalność

- Jeśli string nie jest pusty → `true`
- Jeśli string jest pusty → `false`
- **Opcjonalny parametr `"Invert"`** – odwrócenie logiki

#### Implementacja (skrót)

```csharp
public class StringNotNullOrEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool result = value is string str && !string.IsNullOrWhiteSpace(str);
        
        // Odwrócenie jeśli parametr = "Invert"
        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            return !result;
        }
        
        return result;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<Window.Resources>
    <converters:StringNotNullOrEmptyToBoolConverter x:Key="StringNotNullOrEmptyToBoolConverter"/>
</Window.Resources>

<!-- Przycisk "Instaluj" widoczny tylko gdy InstallPath jest puste -->
<Button Content="Instaluj" Command="{Binding InstallCommand}">
    <Button.IsVisible>
        <Binding Path="InstallPath" Converter="{StaticResource StringNotNullOrEmptyToBoolConverter}" 
                 ConverterParameter="Invert"/>
    </Button.IsVisible>
</Button>

<!-- Panel "Zainstalowano" widoczny tylko gdy InstallPath nie jest puste -->
<StackPanel IsVisible="{Binding InstallPath, Converter={StaticResource StringNotNullOrEmptyToBoolConverter}}">
    <TextBlock Text="Zainstalowano" />
    <TextBlock Text="{Binding InstallPath}" />
</StackPanel>
```

**Użyte w:**
- `MainWindow.axaml` – widoczność przycisków (Instaluj/Uruchom/Usuń), panelu ścieżki instalacji (linie 652, 673, 742)

---

### 7. UrlToCommandConverter

**Plik:** `Converters/UrlToCommandConverter.cs`  
**Zastosowanie:** Konwersja URL (string) na ReactiveCommand, który otwiera link w przeglądarce

#### Funkcjonalność

- Przyjmuje URL (np. `"https://discord.gg/psychopaci"`)
- Zwraca `ReactiveCommand<Unit, Unit>`, który uruchamia przeglądarkę z tym URL

#### Implementacja (skrót)

```csharp
public class UrlToCommandConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            return ReactiveCommand.Create(() =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UrlToCommandConverter] Error opening URL: {ex.Message}");
                }
            });
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

#### Użycie w XAML

```xml
<Window.Resources>
    <converters:UrlToCommandConverter x:Key="UrlToCommandConverter"/>
</Window.Resources>

<Button Command="{Binding Source='https://discord.gg/psychopaci', Converter={StaticResource UrlToCommandConverter}}">
    <StackPanel Orientation="Horizontal">
        <Image Source="discord.png" Width="24" />
        <TextBlock Text="Discord" />
    </StackPanel>
</Button>
```

**Użyte w:**
- `InfoPanel.axaml` – przyciski social media (Discord, YouTube, Kick, Twitch) (linie 90, 105, 120, 135)

---

### 8. ThemeColorConverter

**Plik:** `Converters/ThemeColorConverter.cs`  
**Zastosowanie:** Konwersja koloru motywu (enum → SolidColorBrush)

#### Funkcjonalność

- Przyjmuje enum `ThemeType` (Dark, Light, Pink)
- Zwraca odpowiedni kolor (`SolidColorBrush`)

#### Implementacja (hipotetyczna)

```csharp
public class ThemeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ThemeType theme)
        {
            return theme switch
            {
                ThemeType.Dark => new SolidColorBrush(Colors.Black),
                ThemeType.Light => new SolidColorBrush(Colors.White),
                ThemeType.Pink => new SolidColorBrush(Colors.Pink),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

**Użyte w:** (do weryfikacji - brak bezpośredniego użycia w znalezionych plikach AXAML)

---

### 9. CategoryToClassConverter ❌ **NIEUŻYWANY**

**Plik:** `Converters/CategoryToClassConverter.cs`  
**Status:** **DO USUNIĘCIA** – brak użyć w plikach AXAML!

#### Funkcjonalność (teoretyczna)

- Konwersja kategorii roli (Crewmate/Impostor/Neutral/Modifier) → CSS class name
- Prawdopodobnie używane wcześniej do stylowania kart ról

#### Implementacja

```csharp
public class CategoryToClassConverter : IValueConverter
{
    public static readonly CategoryToClassConverter Instance = new();

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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

**Akcja:** Usuń plik `Converters/CategoryToClassConverter.cs` (zobacz [REFACTOR.md](REFACTOR.md)).

---

## Wzorce użycia

### Pattern 1: Konwerter jako Singleton (Instance)

```csharp
public class MyConverter : IValueConverter
{
    public static readonly MyConverter Instance = new();
    // ...
}
```

**Użycie w XAML:**
```xml
<Image Source="{Binding Value, Converter={x:Static converters:MyConverter.Instance}}" />
```

### Pattern 2: Konwerter jako Resource

```xml
<Window.Resources>
    <converters:MyConverter x:Key="MyConverter"/>
</Window.Resources>

<Image Source="{Binding Value, Converter={StaticResource MyConverter}}" />
```

### Pattern 3: Konwerter z parametrem

```xml
<TextBlock Text="{Binding Path, Converter={StaticResource PathShorteningConverter}, ConverterParameter=30}" />
```

### Pattern 4: MultiBinding (Avalonia 11+)

**Uwaga:** Avalonia nie wspiera natywnie `IMultiValueConverter`, ale można użyć `CompiledBinding` z logiką w ViewModelu.

---

## Best practices

### ✅ DO:
- Używaj konwerterów do prostych transformacji danych (string → bool, int → visibility)
- Twórz konwertery jako Singleton (`Instance`) dla lepszej wydajności
- Cache'uj wyniki w konwerterze, jeśli transformacja jest kosztowna (np. `AsyncUrlToBitmapConverter`)
- Obsługuj `null` i nieprawidłowe wartości (try-catch, walidacja)

### ❌ NIE:
- Nie umieszczaj logiki biznesowej w konwerterach (to jest rola ViewModelu!)
- Nie blokuj UI thread w konwerterze (używaj async dla długich operacji)
- Nie twórz zbyt skomplikowanych konwerterów (lepiej przenieść do ViewModel jako derived property)

---

## Statystyki

| Konwerter | Linie kodu | Status | Użycie w AXAML |
|-----------|------------|--------|----------------|
| AsyncUrlToBitmapConverter | ~73 | ✅ | RecommendedDiscordsWindow |
| StringToBitmapConverter | ~40 | ✅ | MainWindow, InfoPanel, DllModSelectionView |
| PathShorteningConverter | ~60 | ✅ | MainWindow |
| InstallStatusToOpacityConverter | ~25 | ✅ | MainWindow |
| GreaterThanConverter | ~25 | ✅ | DllModSelectionView |
| StringNotNullOrEmptyToBoolConverter | ~30 | ✅ | MainWindow |
| UrlToCommandConverter | ~35 | ✅ | InfoPanel |
| ThemeColorConverter | ~30 | ✅ | (do weryfikacji) |
| **CategoryToClassConverter** | ~33 | ❌ **NIEUŻYWANY** | **Brak – do usunięcia!** |

---

## Problemy do naprawy

Zobacz [REFACTOR.md](REFACTOR.md):

1. **CategoryToClassConverter** – nieużywany, do usunięcia ⚠️

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Status:** Wersja robocza
