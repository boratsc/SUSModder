# 14 – Splash screen video przez WebView2

**Priorytet:** 🟡 P2
**Effort:** ~6-8h (przekroczone)
**Status:** 🛑 **Zaniechane (2026-05-27)** – nierozwiązywalne problemy techniczne z WebView2. Zostaje statyczny splash screen (`splashscreen.jpg`).

## Cel

Zamiana statycznego splash screena (`splashscreen.jpg` 600×420) na animowany MP4 H.264 640×640 z wykorzystaniem `NativeWebView` (`Avalonia.Controls.WebView`).

## Co działa

- [x] SplashWindow 640×640, kwadrat
- [x] `SplashAnimation.mp4` kopiowany do outputu (MSBuild Target `CopySplashVideo`)
- [x] `splash-player.html` jako `EmbeddedResource` (ładowany przez `GetManifestResourceStream`)
- [x] Fallback: `splashscreen.jpg` w XAML Image, widoczny gdy WebView2 nie działa
- [x] Pasek postępu i tekst overlay działają normalnie
- [x] EpicAuthDialog zmigrowany na `NativeWebView`

## Co nie działa – problemy i próby

### Problem 1: Wideo startuje małe (240×120) i się rozciąga

**Przyczyna:** `NativeWebView` (native HWND WebView2) przy `IsVisible=false→true` startuje layout od małego rozmiaru. WebView2 native window tworzy się w ~300×150 zanim Avalonia zrobi layout na 640×640.

**Próby naprawy:**

| Próba | Co zrobiono | Wynik |
|-------|-------------|-------|
| Opóźnienie (800ms / 1500ms) | `Task.Delay` przed `IsVisible=true` | Wciąż małe wideo na starcie |
| MinWidth/MinHeight 640 | Atrybuty XAML | Nie pomogło – layout przeskakuje |
| Dwa pliki HTML (preload/play) | Osobny plik z autoplay, nawigacja między nimi | WebView2 nie wspiera query stringów w `file://` URI – "file not found" |
| ZIndex zamiast IsVisible | Image ZIndex=1 (na wierzchu), potem swap | Native HWND zawsze na wierzchu – ZIndex nie działa na native controle |
| Cover DIV w HTML | `<div id="cover">` z splashscreen.jpg jako CSS background, wideo schowane, cover znika po opóźnieniu | Wideo w ogóle się nie pojawiło |

### Problem 2: autoplay + opóźnienie = wideo startuje od środka

Bo wideo gra w tle podczas opóźnienia. Rozwiązane dwoma plikami: preload (bez autoplay) → play (z autoplay), ale to wprowadziło problem z query stringami (wyżej).

### Problem 3: Query stringi w file:// URI nie działają

`new Uri(path + "?play=1")` tworzy URI z query stringiem, ale WebView2 w file:// URI nie rozpoznaje go poprawnie → "file not found".

## Aktualna architektura (stan na 2026-05-25)

### XAML

```xml
<Panel>
    <!-- Fallback static image -->
    <Image Source="/Assets/splashscreen.jpg" Stretch="UniformToFill"
           HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>

    <!-- NativeWebView z HTML5 video -->
    <wv:NativeWebView x:Name="SplashVideo"
                        Width="640" Height="640"
                        HorizontalAlignment="Left" VerticalAlignment="Top" />
</Panel>
```

### HTML (ostatnia wersja – cover DIV)

```html
<style>
  #cover {
    position: absolute; top: 0; left: 0;
    width: 640px; height: 640px;
    background: url('SPLASH_JPG_FILE') center/cover no-repeat;
    z-index: 10;
    transition: opacity 0.4s ease-in;
    opacity: 1;
  }
  #cover.hidden { opacity: 0; pointer-events: none; }
  video {
    position: absolute; top: 0; left: 0;
    width: 640px; height: 640px; object-fit: cover;
  }
</style>
<div id="cover"></div>
<video id="splash" muted playsinline preload="auto">
  <source src="VIDEO_SRC" type="video/mp4">
</video>
<script>
  video.addEventListener('canplaythrough', function onReady() {
    video.removeEventListener('canplaythrough', onReady);
    setTimeout(function() {
      cover.classList.add('hidden');
      video.loop = false; video.currentTime = 0; video.play();
    }, 1500);
  });
  video.addEventListener('ended', function() {
    cover.classList.remove('hidden');
  });
</script>
```

