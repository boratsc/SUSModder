#pragma once

#include <mutex>
#include <string>

namespace bootstrapper {

class Logger {
public:
    static Logger& Instance();

    bool Initialize();
    void Info(const std::wstring& message);
    void Error(const std::wstring& message);
    std::wstring GetLogPath() const;

private:
    Logger() = default;

    void WriteLine(const std::wstring& level, const std::wstring& message);

    std::wstring logPath_;
    mutable std::mutex mutex_;
};

} // namespace bootstrapper
