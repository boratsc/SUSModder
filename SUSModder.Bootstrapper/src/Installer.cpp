#include "Installer.h"

#include "Logger.h"
#include "Win32Helpers.h"

#include <filesystem>

namespace bootstrapper {

bool Installer::PrepareSeedInstall(const std::wstring& packagePath,
    const ReleaseAsset& asset,
    const std::wstring& channel,
    const std::wstring& updateExePath,
    const StatusCallback& onStatus,
    std::wstring& errorMessage,
    std::wstring& launchPath) const {
    const std::wstring installRoot = GetDefaultInstallRoot();
    const std::wstring packagesDirectory = installRoot + L"\\packages";
    const std::wstring currentDirectory = installRoot + L"\\current";
    const std::wstring extractDirectory = GetTempDownloadDirectory() + L"\\extract";
    launchPath.clear();

    if (onStatus) {
        onStatus(L"Przygotowywanie katalogu instalacyjnego...");
    }

    try {
        std::filesystem::remove_all(std::filesystem::path(extractDirectory));
        std::filesystem::remove_all(std::filesystem::path(currentDirectory));
    }
    catch (...) {
    }

    if (!EnsureDirectoryExists(installRoot) || !EnsureDirectoryExists(packagesDirectory) || !EnsureDirectoryExists(extractDirectory) || !EnsureDirectoryExists(currentDirectory)) {
        errorMessage = L"Nie udalo sie utworzyc katalogow instalacyjnych.";
        return false;
    }

    const std::wstring destinationPackage = packagesDirectory + L"\\" + asset.fileName;

    try {
        std::filesystem::copy_file(packagePath, destinationPackage, std::filesystem::copy_options::overwrite_existing);
    }
    catch (...) {
        errorMessage = L"Nie udalo sie skopiowac pobranego pakietu do katalogu packages.";
        return false;
    }

    if (!updateExePath.empty() && std::filesystem::exists(std::filesystem::path(updateExePath))) {
        try {
            std::filesystem::copy_file(updateExePath, std::filesystem::path(installRoot) / "Update.exe", std::filesystem::copy_options::overwrite_existing);
            Logger::Instance().Info(L"Update.exe copied to install root.");
        }
        catch (...) {
            Logger::Instance().Error(L"Failed to copy optional Update.exe to install root.");
        }
    }

    if (onStatus) {
        onStatus(L"Rozpakowywanie pakietu aplikacji...");
    }

    unsigned long extractExitCode = 0;
    const std::wstring extractCommand = L"\"C:\\Windows\\System32\\tar.exe\" -xf \"" + packagePath + L"\" -C \"" + extractDirectory + L"\"";
    if (!RunProcessAndWait(extractCommand, extractExitCode) || extractExitCode != 0) {
        errorMessage = L"Nie udalo sie rozpakowac paczki .nupkg przy pomocy tar.exe.";
        return false;
    }

    const std::filesystem::path extractedAppDirectory = std::filesystem::path(extractDirectory) / "lib" / "app";
    if (!std::filesystem::exists(extractedAppDirectory)) {
        errorMessage = L"Rozpakowana paczka nie zawiera katalogu lib/app.";
        return false;
    }

    try {
        std::filesystem::copy(extractedAppDirectory, std::filesystem::path(currentDirectory),
            std::filesystem::copy_options::recursive | std::filesystem::copy_options::overwrite_existing);
    }
    catch (...) {
        errorMessage = L"Nie udalo sie skopiowac plikow aplikacji do katalogu current.";
        return false;
    }

    launchPath = currentDirectory + L"\\SUSModder.exe";
    if (!std::filesystem::exists(std::filesystem::path(launchPath))) {
        errorMessage = L"Po rozpakowaniu nie znaleziono current\\SUSModder.exe.";
        return false;
    }

    const std::wstring markerPath = installRoot + L"\\bootstrapper-install-info.txt";
    WriteTextFile(markerPath,
        L"Package: " + asset.fileName + L"\n"
        L"Version: " + asset.version + L"\n"
        L"Channel: " + channel + L"\n"
        L"Update.exe copied: " + std::wstring((!updateExePath.empty() && std::filesystem::exists(std::filesystem::path(updateExePath))) ? L"yes" : L"no") + L"\n");

    Logger::Instance().Info(L"Seed package copied to install root: " + destinationPackage);
    return true;
}

} // namespace bootstrapper
