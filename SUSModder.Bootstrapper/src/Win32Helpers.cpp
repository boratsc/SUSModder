#include "Win32Helpers.h"

#include <windows.h>

#include <shlobj.h>

#include <objbase.h>
#include <shobjidl.h>
#include <winreg.h>

#include <algorithm>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <sstream>

namespace bootstrapper {

namespace {

std::wstring GetKnownFolderPath(REFKNOWNFOLDERID folderId) {
    PWSTR rawPath = nullptr;
    const HRESULT result = SHGetKnownFolderPath(folderId, KF_FLAG_CREATE, nullptr, &rawPath);
    if (FAILED(result) || rawPath == nullptr) {
        return L".";
    }

    std::wstring path(rawPath);
    CoTaskMemFree(rawPath);
    return path;
}

bool CreateShellLinkFile(const std::wstring& targetPath, const std::wstring& linkPath, const std::wstring& workingDirectory, const std::wstring& description) {
    IShellLinkW* shellLink = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW, reinterpret_cast<void**>(&shellLink));
    if (FAILED(hr) || shellLink == nullptr) {
        return false;
    }

    shellLink->SetPath(targetPath.c_str());
    shellLink->SetWorkingDirectory(workingDirectory.c_str());
    shellLink->SetDescription(description.c_str());
    shellLink->SetIconLocation(targetPath.c_str(), 0);

    IPersistFile* persistFile = nullptr;
    hr = shellLink->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&persistFile));
    if (FAILED(hr) || persistFile == nullptr) {
        shellLink->Release();
        return false;
    }

    hr = persistFile->Save(linkPath.c_str(), TRUE);
    persistFile->Release();
    shellLink->Release();
    return SUCCEEDED(hr);
}

bool IsValidPortableExecutable(const std::wstring& filePath) {
    std::ifstream file(std::filesystem::path(filePath), std::ios::binary);
    if (!file.is_open()) {
        return false;
    }

    unsigned char mz[2] = { 0, 0 };
    file.read(reinterpret_cast<char*>(mz), sizeof(mz));
    if (file.gcount() != 2 || mz[0] != 'M' || mz[1] != 'Z') {
        return false;
    }

    file.seekg(0x3C, std::ios::beg);
    unsigned int peOffset = 0;
    file.read(reinterpret_cast<char*>(&peOffset), sizeof(peOffset));
    if (!file) {
        return false;
    }

    file.seekg(peOffset, std::ios::beg);
    unsigned char pe[4] = { 0, 0, 0, 0 };
    file.read(reinterpret_cast<char*>(pe), sizeof(pe));
    if (file.gcount() != 4) {
        return false;
    }

    return pe[0] == 'P' && pe[1] == 'E' && pe[2] == 0 && pe[3] == 0;
}

bool WriteFallbackUninstallScript(const std::wstring& installRoot, std::wstring& scriptPath) {
    scriptPath = installRoot + L"\\uninstall.cmd";

    std::wstringstream script;
    script << L"@echo off\r\n";
    script << L"setlocal\r\n";
    script << L"set \"ROOT=" << installRoot << L"\"\r\n";
    script << L"set \"STARTMENU=%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\SUSModder\"\r\n";
    script << L"del \"%USERPROFILE%\\Desktop\\SUSModder.lnk\" /f /q >nul 2>&1\r\n";
    script << L"rmdir /s /q \"%STARTMENU%\" >nul 2>&1\r\n";
    script << L"reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SUSModder\" /f >nul 2>&1\r\n";
    script << L"start \"\" cmd.exe /c \"timeout /t 1 /nobreak >nul & rmdir /s /q \"\"%ROOT%\"\"\"\r\n";
    script << L"endlocal\r\n";
    script << L"exit /b 0\r\n";

    return WriteTextFile(scriptPath, script.str());
}

} // namespace

std::wstring GetAppDataRoot() {
    return GetKnownFolderPath(FOLDERID_LocalAppData) + L"\\SUSModderBootstrapper";
}

std::wstring GetAppLogDirectory() {
    return GetAppDataRoot() + L"\\logs";
}

std::wstring GetTempDownloadDirectory() {
    return GetAppDataRoot() + L"\\temp";
}

std::wstring GetUserSettingsDirectory() {
    return GetKnownFolderPath(FOLDERID_RoamingAppData) + L"\\SUSModder";
}

std::wstring GetDefaultInstallRoot() {
    return GetKnownFolderPath(FOLDERID_LocalAppData) + L"\\SUSModder";
}

bool EnsureDirectoryExists(const std::wstring& path) {
    if (path.empty()) {
        return false;
    }

    const int result = SHCreateDirectoryExW(nullptr, path.c_str(), nullptr);
    return result == ERROR_SUCCESS || result == ERROR_FILE_EXISTS || result == ERROR_ALREADY_EXISTS;
}

