#pragma once

#include <atomic>
#include <functional>
#include <string>
#include <thread>

namespace bootstrapper {

enum class InstallerLanguage;

class InstallController {
public:
    using StatusCallback = std::function<void(const std::wstring&)>;
    using ProgressCallback = std::function<void(unsigned int)>;
    using CompletionCallback = std::function<void(bool, const std::wstring&)>;

    InstallController();
    ~InstallController();

    void SetStatusCallback(StatusCallback callback);
    void SetProgressCallback(ProgressCallback callback);
    void SetCompletionCallback(CompletionCallback callback);

    void Start(const std::wstring& channel, bool createStartMenuShortcut, bool createDesktopShortcut, InstallerLanguage language);
    void Cancel();
    bool IsBusy() const;

private:
    void Run(const std::wstring& channel, bool createStartMenuShortcut, bool createDesktopShortcut, InstallerLanguage language);
    void PublishStatus(const std::wstring& message) const;
    void PublishProgress(unsigned int value) const;
    void PublishCompletion(bool success, const std::wstring& message) const;

    std::atomic<bool> busy_ = false;
    std::atomic<bool> cancelRequested_ = false;
    std::atomic<bool> launchIssued_ = false;
    std::thread worker_;
    StatusCallback statusCallback_;
    ProgressCallback progressCallback_;
    CompletionCallback completionCallback_;
};

} // namespace bootstrapper
