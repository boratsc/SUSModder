#include "MainWindow.h"

#include "Logger.h"
#include "ManifestClient.h"

#include <CommCtrl.h>

#include <uxtheme.h>
#include <winreg.h>

#include <thread>

namespace bootstrapper {

namespace {

constexpr wchar_t kWindowClassName[] = L"SUSModderBootstrapperMainWindow";
constexpr int kAppIconId = 101;
constexpr int kWindowWidth = 980;
constexpr int kWindowHeight = 640;

constexpr COLORREF kAppBackground = RGB(24, 27, 33);
constexpr COLORREF kHeroBackground = RGB(31, 37, 48);
constexpr COLORREF kCardBackground = RGB(34, 41, 53);
constexpr COLORREF kTitleColor = RGB(238, 243, 250);
constexpr COLORREF kMutedColor = RGB(169, 180, 196);
constexpr COLORREF kStatusColor = RGB(214, 224, 239);
constexpr COLORREF kSuccessColor = RGB(103, 208, 157);
constexpr COLORREF kErrorColor = RGB(241, 124, 117);
constexpr COLORREF kWarmAccent = RGB(255, 170, 93);
constexpr COLORREF kCoolAccent = RGB(108, 193, 230);
constexpr COLORREF kSoftLine = RGB(63, 72, 87);

constexpr int kEyebrowId = 1000;
constexpr int kTitleId = 1001;
constexpr int kSubtitleId = 1002;
constexpr int kStatusId = 1003;
constexpr int kProgressId = 1004;
constexpr int kInstallButtonId = 1005;
constexpr int kCancelButtonId = 1006;
constexpr int kRetryButtonId = 1007;
constexpr int kAdvancedButtonId = 1008;
constexpr int kChannelLabelId = 1009;
constexpr int kChannelComboId = 1010;
constexpr int kBodyId = 1011;
constexpr int kStatusLabelId = 1012;
constexpr int kResultId = 1013;
constexpr int kFooterId = 1014;
constexpr int kStartMenuCheckboxId = 1015;
constexpr int kDesktopCheckboxId = 1016;
constexpr int kTargetVersionLabelId = 1017;
constexpr int kTargetVersionId = 1018;
constexpr int kLanguagePlRadioId = 1019;
constexpr int kLanguageEnRadioId = 1020;

constexpr UINT kStatusMessage = WM_APP + 1;
constexpr UINT kProgressMessage = WM_APP + 2;
constexpr UINT kCompletionMessage = WM_APP + 3;
constexpr UINT kTargetVersionMessage = WM_APP + 4;

HMENU ControlId(int value) {
    return reinterpret_cast<HMENU>(static_cast<INT_PTR>(value));
}

HFONT CreateUiFont(int height, int weight) {
    return CreateFontW(
        -height,
        0,
        0,
        0,
        weight,
        FALSE,
        FALSE,
        FALSE,
        DEFAULT_CHARSET,
        OUT_OUTLINE_PRECIS,
        CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY,
        VARIABLE_PITCH,
        L"Segoe UI");
}

void DrawTextBlock(HDC deviceContext, const RECT& rect, const std::wstring& text, COLORREF color, UINT format) {
    SetBkMode(deviceContext, TRANSPARENT);
    SetTextColor(deviceContext, color);
    RECT textRect = rect;
    DrawTextW(deviceContext, text.c_str(), -1, &textRect, format);
}

bool IsSystemAppsDarkTheme() {
    DWORD value = 1;
    DWORD valueSize = sizeof(value);
    const LONG result = RegGetValueW(
        HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"AppsUseLightTheme",
        RRF_RT_REG_DWORD,
        nullptr,
        &value,
        &valueSize);

    if (result != ERROR_SUCCESS) {
        return false;
    }

    return value == 0;
}

void ApplySystemTitleBarTheme(HWND windowHandle) {
    using DwmSetWindowAttributeFn = HRESULT(WINAPI*)(HWND, DWORD, LPCVOID, DWORD);

    HMODULE dwmapiModule = LoadLibraryW(L"dwmapi.dll");
    if (dwmapiModule == nullptr) {
        return;
    }

    const auto setWindowAttribute =
        reinterpret_cast<DwmSetWindowAttributeFn>(GetProcAddress(dwmapiModule, "DwmSetWindowAttribute"));
    if (setWindowAttribute != nullptr) {
        const BOOL useDark = IsSystemAppsDarkTheme() ? TRUE : FALSE;
        constexpr DWORD kUseImmersiveDarkMode = 20;
        setWindowAttribute(windowHandle, kUseImmersiveDarkMode, &useDark, sizeof(useDark));
    }

    FreeLibrary(dwmapiModule);
}

void ApplySystemThemeToControl(HWND handle, const wchar_t* subAppName = L"Explorer") {
    using SetWindowThemeFn = HRESULT(WINAPI*)(HWND, LPCWSTR, LPCWSTR);

    if (handle == nullptr) {
        return;
    }

    static HMODULE uxThemeModule = LoadLibraryW(L"uxtheme.dll");
    if (uxThemeModule == nullptr) {
        return;
    }

    const auto setWindowTheme =
        reinterpret_cast<SetWindowThemeFn>(GetProcAddress(uxThemeModule, "SetWindowTheme"));
    if (setWindowTheme != nullptr) {
        setWindowTheme(handle, subAppName, nullptr);
    }
}

} // namespace

MainWindow::MainWindow(HINSTANCE instanceHandle)
    : instanceHandle_(instanceHandle) {
}

MainWindow::~MainWindow() {
    DeleteObject(titleFont_);
    DeleteObject(eyebrowFont_);
    DeleteObject(subtitleFont_);
    DeleteObject(bodyFont_);
    DeleteObject(statusFont_);
    DeleteObject(smallFont_);
    DeleteObject(languageFont_);
    DeleteObject(backgroundBrush_);
    DeleteObject(heroBrush_);
    DeleteObject(cardBrush_);
    DeleteObject(warmAccentBrush_);
    DeleteObject(coolAccentBrush_);
}

bool MainWindow::Create() {
    backgroundBrush_ = CreateSolidBrush(kAppBackground);
    heroBrush_ = CreateSolidBrush(kHeroBackground);
    cardBrush_ = CreateSolidBrush(kCardBackground);
    warmAccentBrush_ = CreateSolidBrush(kWarmAccent);
    coolAccentBrush_ = CreateSolidBrush(kCoolAccent);
    CreateFonts();

    WNDCLASSEXW windowClass{};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.lpfnWndProc = &MainWindow::WindowProc;
    windowClass.hInstance = instanceHandle_;
    windowClass.lpszClassName = kWindowClassName;
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.hbrBackground = backgroundBrush_;
    windowClass.hIcon = LoadIconW(instanceHandle_, MAKEINTRESOURCEW(kAppIconId));
    windowClass.hIconSm = LoadIconW(instanceHandle_, MAKEINTRESOURCEW(kAppIconId));

    if (RegisterClassExW(&windowClass) == 0) {
        return false;
    }

    windowHandle_ = CreateWindowExW(
        0,
        kWindowClassName,
        L"Instalator SUSModder",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        kWindowWidth,
        kWindowHeight,
        nullptr,
        nullptr,
        instanceHandle_,
        this);

    if (windowHandle_ == nullptr) {
        return false;
    }

    CreateChildControls();
    ApplySystemTitleBarTheme(windowHandle_);
    const LANGID uiLang = GetUserDefaultUILanguage();
    language_ = PRIMARYLANGID(uiLang) == LANG_POLISH ? InstallerLanguage::Polish : InstallerLanguage::English;
    SendMessageW(languagePlRadioHandle_, BM_SETCHECK, language_ == InstallerLanguage::Polish ? BST_CHECKED : BST_UNCHECKED, 0);
    SendMessageW(languageEnRadioHandle_, BM_SETCHECK, language_ == InstallerLanguage::English ? BST_CHECKED : BST_UNCHECKED, 0);
    ApplyFonts();
    ApplyLocalizedTexts();
    UpdateAdvancedLayout();

    controller_.SetStatusCallback([this](const std::wstring& status) {
        PostMessageW(windowHandle_, kStatusMessage, 0, reinterpret_cast<LPARAM>(new std::wstring(status)));
    });
    controller_.SetProgressCallback([this](unsigned int progress) {
        PostMessageW(windowHandle_, kProgressMessage, static_cast<WPARAM>(progress), 0);
    });
    controller_.SetCompletionCallback([this](bool success, const std::wstring& message) {
        const auto payload = new std::wstring(message);
        PostMessageW(windowHandle_, kCompletionMessage, success ? 1 : 0, reinterpret_cast<LPARAM>(payload));
    });

    ShowWindow(windowHandle_, SW_SHOWDEFAULT);
    UpdateWindow(windowHandle_);
    RefreshTargetVersionAsync();
    return true;
}

int MainWindow::Run() {
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    return static_cast<int>(message.wParam);
}

void MainWindow::CreateFonts() {
    eyebrowFont_ = CreateUiFont(14, FW_SEMIBOLD);
    titleFont_ = CreateUiFont(30, FW_BOLD);
    subtitleFont_ = CreateUiFont(15, FW_SEMIBOLD);
    bodyFont_ = CreateUiFont(14, FW_NORMAL);
    statusFont_ = CreateUiFont(16, FW_SEMIBOLD);
    smallFont_ = CreateUiFont(13, FW_NORMAL);
    languageFont_ = CreateFontW(-16, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_OUTLINE_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        VARIABLE_PITCH, L"Segoe UI Emoji");
}

void MainWindow::ApplyFonts() const {
    const HWND handles[] = {
        eyebrowHandle_, titleHandle_, subtitleHandle_, bodyHandle_, targetVersionLabelHandle_, targetVersionHandle_,
        statusLabelHandle_, statusHandle_,
        resultHandle_, advancedButtonHandle_, channelLabelHandle_, channelComboHandle_, languagePlRadioHandle_, languageEnRadioHandle_, startMenuShortcutCheckboxHandle_,
        desktopShortcutCheckboxHandle_, installButtonHandle_,
        cancelButtonHandle_, retryButtonHandle_, footerHandle_
    };

    for (HWND handle : handles) {
        if (handle != nullptr) {
            HFONT font = bodyFont_;
            if (handle == eyebrowHandle_) font = eyebrowFont_;
            if (handle == titleHandle_) font = titleFont_;
            if (handle == subtitleHandle_) font = subtitleFont_;
            if (handle == statusLabelHandle_ || handle == statusHandle_ || handle == resultHandle_) font = statusFont_;
            if (handle == targetVersionHandle_) font = subtitleFont_;
            if (handle == languagePlRadioHandle_ || handle == languageEnRadioHandle_) font = languageFont_;
            if (handle == footerHandle_ || handle == advancedButtonHandle_ || handle == channelLabelHandle_) font = smallFont_;
            SendMessageW(handle, WM_SETFONT, reinterpret_cast<WPARAM>(font), TRUE);
        }
    }
}

std::wstring MainWindow::Localize(const std::wstring& polish, const std::wstring& english) const {
    return language_ == InstallerLanguage::Polish ? polish : english;
}

void MainWindow::ApplyLocalizedTexts() {
    SetWindowTextW(windowHandle_, Localize(L"Instalator SUSModder", L"SUSModder Installer").c_str());
    SetWindowTextW(eyebrowHandle_, Localize(L"MENEDŻER MODÓW DO AMONG US", L"AMONG US MOD MANAGER").c_str());
    SetWindowTextW(titleHandle_, Localize(L"Instalator SUSModder", L"SUSModder Installer").c_str());
    SetWindowTextW(subtitleHandle_, Localize(L"Szybka instalacja i aktualizacje modów\r\ndla Among Us.", L"Fast install and updates\r\nfor Among Us mods.").c_str());
    SetWindowTextW(bodyHandle_, Localize(
        L"SUSModder to narzędzie do instalacji, konfiguracji i uruchamiania modów do Among Us. Instalator pobiera najnowszą wersję, sprawdza integralność paczek i przygotowuje aplikację do kolejnych aktualizacji.",
        L"SUSModder helps you install, configure, and launch Among Us mods. The installer downloads the latest version, verifies package integrity, and prepares the app for future updates.").c_str());

    SetWindowTextW(targetVersionLabelHandle_, Localize(L"Wersja do instalacji", L"Version to install").c_str());
    SetWindowTextW(statusLabelHandle_, Localize(L"Status instalacji", L"Installation status").c_str());
    SetWindowTextW(advancedButtonHandle_, advancedVisible_
        ? Localize(L"Ukryj", L"Hide").c_str()
        : Localize(L"Zaawansowane", L"Advanced").c_str());
    SetWindowTextW(channelLabelHandle_, Localize(L"Kanał aktualizacji", L"Update channel").c_str());
    SetWindowTextW(startMenuShortcutCheckboxHandle_, Localize(L"Utwórz wpis w menu Start", L"Create Start Menu entry").c_str());
    SetWindowTextW(desktopShortcutCheckboxHandle_, Localize(L"Utwórz skrót na pulpicie", L"Create Desktop shortcut").c_str());
    SetWindowTextW(installButtonHandle_, Localize(L"Zainstaluj SUSModder", L"Install SUSModder").c_str());
    SetWindowTextW(retryButtonHandle_, Localize(L"Spróbuj ponownie", L"Try again").c_str());
    SetWindowTextW(statusHandle_, Localize(L"Gotowy do pobrania aktualnej wersji SUSModder.", L"Ready to download the latest SUSModder version.").c_str());
    SetWindowTextW(footerHandle_, Localize(
        L"Domyślnie instalowany jest kanał release. Kanał beta służy do testowania nowych funkcji i może być mniej stabilny.",
        L"By default, the release channel is installed. The beta channel is for testing new features and may be less stable.").c_str());

    InvalidateRect(windowHandle_, nullptr, TRUE);
    UpdateWindow(windowHandle_);

    RefreshTargetVersionAsync();
}

LRESULT CALLBACK MainWindow::WindowProc(HWND windowHandle, UINT message, WPARAM wParam, LPARAM lParam) {
    MainWindow* self = nullptr;
    if (message == WM_NCCREATE) {
        const auto* createStruct = reinterpret_cast<CREATESTRUCTW*>(lParam);
        self = static_cast<MainWindow*>(createStruct->lpCreateParams);
        SetWindowLongPtrW(windowHandle, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->windowHandle_ = windowHandle;
    } else {
        self = reinterpret_cast<MainWindow*>(GetWindowLongPtrW(windowHandle, GWLP_USERDATA));
    }

    if (self != nullptr) {
        return self->HandleMessage(message, wParam, lParam);
    }

    return DefWindowProcW(windowHandle, message, wParam, lParam);
}

LRESULT MainWindow::HandleMessage(UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
    case WM_COMMAND:
        switch (LOWORD(wParam)) {
        case kInstallButtonId:
            OnInstallClicked();
            return 0;
        case kCancelButtonId:
            OnCancelClicked();
            return 0;
        case kRetryButtonId:
            OnRetryClicked();
            return 0;
        case kAdvancedButtonId:
            ToggleAdvancedOptions();
            return 0;
        case kChannelComboId:
            if (HIWORD(wParam) == CBN_SELCHANGE) {
                RefreshTargetVersionAsync();
                return 0;
            }
            break;
        case kLanguagePlRadioId:
        case kLanguageEnRadioId:
            if (HIWORD(wParam) == BN_CLICKED) {
                language_ = LOWORD(wParam) == kLanguageEnRadioId ? InstallerLanguage::English : InstallerLanguage::Polish;
                ApplyLocalizedTexts();
                return 0;
            }
            break;
        default:
            break;
        }
        break;
    case WM_PAINT: {
        PAINTSTRUCT paintStruct{};
        HDC deviceContext = BeginPaint(windowHandle_, &paintStruct);
        Paint(deviceContext);
        EndPaint(windowHandle_, &paintStruct);
        return 0;
    }
    case WM_CTLCOLORSTATIC: {
        HDC deviceContext = reinterpret_cast<HDC>(wParam);
        const HWND controlHandle = reinterpret_cast<HWND>(lParam);

        bool useOpaqueBackground = false;
        HBRUSH background = backgroundBrush_;
        if (controlHandle == eyebrowHandle_ || controlHandle == titleHandle_ || controlHandle == subtitleHandle_) {
            background = heroBrush_;
        }

        if (controlHandle == statusHandle_ || controlHandle == resultHandle_) {
            useOpaqueBackground = true;
        }

        SetBkMode(deviceContext, useOpaqueBackground ? OPAQUE : TRANSPARENT);
        SetBkColor(deviceContext, kAppBackground);

        COLORREF textColor = kMutedColor;
        if (controlHandle == eyebrowHandle_) textColor = RGB(147, 212, 255);
        else if (controlHandle == titleHandle_) textColor = RGB(246, 250, 255);
        else if (controlHandle == subtitleHandle_) textColor = RGB(196, 206, 221);
        else if (controlHandle == bodyHandle_) textColor = RGB(175, 186, 202);
        else if (controlHandle == targetVersionLabelHandle_) textColor = RGB(153, 165, 184);
        else if (controlHandle == targetVersionHandle_) textColor = kTitleColor;
        else if (controlHandle == statusLabelHandle_) textColor = RGB(153, 165, 184);
        else if (controlHandle == statusHandle_) textColor = kStatusColor;
        else if (controlHandle == footerHandle_) textColor = kMutedColor;
        else if (controlHandle == resultHandle_) textColor = lastOperationSucceeded_ ? kSuccessColor : kErrorColor;
        else if (controlHandle == channelLabelHandle_) textColor = kMutedColor;

        SetTextColor(deviceContext, textColor);
        return reinterpret_cast<LRESULT>(background);
    }
    case kStatusMessage: {
        auto* status = reinterpret_cast<std::wstring*>(lParam);
        UpdateStatus(*status);
        delete status;
        return 0;
    }
    case kProgressMessage:
        UpdateProgress(static_cast<unsigned int>(wParam));
        return 0;
    case kCompletionMessage: {
        auto* messageText = reinterpret_cast<std::wstring*>(lParam);
        UpdateCompletionState(wParam != 0, *messageText);
        delete messageText;
        return 0;
    }
    case kTargetVersionMessage: {
        auto* messageText = reinterpret_cast<std::wstring*>(lParam);
        UpdateTargetVersion(*messageText);
        delete messageText;
        return 0;
    }
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        break;
    }

    return DefWindowProcW(windowHandle_, message, wParam, lParam);
}

void MainWindow::CreateChildControls() {
    eyebrowHandle_ = CreateWindowW(L"STATIC", L"POLSKI MOD MANAGER DLA AMONG US", WS_CHILD | WS_VISIBLE,
        42, 42, 332, 20, windowHandle_, ControlId(kEyebrowId), instanceHandle_, nullptr);
    titleHandle_ = CreateWindowW(L"STATIC", L"Instalator SUSModder", WS_CHILD | WS_VISIBLE,
        42, 68, 332, 46, windowHandle_, ControlId(kTitleId), instanceHandle_, nullptr);
    subtitleHandle_ = CreateWindowW(L"STATIC", L"Szybka instalacja i aktualizacje modow\r\ndla Among Us.", WS_CHILD | WS_VISIBLE,
        42, 122, 332, 58, windowHandle_, ControlId(kSubtitleId), instanceHandle_, nullptr);

    bodyHandle_ = CreateWindowW(
        L"STATIC",
        L"SUSModder to narzedzie do instalacji, konfiguracji i uruchamiania modow do Among Us. Instalator pobiera najnowsza wersje, sprawdza integralnosc paczek i przygotowuje aplikacje do kolejnych aktualizacji.",
        WS_CHILD | WS_VISIBLE,
        434, 56, 300, 124, windowHandle_, ControlId(kBodyId), instanceHandle_, nullptr);

    targetVersionLabelHandle_ = CreateWindowW(L"STATIC", L"Wersja do instalacji", WS_CHILD | WS_VISIBLE,
        434, 188, 220, 22, windowHandle_, ControlId(kTargetVersionLabelId), instanceHandle_, nullptr);
    targetVersionHandle_ = CreateWindowW(L"STATIC", L"Sprawdzanie...", WS_CHILD | WS_VISIBLE,
        434, 210, 500, 22, windowHandle_, ControlId(kTargetVersionId), instanceHandle_, nullptr);

    statusLabelHandle_ = CreateWindowW(L"STATIC", L"Status instalacji", WS_CHILD | WS_VISIBLE,
        434, 246, 220, 24, windowHandle_, ControlId(kStatusLabelId), instanceHandle_, nullptr);
    statusHandle_ = CreateWindowW(L"STATIC", L"Gotowy do pobrania aktualnej wersji SUSModder.", WS_CHILD | WS_VISIBLE,
        434, 272, 500, 44, windowHandle_, ControlId(kStatusId), instanceHandle_, nullptr);
    resultHandle_ = CreateWindowW(L"STATIC", L"", WS_CHILD | WS_VISIBLE,
        434, 318, 500, 56, windowHandle_, ControlId(kResultId), instanceHandle_, nullptr);

    progressHandle_ = CreateWindowExW(0, PROGRESS_CLASSW, nullptr, WS_CHILD | WS_VISIBLE | PBS_SMOOTH,
        434, 386, 500, 18, windowHandle_, ControlId(kProgressId), instanceHandle_, nullptr);
    SendMessageW(progressHandle_, PBM_SETRANGE, 0, MAKELPARAM(0, 100));
    SendMessageW(progressHandle_, PBM_SETPOS, 0, 0);
    SendMessageW(progressHandle_, PBM_SETBKCOLOR, 0, static_cast<LPARAM>(RGB(46, 54, 66)));
    SendMessageW(progressHandle_, PBM_SETBARCOLOR, 0, static_cast<LPARAM>(RGB(85, 187, 232)));

    advancedButtonHandle_ = CreateWindowW(L"BUTTON", L"Zaawansowane", WS_CHILD | WS_VISIBLE,
        804, 562, 130, 22, windowHandle_, ControlId(kAdvancedButtonId), instanceHandle_, nullptr);
    channelLabelHandle_ = CreateWindowW(L"STATIC", L"Kanal aktualizacji", WS_CHILD,
        434, 454, 160, 20, windowHandle_, ControlId(kChannelLabelId), instanceHandle_, nullptr);
    channelComboHandle_ = CreateWindowW(L"COMBOBOX", nullptr, WS_CHILD | CBS_DROPDOWNLIST | WS_VSCROLL,
        434, 476, 180, 200, windowHandle_, ControlId(kChannelComboId), instanceHandle_, nullptr);
    SendMessageW(channelComboHandle_, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"release"));
    SendMessageW(channelComboHandle_, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"beta"));
    SendMessageW(channelComboHandle_, CB_SETCURSEL, 0, 0);

    languagePlRadioHandle_ = CreateWindowW(L"BUTTON", L"🇵🇱", WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON | WS_GROUP,
        760, 46, 44, 24, windowHandle_, ControlId(kLanguagePlRadioId), instanceHandle_, nullptr);
    languageEnRadioHandle_ = CreateWindowW(L"BUTTON", L"🇬🇧", WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON,
        820, 46, 44, 24, windowHandle_, ControlId(kLanguageEnRadioId), instanceHandle_, nullptr);

    startMenuShortcutCheckboxHandle_ = CreateWindowW(L"BUTTON", L"Utwórz wpis w menu Start", WS_CHILD | WS_VISIBLE | BS_AUTOCHECKBOX | WS_TABSTOP,
        434, 468, 280, 24, windowHandle_, ControlId(kStartMenuCheckboxId), instanceHandle_, nullptr);
    SendMessageW(startMenuShortcutCheckboxHandle_, BM_SETCHECK, BST_CHECKED, 0);

    desktopShortcutCheckboxHandle_ = CreateWindowW(L"BUTTON", L"Utwórz skrót na pulpicie", WS_CHILD | WS_VISIBLE | BS_AUTOCHECKBOX | WS_TABSTOP,
        434, 496, 280, 24, windowHandle_, ControlId(kDesktopCheckboxId), instanceHandle_, nullptr);
    SendMessageW(desktopShortcutCheckboxHandle_, BM_SETCHECK, BST_CHECKED, 0);

    installButtonHandle_ = CreateWindowW(L"BUTTON", L"Zainstaluj SUSModder", WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON,
        434, 418, 240, 38, windowHandle_, ControlId(kInstallButtonId), instanceHandle_, nullptr);
    retryButtonHandle_ = CreateWindowW(L"BUTTON", L"Spróbuj ponownie", WS_CHILD,
        688, 418, 150, 38, windowHandle_, ControlId(kRetryButtonId), instanceHandle_, nullptr);

    footerHandle_ = CreateWindowW(L"STATIC", L"Domyślnie instalowany jest kanał release. Kanał beta służy do testowania nowych funkcji i może być mniej stabilny.", WS_CHILD | WS_VISIBLE,
        434, 560, 360, 52, windowHandle_, ControlId(kFooterId), instanceHandle_, nullptr);

    ApplySystemThemeToControl(installButtonHandle_);
    ApplySystemThemeToControl(retryButtonHandle_);
    ApplySystemThemeToControl(advancedButtonHandle_);
    ApplySystemThemeToControl(startMenuShortcutCheckboxHandle_);
    ApplySystemThemeToControl(desktopShortcutCheckboxHandle_);
    ApplySystemThemeToControl(languagePlRadioHandle_);
    ApplySystemThemeToControl(languageEnRadioHandle_);
    ApplySystemThemeToControl(channelComboHandle_);
    ApplySystemThemeToControl(progressHandle_);

    ShowWindow(channelLabelHandle_, SW_HIDE);
    ShowWindow(channelComboHandle_, SW_HIDE);
    ShowWindow(retryButtonHandle_, SW_HIDE);
}

