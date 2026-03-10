#include "Logger.h"
#include "MainWindow.h"

#include <windows.h>

#include <CommCtrl.h>

int APIENTRY wWinMain(HINSTANCE instanceHandle, HINSTANCE, PWSTR, int) {
    INITCOMMONCONTROLSEX controls{};
    controls.dwSize = sizeof(controls);
    controls.dwICC = ICC_PROGRESS_CLASS | ICC_STANDARD_CLASSES;
    InitCommonControlsEx(&controls);

    bootstrapper::Logger::Instance().Initialize();
    bootstrapper::Logger::Instance().Info(L"Bootstrapper started");

    bootstrapper::MainWindow mainWindow(instanceHandle);
    if (!mainWindow.Create()) {
        MessageBoxW(nullptr, L"Nie udalo sie uruchomic okna bootstrappera.", L"SUSModder Installer", MB_OK | MB_ICONERROR);
        return 1;
    }

    return mainWindow.Run();
}
