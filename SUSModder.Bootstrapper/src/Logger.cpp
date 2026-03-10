#include "Logger.h"

#include "Win32Helpers.h"

#include <windows.h>

#include <fstream>
#include <iomanip>
#include <sstream>

namespace bootstrapper {

namespace {

std::wstring TimestampNow() {
    SYSTEMTIME localTime{};
    GetLocalTime(&localTime);

    std::wstringstream stream;
    stream << std::setfill(L'0')
           << localTime.wYear << L'-'
           << std::setw(2) << localTime.wMonth << L'-'
           << std::setw(2) << localTime.wDay << L' '
           << std::setw(2) << localTime.wHour << L':'
           << std::setw(2) << localTime.wMinute << L':'
           << std::setw(2) << localTime.wSecond;
    return stream.str();
}

} // namespace

Logger& Logger::Instance() {
    static Logger instance;
    return instance;
}

bool Logger::Initialize() {
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!logPath_.empty()) {
            return true;
        }

        const std::wstring logDirectory = GetAppLogDirectory();
        if (!EnsureDirectoryExists(logDirectory)) {
            return false;
        }

        logPath_ = logDirectory + L"\\bootstrapper.log";
    }

    WriteLine(L"INFO", L"Logger initialized");
    return true;
}

void Logger::Info(const std::wstring& message) {
    WriteLine(L"INFO", message);
}

void Logger::Error(const std::wstring& message) {
    WriteLine(L"ERROR", message);
}

std::wstring Logger::GetLogPath() const {
    std::lock_guard<std::mutex> lock(mutex_);
    return logPath_;
}

void Logger::WriteLine(const std::wstring& level, const std::wstring& message) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (logPath_.empty()) {
        return;
    }

    std::wofstream file(logPath_, std::ios::app);
    if (!file.is_open()) {
        return;
    }

    file << L'[' << TimestampNow() << L"] [" << level << L"] " << message << L"\n";
}

} // namespace bootstrapper