void MainWindow::Paint(HDC deviceContext) {
    RECT clientRect{};
    GetClientRect(windowHandle_, &clientRect);
    FillRect(deviceContext, &clientRect, backgroundBrush_);

    RECT heroRect{ 24, 24, 404, clientRect.bottom - 24 };
    PaintHeroPanel(deviceContext, heroRect);

    HPEN linePen = CreatePen(PS_SOLID, 1, kSoftLine);
    HGDIOBJ oldPen = SelectObject(deviceContext, linePen);
    MoveToEx(deviceContext, 414, 34, nullptr);
    LineTo(deviceContext, 414, clientRect.bottom - 34);
    SelectObject(deviceContext, oldPen);
    DeleteObject(linePen);

    PaintFeatureCard(deviceContext, RECT{ 44, 186, 382, 306 },
        Localize(L"Szybki start", L"Quick start"),
        Localize(L"Instalator pobiera tylko potrzebne pliki i od razu przygotowuje środowisko.", L"The installer downloads only what is needed and prepares the environment right away."),
        kWarmAccent);
    PaintFeatureCard(deviceContext, RECT{ 44, 320, 382, 440 },
        Localize(L"Bezpieczna weryfikacja", L"Secure verification"),
        Localize(L"Każda paczka jest sprawdzana przed instalacją, aby ograniczyć ryzyko błędów.", L"Every package is verified before install to reduce the risk of corrupted files."),
        kCoolAccent);
    PaintFeatureCard(deviceContext, RECT{ 44, 454, 382, 574 },
        Localize(L"Gotowe na aktualizacje", L"Ready for updates"),
        Localize(L"Po pierwszej instalacji SUSModder obsługuje kolejne aktualizacje bez nowego instalatora.", L"After the first install, SUSModder handles future updates without a new installer."),
        RGB(116, 201, 170));
}

