#pragma once

#include <windows.h>

#include "InstallController.h"

#include <string>

namespace bootstrapper {

enum class InstallerLanguage {
    Polish,
    English
};

class MainWindow {
public:
    explicit MainWindow(HINSTANCE instanceHandle);
    ~MainWindow();

    bool Create();
    int Run();

private:
    static LRESULT CALLBACK WindowProc(HWND windowHandle, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam);

    void CreateChildControls();
    void CreateFonts();
    void ApplyFonts() const;
    void ApplyLocalizedTexts();
    void Paint(HDC deviceContext);
    void PaintHeroPanel(HDC deviceContext, const RECT& rect);
    void PaintFeatureCard(HDC deviceContext, const RECT& rect, const std::wstring& title, const std::wstring& body, COLORREF accent) const;
    std::wstring Localize(const std::wstring& polish, const std::wstring& english) const;
    void UpdateStatus(const std::wstring& status);
    void UpdateTargetVersion(const std::wstring& text);
    void UpdateProgress(unsigned int progress);
    void UpdateCompletionState(bool success, const std::wstring& message);
    void UpdateAdvancedLayout();
    void RefreshTargetVersionAsync();
    void OnInstallClicked();
    void OnCancelClicked();
    void OnRetryClicked();
    void ToggleAdvancedOptions();
    std::wstring GetSelectedChannel() const;
    bool ShouldCreateStartMenuShortcut() const;
    bool ShouldCreateDesktopShortcut() const;

    HINSTANCE instanceHandle_ = nullptr;
    HWND windowHandle_ = nullptr;
    HWND eyebrowHandle_ = nullptr;
    HWND titleHandle_ = nullptr;
    HWND subtitleHandle_ = nullptr;
    HWND bodyHandle_ = nullptr;
    HWND targetVersionLabelHandle_ = nullptr;
    HWND targetVersionHandle_ = nullptr;
    HWND statusLabelHandle_ = nullptr;
    HWND statusHandle_ = nullptr;
    HWND resultHandle_ = nullptr;
    HWND progressHandle_ = nullptr;
    HWND advancedButtonHandle_ = nullptr;
    HWND channelLabelHandle_ = nullptr;
    HWND channelComboHandle_ = nullptr;
    HWND languagePlRadioHandle_ = nullptr;
    HWND languageEnRadioHandle_ = nullptr;
    HWND startMenuShortcutCheckboxHandle_ = nullptr;
    HWND desktopShortcutCheckboxHandle_ = nullptr;
    HWND installButtonHandle_ = nullptr;
    HWND cancelButtonHandle_ = nullptr;
    HWND retryButtonHandle_ = nullptr;
    HWND footerHandle_ = nullptr;

    HFONT titleFont_ = nullptr;
    HFONT eyebrowFont_ = nullptr;
    HFONT subtitleFont_ = nullptr;
    HFONT bodyFont_ = nullptr;
    HFONT statusFont_ = nullptr;
    HFONT smallFont_ = nullptr;
    HFONT languageFont_ = nullptr;

    HBRUSH backgroundBrush_ = nullptr;
    HBRUSH heroBrush_ = nullptr;
    HBRUSH cardBrush_ = nullptr;
    HBRUSH warmAccentBrush_ = nullptr;
    HBRUSH coolAccentBrush_ = nullptr;

    bool advancedVisible_ = false;
    bool installCompleted_ = false;
    bool lastOperationSucceeded_ = false;
    InstallerLanguage language_ = InstallerLanguage::Polish;
    std::wstring completionMessage_;
    InstallController controller_;
};

} // namespace bootstrapper