### C# InitializeVideoAsync (ostatnia – maksymalnie uproszczona)

```csharp
// NativeWebView widoczny od startu, explict 640x640
// Jeden HTML, brak IsVisible/ZIndex/drugiego pliku
var html = LoadEmbeddedHtml()
    .Replace("VIDEO_SRC", videoUri)
    .Replace("SPLASH_JPG_FILE", splashUri);
File.WriteAllTextAsync(htmlPath, html);
webView.Source = new Uri(htmlPath);
// Resztą steruje JS w HTML
```

## Kluczowe problemy do rozwiązania

1. **NativeWebView layout**: native HWND WebView2 nie respektuje ZIndex, Opacity, ani MinWidth/MinHeight przy `IsVisible=false→true`. Zawsze renderuje się na wierzchu Avalonia controls. Przy starcie tworzy HWND w małym rozmiarze.

2. **Komunikacja JS → C#**: `NativeWebView` nie ma `EvaluateJavaScriptAsync`. Jedyny kanał to `NavigationStarted` – intercept nawigacji na specjalny URL. Działa to, ale ogranicza.

3. **Brak transparentności**: WebView2 HWND jest nieprzezroczysty – nie można umieścić Avalonia Image na wierzchu.

## Potencjalne alternatywy na przyszłość

| Alternatywa | Zalety | Wady |
|-------------|--------|------|
| `WebView` (zamiast `NativeWebView`) | Ma `EvaluateJavaScriptAsync`, lepsza kontrola JS | Wymaga sprawdzenia kompatybilności z Avalonia 12 |
| Wyciągnięcie klatek MP4 → sekwencja JPEG | Brak WebView2, działa zawsze | Więcej assetów, frame extraction potrzebuje FFmpeg |
| Statyczny splashscreen.jpg + pasek postępu | Działa od lat, zero błędów | Brak animacji |
| Zewnętrzne okno wideo (Windows Media Player) | Działa natywnie | Brzydkie, poza kontrolą Avalonii |

## Zmienione pliki

- `SUSModder/Views/SplashWindow.axaml` – layout, NativeWebView, ZIndex/rozmiary
- `SUSModder/Views/SplashWindow.axaml.cs` – InitializeVideoAsync (wiele wersji)
- `SUSModder/Assets/splash-player.html` – HTML wrapper (wiele wersji, ostatnia: cover DIV)
- `SUSModder/Assets/SplashAnimation.mp4` – plik wideo (nowy, 640×640)
- `SUSModder/Assets/splashscreen.jpg` – nowa wersja (pierwsza/ostatnia klatka animacji)
- `SUSModder/SUSModder.csproj` – CopySplashVideo target, AvaloniaResource, pakiety
- `SUSModder/Views/EpicAuthDialog.axaml` – migracja na NativeWebView
- `SUSModder/Views/EpicAuthDialog.axaml.cs` – migracja na NativeWebView
- `SUSModder/ViewModels/EpicAuthDialogViewModel.cs` – WebView2 Runtime detection

## Uwagi na przyszłość

- `File.Delete` tymczasowych HTML (`_splash.html`) w `CloseWithFadeAsync`
- `SplashAnimation.mp4` (~1MB) kopiowany przez MSBuild Target, nie Content
- Testowano na .NET 10 + Avalonia 12 + WebView2 Runtime
- Wszystkie buildy przechodzą (0 błędów, 0 warningów)