void MainWindow::PaintHeroPanel(HDC deviceContext, const RECT& rect) {
    HGDIOBJ oldBrush = SelectObject(deviceContext, heroBrush_);
    HPEN heroPen = CreatePen(PS_SOLID, 1, kHeroBackground);
    HGDIOBJ oldPen = SelectObject(deviceContext, heroPen);
    RoundRect(deviceContext, rect.left, rect.top, rect.right, rect.bottom, 28, 28);

    RECT badgeRect{ rect.left + 36, rect.top + 170, rect.left + 96, rect.top + 230 };
    SelectObject(deviceContext, warmAccentBrush_);
    HPEN transparentPen = CreatePen(PS_SOLID, 1, kWarmAccent);
    SelectObject(deviceContext, transparentPen);
    RoundRect(deviceContext, badgeRect.left, badgeRect.top, badgeRect.right, badgeRect.bottom, 24, 24);

    HFONT badgeFont = CreateUiFont(34, FW_BOLD);
    HGDIOBJ oldFont = SelectObject(deviceContext, badgeFont);
    DrawTextBlock(deviceContext, badgeRect, L"S", RGB(24, 27, 33), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    SelectObject(deviceContext, oldFont);
    DeleteObject(badgeFont);

    RECT ribbonRect{ rect.right - 176, rect.bottom - 66, rect.right - 44, rect.bottom - 38 };
    SelectObject(deviceContext, coolAccentBrush_);
    RoundRect(deviceContext, ribbonRect.left, ribbonRect.top, ribbonRect.right, ribbonRect.bottom, 14, 14);
    DrawTextBlock(deviceContext, ribbonRect, L"Among Us mods", RGB(20, 25, 33), DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    SelectObject(deviceContext, oldBrush);
    SelectObject(deviceContext, oldPen);
    DeleteObject(heroPen);
    DeleteObject(transparentPen);
}

void MainWindow::PaintFeatureCard(HDC deviceContext, const RECT& rect, const std::wstring& title, const std::wstring& body, COLORREF accent) const {
    HGDIOBJ oldBrush = SelectObject(deviceContext, cardBrush_);
    HPEN outlinePen = CreatePen(PS_SOLID, 1, kSoftLine);
    HGDIOBJ oldPen = SelectObject(deviceContext, outlinePen);
    RoundRect(deviceContext, rect.left, rect.top, rect.right, rect.bottom, 20, 20);

    RECT stripeRect{ rect.left + 18, rect.top + 16, rect.left + 24, rect.bottom - 16 };
    HBRUSH accentBrush = CreateSolidBrush(accent);
    FillRect(deviceContext, &stripeRect, accentBrush);
    DeleteObject(accentBrush);

    RECT titleRect{ rect.left + 42, rect.top + 16, rect.right - 18, rect.top + 40 };
    RECT bodyRect{ rect.left + 42, rect.top + 42, rect.right - 20, rect.bottom - 16 };

    HGDIOBJ oldFont = SelectObject(deviceContext, subtitleFont_);
    DrawTextBlock(deviceContext, titleRect, title, kTitleColor, DT_LEFT | DT_TOP | DT_SINGLELINE);
    SelectObject(deviceContext, smallFont_);
    DrawTextBlock(deviceContext, bodyRect, body, kMutedColor, DT_LEFT | DT_TOP | DT_WORDBREAK);
    SelectObject(deviceContext, oldFont);

    SelectObject(deviceContext, oldBrush);
    SelectObject(deviceContext, oldPen);
    DeleteObject(outlinePen);
}

void MainWindow::UpdateStatus(const std::wstring& status) {
    SetWindowTextW(statusHandle_, status.c_str());
    InvalidateRect(statusHandle_, nullptr, TRUE);
    UpdateWindow(statusHandle_);
}

void MainWindow::UpdateTargetVersion(const std::wstring& text) {
    SetWindowTextW(targetVersionHandle_, text.c_str());
    InvalidateRect(targetVersionHandle_, nullptr, TRUE);
    UpdateWindow(targetVersionHandle_);
}

void MainWindow::UpdateProgress(unsigned int progress) {
    SendMessageW(progressHandle_, PBM_SETPOS, progress, 0);
}

void MainWindow::UpdateCompletionState(bool success, const std::wstring& message) {
    installCompleted_ = true;
    lastOperationSucceeded_ = success;
    completionMessage_ = message;

    EnableWindow(installButtonHandle_, TRUE);
    SetWindowTextW(installButtonHandle_, success ? Localize(L"Zainstaluj ponownie", L"Install again").c_str() : Localize(L"Spróbuj jeszcze raz", L"Try again").c_str());
    ShowWindow(retryButtonHandle_, success ? SW_HIDE : SW_SHOW);
    SetWindowTextW(resultHandle_, completionMessage_.c_str());
    InvalidateRect(resultHandle_, nullptr, TRUE);
    UpdateWindow(resultHandle_);
    InvalidateRect(windowHandle_, nullptr, TRUE);

    if (success) {
        PostMessageW(windowHandle_, WM_CLOSE, 0, 0);
    }
}

void MainWindow::UpdateAdvancedLayout() {
    const bool showAdvanced = advancedVisible_;
    ShowWindow(channelLabelHandle_, showAdvanced ? SW_SHOW : SW_HIDE);
    ShowWindow(channelComboHandle_, showAdvanced ? SW_SHOW : SW_HIDE);

    // Channel selector and shortcut checkboxes share the same vertical area.
    ShowWindow(startMenuShortcutCheckboxHandle_, showAdvanced ? SW_HIDE : SW_SHOW);
    ShowWindow(desktopShortcutCheckboxHandle_, showAdvanced ? SW_HIDE : SW_SHOW);

    SetWindowTextW(advancedButtonHandle_, showAdvanced
        ? Localize(L"Ukryj", L"Hide").c_str()
        : Localize(L"Zaawansowane", L"Advanced").c_str());
}

void MainWindow::RefreshTargetVersionAsync() {
    const std::wstring channel = GetSelectedChannel();
    UpdateTargetVersion(Localize(L"Sprawdzanie...", L"Checking..."));

    std::thread([this, channel]() {
        ManifestClient client;
        std::wstring errorMessage;
        const ReleaseManifest manifest = client.LoadManifest(channel, errorMessage);

        std::wstring versionText;
        if (manifest.success && !manifest.latestVersion.empty()) {
            versionText = Localize(L"Wersja do instalacji: ", L"Version to install: ") + manifest.latestVersion;
        } else {
            versionText = Localize(L"Wersja do instalacji: nieznana", L"Version to install: unknown");
        }

        PostMessageW(windowHandle_, kTargetVersionMessage, 0, reinterpret_cast<LPARAM>(new std::wstring(versionText)));
    }).detach();
}

void MainWindow::OnInstallClicked() {
    installCompleted_ = false;
    lastOperationSucceeded_ = false;
    completionMessage_.clear();
    SetWindowTextW(resultHandle_, Localize(
        L"Instalator pobierze najnowszą wersję i przygotuje pliki gotowe do uruchomienia.",
        L"The installer will download the latest version and prepare ready-to-run files.").c_str());
    ShowWindow(retryButtonHandle_, SW_HIDE);
    EnableWindow(installButtonHandle_, FALSE);
    UpdateStatus(Localize(L"Rozpoczynanie instalacji...", L"Starting installation..."));
    controller_.Start(GetSelectedChannel(), ShouldCreateStartMenuShortcut(), ShouldCreateDesktopShortcut(), language_);
}

void MainWindow::OnCancelClicked() {
    if (controller_.IsBusy()) {
        controller_.Cancel();
        UpdateStatus(Localize(L"Anulowanie instalacji...", L"Cancelling installation..."));
        SetWindowTextW(resultHandle_, Localize(
            L"Przerywamy pobieranie i porządkujemy pliki tymczasowe.",
            L"Stopping download and cleaning temporary files.").c_str());
        return;
    }

    DestroyWindow(windowHandle_);
}

void MainWindow::OnRetryClicked() {
    OnInstallClicked();
}

void MainWindow::ToggleAdvancedOptions() {
    advancedVisible_ = !advancedVisible_;
    UpdateAdvancedLayout();
}

std::wstring MainWindow::GetSelectedChannel() const {
    const LRESULT index = SendMessageW(channelComboHandle_, CB_GETCURSEL, 0, 0);
    if (index == 1) {
        return L"beta";
    }

    return L"release";
}

bool MainWindow::ShouldCreateStartMenuShortcut() const {
    return SendMessageW(startMenuShortcutCheckboxHandle_, BM_GETCHECK, 0, 0) == BST_CHECKED;
}

bool MainWindow::ShouldCreateDesktopShortcut() const {
    return SendMessageW(desktopShortcutCheckboxHandle_, BM_GETCHECK, 0, 0) == BST_CHECKED;
}

} // namespace bootstrapper
