#include "InstallController.h"

#include "MainWindow.h"

#include "Downloader.h"
#include "HashVerifier.h"
#include "Installer.h"
#include "Logger.h"
#include "ManifestClient.h"
#include "SettingsWriter.h"
#include "Win32Helpers.h"

#include <algorithm>
#include <chrono>
#include <filesystem>
#include <utility>

namespace bootstrapper {

InstallController::InstallController() = default;

InstallController::~InstallController() {
    Cancel();
    if (worker_.joinable()) {
        worker_.join();
    }
}

void InstallController::SetStatusCallback(StatusCallback callback) {
    statusCallback_ = std::move(callback);
}

void InstallController::SetProgressCallback(ProgressCallback callback) {
    progressCallback_ = std::move(callback);
}

void InstallController::SetCompletionCallback(CompletionCallback callback) {
    completionCallback_ = std::move(callback);
}

void InstallController::Start(const std::wstring& channel, bool createStartMenuShortcut, bool createDesktopShortcut, InstallerLanguage language) {
    if (busy_) {
        return;
    }

    cancelRequested_ = false;
    launchIssued_ = false;
    busy_ = true;
    if (worker_.joinable()) {
        worker_.join();
    }

    worker_ = std::thread(&InstallController::Run, this, channel, createStartMenuShortcut, createDesktopShortcut, language);
}

void InstallController::Cancel() {
    cancelRequested_ = true;
}

bool InstallController::IsBusy() const {
    return busy_;
}

void InstallController::Run(const std::wstring& channel, bool createStartMenuShortcut, bool createDesktopShortcut, InstallerLanguage language) {
    const bool english = language == InstallerLanguage::English;
    const auto L = [english](const std::wstring& polish, const std::wstring& en) {
        return english ? en : polish;
    };

    Logger::Instance().Info(L"Bootstrapper flow started for channel: " + channel);
    PublishStatus(L(L"Przygotowanie instalatora...", L"Preparing installer..."));
    PublishProgress(5);
    std::this_thread::sleep_for(std::chrono::milliseconds(150));

    if (cancelRequested_) {
        busy_ = false;
        PublishCompletion(false, L(L"Instalacja anulowana.", L"Installation canceled."));
        return;
    }

    ManifestClient manifestClient;
    std::wstring errorMessage;
    PublishStatus(L(L"Pobieranie manifestu release...", L"Downloading release manifest..."));
    PublishProgress(15);
    const ReleaseManifest manifest = manifestClient.LoadManifest(channel, errorMessage);
    if (!manifest.success) {
        busy_ = false;
        PublishCompletion(false, errorMessage.empty() ? L(L"Nie udało się pobrać manifestu release.", L"Failed to download release manifest.") : errorMessage);
        return;
    }

    const auto assetIt = std::find_if(manifest.releases.begin(), manifest.releases.end(), [](const ReleaseAsset& asset) {
        return asset.isFullPackage;
    });

    if (assetIt == manifest.releases.end()) {
        busy_ = false;
        PublishCompletion(false, L(L"Manifest nie zawiera pełnego pakietu .nupkg.", L"Manifest does not contain a full .nupkg package."));
        return;
    }

    const ReleaseAsset asset = *assetIt;
    const std::wstring packageUrl = JoinUrl(manifest.downloadBaseUrl, asset.fileName);
    const std::wstring optionalUpdateExeUrl = JoinUrl(manifest.downloadBaseUrl, L"Update.exe");

    const std::wstring tempDirectory = GetTempDownloadDirectory();
    EnsureDirectoryExists(tempDirectory);
    const std::wstring packagePath = tempDirectory + L"\\" + asset.fileName;
    const std::wstring updateExePath = tempDirectory + L"\\Update.exe";

    PublishStatus(L(L"Pobieranie pakietu instalacyjnego...", L"Downloading installation package..."));
    Downloader downloader;
    if (!downloader.DownloadFile(packageUrl, packagePath, [this](unsigned int progress) {
        PublishProgress(15 + static_cast<unsigned int>((progress * 55) / 100));
    }, errorMessage)) {
        busy_ = false;
        PublishCompletion(false, errorMessage.empty() ? L(L"Nie udało się pobrać pakietu instalacyjnego.", L"Failed to download installation package.") : errorMessage);
        return;
    }

    PublishStatus(L(L"Weryfikacja integralności pakietu...", L"Verifying package integrity..."));
    PublishProgress(75);
    HashVerifier hashVerifier;
    std::wstring actualHash;
    if (!hashVerifier.VerifyFileSha256(packagePath, asset.sha256, actualHash)) {
        busy_ = false;
        PublishCompletion(false, L(L"Suma SHA-256 pobranego pakietu nie zgadza się z manifestem.", L"SHA-256 checksum does not match manifest value."));
        return;
    }

    PublishStatus(L(L"Sprawdzanie pomocniczego pliku Update.exe...", L"Checking optional Update.exe..."));
    PublishProgress(80);
    std::wstring updateExeError;
    if (!downloader.DownloadFile(optionalUpdateExeUrl, updateExePath, nullptr, updateExeError)) {
        Logger::Instance().Info(L"Optional Update.exe is not available yet: " + updateExeError);
    } else {
        Logger::Instance().Info(L"Optional Update.exe downloaded successfully.");
    }

    SettingsWriter settingsWriter;
    const std::wstring languageCode = language == InstallerLanguage::English ? L"en" : L"pl";
    if (!settingsWriter.SaveSelectedChannelAndLanguage(channel, languageCode)) {
        Logger::Instance().Error(L"Nie udało się zapisać user-settings.json dla kanału: " + channel);
    }

    PublishStatus(L(L"Przygotowywanie plików aplikacji...", L"Preparing application files..."));
    PublishProgress(85);
    Installer installer;
    std::wstring launchPath;
    if (!installer.PrepareSeedInstall(packagePath, asset, channel, updateExePath, [this](const std::wstring& status) {
        PublishStatus(status);
    }, errorMessage, launchPath)) {
        busy_ = false;
        PublishCompletion(false, errorMessage);
        return;
    }

    PublishStatus(L(L"Rejestrowanie aplikacji w systemie Windows...", L"Registering application in Windows..."));
    PublishProgress(92);
    const std::wstring installRoot = GetDefaultInstallRoot();
    if (!RegisterApplicationInUninstallRegistry(asset.version, launchPath, installRoot)) {
        busy_ = false;
        PublishCompletion(false, L(L"Nie udało się zarejestrować SUSModder w systemie Windows.", L"Failed to register SUSModder in Windows."));
        return;
    }

    PublishStatus(L(L"Tworzenie skrótów...", L"Creating shortcuts..."));
    PublishProgress(96);
    std::wstring shortcutError;
    if (!CreateApplicationShortcuts(launchPath, createStartMenuShortcut, createDesktopShortcut, shortcutError)) {
        busy_ = false;
        PublishCompletion(false, shortcutError.empty() ? L(L"Nie udało się utworzyć skrótów aplikacji.", L"Failed to create application shortcuts.") : shortcutError);
        return;
    }

    if (!launchPath.empty() && std::filesystem::exists(std::filesystem::path(launchPath)) && !launchIssued_.exchange(true)) {
        const bool launchStarted = LaunchProcessDetached(launchPath);
        if (!launchStarted) {
            Logger::Instance().Error(L"Nie udało się uruchomić zainstalowanej aplikacji: " + launchPath);
        }
    }

    PublishStatus(L(L"Instalacja zakończona powodzeniem.", L"Installation completed successfully."));
    PublishProgress(100);
    busy_ = false;
    PublishCompletion(true, L(L"SUSModder jest gotowy. Aplikacja została pobrana i uruchomiona.", L"SUSModder is ready. The application has been downloaded and launched."));
}

void InstallController::PublishStatus(const std::wstring& message) const {
    if (statusCallback_) {
        statusCallback_(message);
    }
}

void InstallController::PublishProgress(unsigned int value) const {
    if (progressCallback_) {
        progressCallback_(value);
    }
}

void InstallController::PublishCompletion(bool success, const std::wstring& message) const {
    if (completionCallback_) {
        completionCallback_(success, message);
    }
}

} // namespace bootstrapper
