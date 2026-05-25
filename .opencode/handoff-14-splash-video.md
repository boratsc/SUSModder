# Handoff: 14 – Splash Video WebView2

**Utworzono:** 2026-05-25
**Status:** 🛑 Wstrzymane – nierozwiązany problem z layoutem NativeWebView

---

## USER REQUESTS (AS-IS)

- "chcę dodać zamiast obecnego splashscreena animację ładowania, w jakim formacie najlepiej aby to było zakodowane? mp4 h264 czy coś innego?"
- "dodałem splash w formacie mp4 - h264, 1,5mb, ale jeżeli to pociągnie za sobą 50mb zależności to trochę się mija z celem, choć windows bazowo wszędzie widze, że odtwarza ten filmik bez problemu bez dodatkowych rzeczy. Zamieniłem też splashscreen.jpg, nowy potrzebe być w kwadracie, 640x640 zamiast 640x420"
- "zapisz to jako plan do D:\Development\SUSModder\DOC\2026-05-25 - frontend-ideas pod numerem 14"
- "implementuj"
- "SplashAnimation.mp4 - taka jest nazwa pliku - obecnie chyba jest błędna"
- "a jako fallback powinien być obrazek po prostu splashscreen.jpg"
- "załadował się obrazek a nie wideo, czyli nie zadziałało niestety"
- "Dobra, teraz jest tak, żę początkowo ładuje się taki wykrzywione małę wideo, potem się dopiero rozciąga. Żeby temu zapobiec start powinien być opóźniony a w tym czasie pokazany powinien być splashscreen.jpg (splashscreen to pierwsza klatka i ostatnia tej animacji)"
- "dalej jest tak samo pokazuje się splashscreen, potem video nie wiem, 240x120px które po chwili się rozciąga dopiero"
- "niby jest ok, ale startuje od połowy mniej wiecej zamiast od początku. Dodatkowo - jak się skończy to powinien być z powrotem splashscreen, ta animacja trwa 8s, więc fajnie jak by się nie zapętlała"
- "nie pojawił się w ogóle wideo, pojawiło się, że nie można odnaleźć pliku"
- "wróciliśmy do punktu wyjścia niestety, znów najpierw obraz jest pomniejszony potem dopiero się rozszerza"
- "teraz przez cały czas jest wideo pomniejszone które jest na pierwszej klatce a nie ma splasha"
- "teraz w ogóle się nie pokazuje wideo, dobra, zapisz stan & 'd:\Development\SUSModder\DOC\2026-05-25 - frontend-ideas\14-splash-video-webview2.md' i wrócimy do tematu później, na razie nie działa prawidłowo"

## EXPLICIT CONSTRAINTS (VERBATIM)

- "jeżeli to pociągnie za sobą 50mb zależności to trochę się mija z celem"
- "windows bazowo wszędzie widze, że odtwarza ten filmik bez problemu bez dodatkowych rzeczy"
- "kwadracie, 640x640 zamiast 640x420"
- "SplashAnimation.mp4 - taka jest nazwa pliku"
- "jako fallback powinien być obrazek po prostu splashscreen.jpg"
- "początkowo ładuje się taki wykrzywione małe wideo, potem się dopiero rozciąga. Żeby temu zapobiec start powinien być opóźniony a w tym czasie pokazany powinien być splashscreen.jpg (splashscreen to pierwsza klatka i ostatnia tej animacji)"

## GOAL

Działający splash screen z animacją MP4 H.264 640×640, która startuje płynnie (bez małego/zniekształconego obrazu na początku) i po zakończeniu pokazuje splashscreen.jpg. Używa NativeWebView z Avalonia.Controls.WebView bez dodatkowych zależności.

## CO ZROBIONO

### Działa
- SplashWindow 640×640
- `SplashAnimation.mp4` kopiowany do outputu (MSBuild Target `CopySplashVideo`)
- `splash-player.html` jako `EmbeddedResource`
- Fallback splashscreen.jpg w XAML Image
- EpicAuthDialog zmigrowany na NativeWebView
- Wszystkie buildy przechodzą (0 błędów)

### Próby które nie rozwiązały problemu
1. **Task.Delay + IsVisible** – wideo startuje małe, potem się rozciąga
2. **MinWidth/MinHeight 640** – nie pomogło, layout nadal skacze
3. **Dwa pliki HTML (preload/play)** – query string w file:// nie działa w WebView2
4. **ZIndex zamiast IsVisible** – native HWND zawsze na wierzchu, ZIndex ignorowany
5. **Cover DIV w HTML** – wideo w ogóle się nie pojawiło
6. **Autoplay delay** – wideo startuje od środka (gra w tle)

## KLUCZOWY PROBLEM

`NativeWebView` (native HWND WebView2):
- Gdy `IsVisible=false→true`: layout Avalonii daje najpierw mały rozmiar (~300×150), potem resize do 640×640
- ZIndex nie działa – native HWND zawsze na wierzchu Avalonia controls
- Opacity nie działa – native HWND jest nieprzezroczysty
- `EvaluateJavaScriptAsync` niedostępne – jedyny kanał JS↔C# to `NavigationStarted`

## AKTUALNY STAN KODU (NAJNOWSZA WERSJA)

### XAML
- Image (splashscreen.jpg) – fallback, Stretch
- NativeWebView – Width/Height 640, Left/Top, widoczny od startu

### HTML
- Cover DIV z splashscreen.jpg jako CSS background (z-index:10)
- Wideo pod coverem
- JS: canplaythrough → 1.5s → cover.hidden + play()
- JS: ended → cover pokazuje się z powrotem

### C#
- InitializeVideoAsync: jeden HTML, `webView.Source = new Uri(htmlPath)`
- Żadnego IsVisible/ZIndex/drugiego pliku

## NASTĘPNE KROKI (DO ROZWAŻENIA)

1. **Spróbować `WebView` zamiast `NativeWebView`** – ma `EvaluateJavaScriptAsync`, może inaczej obsługuje layout. Wymaga sprawdzenia czy działa na Windows z Avalonia 12.
2. **Frame extraction** – wyciągnąć klatki z MP4 (FFmpeg lub ręcznie) → sekwencja JPEG → animacja przez timer. Zero WebView2, zero problemów z HWND.
3. **Powrót do statycznego splashscreen.jpg** – najprostsze, działa, user może zaakceptować.

## ZMIENIONE PLIKI (NIEZCOMMITOWANE)

- `SUSModder/Views/SplashWindow.axaml` – layout + NativeWebView
- `SUSModder/Views/SplashWindow.axaml.cs` – InitializeVideoAsync
- `SUSModder/Assets/splash-player.html` – HTML wrapper (cover DIV)
- `SUSModder/Assets/SplashAnimation.mp4` – nowy plik wideo
- `SUSModder/Assets/splashscreen.jpg` – nowa wersja (klatka animacji)
- `SUSModder/SUSModder.csproj` – CopySplashVideo target
- `SUSModder/Views/EpicAuthDialog.axaml` – migracja NativeWebView
- `SUSModder/Views/EpicAuthDialog.axaml.cs` – migracja NativeWebView
- `SUSModder/ViewModels/EpicAuthDialogViewModel.cs` – WebView2 Runtime detection
- `DOC/2026-05-25 - frontend-ideas/14-splash-video-webview2.md` – plan + stan