bool WriteTextFile(const std::wstring& path, const std::wstring& contents) {
    std::wofstream file(std::filesystem::path(path), std::ios::trunc);
    if (!file.is_open()) {
        return false;
    }

    file << contents;
    return true;
}

bool RunProcessAndWait(const std::wstring& commandLine, unsigned long& exitCode) {
    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    startupInfo.dwFlags = STARTF_USESHOWWINDOW;
    startupInfo.wShowWindow = SW_HIDE;

    PROCESS_INFORMATION processInformation{};
    std::wstring mutableCommandLine = commandLine;

    const BOOL created = CreateProcessW(
        nullptr,
        mutableCommandLine.data(),
        nullptr,
        nullptr,
        FALSE,
        CREATE_NO_WINDOW,
        nullptr,
        nullptr,
        &startupInfo,
        &processInformation);

    if (!created) {
        exitCode = GetLastError();
        return false;
    }

    WaitForSingleObject(processInformation.hProcess, INFINITE);
    GetExitCodeProcess(processInformation.hProcess, &exitCode);
    CloseHandle(processInformation.hThread);
    CloseHandle(processInformation.hProcess);
    return true;
}

bool LaunchProcessDetached(const std::wstring& executablePath) {
    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);

    PROCESS_INFORMATION processInformation{};
    std::wstring commandLine = L"\"" + executablePath + L"\"";

    const BOOL created = CreateProcessW(
        nullptr,
        commandLine.data(),
        nullptr,
        nullptr,
        FALSE,
        0,
        nullptr,
        nullptr,
        &startupInfo,
        &processInformation);

    if (!created) {
        return false;
    }

    CloseHandle(processInformation.hThread);
    CloseHandle(processInformation.hProcess);
    return true;
}

