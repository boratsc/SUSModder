#include "SettingsWriter.h"

#include "Win32Helpers.h"

#include <fstream>

namespace bootstrapper {

bool SettingsWriter::SaveSelectedChannelAndLanguage(const std::wstring& channel, const std::wstring& languageCode) const {
    const std::wstring directory = GetUserSettingsDirectory();
    if (!EnsureDirectoryExists(directory)) {
        return false;
    }

    const std::wstring path = directory + L"\\user-settings.json";
    const std::wstring contents =
        L"{\n"
        L"  \"mode\": \"\",\n"
        L"  \"lastLaunchId\": 0,\n"
        L"  \"theme\": \"dark\",\n"
        L"  \"language\": \"" + languageCode + L"\",\n"
        L"  \"telemetryEnabled\": true,\n"
        L"  \"modsInstallPath\": \"\",\n"
        L"  \"licenseAccepted\": false,\n"
        L"  \"firstRunDate\": \"\",\n"
        L"  \"updateChannel\": \"" + channel + L"\",\n"
        L"  \"vanillaInstallPath\": \"\"\n"
        L"}\n";

    return WriteTextFile(path, contents);
}

} // namespace bootstrapper