bool RegisterApplicationInUninstallRegistry(const std::wstring& appVersion, const std::wstring& launchExePath, const std::wstring& installRoot) {
    constexpr const wchar_t kUninstallKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SUSModder";

    HKEY keyHandle = nullptr;
    DWORD disposition = 0;
    const LONG createResult = RegCreateKeyExW(
        HKEY_CURRENT_USER,
        kUninstallKey,
        0,
        nullptr,
        REG_OPTION_NON_VOLATILE,
        KEY_SET_VALUE,
        nullptr,
        &keyHandle,
        &disposition);

    if (createResult != ERROR_SUCCESS || keyHandle == nullptr) {
        return false;
    }

    auto setStringValue = [keyHandle](const wchar_t* name, const std::wstring& value) {
        return RegSetValueExW(
            keyHandle,
            name,
            0,
            REG_SZ,
            reinterpret_cast<const BYTE*>(value.c_str()),
            static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS;
    };

    auto setDwordValue = [keyHandle](const wchar_t* name, DWORD value) {
        return RegSetValueExW(
            keyHandle,
            name,
            0,
            REG_DWORD,
            reinterpret_cast<const BYTE*>(&value),
            sizeof(value)) == ERROR_SUCCESS;
    };

    std::wstring installLocation = installRoot;

    std::wstring uninstallString;
    std::wstring quietUninstallString;
    const std::wstring updateExePath = std::filesystem::path(installRoot).append(L"Update.exe").wstring();
    if (std::filesystem::exists(std::filesystem::path(updateExePath)) && IsValidPortableExecutable(updateExePath)) {
        uninstallString = L"\"" + updateExePath + L"\" uninstall";
        quietUninstallString = uninstallString + L" -s";
    } else {
        std::wstring scriptPath;
        const std::wstring cmdExe = L"C:\\Windows\\System32\\cmd.exe";
        if (WriteFallbackUninstallScript(installRoot, scriptPath)) {
            uninstallString = L"\"" + cmdExe + L"\" /c \"\"" + scriptPath + L"\"\"";
            quietUninstallString = uninstallString;
        } else {
            uninstallString = L"\"" + cmdExe + L"\" /c echo Aby usunac aplikacje, usun katalog: " + installRoot + L" && pause";
            quietUninstallString = uninstallString;
        }
        setDwordValue(L"NoModify", 1);
    }

    DWORD estimatedSizeKb = 100000;
    try {
        unsigned long long totalBytes = 0;
        if (std::filesystem::exists(std::filesystem::path(installLocation))) {
            for (const auto& entry : std::filesystem::recursive_directory_iterator(std::filesystem::path(installLocation))) {
                if (entry.is_regular_file()) {
                    totalBytes += entry.file_size();
                }
            }
            estimatedSizeKb = static_cast<DWORD>(totalBytes / 1024ULL);
        }
    }
    catch (...) {
    }

    SYSTEMTIME localTime{};
    GetLocalTime(&localTime);
    wchar_t installDate[16] = {};
    swprintf_s(installDate, L"%04d%02d%02d", localTime.wYear, localTime.wMonth, localTime.wDay);

    bool success = true;
    success = success && setStringValue(L"DisplayName", L"SUSModder");
    success = success && setStringValue(L"DisplayVersion", appVersion);
    success = success && setStringValue(L"Publisher", L"SUSModder Team");
    success = success && setStringValue(L"DisplayIcon", launchExePath);
    success = success && setStringValue(L"InstallLocation", installLocation);
    success = success && setStringValue(L"UninstallString", uninstallString);
    success = success && setStringValue(L"QuietUninstallString", quietUninstallString);
    success = success && setStringValue(L"URLInfoAbout", L"https://susmodder.app");
    success = success && setStringValue(L"HelpLink", L"https://susmodder.app/help");
    success = success && setStringValue(L"InstallDate", installDate);
    success = success && setDwordValue(L"EstimatedSize", estimatedSizeKb);

    RegCloseKey(keyHandle);
    return success;
}

bool CreateApplicationShortcuts(const std::wstring& launchExePath, bool createStartMenuShortcut, bool createDesktopShortcut, std::wstring& errorMessage) {
    errorMessage.clear();

    if (!createStartMenuShortcut && !createDesktopShortcut) {
        return true;
    }

    if (launchExePath.empty() || !std::filesystem::exists(std::filesystem::path(launchExePath))) {
        errorMessage = L"Nie znaleziono pliku SUSModder.exe do utworzenia skrotow.";
        return false;
    }

    const std::wstring workingDirectory = std::filesystem::path(launchExePath).parent_path().wstring();

    HRESULT initHr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool comInitialized = SUCCEEDED(initHr);
    const bool mustUninitialize = initHr == S_OK || initHr == S_FALSE;

    if (!comInitialized) {
        errorMessage = L"Nie udalo sie zainicjalizowac COM do tworzenia skrotow.";
        return false;
    }

    bool success = true;

    if (createStartMenuShortcut) {
        const std::wstring programsPath = GetKnownFolderPath(FOLDERID_Programs);
        const std::wstring appFolder = programsPath + L"\\SUSModder";
        if (!EnsureDirectoryExists(appFolder)) {
            success = false;
            errorMessage = L"Nie udalo sie utworzyc katalogu menu Start dla SUSModder.";
        } else {
            const std::wstring linkPath = appFolder + L"\\SUSModder.lnk";
            if (!CreateShellLinkFile(launchExePath, linkPath, workingDirectory, L"SUSModder")) {
                success = false;
                errorMessage = L"Nie udalo sie utworzyc skrotu w menu Start.";
            }
        }
    }

    if (success && createDesktopShortcut) {
        const std::wstring desktopPath = GetKnownFolderPath(FOLDERID_Desktop);
        const std::wstring linkPath = desktopPath + L"\\SUSModder.lnk";
        if (!CreateShellLinkFile(launchExePath, linkPath, workingDirectory, L"SUSModder")) {
            success = false;
            errorMessage = L"Nie udalo sie utworzyc skrotu na pulpicie.";
        }
    }

    if (mustUninitialize) {
        CoUninitialize();
    }

    return success;
}

std::wstring GetLastErrorMessage(unsigned long errorCode) {
    LPWSTR messageBuffer = nullptr;
    const DWORD size = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        errorCode,
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        reinterpret_cast<LPWSTR>(&messageBuffer),
        0,
        nullptr);

    std::wstring message;
    if (size > 0 && messageBuffer != nullptr) {
        message.assign(messageBuffer, size);
        LocalFree(messageBuffer);
    }

    return message;
}

std::wstring Trim(const std::wstring& value) {
    const auto begin = std::find_if_not(value.begin(), value.end(), [](wchar_t ch) { return std::iswspace(ch) != 0; });
    const auto end = std::find_if_not(value.rbegin(), value.rend(), [](wchar_t ch) { return std::iswspace(ch) != 0; }).base();
    if (begin >= end) {
        return L"";
    }

    return std::wstring(begin, end);
}

std::wstring ToLowerInvariant(const std::wstring& value) {
    std::wstring result = value;
    std::transform(result.begin(), result.end(), result.begin(), [](wchar_t ch) {
        return static_cast<wchar_t>(std::towlower(ch));
    });
    return result;
}

std::wstring JoinUrl(const std::wstring& baseUrl, const std::wstring& relativePath) {
    if (relativePath.rfind(L"http://", 0) == 0 || relativePath.rfind(L"https://", 0) == 0) {
        return relativePath;
    }

    if (baseUrl.empty()) {
        return relativePath;
    }

    if (relativePath.empty()) {
        return baseUrl;
    }

    const bool baseHasSlash = baseUrl.back() == L'/';
    const bool pathHasSlash = relativePath.front() == L'/';

    if (baseHasSlash && pathHasSlash) {
        return baseUrl + relativePath.substr(1);
    }

    if (!baseHasSlash && !pathHasSlash) {
        return baseUrl + L"/" + relativePath;
    }

    return baseUrl + relativePath;
}

} // namespace bootstrapper
